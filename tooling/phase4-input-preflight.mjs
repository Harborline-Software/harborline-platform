import {execFileSync} from 'node:child_process'
import {createHash, randomUUID} from 'node:crypto'
import {lstatSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import path from 'node:path'

const MAIN_REF = 'refs/remotes/origin/main'
const FORGE = 'https://github.com/Harborline-Software/harborline-platform.git'
const FORGE_API = 'repos/Harborline-Software/harborline-platform/commits/main'
const OBJECT_ID = /^[0-9a-f]{40}$/
const OBSERVATION_ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/
const digest = value => createHash('sha256').update(value).digest('hex')
const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
}).trim()
const failure = error => error instanceof Error ? error.message : String(error)

function commit(root, ref) {
  try { return git(root, 'rev-parse', '--verify', `${ref}^{commit}`) } catch { return null }
}

function ancestor(root, base, tip) {
  if (!base || !tip) return false
  try { git(root, 'merge-base', '--is-ancestor', base, tip); return true } catch { return false }
}

function mainFetchEntry(text, sha) {
  return text.split(/\r?\n/).find(line => {
    const [object, marker, description] = line.split('\t')
    return object === sha && marker === '' && description?.startsWith("branch 'main' of ")
  }) ?? null
}

// The command itself selects main from a fixed forge, independently of a clone's local origin.
// A fallback is a new, observed main fetch, corroborated by the fixed forge API, never an mtime.
export function observePhase4Forge(root, {now = () => new Date(), execute = execFileSync} = {}) {
  const command = (executable, args) => execute(executable, args, {
    cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
  }).trim()
  let directFailure
  for (const method of ['direct-forge-fetch', 'verified-parent-fallback']) {
    const startedAt = now().toISOString()
    let fetch
    try {
      const source = method === 'direct-forge-fetch' ? FORGE : git(root, 'remote', 'get-url', 'origin')
      command('git', ['fetch', '--no-tags', '--no-recurse-submodules', source,
        '+refs/heads/main:refs/remotes/origin/main'])
      const sha = commit(root, MAIN_REF)
      const fetchHeadPath = git(root, 'rev-parse', '--path-format=absolute', '--git-path', 'FETCH_HEAD')
      const entry = mainFetchEntry(readFileSync(fetchHeadPath, 'utf8'), sha)
      if (!entry) throw new Error('the completed fetch has no matching selected main entry in FETCH_HEAD')
      const completedAt = now().toISOString()
      fetch = {method, source, mainRef: MAIN_REF, sha, startedAt, completedAt, fetchHeadEntry: entry}
      const forgeMain = method === 'direct-forge-fetch'
        ? sha
        : command('gh', ['api', FORGE_API, '--jq', '.sha'])
      if (!OBJECT_ID.test(forgeMain ?? '')) throw new Error('forge main lookup returned no full commit identity')
      return {
        forgeMain,
        fetch,
        ...(directFailure ? {directFailure} : {}),
      }
    } catch (error) {
      if (method === 'direct-forge-fetch') directFailure = failure(error)
      else return {forgeMain: null, fetch: fetch ?? null, directFailure, authorityFailure: failure(error)}
    }
  }
}

function reviewCensus(root, tree, mode) {
  const records = []
  const candidates = []
  const issues = []
  let listing
  try {
    listing = execFileSync('git', ['ls-tree', '-r', '-z', tree, '--', 'docs/evidence/design-review'], {
      cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
    })
  } catch (error) {
    return {records, candidates, issues: [{code: 'review-census-unreadable', detail: failure(error)}]}
  }
  let entries = listing.split('\0').filter(Boolean).map(entry => {
    const match = /^(\d+) (\S+) ([0-9a-f]+)\t([\s\S]+)$/.exec(entry)
    if (!match) return {invalid: true}
    const [, , type, blob, recordPath] = match
    return {type, blob, recordPath}
  })
  if (mode === 'current-index') {
    entries = []
    function visit(relative) {
      for (const item of readdirSync(path.join(root, relative), {withFileTypes: true}).sort((a, b) => a.name.localeCompare(b.name))) {
        const recordPath = `${relative}/${item.name}`
        if (item.isDirectory()) {
          if (item.name.endsWith('.json')) issues.push({code: 'review-record-unreadable', path: recordPath, detail: 'review candidate is a directory'})
          else visit(recordPath)
        }
        else if (item.name.endsWith('.json')) {
          if (!item.isFile()) { entries.push({invalid: true}); continue }
          try {
            const bytes = readFileSync(path.join(root, recordPath))
            const blob = createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex')
            entries.push({type: 'blob', blob, recordPath, raw: bytes.toString('utf8')})
          } catch (error) { issues.push({code: 'review-record-unreadable', path: recordPath, detail: failure(error)}) }
        }
      }
    }
    try { visit('docs/evidence/design-review') }
    catch (error) { issues.push({code: 'review-census-unreadable', detail: failure(error)}) }
  }
  for (const entry of entries) {
    if (entry.invalid) {
      issues.push({code: 'review-census-unreadable', detail: 'invalid Git tree entry'})
      continue
    }
    const {type, blob, recordPath} = entry
    if (!recordPath.endsWith('.json')) continue
    candidates.push({path: recordPath, blob})
    try {
      if (type !== 'blob') throw new Error('review candidate is not a blob')
      const raw = entry.raw ?? git(root, 'cat-file', 'blob', blob)
      const record = JSON.parse(raw)
      if (!record || typeof record !== 'object' || Array.isArray(record))
        throw new Error('review candidate must be a JSON object')
      if (record.reference !== undefined && (!record.reference || typeof record.reference !== 'object' || Array.isArray(record.reference)))
        throw new Error('review reference must be an object')
      const provenance = record.reference?.provenance
      if (provenance !== undefined && (!provenance || typeof provenance !== 'object' || Array.isArray(provenance)))
        throw new Error('review provenance must be an object')
      if (provenance?.kind !== 'git-index-surface-v1') continue
      records.push({path: recordPath, blob, baseHead: provenance.baseHead})
    } catch (error) {
      issues.push({code: 'review-record-unreadable', path: recordPath, blob, detail: failure(error)})
    }
  }
  return {records, candidates, issues}
}

function snapshot(root, subject = {}) {
  const head = commit(root, 'HEAD')
  const trackingMain = commit(root, MAIN_REF)
  let testedTree = null
  try { testedTree = git(root, 'write-tree') } catch {}
  const census = testedTree ? reviewCensus(root, testedTree, subject.mode) : {records: [], candidates: [], issues: []}
  let mergeBase = null
  if (head && trackingMain) {
    try { mergeBase = git(root, 'merge-base', head, trackingMain) } catch {}
  }
  return {
    head, trackingMain, testedTree, mergeBase,
    subject: {baseHead: subject.baseHead ?? head, testedTree: subject.testedTree ?? testedTree, mode: subject.mode},
    reviews: census.records.map(record => ({
      ...record,
      commitExists: OBJECT_ID.test(record.baseHead ?? '') && commit(root, record.baseHead) === record.baseHead,
      ancestorOfHead: OBJECT_ID.test(record.baseHead ?? '') && ancestor(root, record.baseHead, head),
      ancestorOfTracking: OBJECT_ID.test(record.baseHead ?? '') && ancestor(root, record.baseHead, trackingMain),
    })),
    reviewCandidates: census.candidates,
    censusIssues: census.issues,
  }
}

function assess(root, inputs, authority, measuredAt) {
  const issues = [...inputs.censusIssues]
  const forgeMain = authority?.forgeMain ?? null
  const fetch = authority?.fetch ?? null
  const relation = inputs.trackingMain && forgeMain
    ? inputs.trackingMain === forgeMain ? 'equal'
      : ancestor(root, inputs.trackingMain, forgeMain) ? 'ancestor' : 'unrelated'
    : 'missing'
  const fetchTime = Date.parse(fetch?.completedAt ?? '')
  const ageMs = Number.isFinite(fetchTime) ? Date.parse(measuredAt) - fetchTime : null
  if (!OBJECT_ID.test(forgeMain ?? '')) issues.push({code: 'forge-lookup-failed', detail: authority?.authorityFailure ?? 'forge main unavailable'})
  if (!inputs.head) issues.push({code: 'head-missing'})
  if (!inputs.trackingMain) issues.push({code: 'tracking-main-missing'})
  if (relation === 'unrelated') issues.push({code: 'tracking-main-unrelated-to-forge'})
  if (!inputs.testedTree || inputs.subject.baseHead !== inputs.head || inputs.subject.testedTree !== inputs.testedTree)
    issues.push({code: 'tested-subject-mismatch'})
  if (!inputs.trackingMain || inputs.mergeBase !== inputs.trackingMain)
    issues.push({code: 'head-main-divergence'})
  const known = fetch
    && ['direct-forge-fetch', 'verified-parent-fallback'].includes(fetch.method)
    && typeof fetch.source === 'string' && fetch.source.length > 0
    && fetch.mainRef === MAIN_REF && fetch.sha === inputs.trackingMain
    && typeof fetch.fetchHeadEntry === 'string'
    && mainFetchEntry(fetch.fetchHeadEntry, inputs.trackingMain)
    && Number.isFinite(ageMs) && ageMs >= 0
  if (!known) issues.push({code: 'main-fetch-history-unknown'})
  if (fetch?.method === 'verified-parent-fallback' && fetch.sha !== forgeMain)
    issues.push({code: 'parent-fallback-forge-mismatch'})
  for (const record of inputs.reviews) {
    if (!record.commitExists) issues.push({code: 'review-base-missing', path: record.path, baseHead: record.baseHead ?? null})
    else if (!record.ancestorOfHead || !record.ancestorOfTracking)
      issues.push({code: 'review-base-ancestry', path: record.path, baseHead: record.baseHead})
  }
  return {forgeMain, fetch, trackingToForge: relation, fetchAgeMs: known ? ageMs : null, issues}
}

function immutable(file, value) {
  writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`, {flag: 'wx'})
}

function inheritedRecord(root, binding) {
  const common = git(root, 'rev-parse', '--path-format=absolute', '--git-common-dir')
  const recordPath = path.resolve(binding.recordPath)
  const observationRoot = path.dirname(path.dirname(recordPath))
  const relative = path.relative(common, observationRoot)
  if (relative.startsWith('..') || path.isAbsolute(relative)
    || path.basename(observationRoot) !== 'harborline-phase4-inputs'
    || path.basename(recordPath) !== 'preflight.json')
    throw new Error('inherited preflight is outside the shared Git observation directory')
  const bytes = readFileSync(recordPath)
  if (digest(bytes) !== binding.sha256) throw new Error('inherited preflight bytes do not match their IPC identity')
  const preflight = JSON.parse(bytes)
  if (preflight.status !== 'PASS') throw new Error('inherited preflight did not pass')
  return {preflight, observationRoot}
}

function checkUnchanged(root, preflight, subject) {
  const current = snapshot(root, subject)
  const before = preflight.inputs
  if (JSON.stringify(current) !== JSON.stringify(before)) {
    const error = new Error('Phase 4 Git inputs changed after preflight')
    error.code = 'launch-inputs-changed'
    error.observedInputs = current
    throw error
  }
  return current
}

export function assertPhase4SubjectUnchanged(root, {head, testedTree}) {
  if (git(root, 'rev-parse', 'HEAD') !== head || git(root, 'write-tree') !== testedTree)
    throw new Error('Platform HEAD or staged tree changed while the phase-4 gate was running')
}

export function projectPhase4InputEvidence(preflight) {
  const identity = value => typeof value === 'string' && OBJECT_ID.test(value) ? value : undefined
  const observationId = value => typeof value === 'string' && OBSERVATION_ID.test(value) ? value : undefined
  const timestamp = value => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/.test(value)
    && Number.isFinite(Date.parse(value)) && new Date(value).toISOString() === value ? value : undefined
  const relativePath = value => typeof value === 'string' && value.split('/').every(part =>
    /^[A-Za-z0-9._-]+$/.test(part) && part !== '.' && part !== '..') ? value : undefined
  const mode = value => ['current-index', 'exact-staged-tree'].includes(value) ? value : undefined
  const issueCodes = new Set([
    'review-census-unreadable', 'review-record-unreadable', 'forge-lookup-failed', 'head-missing',
    'tracking-main-missing', 'tracking-main-unrelated-to-forge', 'tested-subject-mismatch',
    'head-main-divergence', 'main-fetch-history-unknown', 'parent-fallback-forge-mismatch',
    'review-base-missing', 'review-base-ancestry', 'inherited-preflight-invalid', 'launch-inputs-changed',
  ])
  const record = value => ({path: relativePath(value?.path), blob: identity(value?.blob), baseHead: identity(value?.baseHead)})
  return {
    schemaVersion: 1,
    attemptId: observationId(preflight.attemptId),
    preflightId: observationId(preflight.preflightId),
    parentPreflightId: observationId(preflight.parentPreflightId),
    kind: ['gate', 'receipt'].includes(preflight.kind) ? preflight.kind : undefined,
    measuredAt: timestamp(preflight.measuredAt),
    status: ['PASS', 'FAIL'].includes(preflight.status) ? preflight.status : undefined,
    forgeMain: identity(preflight.forgeMain),
    inputs: {
      head: identity(preflight.inputs?.head),
      trackingMain: identity(preflight.inputs?.trackingMain),
      testedTree: identity(preflight.inputs?.testedTree),
      mergeBase: identity(preflight.inputs?.mergeBase),
      subject: {
        baseHead: identity(preflight.inputs?.subject?.baseHead),
        testedTree: identity(preflight.inputs?.subject?.testedTree),
        mode: mode(preflight.inputs?.subject?.mode),
      },
      reviews: Array.isArray(preflight.inputs?.reviews) ? preflight.inputs.reviews.map(record) : [],
      reviewCandidates: Array.isArray(preflight.inputs?.reviewCandidates) ? preflight.inputs.reviewCandidates.map(record) : [],
    },
    fetch: preflight.fetch ? {
      method: ['direct-forge-fetch', 'verified-parent-fallback'].includes(preflight.fetch.method) ? preflight.fetch.method : undefined,
      mainRef: preflight.fetch.mainRef === MAIN_REF ? MAIN_REF : undefined,
      sha: identity(preflight.fetch.sha),
      startedAt: timestamp(preflight.fetch.startedAt),
      completedAt: timestamp(preflight.fetch.completedAt),
    } : undefined,
    issues: Array.isArray(preflight.issues) ? preflight.issues.filter(issue => issueCodes.has(issue?.code)).map(issue => ({
      code: issue.code, path: relativePath(issue.path), blob: identity(issue.blob), baseHead: identity(issue.baseHead),
    })) : [],
  }
}

export function prunePhase4InputAttempts(observationRoot, currentAttemptId, inherited) {
  const rootStat = lstatSync(observationRoot)
  if (!rootStat.isDirectory() || rootStat.isSymbolicLink()) return
  const nodes = new Map()
  const byPreflight = new Map()
  for (const entry of readdirSync(observationRoot, {withFileTypes: true})) {
    if (!OBSERVATION_ID.test(entry.name)) continue
    if (!entry.isDirectory() || entry.isSymbolicLink()) return
    const directory = path.join(observationRoot, entry.name)
    const documents = new Map()
    let ambiguous = false
    for (const item of readdirSync(directory, {withFileTypes: true})) {
      if (!['started.json', 'preflight.json', 'launch.json', 'result.json'].includes(item.name)
        || !item.isFile() || item.isSymbolicLink()) { ambiguous = true; continue }
      try { documents.set(item.name, JSON.parse(readFileSync(path.join(directory, item.name), 'utf8'))) }
      catch { ambiguous = true }
    }
    const started = documents.get('started.json')
    const preflight = documents.get('preflight.json')
    const result = documents.get('result.json')
    const launch = documents.get('launch.json')
    const records = [...documents.values(), result?.preflight].filter(Boolean)
    const ids = new Set(records.map(record => record.preflightId).filter(id => OBSERVATION_ID.test(id ?? '')))
    const parents = new Set(records.map(record => record.parentPreflightId).filter(id => OBSERVATION_ID.test(id ?? '')))
    const unknownRelationship = !ids.size && (!started || !Object.hasOwn(started, 'parentPreflightId'))
    if (unknownRelationship) return
    const complete = !ambiguous && started?.schemaVersion === 1 && preflight?.schemaVersion === 1
      && result?.schemaVersion === 1 && ids.size === 1 && parents.size <= 1
      && records.every(record => record.attemptId === entry.name)
      && OBSERVATION_ID.test(preflight.preflightId ?? '') && result.preflightId === preflight.preflightId
      && JSON.stringify(result.preflight) === JSON.stringify(preflight)
      && (started.parentPreflightId === undefined || started.parentPreflightId === (preflight.parentPreflightId ?? null))
      && (preflight.parentPreflightId === undefined || OBSERVATION_ID.test(preflight.parentPreflightId))
      && started.kind === preflight.kind
      && Number.isFinite(Date.parse(result.finishedAt)) && Number.isInteger(result.exitCode)
      && ['PASS', 'FAIL'].includes(preflight.status)
      && result.status === (result.exitCode === 0 ? 'PASS' : 'FAIL')
      && (result.launched === true ? preflight.status === 'PASS' && launch?.preflightId === preflight.preflightId
        : result.launched === false && !launch)
    const node = {directory, ids, parents, complete, finishedAt: Date.parse(result?.finishedAt), neighbors: new Set()}
    nodes.set(entry.name, node)
    for (const id of ids) {
      if (!byPreflight.has(id)) byPreflight.set(id, [])
      byPreflight.get(id).push(node)
    }
  }
  for (const node of nodes.values()) {
    for (const id of [...node.ids, ...node.parents]) {
      const related = byPreflight.get(id) ?? []
      if (!related.length || related.length > 1) node.complete = false
      for (const other of related) {
        if (related.length > 1) other.complete = false
        node.neighbors.add(other)
        other.neighbors.add(node)
      }
    }
  }
  for (const node of nodes.values()) {
    const ancestors = new Set()
    const pending = [...node.parents]
    while (pending.length) {
      const id = pending.pop()
      if (node.ids.has(id)) { node.complete = false; break }
      if (ancestors.has(id)) continue
      ancestors.add(id)
      for (const parent of byPreflight.get(id) ?? []) pending.push(...parent.parents)
    }
  }
  const families = []
  const visited = new Set()
  for (const node of nodes.values()) {
    if (visited.has(node)) continue
    const family = []
    const pending = [node]
    while (pending.length) {
      const member = pending.pop()
      if (visited.has(member)) continue
      visited.add(member)
      family.push(member)
      pending.push(...member.neighbors)
    }
    if (family.every(member => member.complete)) families.push(family)
  }
  families.sort((a, b) => Math.max(...b.map(node => node.finishedAt)) - Math.max(...a.map(node => node.finishedAt))
    || a[0].directory.localeCompare(b[0].directory))
  let retained = 0
  for (const family of families) {
    const protectedFamily = family.some(member => path.basename(member.directory) === currentAttemptId
      || (typeof inherited?.recordPath === 'string' && path.dirname(path.resolve(inherited.recordPath)) === member.directory))
    if (retained < 32 || protectedFamily) { retained += family.length; continue }
    for (const node of family) {
      if (!lstatSync(node.directory).isDirectory() || lstatSync(node.directory).isSymbolicLink()) return
    }
    for (const node of family) rmSync(node.directory, {recursive: true})
  }
}

export async function runPhase4InputAttempt({
  repositoryRoot, kind, subject = {}, inherited, launch,
  observeForge = observePhase4Forge, now = () => new Date(), beforeLaunch,
}) {
  const privateGit = git(repositoryRoot, 'rev-parse', '--path-format=absolute', '--git-dir')
  let parent
  let parentError
  if (inherited) {
    try { parent = inheritedRecord(repositoryRoot, inherited) } catch (error) { parentError = failure(error) }
  }
  const observationRoot = parent?.observationRoot ?? path.join(privateGit, 'harborline-phase4-inputs')
  mkdirSync(observationRoot, {recursive: true})
  const attemptId = randomUUID()
  const directory = path.join(observationRoot, attemptId)
  mkdirSync(directory)
  const startedAt = now().toISOString()
  const inputs = snapshot(repositoryRoot, subject)
  immutable(path.join(directory, 'started.json'), {
    schemaVersion: 1, attemptId, kind, startedAt, inputs, forgeMain: null, fetchAgeMs: null,
    parentPreflightId: parent?.preflight.preflightId ?? null,
    authorityStatus: 'not-yet-measured',
  })
  let authority
  if (parent) authority = {forgeMain: parent.preflight.forgeMain, fetch: parent.preflight.fetch}
  else if (!inherited) {
    try { authority = await observeForge(repositoryRoot, {now}) }
    catch (error) { authority = {authorityFailure: failure(error)} }
  }
  // A successful direct/fallback fetch may have updated tracking main. Measure afterward.
  const measuredInputs = parent || inherited ? inputs : snapshot(repositoryRoot, subject)
  const measuredAt = now().toISOString()
  const assessed = assess(repositoryRoot, measuredInputs, authority, measuredAt)
  if (parentError) assessed.issues.push({code: 'inherited-preflight-invalid', detail: parentError})
  if (parent) {
    try { checkUnchanged(repositoryRoot, parent.preflight, subject) }
    catch (error) { assessed.issues.push({code: 'launch-inputs-changed', detail: failure(error)}) }
  }
  const preflight = {
    schemaVersion: 1, attemptId, preflightId: randomUUID(), kind, measuredAt,
    ...(parent ? {
      parentPreflightId: parent.preflight.preflightId,
      parentMeasurement: {inputs: parent.preflight.inputs, forgeMain: parent.preflight.forgeMain,
        fetch: parent.preflight.fetch, fetchAgeMs: parent.preflight.fetchAgeMs},
    } : {}),
    inputs: measuredInputs,
    ...assessed,
    ...(authority?.directFailure ? {directForgeFailure: authority.directFailure} : {}),
    status: assessed.issues.length ? 'FAIL' : 'PASS',
  }
  const recordPath = path.join(directory, 'preflight.json')
  immutable(recordPath, preflight)
  const binding = {recordPath, sha256: digest(readFileSync(recordPath))}
  let launched = false
  let exitCode = 1
  let result
  let error
  let launchInputs
  const recheck = () => {
    launchInputs = checkUnchanged(repositoryRoot, preflight, subject)
    return launchInputs
  }
  try {
    if (preflight.status !== 'PASS') throw new Error('Phase 4 input preflight refused launch')
    if (beforeLaunch) await beforeLaunch(preflight)
    recheck()
    immutable(path.join(directory, 'launch.json'), {
      attemptId, preflightId: preflight.preflightId, launchedAt: now().toISOString(), inputs: launchInputs,
    })
    launched = true
    result = await launch({preflight, binding, recheck})
    if (!Number.isInteger(result?.exitCode)) throw new Error('Phase 4 launch returned no actual exit code')
    exitCode = result.exitCode
  } catch (caught) {
    error = {code: caught.code ?? 'phase4-attempt-refused-or-failed', message: failure(caught),
      ...(caught.observedInputs ? {observedInputs: caught.observedInputs} : {})}
  }
  const observation = {
    schemaVersion: 1, attemptId, preflightId: preflight.preflightId, finishedAt: now().toISOString(),
    preflight, launched, exitCode, status: exitCode === 0 ? 'PASS' : 'FAIL',
    ...(result ? {result} : {}), ...(error ? {error} : {}),
  }
  immutable(path.join(directory, 'result.json'), observation)
  try { prunePhase4InputAttempts(observationRoot, attemptId, inherited) }
  catch (caught) { process.stderr.write(`Phase 4 input history cleanup unavailable: ${failure(caught)}\n`) }
  return {directory, preflight, launched, exitCode, error, observation}
}
