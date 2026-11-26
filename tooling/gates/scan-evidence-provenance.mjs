#!/usr/bin/env node
// A committed docs/evidence/phase-4/gate.json must name a run that every clone of main can produce.
//
// T-568 committed evidence recording subject.baseHead = 28a17f71, its own lane branch's HEAD. The
// squash merge discarded that commit, so `git branch -r --contains` finds it on nothing: only a
// clone that happens to have kept the loose object can produce it. tooling/release-receipt.mjs
// refuses such evidence, correctly and unchanged (release-receipt-evidence-run-not-in-repository),
// but it refuses at GATE time -- an hour in, on whichever runner's reused clone no longer holds the
// object. macpro's held it and mac16's did not, so every platform merge group the queue routed to
// mac16 failed, weeks after the commit that caused it (T-674). Refusing late is what turned one bad
// commit into an intermittent queue failure; this refuses at the commit that writes it.
//
// design-review provenance already learned this, in its own first line: "New judgements bind staged
// blobs, not a branch commit which a squash merge can discard." Same rule, same reason, applied to
// the phase-4 evidence -- and see checkBase in gates/design-review-provenance.mjs, which is this
// assertion for that record.
//
// The rule: the recorded run is a COMPLETE run at a commit main already holds.
//
//   subject.baseHead    an ancestor of main. Anything else dies with the lane branch.
//   subject.testedTree  that commit's own tree, which is the only tree a main commit makes durable.
//                       A dirty-index run names a tree that hangs off no commit, so it is lost the
//                       same way baseHead is, and release-receipt refuses it on the same line.
//   gallery-gate        present and passed, i.e. the run was not headless. gate-rows.mjs reads
//                       `gate?.results?.find(r => r.id === 'gallery-gate')?.passed` to decide every
//                       module's assertAccessible row, so headless evidence turns fifteen modules
//                       UNBUILT and catalog-final then refuses their status claims.
//
// That last one is here because the gate CANNOT catch it. run-phase-4-gate.mjs writes gate.json
// only after catalog-final has run, so catalog-final always judges the PREVIOUS recording -- a
// headless run therefore passes locally and poisons the next run instead. Measured the hard way on
// T-674: a headless re-record passed a full local gate and failed catalog-final in CI 40 minutes
// later, with fifteen `assertAccessible=UNBUILT` errors and nothing pointing at the cause.
import {execFileSync} from 'node:child_process'
import {readFileSync, writeFileSync, mkdirSync, readdirSync, lstatSync, rmSync} from 'node:fs'
import {createHash} from 'node:crypto'
import {isDeepStrictEqual} from 'node:util'
import {reconcileReceiptShards} from '../collect-gallery-shards.mjs'
import {expectedChecks, reconcileChecks} from '../gallery-observations.mjs'
import {requiredStepIds} from '../gate-contract.mjs'
import {checkReleaseReceipt, emitReleaseReceipt} from '../release-receipt.mjs'
import {isAbsolute, relative, resolve, sep} from 'node:path'

export const EVIDENCE_PATH = 'docs/evidence/phase-4/gate.json'
const OBJECT_ID = /^[0-9a-f]{40}$/

const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
}).trim()
const succeeds = (root, ...args) => { try { git(root, ...args); return true } catch { return false } }

/** The ref carrying main's history, or null when this clone has not fetched it. */
function mainRef(root) {
  return ['refs/remotes/origin/main', 'refs/heads/main']
    .find(ref => succeeds(root, 'rev-parse', '--verify', '--quiet', ref)) ?? null
}

/** `{field, reason}` naming the field that refused, or null when the recorded run is durable. */
export function checkEvidenceProvenance(root, bytes) {
  let recorded
  try { recorded = JSON.parse(bytes) } catch { return {field: 'subject', reason: 'the evidence is not readable JSON'} }
  if (recorded?.schemaVersion !== 3 || recorded?.phase !== 4 || recorded?.status !== 'PASS') {
    return {field: 'status', reason: 'durable evidence must be a schema-v3 phase-4 PASS'}
  }
  const {baseHead, testedTree} = recorded?.subject ?? {}
  const main = mainRef(root)
  // Fail closed. A clone with no main history cannot judge this, and "cannot judge" is never "pass".
  if (!main) return {field: 'subject.baseHead', reason: 'this clone holds no main history to check against; fetch main'}
  if (!OBJECT_ID.test(baseHead ?? '')) return {field: 'subject.baseHead', reason: `not an object id: ${baseHead}`}
  if (!succeeds(root, 'merge-base', '--is-ancestor', baseHead, main)) {
    return {
      field: 'subject.baseHead',
      reason: `${baseHead} is not an ancestor of ${main}. A squash merge discards a lane branch's commits,`
        + ' so evidence recorded on a branch names an object main can never produce. Re-run the gate on a'
        + ' clean checkout of a commit main already holds and commit that report.',
    }
  }
  if (testedTree !== git(root, 'rev-parse', `${baseHead}^{tree}`)) {
    return {
      field: 'subject.testedTree',
      reason: `${testedTree} is not the tree of ${baseHead}. The recorded run must be a clean run:`
        + ' a tree written from a dirty index hangs off no commit and is lost with the branch.',
    }
  }
  if (!recorded.results?.some(result => result.id === 'gallery-gate' && result.passed)) {
    return {
      field: 'results[gallery-gate]',
      reason: 'the recorded run is headless, so it carries no gallery evidence. gate-rows.mjs reads'
        + ' this result to decide assertAccessible, and catalog-final then refuses every module'
        + ' claiming implemented-gate-model-green. Record with the gallery: no HARBORLINE_GATE_HEADLESS.',
    }
  }
  return null
}

if (import.meta.main && !process.argv.includes('--receipt') && !process.argv.includes('--release-policy') && !process.argv.includes('--release-evidence')) {
  const root = resolve(process.argv[2] ?? '.')
  let bytes
  // No recorded evidence is nothing to bind. Its absence is validate-repository.mjs's business.
  try { bytes = readFileSync(resolve(root, EVIDENCE_PATH), 'utf8') } catch { process.exit(0) }
  const refusal = checkEvidenceProvenance(root, bytes)
  if (refusal) {
    process.stderr.write(`${EVIDENCE_PATH}: ${refusal.field} - ${refusal.reason}\n`)
    process.exitCode = 1
  }
}

// Gallery receipt v1. Identity and binding are independently checked; test results remain reported.
// GitHub CLI owns Sigstore cryptography, roots, certificate chain and transparency verification.
export const RECEIPT_REPOSITORY = 'Harborline-Software/harborline-platform'
export const RECEIPT_TYPE = 'https://harborline.software/attestations/gallery-run/v1'
const SIGNER = `${RECEIPT_REPOSITORY}/.github/workflows/sign-gallery-receipt.yml`
const SIGNER_URI = `https://github.com/${SIGNER}@refs/heads/main`
const GALLERY_STEPS = ['gallery-structure', 'react-gallery-typecheck', 'react-storybook-build',
  'blazor-gallery-clean', 'blazor-gallery-build', 'gallery-tests-typecheck', 'playwright-browser',
  'playwright-browser-launch', 'gallery-accessibility-and-parity']
// ADR-0198 d1: full receipts never inherit the headless landing contract.
const receiptStepIds = requiredStepIds.includes('gallery-gate') ? requiredStepIds : [...requiredStepIds.slice(0, -2), 'gallery-gate', ...requiredStepIds.slice(-2)]
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')
const checked = value => ({assurance: 'signer-checked', value})
const reported = value => ({assurance: 'run-reported', value})
const requireThat = (condition, message) => { if (!condition) throw new Error(`gallery receipt: ${message}`) }
const jsonFile = path => JSON.parse(readFileSync(path, 'utf8'))
function gitBytes(root, ...args) {
  return execFileSync('git', args, {cwd: root, maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']})
}
function objectId(value) {
  requireThat(OBJECT_ID.test(value ?? ''), 'invalid Git object id')
  return value
}

// Fixed format: default ls-tree mode/type/object<TAB>path, NUL-delimited, byte-sorted, final NUL.
// -z avoids quoting, escaping or ambiguity for tabs/newlines/non-ASCII paths.
export function receiptTree(root, commit) {
  const gitTree = git(root, 'rev-parse', `${objectId(commit)}^{tree}`)
  const bytes = gitBytes(root, 'ls-tree', '-r', '--full-tree', '-z', gitTree)
  const entries = []
  for (let start = 0, end; (end = bytes.indexOf(0, start)) >= 0; start = end + 1) entries.push(bytes.subarray(start, end))
  const listing = Buffer.concat(entries.sort(Buffer.compare).flatMap(entry => [entry, Buffer.from([0])]))
  return {gitTree, listing, sha256: sha256(listing)}
}

// Candidate data is read directly from Git blobs. No checkout, symlink traversal, loader or script.
export function receiptScope(root, commit) {
  objectId(commit)
  const entries = gitBytes(root, 'ls-tree', '-r', '--full-tree', '-z', commit).toString('utf8').split('\0').filter(Boolean)
  const blobs = new Map(entries.map(entry => {
    const match = /^(100644|100755) blob ([a-f0-9]{40})\t([\s\S]+)$/.exec(entry)
    return match ? [match[3], match[2]] : [null, null]
  }))
  const data = path => {
    requireThat(blobs.has(path), `missing regular data blob ${path}`)
    return JSON.parse(gitBytes(root, 'cat-file', 'blob', blobs.get(path)).toString('utf8'))
  }
  const modules = [...blobs.keys()].filter(path => /^catalog\/modules\/hlp\.ui\.[^/]+\.json$/.test(path ?? ''))
    .map(path => data(path)).sort((a, b) => a.moduleId.localeCompare(b.moduleId))
  const moduleIds = modules.filter(row => Object.values(row.module.projections ?? {}).some(p => p.conformanceRunner)).map(row => row.moduleId)
  const visual = modules.filter(row => row.module.presentation?.disposition === 'visual').map(row => row.moduleId)
  const scenarios = visual.flatMap(id => data(`specs/modules/ui/${id}/scenarios.json`).scenarios.map(s => s.id)).sort()
  const gallery = [...blobs.keys()].filter(path => /^gallery\/scenarios\/hlp\.ui\.[^/]+\.json$/.test(path ?? '')).map(path => data(path))
  const galleryIds = gallery.flatMap(row => row.scenarios.map(s => s.id)).sort()
  requireThat(scenarios.length > 0 && scenarios.every(id => typeof id === 'string') && new Set(scenarios).size === scenarios.length
    && isDeepStrictEqual(scenarios, galleryIds), 'tree spec/gallery scenario lists differ or repeat')
  const qualityCases = gallery.reduce((sum, row) => {
    const quality = data(row.qualityFixture)
    return sum + quality.accessibilityCases.length + quality.internationalizationCases.length
      + quality.themingCases.length + (quality.dismissalCases ?? []).length
  }, 0)
  const sharedResults = moduleIds.reduce((sum, id) => {
    const cases = data(`conformance/${id}/fixtures.yaml`).cases.length
    const path = `conformance/${id}/runners/run-shared.mjs`
    requireThat(blobs.has(path), 'missing shared runner blob')
    const runner = gitBytes(root, 'cat-file', 'blob', blobs.get(path)).toString('utf8')
    return sum + (runner.includes('run-ui-module-shared') ? cases + 1 : cases * 2)
  }, 0)
  const checkModules = gallery.map(row => {
    const quality = data(row.qualityFixture)
    return {...row, locales: Object.keys(quality.locales ?? {}).length, themes: (quality.themes ?? []).length}
  })
  return {moduleIds, generationSmokeModules: visual.length, scenarios, qualityCases, sharedResults,
    allowedSkippedIds: data('gallery/ci-exemptions.json').wallClock,
    expectedChecks: expectedChecks(checkModules, {ciGallery: true, exemptions: data('gallery/ci-exemptions.json')})}
}

export function scopeMatchesGate(scope, gate) {
  return isDeepStrictEqual(scope.moduleIds, gate?.moduleIds)
    && scope.generationSmokeModules === gate.counts?.generationSmokeModules
    && scope.scenarios.length === gate.counts?.generationSmokeScenarios
    && scope.scenarios.length === gate.counts?.galleryScenarios
    && scope.qualityCases === gate.counts?.qualityCases && scope.sharedResults === gate.counts?.sharedResults
}

export function needsGalleryReceipt(root, head, gate) {
  const scope = receiptScope(root, head)
  return !scopeMatchesGate(scope, gate) || !isDeepStrictEqual(scope, receiptScope(root, objectId(gate?.subject?.baseHead)))
}

export function queueIdentity(run, expected) {
  requireThat(run?.event === 'merge_group' && run.status === 'completed' && run.name === 'verify'
    && run.path === '.github/workflows/verify.yml' && run.repository?.full_name === RECEIPT_REPOSITORY
    && run.head_repository?.full_name === RECEIPT_REPOSITORY
    && run.head_sha === expected.head && String(run.id) === String(expected.id)
    && Number(run.run_attempt) === Number(expected.attempt), 'queue run identity/event/head/attempt differs')
  return {id: run.id, attempt: run.run_attempt, event: run.event, head: run.head_sha,
    repository: run.repository.full_name, status: run.status, workflow: run.path, conclusion: run.conclusion}
}

export function verifyObservationArchive({artifact, expected, bytes}) {
  requireThat(Number.isSafeInteger(artifact?.id) && artifact.id > 0 && !artifact.expired
    && artifact.workflow_run?.id === expected.runId && artifact.name === expected.name
    && artifact.digest === `sha256:${sha256(bytes)}`, 'raw artifact identity or digest differs')
}

function downloadObservations({directory, producer}) {
  const listing = api(`repos/${RECEIPT_REPOSITORY}/actions/runs/${producer.runId}/artifacts?per_page=100`)
  requireThat(Array.isArray(listing.artifacts) && listing.total_count === listing.artifacts.length && listing.total_count <= 100,
    'incomplete raw artifact census')
  const inputs = [{name: `candidate-phase4-${producer.runId}-${producer.attempt}`, file: 'phase4.json'},
    ...[1,2,3,4,5,6].map(id => ({name: `receipt-shard-${producer.runId}-${producer.attempt}-${id}`, file: `gallery-shard-${id}.json`}))]
  const identities = []
  mkdirSync(directory, {recursive: true})
  for (const input of inputs) {
    const matches = listing.artifacts.filter(row => row.name === input.name)
    requireThat(matches.length === 1, 'missing or duplicate raw artifact')
    const artifact = matches[0]
    requireThat(Number.isSafeInteger(artifact.id) && artifact.id > 0, 'invalid raw artifact id')
    const bytes = execFileSync('gh', ['api', `repos/${RECEIPT_REPOSITORY}/actions/artifacts/${artifact.id}/zip`],
      {maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']})
    verifyObservationArchive({artifact, expected: {runId: producer.runId, attempt: producer.attempt, name: input.name}, bytes})
    // Standard-library ZIP parsing only; no archive path is extracted or executed. One bounded JSON file.
    const data = execFileSync(process.platform === 'win32' ? 'python' : 'python3', ['-c',
      'import sys,io,zipfile\nz=zipfile.ZipFile(io.BytesIO(sys.stdin.buffer.read()))\na=z.infolist()\nassert len(a)==1 and a[0].filename==sys.argv[1] and a[0].file_size<=67108864\nsys.stdout.buffer.write(z.read(a[0]))', input.file],
      {input: bytes, maxBuffer: 64 * 1024 * 1024, stdio: ['pipe', 'pipe', 'pipe']})
    JSON.parse(data)
    writeFileSync(resolve(directory, input.file), data)
    identities.push({id: artifact.id, name: input.name, digest: artifact.digest, file: input.file, sha256: sha256(data)})
  }
  return identities
}

// Candidate processes may forge their observations. Only the fresh data-only job owns
// the expected inventory and reconciliation; Git blobs, never a mutable checkout, own scope.
export function collectTrustedGateData({root, head, gate, shards, producer}) {
  const tree = receiptTree(root, head), scope = receiptScope(root, head)
  requireThat(gate?.schemaVersion === 3 && gate.phase === 4 && gate.status === 'PASS', 'reported gate failed or malformed')
  requireThat(gate?.subject?.baseHead === head && gate.subject.testedTree === tree.gitTree, 'reported subject differs')
  requireThat(gate?.results?.length === receiptStepIds.length && receiptStepIds.every((id, i) =>
    gate.results[i]?.id === id && gate.results[i].passed === true && gate.results[i].exitCode === 0
      && !gate.results[i].reusedFrom), 'full fresh reported steps required')
  requireThat(Number.isInteger(gate.counts?.nativeTests) && gate.counts.nativeTests > 0, 'reported native test count absent')
  requireThat(shards.length > 0 && shards.every(shard => shard.provenance?.sourceHead === head
    && String(shard.provenance.runId) === String(producer.runId)), 'shard invocation differs')
  const reconciled = reconcileReceiptShards(shards, scope.scenarios, GALLERY_STEPS, scope.allowedSkippedIds)
  requireThat(reconcileChecks(reconciled.observation.checks, scope.expectedChecks) === 'exact', 'raw check reconciliation failed')
  return {schemaVersion: 3, phase: 4, status: 'PASS',
    subject: {repository: 'harborline-platform', baseHead: head, testedTree: tree.gitTree, mode: 'exact-staged-tree'},
    requiredStepIds: [...receiptStepIds], moduleIds: scope.moduleIds,
    results: gate.results.map(({id, passed, exitCode, report}) => ({id, passed, exitCode, ...(report ? {report} : {})})),
    designReview: gate.designReview,
    counts: {nativeTests: gate.counts.nativeTests, generationSmokeModules: scope.generationSmokeModules, generationSmokeScenarios: scope.scenarios.length,
      galleryScenarios: scope.scenarios.length, qualityCases: scope.qualityCases, sharedResults: scope.sharedResults,
      scenarioBrowserTests: reconciled.observation.scenarioBrowserTests, galleryChecks: reconciled.observation.checks},
    invariants: {expectedSharedResults: scope.sharedResults, expectedGenerationSmokeModules: scope.generationSmokeModules},
    scenarioReconciliation: 'exact', checkReconciliation: 'exact',
    assurance: {status: 'run-reported', results: 'run-reported', designReview: 'run-reported', requiredStepIds: 'signer-checked',
      subject: 'signer-checked', invariants: 'signer-checked',
      counts: {nativeTests: 'run-reported', generationSmokeModules: 'signer-checked', generationSmokeScenarios: 'signer-checked',
        galleryScenarios: 'signer-checked', qualityCases: 'signer-checked', sharedResults: 'signer-checked',
        scenarioBrowserTests: 'signer-checked', galleryChecks: 'signer-checked'},
      scenarioReconciliation: 'signer-checked', checkReconciliation: 'signer-checked'}}
}

export function buildGalleryPredicate({root, head, queue, producer, gate, shards, artifacts, artifactFiles, now = new Date()}) {
  const tree = receiptTree(root, head)
  const scope = receiptScope(root, head)
  requireThat(gate?.schemaVersion === 3 && gate.phase === 4 && gate.status === 'PASS'
    && gate.subject?.baseHead === head && gate.subject?.testedTree === tree.gitTree, 'phase-4 subject or status differs')
  const steps = receiptStepIds
  requireThat(isDeepStrictEqual(gate.requiredStepIds, steps) && gate.results?.length === steps.length
    && steps.every((id, i) => gate.results[i]?.id === id && gate.results[i]?.passed === true && !gate.results[i]?.reusedFrom), 'full fresh phase-4 steps required')
  requireThat(scopeMatchesGate(scope, gate) && gate.scenarioReconciliation === 'exact'
    && gate.checkReconciliation === 'exact' && gate.counts?.scenarioBrowserTests === scope.scenarios.length, 'phase-4 measured scope differs')
  requireThat(shards.every(shard => shard.provenance?.sourceHead === head
    && String(shard.provenance.runId) === String(producer.runId)), 'shard invocation differs')
  const reconciled = reconcileReceiptShards(shards, scope.scenarios, GALLERY_STEPS, scope.allowedSkippedIds)
  requireThat(reconcileChecks(reconciled.observation.checks, scope.expectedChecks) === 'exact'
    && isDeepStrictEqual(reconciled.observation.checks, gate.counts.galleryChecks), 'shard and phase-4 check counts differ')
  requireThat(OBJECT_ID.test(producer.revision) && Number.isSafeInteger(producer.runId)
    && Number.isSafeInteger(producer.attempt), 'producer identity missing')
  const waitMs = now.getTime() - Date.parse(producer.queueCompletedAt)
  requireThat(Number.isFinite(waitMs) && waitMs >= 0, 'queue completion time missing')
  return {
    buildDefinition: checked({buildType: RECEIPT_TYPE, externalParameters: {gitCommit: head, gitTree: tree.gitTree},
      resolvedDependencies: [{uri: `git+https://github.com/${RECEIPT_REPOSITORY}`, digest: {gitCommit: head}},
        {uri: SIGNER_URI, digest: {gitCommit: producer.revision}}]}),
    runDetails: checked({repository: RECEIPT_REPOSITORY, workflow: SIGNER_URI, revision: producer.revision,
      runId: producer.runId, attempt: producer.attempt, recorderVersion: 'gallery-run/v1', queue, waitMs,
      startedAt: producer.queueCompletedAt, finishedAt: now.toISOString()}),
    scope: checked(scope), artifacts: checked(artifacts), artifactFiles: checked(artifactFiles),
    reconciliation: checked({scenarios: reconciled.observation.scenarioBrowserTests, result: 'exact'}),
    shardResults: reported(shards.map(({playwrightReport, ...summary}) => summary)), phase4: reported(gate),
  }
}

function api(path, {timeout = 60000} = {}) {
  return JSON.parse(execFileSync('gh', ['api', path], {encoding: 'utf8', maxBuffer: 32 * 1024 * 1024,
    timeout, stdio: ['ignore', 'pipe', 'pipe']}))
}

export async function waitForReleaseRunPrerequisites({readApi = api, now = Date.now,
  wait = ms => new Promise(resolve => setTimeout(resolve, ms)), maxWaitMs = 600000, pollMs = 10000, ...expected}) {
  if (!Number.isFinite(maxWaitMs) || maxWaitMs <= 0 || maxWaitMs > 1800000
    || !Number.isFinite(pollMs) || pollMs <= 0) throw new Error('release prerequisites: invalid polling budget')
  const deadline = now() + maxWaitMs
  const boundedApi = path => {
    const remaining = deadline - now()
    if (remaining <= 0) throw new Error('release prerequisites: timeout')
    return readApi(path, {timeout: Math.min(60000, remaining)})
  }
  while (now() < deadline) {
    try {
      const result = releaseRunPrerequisites({...expected, readApi: boundedApi})
      if (now() < deadline) return result
    } catch {
      // Never turn an unavailable record or an incomplete attempt into an admission.
    }
    const remaining = deadline - now()
    if (remaining > 0) await wait(Math.min(pollMs, remaining))
  }
  throw new Error('release prerequisites: timeout')
}

// Run records are prerequisites only. The producer must still perform the trusted main-side
// computations; a green candidate job never substitutes for the fresh receipt or SPDX scan.
export function releaseRunPrerequisites({head, queueId, actionsAppId, readApi = api}) {
  const require = (condition, reason) => {
    if (!condition) throw new Error(`release prerequisites: ${reason}`)
  }
  const prefix = `repos/${RECEIPT_REPOSITORY}/`
  const read = suffix => {
    try { return readApi(prefix + suffix) } catch { throw new Error('release prerequisites: API unavailable') }
  }
  require(OBJECT_ID.test(head ?? '') && Number.isSafeInteger(queueId) && queueId > 0
    && Number.isSafeInteger(actionsAppId) && actionsAppId > 0, 'invalid expected identity')
  const checkRun = (run, id, workflow, names) => {
    require(run?.id === id && run.path === `.github/workflows/${workflow}.yml`
      && run.event === 'merge_group' && run.head_sha === head
      && run.repository?.full_name === RECEIPT_REPOSITORY
      && run.head_repository?.full_name === RECEIPT_REPOSITORY
      && run.status === 'completed' && run.conclusion === 'success'
      && Number.isSafeInteger(run.run_attempt) && run.run_attempt > 0
      && Number.isSafeInteger(run.check_suite_id) && run.check_suite_id > 0, 'run source or result differs')
    const attempt = read(`actions/runs/${id}/attempts/${run.run_attempt}`)
    require(['id', 'path', 'event', 'head_sha', 'status', 'conclusion', 'run_attempt', 'check_suite_id']
      .every(field => attempt?.[field] === run[field])
      && attempt.repository?.full_name === RECEIPT_REPOSITORY
      && attempt.head_repository?.full_name === RECEIPT_REPOSITORY, 'latest attempt differs')
    const listing = read(`actions/runs/${id}/attempts/${run.run_attempt}/jobs?per_page=100`)
    require(Array.isArray(listing?.jobs) && listing.total_count === listing.jobs.length
      && listing.total_count <= 100, 'incomplete job inventory')
    for (const name of names) {
      const matches = listing.jobs.filter(job => job.name === name)
      require(matches.length === 1, `missing or duplicate ${name}`)
      const job = matches[0]
      require(job.run_id === id && job.head_sha === head && job.status === 'completed'
        && job.conclusion === 'success', `unsuccessful or unrelated ${name}`)
      require(Number.isSafeInteger(job.id) && job.id > 0
        && job.check_run_url === `https://api.github.com/${prefix}check-runs/${job.id}`, `untrusted check URL for ${name}`)
      const check = read(`check-runs/${job.id}`)
      require(check?.id === job.id && check.name === name && check.head_sha === head
        && check.status === 'completed' && check.conclusion === 'success'
        && check.app?.id === actionsAppId && check.check_suite?.id === run.check_suite_id,
      `unknown check source or association for ${name}`)
    }
    return {id, attempt: run.run_attempt}
  }
  const queue = checkRun(read(`actions/runs/${queueId}`), queueId, 'verify',
    ['phase-4-gate', ...[1, 2, 3, 4, 5, 6].map(i => `gallery-shard (${i})`), 'gallery-collect', 'verify'])
  const listing = read(`actions/workflows/license-sbom.yml/runs?event=merge_group&head_sha=${head}&per_page=100`)
  require(Array.isArray(listing?.workflow_runs) && listing.total_count === listing.workflow_runs.length
    && listing.total_count > 0 && listing.total_count <= 100, 'missing or incomplete SBOM run inventory')
  require(listing.workflow_runs.every(run => Number.isSafeInteger(run.id) && run.id > 0), 'invalid SBOM run identity')
  const sbomId = Math.max(...listing.workflow_runs.map(run => run.id))
  const sbom = checkRun(read(`actions/runs/${sbomId}`), sbomId, 'license-sbom', ['sbom'])
  return {queue, sbom}
}

// Certificate fields here must come from gh's authenticated verification result, never the
// predicate. The policy revision is supplied by the separately selected external verifier.
export function verifyReleaseInvocation({root, approvedCommit, testedCommit, testedTree,
  acceptedSignerRevision, actionsAppId, queue, sbom, producer, certificate, readApi = api, allowActiveProducer = false}) {
  const require = (condition, reason) => {
    if (!condition) throw new Error(`release invocation: ${reason}`)
  }
  require([approvedCommit, testedCommit, testedTree, acceptedSignerRevision, producer?.revision]
    .every(value => OBJECT_ID.test(value ?? '')), 'missing full Git identity')
  require(testedCommit === approvedCommit, 'tested commit differs from release commit')
  let tree
  try { tree = git(root, 'rev-parse', `${approvedCommit}^{tree}`) } catch { throw new Error('release invocation: approved tree unavailable') }
  require(testedTree === tree, 'tested tree differs from approved tree')
  require(producer.revision === acceptedSignerRevision, 'producer revision differs from current policy')
  require(acceptedSignerRevision !== approvedCommit
    && succeeds(root, 'merge-base', '--is-ancestor', acceptedSignerRevision, approvedCommit),
  'producer revision is not a strict ancestor of release')
  require(Number.isSafeInteger(producer.id) && producer.id > 0
    && Number.isSafeInteger(producer.attempt) && producer.attempt > 0, 'invalid producer invocation')
  require(certificate?.buildSignerURI === SIGNER_URI && certificate.buildSignerDigest === acceptedSignerRevision,
    'certificate signer differs from current policy')
  require(certificate.sourceRepositoryURI === `https://github.com/${RECEIPT_REPOSITORY}`
    && certificate.sourceRepositoryRef === 'refs/heads/main'
    && certificate.sourceRepositoryDigest === acceptedSignerRevision, 'certificate source differs')
  require(certificate.buildTrigger === 'workflow_run' && certificate.runnerEnvironment === 'github-hosted',
    'certificate trigger or runner differs')
  require(certificate.runInvocationURI === `https://github.com/${RECEIPT_REPOSITORY}/actions/runs/${producer.id}/attempts/${producer.attempt}`,
    'certificate invocation differs')
  let prerequisites
  try { prerequisites = releaseRunPrerequisites({head: approvedCommit, queueId: queue?.id, actionsAppId, readApi}) }
  catch { throw new Error('release invocation: live queue or SBOM prerequisites refused') }
  require(isDeepStrictEqual(prerequisites.queue, queue) && isDeepStrictEqual(prerequisites.sbom, sbom),
    'signed queue or SBOM attempt is not latest')
  const read = suffix => {
    try { return readApi(`repos/${RECEIPT_REPOSITORY}/${suffix}`) }
    catch { throw new Error('release invocation: live producer API unavailable') }
  }
  const latest = read(`actions/runs/${producer.id}`)
  require(latest?.id === producer.id && latest.run_attempt === producer.attempt
    && latest.event === 'workflow_run' && latest.path === '.github/workflows/verify-gallery-receipt.yml'
    && latest.head_sha === acceptedSignerRevision && latest.repository?.full_name === RECEIPT_REPOSITORY
    && ((latest.status === 'completed' && latest.conclusion === 'success')
      || (allowActiveProducer && latest.status === 'in_progress' && latest.conclusion === null)), 'latest producer source or result differs')
  const attempt = read(`actions/runs/${producer.id}/attempts/${producer.attempt}`)
  require(['id', 'run_attempt', 'event', 'path', 'head_sha', 'status', 'conclusion']
    .every(field => attempt?.[field] === latest[field])
    && attempt.repository?.full_name === RECEIPT_REPOSITORY, 'producer attempt differs')
  return {...prerequisites, producer: {id: producer.id, attempt: producer.attempt}}
}

// Always fetch current repository configuration. A released file, artifact, predicate or dispatch
// input cannot supply this snapshot or choose the verifier/source pin.
// These outputs come only from the credential-isolated main reader in this run/attempt.
// They are bootstrap/planning inputs; they never establish the final live asset boundary.
export function readPolicySnapshot({envelope, digest, context}) {
  try {
    const snapshot = JSON.parse(Buffer.from(envelope ?? '', 'base64').toString('utf8'))
    if (snapshot.schemaVersion !== 1 || !/^[0-9a-f]{64}$/.test(digest ?? '')
      || !OBJECT_ID.test(context?.revision ?? '')
      || context.repository !== RECEIPT_REPOSITORY
      || !/^[1-9]\d*$/.test(String(context.runId ?? '')) || !/^[1-9]\d*$/.test(String(context.attempt ?? ''))
      || !['repository', 'runId', 'attempt', 'revision'].every(key => snapshot[key] === context[key])
      || snapshot.response?.name !== 'RELEASE_GATE_POLICY' || typeof snapshot.response.value !== 'string'
      || sha256(Buffer.from(snapshot.response.value, 'utf8')) !== digest) throw new Error('binding')
    return snapshot.response
  } catch { throw new Error('release policy snapshot: missing or invalid current-attempt reader output') }
}

function currentReleasePolicy(readApi) {
  let response, policy
  try {
    response = readApi === api ? readPolicySnapshot({
      envelope: process.env.RELEASE_POLICY_ENVELOPE, digest: process.env.RELEASE_POLICY_SHA256,
      context: {repository: process.env.GITHUB_REPOSITORY, runId: process.env.GITHUB_RUN_ID,
        attempt: process.env.GITHUB_RUN_ATTEMPT, revision: process.env.GITHUB_SHA},
    }) : readApi(`repos/${RECEIPT_REPOSITORY}/actions/variables/RELEASE_GATE_POLICY`)
    if (response?.name !== 'RELEASE_GATE_POLICY' || typeof response.value !== 'string') throw new Error('missing owner variable')
    policy = JSON.parse(response.value)
  } catch { throw new Error('release policy: current owner configuration unavailable or malformed') }
  const bytes = Buffer.from(response.value, 'utf8')
  return {policy, bytes, sha256: sha256(bytes)}
}

// Gallery-only runs need the same immutable API input, but do not become release evidence.
// In particular, a main push may test its own source revision; only release admission requires
// strict prior-main verifier/signer ancestry. No branch-tip fallback is available for API inputs.
export function loadTrustedApiInput({readApi = api} = {}) {
  const loaded = currentReleasePolicy(readApi)
  if (loaded.policy?.schemaVersion !== 1 || !OBJECT_ID.test(loaded.policy.apiRevision ?? ''))
    throw new Error('release policy: immutable API input required')
  return {apiRevision: loaded.policy.apiRevision, policySha256: loaded.sha256}
}

export function loadReleaseGatePolicy({root, approvedCommit, readApi = api}) {
  const require = (condition, reason) => {
    if (!condition) throw new Error(`release policy: ${reason}`)
  }
  const loaded = currentReleasePolicy(readApi), {policy} = loaded
  require(policy?.schemaVersion === 1 && OBJECT_ID.test(approvedCommit ?? ''), 'invalid policy schema or release identity')
  require([policy.verifierRevision, policy.signerRevision, policy.apiRevision].every(value => OBJECT_ID.test(value ?? '')),
    'full immutable verifier, signer and API revisions required')
  require(Array.isArray(policy.revokedSignerRevisions)
    && policy.revokedSignerRevisions.every(value => OBJECT_ID.test(value ?? ''))
    && new Set(policy.revokedSignerRevisions).size === policy.revokedSignerRevisions.length, 'revocation list required')
  require(!policy.revokedSignerRevisions.includes(policy.signerRevision), 'signer is revoked')
  for (const field of ['verifierRevision', 'signerRevision']) {
    require(policy[field] !== approvedCommit && succeeds(root, 'merge-base', '--is-ancestor', policy[field], approvedCommit),
      `${field} must be a resolvable strict ancestor of release`)
  }
  require([policy.actionsAppId, policy.galleryAppId, policy.reviewGateAppId]
    .every(value => Number.isSafeInteger(value) && value > 0), 'native App identities required')
  require(policy.galleryAppId !== policy.actionsAppId && policy.reviewGateAppId !== policy.actionsAppId,
    'dedicated check publishers cannot use the Actions identity')
  return loaded
}

if (import.meta.main && process.argv.includes('--release-policy')) {
  try {
    const [subject, approvedCommit, snapshot] = process.argv.slice(process.argv.indexOf('--release-policy') + 1)
    if (!subject || !approvedCommit || !snapshot) throw new Error('missing policy arguments')
    const root = resolve(subject), executableRoot = resolve(import.meta.dirname, '../..')
    const loaded = loadReleaseGatePolicy({root, approvedCommit})
    requireThat(git(executableRoot, 'rev-parse', 'HEAD') === loaded.policy.verifierRevision
      && git(executableRoot, 'status', '--porcelain') === '', 'external verifier checkout differs from current policy')
    const destination = resolve(snapshot)
    for (const directory of [root, executableRoot]) {
      const name = relative(directory, destination)
      requireThat(name === '..' || name.startsWith(`..${sep}`) || isAbsolute(name), 'policy snapshot must stay outside executable and released trees')
    }
    writeFileSync(destination, loaded.bytes)
    if (process.env.GITHUB_OUTPUT) writeFileSync(process.env.GITHUB_OUTPUT,
      `policy-sha256=${loaded.sha256}\nverifier-revision=${loaded.policy.verifierRevision}\n`, {flag: 'a'})
    process.stdout.write(`${JSON.stringify({policySha256: loaded.sha256, verifierRevision: loaded.policy.verifierRevision})}\n`)
  } catch {
    process.stderr.write('release policy refused: current configuration or trusted verifier binding could not be established\n')
    process.exitCode = 1
  }
}

function writeApi(method, path, body) {
  return JSON.parse(execFileSync('gh', ['api', '--method', method, path, '--input', '-'],
    {encoding: 'utf8', input: JSON.stringify(body), stdio: ['pipe', 'pipe', 'pipe']}))
}

// Only read claims returned by the cryptographic verifier, never decode the input bundle ourselves.
export function verifyGalleryReceipt({root, directory, head, queue, revision, execute = execFileSync, readApi = api, now = new Date(), requireStoredReceipt = true}) {
  objectId(revision)
  const tree = receiptTree(root, head)
  const listingPath = resolve(directory, 'tree-listing.bin')
  requireThat(readFileSync(listingPath).equals(tree.listing), 'tree listing differs')
  const verified = JSON.parse(execute('gh', ['attestation', 'verify', listingPath,
    '--bundle', resolve(directory, 'receipt.sigstore.json'), '--repo', RECEIPT_REPOSITORY,
    '--signer-workflow', SIGNER, '--signer-digest', revision, '--source-ref', 'refs/heads/main',
    '--source-digest', revision, '--cert-oidc-issuer', 'https://token.actions.githubusercontent.com',
    '--deny-self-hosted-runners', '--predicate-type', RECEIPT_TYPE, '--format', 'json'],
  {encoding: 'utf8', maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']}))
  requireThat(Array.isArray(verified) && verified.length === 1, 'expected one verified attestation')
  const result = verified[0].verificationResult
  const certificate = result?.signature?.certificate
  requireThat(certificate?.buildSignerURI === SIGNER_URI && certificate.buildSignerDigest === revision
    && certificate.buildTrigger === 'workflow_run', 'certificate signer or trigger differs')
  requireThat(result.verifiedTimestamps?.some(t => t.type === 'Tlog'), 'no verified transparency timestamp')
  const statement = result.statement
  requireThat(statement?._type === 'https://in-toto.io/Statement/v1' && statement.predicateType === RECEIPT_TYPE
    && statement.subject?.length === 1 && statement.subject[0].name === 'gallery-tree'
    && Object.keys(statement.subject[0].digest ?? {}).join() === 'sha256'
    && statement.subject[0].digest.sha256 === tree.sha256, 'statement subject differs')
  const predicate = statement.predicate
  requireThat(Object.keys(predicate ?? {}).sort().join() === 'artifactFiles,artifacts,buildDefinition,phase4,reconciliation,runDetails,scope,shardResults', 'unknown predicate fields')
  for (const [field, entry] of Object.entries(predicate)) requireThat(entry.assurance ===
    (['phase4', 'shardResults'].includes(field) ? 'run-reported' : 'signer-checked'), `unclassified ${field}`)
  const details = predicate.runDetails.value
  requireThat(details.workflow === SIGNER_URI && details.revision === revision, 'producer revision differs')
  const queueRun = queueIdentity(readApi(`repos/${RECEIPT_REPOSITORY}/actions/runs/${queue.id}/attempts/${queue.attempt}`), queue)
  requireThat(isDeepStrictEqual(details.queue, queueRun), 'recorded queue differs from REST API')
  const signerRun = readApi(`repos/${RECEIPT_REPOSITORY}/actions/runs/${details.runId}/attempts/${details.attempt}`)
  requireThat(signerRun.event === 'workflow_run' && signerRun.head_sha === revision
    && signerRun.path === '.github/workflows/verify-gallery-receipt.yml'
    && signerRun.repository?.full_name === RECEIPT_REPOSITORY, 'authenticated producer run differs')
  requireThat(certificate.runInvocationURI === `https://github.com/${RECEIPT_REPOSITORY}/actions/runs/${details.runId}/attempts/${details.attempt}`, 'certificate invocation differs')
  const age = now.getTime() - Date.parse(details.finishedAt)
  requireThat(Number.isFinite(age) && age >= 0 && age < 90 * 86400000, 'expired or future receipt')
  requireThat(Array.isArray(predicate.artifacts.value) && predicate.artifacts.value.length > 0, 'missing artifact identity')
  for (const artifact of predicate.artifacts.value) {
    const observed = readApi(`repos/${RECEIPT_REPOSITORY}/actions/artifacts/${artifact.id}`)
    requireThat(!observed.expired && observed.workflow_run?.id === details.runId
      && observed.name === artifact.name && observed.digest === artifact.digest, 'missing, expired or changed artifact')
  }
  if (requireStoredReceipt) {
    const retained = readApi(`repos/${RECEIPT_REPOSITORY}/actions/runs/${details.runId}/artifacts?per_page=100`).artifacts
      .filter(artifact => artifact.name === `gallery-receipt-${details.runId}-${details.attempt}`)
    requireThat(retained.length === 1 && !retained[0].expired, 'receipt artifact missing or expired')
  }
  const files = predicate.artifactFiles.value
  requireThat(Array.isArray(files) && files.length === predicate.shardResults.value.length + 1
    && new Set(files.map(file => file.name)).size === files.length, 'incomplete artifact file census')
  for (const file of files) {
    requireThat(file.name === 'phase4.json' || /^gallery-shard-\d+\.json$/.test(file.name), 'unexpected artifact file')
    const path = resolve(directory, file.name)
    requireThat(lstatSync(path).isFile() && sha256(readFileSync(path)) === file.sha256, 'artifact file digest differs')
  }
  const rawShards = files.filter(file => file.name !== 'phase4.json').map(file => jsonFile(resolve(directory, file.name)))
  requireThat(isDeepStrictEqual(jsonFile(resolve(directory, 'phase4.json')), predicate.phase4.value), 'phase4 artifact differs')
  const rebuilt = buildGalleryPredicate({root, head, queue: queueRun,
    producer: {revision, runId: details.runId, attempt: details.attempt, queueCompletedAt: details.startedAt},
    gate: predicate.phase4.value, shards: rawShards, artifacts: predicate.artifacts.value, artifactFiles: files,
    now: new Date(details.finishedAt)})
  requireThat(isDeepStrictEqual(rebuilt, predicate), 'signed claims do not reconcile against tree')
  return predicate.phase4.value
}


export const RELEASE_GATE_TYPE = 'https://harborline.software/attestations/release-gate/v1'
const releaseRequire = (value, reason) => { if (!value) throw new Error(`release evidence: ${reason}`) }
const RELEASE_FILES = ['phase4.json', 'release-phase4-receipt.json', 'release-receipt.json',
  ...[1, 2, 3, 4, 5, 6].map(id => `gallery-shard-${id}.json`),
  'sbom/sbom.spdx.json', 'sbom/sbom-evidence.json', 'sbom/license-policy.cjs', 'sbom/syft.yaml'].sort()
function releaseFileInventory(directory) {
  const names = []
  const walk = (where, prefix = '') => {
    for (const entry of readdirSync(where)) {
      const name = prefix + entry, path = resolve(where, entry), stat = lstatSync(path)
      releaseRequire(!stat.isSymbolicLink(), 'linked artifact entry')
      if (stat.isDirectory()) { releaseRequire(name === 'sbom', 'unexpected artifact directory'); walk(path, `${name}/`) }
      else {
        releaseRequire(stat.isFile(), 'non-regular artifact entry')
        if (!['release-inputs.json', 'release-predicate.json', 'release.sigstore.json'].includes(name)) names.push(name)
      }
    }
  }
  walk(directory)
  releaseRequire(isDeepStrictEqual(names.sort(), RELEASE_FILES), 'incomplete or extra release evidence files')
  return names.map(name => ({name, sha256: sha256(readFileSync(resolve(directory, name)))}))
}

export function sealReleaseGateEvidence({root, directory, head, policySha256, queue, sbom, producer,
  queueCompletedAt, now = new Date()}) {
  const files = releaseFileInventory(directory)
  const manifest = {schemaVersion: 1, subject: {commit: head, tree: git(root, 'rev-parse', `${objectId(head)}^{tree}`)},
    queue, sbom, producer, policySha256, queueCompletedAt, recordedAt: now.toISOString(), files}
  const bytes = Buffer.from(JSON.stringify(manifest))
  writeFileSync(resolve(directory, 'release-inputs.json'), bytes)
  return {inventorySha256: sha256(bytes), receiptSha256: files.find(file => file.name === 'release-phase4-receipt.json').sha256,
    companionSha256: files.find(file => file.name === 'release-receipt.json').sha256}
}

export function buildReleaseGatePredicate({root, directory, policy, policySha256, expected, artifact,
  readApi = api, now = new Date()}) {
  const files = releaseFileInventory(directory)
  const bytes = readFileSync(resolve(directory, 'release-inputs.json')), manifest = JSON.parse(bytes)
  releaseRequire(/^[0-9a-f]{64}$/.test(expected?.inventorySha256 ?? '') && sha256(bytes) === expected.inventorySha256,
    'producer inventory digest differs')
  releaseRequire(manifest.schemaVersion === 1 && isDeepStrictEqual(files, manifest.files), 'producer file inventory differs')
  const receiptBytes = readFileSync(resolve(directory, 'release-phase4-receipt.json'))
  const companionBytes = readFileSync(resolve(directory, 'release-receipt.json'))
  releaseRequire(sha256(receiptBytes) === expected.receiptSha256 && sha256(companionBytes) === expected.companionSha256,
    'exact receipt or companion bytes differ from producer outputs')
  const {subject, producer, queue, sbom} = manifest
  releaseRequire(subject?.commit === expected.head && producer?.id === expected.runId && producer?.attempt === expected.attempt,
    'producer invocation or tested commit differs')
  releaseRequire(OBJECT_ID.test(policy?.signerRevision ?? '') && producer.revision === policy.signerRevision
    && policy.signerRevision !== subject.commit && succeeds(root, 'merge-base', '--is-ancestor', policy.signerRevision, subject.commit)
    && !policy.revokedSignerRevisions?.includes(policy.signerRevision), 'source policy or strict ancestry differs')
  releaseRequire(manifest.policySha256 === policySha256, 'policy byte binding differs')
  releaseRequire(subject.tree === git(root, 'rev-parse', `${objectId(subject.commit)}^{tree}`), 'tested tree differs')
  const age = now.getTime() - Date.parse(manifest.recordedAt)
  releaseRequire(Number.isFinite(age) && age >= 0 && age < 90 * 86400000, 'expired or future evidence')
  releaseRequire(Number.isSafeInteger(artifact?.id) && artifact.id === expected.artifactId && !artifact.expired
    && artifact.workflow_run?.id === producer.id && /^sha256:[a-f0-9]{64}$/.test(artifact.digest ?? '')
    && artifact.name === `release-gate-inputs-${producer.id}-${producer.attempt}`, 'exact producer artifact differs')
  const prerequisites = releaseRunPrerequisites({head: subject.commit, queueId: queue?.id, actionsAppId: policy.actionsAppId, readApi})
  releaseRequire(isDeepStrictEqual(prerequisites, {queue, sbom}), 'latest prerequisite attempt differs')
  const receipt = JSON.parse(receiptBytes), gate = receipt.gate, companion = JSON.parse(companionBytes)
  releaseRequire(receipt.schemaVersion === 3 && receipt.phase === 4 && receipt.repository === 'harborline-platform'
    && receipt.baseHead === subject.commit && receipt.testedTree === subject.tree
    && gate?.subject?.mode === 'exact-staged-tree' && sha256(Buffer.from(JSON.stringify(gate))) === receipt.reportSha256,
    'exact phase-4 receipt differs')
  releaseRequire(receipt.orchestration?.revision === policy.signerRevision && receipt.orchestration.apiRevision === policy.apiRevision,
    'main orchestration or API input differs')
  releaseRequire(gate.results?.every(result => result.exitCode === 0), 'main step result failed')
  releaseRequire(isDeepStrictEqual(jsonFile(resolve(directory, 'phase4.json')), gate), 'raw gate report differs')
  releaseRequire(companion.commit === subject.commit && companion.tree === subject.tree
    && companion.transcript?.baseHead === subject.commit && companion.transcript.testedTree === subject.tree
    && companion.transcript.mode === 'exact-staged-tree', 'companion subject differs')
  // Corroboration comes from the byte-bound receipt above, not a local .git file in this data-only signer.
  releaseRequire(!checkReleaseReceipt(root, companionBytes, {requireAttestation: false}), 'companion seed/evidence contract refused')
  const sbomRecord = jsonFile(resolve(directory, 'sbom/sbom-evidence.json'))
  const freshSbomArtifact = readApi(`repos/${RECEIPT_REPOSITORY}/actions/artifacts/${expected.sbomArtifactId}`)
  releaseRequire(Number.isSafeInteger(expected.sbomArtifactId) && freshSbomArtifact?.id === expected.sbomArtifactId
    && !freshSbomArtifact.expired && freshSbomArtifact.workflow_run?.id === producer.id
    && freshSbomArtifact.name === `main-sbom-${producer.id}-${producer.attempt}`
    && /^sha256:[a-f0-9]{64}$/.test(freshSbomArtifact.digest ?? ''), 'fresh main SBOM artifact differs')
  releaseRequire(expected.sbomTestedTree === subject.tree && expected.sbomSha256 === sbomRecord.sbomSha256
    && expected.sbomPolicySha256 === sbomRecord.policySha256, 'fresh read-only SBOM job outputs differ')
  releaseRequire(sbomRecord.result === 'PASS' && sbomRecord.testedCommit === subject.commit && sbomRecord.testedTree === subject.tree
    && sbomRecord.producerRevision === producer.revision && String(sbomRecord.runId) === String(producer.id)
    && String(sbomRecord.attempt) === String(producer.attempt) && sbomRecord.scanner?.version === '1.57.2'
    && sbomRecord.scanner.action === 'e22c389904149dbc22b58101806040fa8d37a610', 'fresh main SBOM invocation differs')
  for (const [field, file] of [['sbomSha256', 'sbom.spdx.json'], ['policySha256', 'license-policy.cjs'], ['configSha256', 'syft.yaml']])
    releaseRequire(sbomRecord[field] === sha256(readFileSync(resolve(directory, 'sbom', file))), 'fresh SBOM bytes differ')
  const mainWorkflow = git(root, 'show', `${producer.revision}:.github/workflows/license-sbom.yml`).replaceAll('\r\n', '\n')
  const blocks = {
    'license-policy.cjs': mainWorkflow.match(/cat > "\$RUNNER_TEMP\/license-policy.cjs" <<'NODE'\n([\s\S]*?)\n          NODE/),
    'syft.yaml': mainWorkflow.match(/cat > "\$RUNNER_TEMP\/syft.yaml" <<'SYFT'\n([\s\S]*?)\n          SYFT/),
  }
  for (const [file, block] of Object.entries(blocks))
    releaseRequire(block && readFileSync(resolve(directory, 'sbom', file), 'utf8') === block[1].split('\n').map(line => line.slice(10)).join('\n') + '\n',
      'SBOM policy/config is not from trusted source')
  const report = jsonFile(resolve(directory, 'sbom/sbom.spdx.json'))
  releaseRequire(['SPDX-2.2', 'SPDX-2.3'].includes(report.spdxVersion) && report.packages?.length > 0, 'missing main SPDX packages')
  releaseRequire(!report.packages.some(pkg => /GPL-2\.0-only|GPL-3\.0|AGPL|SSPL|BUSL|CC-BY-NC/i.test(`${pkg.licenseDeclared} ${pkg.licenseConcluded}`)), 'main license policy refused')
  const shards = [1, 2, 3, 4, 5, 6].map(id => jsonFile(resolve(directory, `gallery-shard-${id}.json`)))
  const gallery = buildGalleryPredicate({root, head: subject.commit, queue: queueIdentity(readApi(`repos/${RECEIPT_REPOSITORY}/actions/runs/${queue.id}/attempts/${queue.attempt}`),
    {id: queue.id, attempt: queue.attempt, head: subject.commit}), producer: {revision: producer.revision, runId: producer.id,
      attempt: producer.attempt, queueCompletedAt: manifest.queueCompletedAt}, gate, shards,
    artifacts: [{id: artifact.id, name: artifact.name, digest: artifact.digest}], artifactFiles: files,
    now: new Date(manifest.recordedAt)})
  return {schemaVersion: 1, subject, producer, queue, sbom, policySha256, apiRevision: policy.apiRevision,
    freshSbom: {artifactId: freshSbomArtifact.id, artifactDigest: freshSbomArtifact.digest,
      reportSha256: expected.sbomSha256, policySha256: expected.sbomPolicySha256, testedTree: expected.sbomTestedTree},
    inventorySha256: expected.inventorySha256, receiptSha256: expected.receiptSha256, companionSha256: expected.companionSha256,
    artifact: {id: artifact.id, name: artifact.name, digest: artifact.digest}, recordedAt: manifest.recordedAt,
    files, gallery}
}

export function verifyReleaseGateEvidence({root, directory, approvedCommit, policy, policySha256,
  execute = execFileSync, readApi = api, now = new Date(), allowActiveProducer = false}) {
  releaseFileInventory(directory) // Reject links and unknown paths before passing any bytes to gh.
  const receipt = resolve(directory, 'release-phase4-receipt.json')
  const verified = JSON.parse(execute('gh', ['attestation', 'verify', receipt, '--bundle', resolve(directory, 'release.sigstore.json'),
    '--repo', RECEIPT_REPOSITORY, '--signer-workflow', SIGNER, '--signer-digest', policy.signerRevision,
    '--source-ref', 'refs/heads/main', '--source-digest', policy.signerRevision,
    '--cert-oidc-issuer', 'https://token.actions.githubusercontent.com', '--deny-self-hosted-runners',
    '--predicate-type', RELEASE_GATE_TYPE, '--format', 'json'], {encoding: 'utf8', maxBuffer: 128 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe']}))
  releaseRequire(Array.isArray(verified) && verified.length === 1, 'one verified release attestation required')
  const result = verified[0].verificationResult, statement = result?.statement, predicate = statement?.predicate
  releaseRequire(result?.verifiedTimestamps?.some(time => time.type === 'Tlog'), 'verified transparency timestamp required')
  releaseRequire(statement?._type === 'https://in-toto.io/Statement/v1' && statement.predicateType === RELEASE_GATE_TYPE
    && statement.subject?.length === 1 && statement.subject[0].name === 'phase4-receipt'
    && Object.keys(statement.subject[0].digest ?? {}).join() === 'sha256'
    && statement.subject[0].digest.sha256 === sha256(readFileSync(receipt)), 'release statement type or exact subject differs')
  releaseRequire(predicate?.subject?.commit === approvedCommit, 'signed commit differs from approved commit')
  const artifact = readApi(`repos/${RECEIPT_REPOSITORY}/actions/artifacts/${predicate.artifact?.id}`)
  releaseRequire(artifact.digest === predicate.artifact.digest, 'producer artifact digest changed')
  const rebuilt = buildReleaseGatePredicate({root, directory, policy, policySha256, artifact, readApi, now,
    expected: {head: approvedCommit, runId: predicate.producer?.id, attempt: predicate.producer?.attempt,
      artifactId: predicate.artifact?.id, inventorySha256: predicate.inventorySha256,
      receiptSha256: predicate.receiptSha256, companionSha256: predicate.companionSha256,
      sbomArtifactId: predicate.freshSbom?.artifactId, sbomSha256: predicate.freshSbom?.reportSha256,
      sbomPolicySha256: predicate.freshSbom?.policySha256, sbomTestedTree: predicate.freshSbom?.testedTree}})
  releaseRequire(isDeepStrictEqual(rebuilt, predicate), 'signed release claims differ from exact files')
  verifyReleaseInvocation({root, approvedCommit, testedCommit: predicate.subject.commit, testedTree: predicate.subject.tree,
    acceptedSignerRevision: policy.signerRevision, actionsAppId: policy.actionsAppId,
    queue: predicate.queue, sbom: predicate.sbom, producer: predicate.producer,
    certificate: result.signature?.certificate, readApi, allowActiveProducer})
  if (!allowActiveProducer) {
    const retained = readApi(`repos/${RECEIPT_REPOSITORY}/actions/runs/${predicate.producer.id}/artifacts?per_page=100`)
    releaseRequire(Array.isArray(retained?.artifacts) && retained.total_count === retained.artifacts.length
      && retained.artifacts.filter(item => item.name === `release-gate-receipt-${predicate.producer.id}-${predicate.producer.attempt}` && !item.expired).length === 1,
      'signed release evidence is no longer retained')
  }
  return predicate
}

// workflow_run is immutable runner-provided context. Do not accept a queue head selected by an
// artifact, PR input, or candidate code. Every privileged entry point repeats the REST binding.
function workflowContext(root) {
  const event = jsonFile(process.env.GITHUB_EVENT_PATH)
  requireThat(process.env.GITHUB_EVENT_NAME === 'workflow_run' && process.env.GITHUB_REF === 'refs/heads/main'
    && process.env.GITHUB_REPOSITORY === RECEIPT_REPOSITORY, 'trusted main workflow_run required')
  const run = event.workflow_run
  const expected = {id: run?.id, attempt: run?.run_attempt, head: run?.head_sha}
  objectId(expected.head)
  const queue = queueIdentity(api(`repos/${RECEIPT_REPOSITORY}/actions/runs/${expected.id}/attempts/${expected.attempt}`), expected)
  const payloadQueue = queueIdentity(run, expected)
  requireThat(isDeepStrictEqual(queue, payloadQueue), 'event and REST queue metadata differ')
  const revision = objectId(process.env.GITHUB_SHA)
  requireThat(git(root, 'rev-parse', 'HEAD') === revision, 'trusted code checkout differs from workflow revision')
  return {event, run, expected, queue, revision}
}

export function receiptCheckPayload({head, conclusion, runId, artifactId, waitMs, scopeChanged}) {
  objectId(head)
  requireThat(['success', 'failure'].includes(conclusion), 'invalid check conclusion')
  return {name: 'gallery-receipt', head_sha: head, status: 'completed', conclusion,
    details_url: `https://github.com/${RECEIPT_REPOSITORY}/actions/runs/${runId}`,
    output: {title: conclusion === 'success' ? 'Gallery identity and binding verified' : 'Gallery receipt refused',
      summary: `Scope changed: ${scopeChanged}. Run: ${runId}. Evidence artifact: ${artifactId ?? 'unavailable'}. Queue wait: ${waitMs ?? 'unavailable'} ms. Results are run-reported; this does not prove test honesty.`}}
}

export function refreshFailureIssue({head, runId, readApi, writeApi}) {
  objectId(head)
  const label = 'phase-4-rerecord-failure'
  const issues = readApi(`repos/${RECEIPT_REPOSITORY}/issues?state=open&labels=${label}&per_page=100`).filter(row => !row.pull_request)
  requireThat(issues.length <= 1, 'duplicate open refresh failure issues')
  const body = `Post-landing phase-4 refresh failed for ${head}.\n\nRun: https://github.com/${RECEIPT_REPOSITORY}/actions/runs/${runId}\n\nThis fix goes ahead of new slices. The gallery receipt gate remains fail-closed.\n\n---\n_Generated by [Claude Code](https://claude.ai/code)_`
  if (issues.length) writeApi('PATCH', `repos/${RECEIPT_REPOSITORY}/issues/${issues[0].number}`, {body})
  else {
    const labels = readApi(`repos/${RECEIPT_REPOSITORY}/labels?per_page=100`)
    if (!labels.some(row => row.name === label)) writeApi('POST', `repos/${RECEIPT_REPOSITORY}/labels`, {name: label, color: 'b60205'})
    writeApi('POST', `repos/${RECEIPT_REPOSITORY}/issues`, {title: 'Post-landing phase-4 refresh failed', body, labels: [label]})
  }
}

if (import.meta.main && process.argv.includes('--receipt')) {
  try {
    const mode = process.argv[process.argv.indexOf('--receipt') + 1]
    const root = resolve('.')
    // Sanitized external ledger authority: only the two fields the existing ledger reads.
    // No private source, credentials, full research prose, or .git objects leave this trusted job.
    if (mode === 'ledger-data') {
      requireThat(git(root, 'rev-parse', 'HEAD') === objectId(process.env.GITHUB_SHA),
        'ledger checkout differs from trusted workflow revision')
      const source = resolve(process.argv.at(-2)), destination = resolve(process.argv.at(-1))
      for (const [directory, field] of [['research', 'reach-platform'], ['tickets', 'status']]) {
        for (const entry of readdirSync(resolve(source, directory), {withFileTypes: true})) {
          if (!entry.isDirectory() || !/^[RT]-\d+-[a-z0-9-]+$/.test(entry.name)) continue
          const file = directory === 'research' ? 'note.md' : 'ticket.md'
          let text
          try { text = readFileSync(resolve(source, directory, entry.name, file), 'utf8') } catch { continue }
          const value = new RegExp(`^${field}:\\s*(.*)$`, 'm').exec(text)?.[1]
            ?? (directory === 'research' ? /^reach:\s*(.*)$/m.exec(text)?.[1] : undefined)
          if (!value || !/^[A-Za-z0-9 ._:/-]{1,300}$/.test(value)) continue
          const output = resolve(destination, directory, entry.name)
          mkdirSync(output, {recursive: true})
          writeFileSync(resolve(output, file), `${field}: ${value}\n`)
        }
      }
    } else if (mode === 'collect') {
      let head, revision, queueCompletedAt
      if (process.env.GITHUB_EVENT_NAME === 'push') {
        const event = jsonFile(process.env.GITHUB_EVENT_PATH)
        revision = head = objectId(process.env.GITHUB_SHA)
        requireThat(process.env.GITHUB_REF === 'refs/heads/main' && process.env.GITHUB_REPOSITORY === RECEIPT_REPOSITORY
          && event.after === head && git(root, 'rev-parse', 'HEAD') === head, 'collector requires landed main')
        queueCompletedAt = event.head_commit?.timestamp
      } else {
        const context = workflowContext(root)
        head = context.expected.head; revision = context.revision; queueCompletedAt = context.run.updated_at
      }
      const directory = resolve(process.env.RECEIPT_DIRECTORY ?? 'receipt')
      requireThat(git(root, 'status', '--porcelain') === '', 'trusted data checkout is dirty')
      const producer = {revision, runId: Number(process.env.GITHUB_RUN_ID), attempt: Number(process.env.GITHUB_RUN_ATTEMPT),
        queueCompletedAt}
      const rawDirectory = resolve(process.env.RUNNER_TEMP, 'raw-observations')
      const inputs = downloadObservations({directory: rawDirectory, producer})
      const gate = collectTrustedGateData({root, head: head, producer,
        gate: jsonFile(resolve(rawDirectory, 'phase4.json')),
        shards: [1,2,3,4,5,6].map(id => jsonFile(resolve(rawDirectory, `gallery-shard-${id}.json`)))})
      mkdirSync(directory, {recursive: true})
      for (const input of inputs.filter(input => input.file !== 'phase4.json'))
        writeFileSync(resolve(directory, input.file), readFileSync(resolve(rawDirectory, input.file)))
      writeFileSync(resolve(directory, 'phase4.json'), JSON.stringify(gate))
      if (process.env.GITHUB_EVENT_NAME === 'workflow_run' && process.env.RELEASE_APPROVED_COMMIT === head) {
        const loaded = loadReleaseGatePolicy({root, approvedCommit: head})
        const receipt = {schemaVersion: 3, phase: 4, repository: 'harborline-platform',
          baseHead: head, testedTree: receiptTree(root, head).gitTree,
          recordedAt: new Date().toISOString(), reportSha256: sha256(Buffer.from(JSON.stringify(gate))), gate,
          orchestration: {revision, apiRevision: loaded.policy.apiRevision}, observations: inputs}
        writeFileSync(resolve(directory, 'release-phase4-receipt.json'), JSON.stringify(receipt))
        const companion = emitReleaseReceipt(root, {baseHead: head, testedTree: receipt.testedTree, mode: 'exact-staged-tree'})
        requireThat(!checkReleaseReceipt(root, companion, {requireAttestation: false}), 'companion data refused')
        writeFileSync(resolve(directory, 'release-receipt.json'), companion)
      }
    } else if (['plan-refresh', 'publish-refresh', 'refresh-alert'].includes(mode)) {
      requireThat(process.env.GITHUB_EVENT_NAME === 'push' && process.env.GITHUB_REF === 'refs/heads/main'
        && process.env.GITHUB_REPOSITORY === RECEIPT_REPOSITORY, 'refresh requires a main push')
      const event = jsonFile(process.env.GITHUB_EVENT_PATH)
      const head = objectId(process.env.GITHUB_SHA)
      requireThat(head === event.after && git(root, 'rev-parse', 'HEAD') === head, 'refresh checkout differs from landed main')
      if (mode === 'refresh-alert') {
        // Alerting must survive the same broken evidence that refused planning or publication.
        // The main-push and checkout identity checks above still apply.
        refreshFailureIssue({head, runId: process.env.GITHUB_RUN_ID, readApi: api, writeApi})
      } else {
        const durable = jsonFile(resolve(root, EVIDENCE_PATH))
        const refusal = checkEvidenceProvenance(root, JSON.stringify(durable))
        requireThat(!refusal, 'durable main evidence failed provenance')
        const changed = shouldRerecord(root, event.before, head, durable)
        if (mode === 'plan-refresh') {
          const inputs = changed ? loadTrustedApiInput() : {apiRevision: '', policySha256: ''}
          writeFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\nhead=${head}\nrevision=${head}\napi-revision=${inputs.apiRevision}\npolicy-sha256=${inputs.policySha256}\n`, {flag: 'a'})
        } else if (mode === 'publish-refresh') {
          requireThat(changed, 'record-only or unchanged-scope landing cannot start a refresh')
          const directory = resolve(process.env.RECEIPT_DIRECTORY ?? 'receipt')
          const report = jsonFile(resolve(directory, 'phase4.json'))
          const shards = readdirSync(directory).filter(name => /^gallery-shard-\d+\.json$/.test(name)).sort()
            .map(name => jsonFile(resolve(directory, name)))
          // Reuse the same fresh full-run/scope/raw-shard validation, without producing a queue receipt.
          buildGalleryPredicate({root, head, queue: null, producer: {revision: head,
            runId: Number(process.env.GITHUB_RUN_ID), attempt: Number(process.env.GITHUB_RUN_ATTEMPT),
            queueCompletedAt: event.head_commit.timestamp}, gate: report, shards, artifacts: [], artifactFiles: []})
          refreshPublication({root, head, report, runId: process.env.GITHUB_RUN_ID, writeApi})
        }
      }
    } else {
      const context = workflowContext(root)
      const {expected, queue, revision, run} = context
      const directory = resolve(process.env.RECEIPT_DIRECTORY ?? 'receipt')
      if (mode === 'plan') {
        const durable = jsonFile(resolve(root, EVIDENCE_PATH))
        const refusal = checkEvidenceProvenance(root, JSON.stringify(durable))
        requireThat(!refusal, 'durable main evidence failed provenance')
        const changed = !!needsGalleryReceipt(root, expected.head, durable)
        const releaseEligible = OBJECT_ID.test(process.env.RELEASE_APPROVED_COMMIT ?? '')
          && process.env.RELEASE_APPROVED_COMMIT === expected.head
        let inputs = changed || releaseEligible ? loadTrustedApiInput() : {apiRevision: '', policySha256: ''}
        if (releaseEligible) {
          const {policy, sha256: policySha256} = loadReleaseGatePolicy({root, approvedCommit: expected.head})
          inputs = {apiRevision: policy.apiRevision, policySha256}
          requireThat(policy.signerRevision === revision, 'producer does not match current release signer policy')
          await waitForReleaseRunPrerequisites({head: expected.head, queueId: expected.id, actionsAppId: policy.actionsAppId})
        }
        writeFileSync(process.env.GITHUB_OUTPUT,
          `changed=${changed}\nhead=${expected.head}\nrevision=${revision}\nrelease-eligible=${releaseEligible}\napi-revision=${inputs.apiRevision}\npolicy-sha256=${inputs.policySha256}\n`, {flag: 'a'})
      } else if (mode === 'build') {
        mkdirSync(directory, {recursive: true})
        const names = readdirSync(directory).filter(name => name === 'phase4.json' || /^gallery-shard-\d+\.json$/.test(name)).sort()
        const artifactFiles = names.map(name => {
          const path = resolve(directory, name)
          requireThat(lstatSync(path).isFile(), 'non-regular artifact file')
          return {name, sha256: sha256(readFileSync(path))}
        })
        const gate = jsonFile(resolve(directory, 'phase4.json'))
        const shards = readdirSync(directory).filter(name => /^gallery-shard-\d+\.json$/.test(name)).sort()
          .map(name => { const path = resolve(directory, name); requireThat(lstatSync(path).isFile(), 'not a regular shard file'); return jsonFile(path) })
        const all = api(`repos/${RECEIPT_REPOSITORY}/actions/runs/${process.env.GITHUB_RUN_ID}/artifacts?per_page=100`).artifacts
        const artifact = all.filter(row => row.name === `gallery-run-${process.env.GITHUB_RUN_ID}-${process.env.GITHUB_RUN_ATTEMPT}`)
        requireThat(artifact.length === 1 && !artifact[0].expired && /^sha256:[a-f0-9]{64}$/.test(artifact[0].digest), 'run artifact unavailable')
        const artifacts = artifact.map(({id, name, digest}) => ({id, name, digest}))
        const predicate = buildGalleryPredicate({root, head: expected.head, queue,
          producer: {revision, runId: Number(process.env.GITHUB_RUN_ID), attempt: Number(process.env.GITHUB_RUN_ATTEMPT),
            queueCompletedAt: run.updated_at}, gate, shards, artifacts, artifactFiles})
        const tree = receiptTree(root, expected.head)
        writeFileSync(resolve(directory, 'tree-listing.bin'), tree.listing)
        writeFileSync(resolve(directory, 'predicate.json'), JSON.stringify(predicate))
        writeFileSync(process.env.GITHUB_OUTPUT, `digest=sha256:${tree.sha256}\n`, {flag: 'a'})
      } else if (mode === 'verify') {
        verifyGalleryReceipt({root, directory, head: expected.head, queue: expected, revision, requireStoredReceipt: false})
      } else if (mode === 'post') {
        let scopeChanged = null
        let passed = false
        let exceeded = false
        const waitMs = Date.now() - Date.parse(run.updated_at)
        try {
          const durable = jsonFile(resolve(root, EVIDENCE_PATH))
          requireThat(!checkEvidenceProvenance(root, JSON.stringify(durable)), 'durable evidence failed provenance')
          scopeChanged = needsGalleryReceipt(root, expected.head, durable)
          passed = process.env.RECEIPT_VERIFIED === 'true' || !scopeChanged
          // Never let a caller's success output substitute for actual cryptographic verification.
          if (passed && scopeChanged) verifyGalleryReceipt({root, directory, head: expected.head, queue: expected, revision})
          const timeout = api(`repos/${RECEIPT_REPOSITORY}/rulesets/22915054`).rules
            .find(rule => rule.type === 'merge_queue')?.parameters?.check_response_timeout_minutes
          requireThat(Number.isFinite(timeout) && timeout > 0, 'merge queue timeout unavailable')
          exceeded = scopeChanged && waitMs > timeout * 60000
          if (exceeded) passed = false
        } catch {
          // Identity was established by workflowContext. Refuse with a check on that queue head;
          // never expose child errors, which can include credential-bearing command input.
          passed = false
        }
        if (exceeded) {
          process.stdout.write('::error::Gallery queue wait exceeded the merge queue status-check timeout; owner action required before landing.\n')
        }
        const artifactId = process.env.RECEIPT_ARTIFACT_ID
        const payload = receiptCheckPayload({head: expected.head, conclusion: passed ? 'success' : 'failure',
          runId: process.env.GITHUB_RUN_ID, artifactId, waitMs, scopeChanged})
        if (exceeded) payload.output.summary += ' Owner action required: queue status-check timeout exceeded.'
        const payloadPath = resolve(directory, 'check.json')
        mkdirSync(directory, {recursive: true})
        writeFileSync(payloadPath, JSON.stringify(payload))
        execFileSync('gh', ['api', '--method', 'POST', `repos/${RECEIPT_REPOSITORY}/check-runs`, '--input', payloadPath],
          {stdio: ['ignore', 'ignore', 'pipe'], env: {...process.env, GH_TOKEN: process.env.RECEIPT_CHECK_TOKEN}})
        writeFileSync(process.env.GITHUB_STEP_SUMMARY, `${payload.output.summary}\n`, {flag: 'a'})
        if (!passed) process.exitCode = 1
      } else throw new Error('unknown receipt mode')
    }
  } catch {
    // Do not print child command errors: Git or HTTP clients can include credential-bearing input.
    process.stderr.write('gallery receipt refused: identity, data, signature or reconciliation could not be established\n')
    process.exitCode = 1
  }
}

// ADR-0140: refresh publication is a data-only, fast-forward update of a dedicated branch.
export function shouldRerecord(root, before, head, durable) {
  objectId(before); objectId(head)
  const paths = gitBytes(root, 'diff', '--name-only', '-z', before, head).toString('utf8').split('\0').filter(Boolean)
  return paths.some(path => path !== EVIDENCE_PATH) && needsGalleryReceipt(root, head, durable)
}

export function refreshPrDescription(head, runId) {
  objectId(head)
  return `Refresh the durable full phase-4 record for landed main commit ${head}.\n\n`
    + `Run: https://github.com/${RECEIPT_REPOSITORY}/actions/runs/${runId}\n\n`
    + 'This PR changes only docs/evidence/phase-4/gate.json. Chris queues it; no automation queues or auto-merges it.\n\n'
    + '🤖 Generated with [Claude Code](https://claude.com/claude-code).'
}

export function refreshPublication({root, head, report, runId, execute = execFileSync, readApi = api, writeApi,
  token = process.env.GH_TOKEN}) {
  const command = (...args) => execute('git', args, {cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
  const branch = 'evidence/phase-4-rerecord'
  requireThat(report?.subject?.baseHead === head && report.subject.testedTree === command('rev-parse', `${objectId(head)}^{tree}`)
    && report.status === 'PASS', 'refresh report does not name landed main')
  command('fetch', '--no-tags', 'origin', 'refs/heads/main:refs/remotes/origin/main')
  requireThat(command('rev-parse', 'origin/main') === head, 'main advanced; refuse stale refresh publication')
  const refs = command('ls-remote', '--heads', 'origin', `refs/heads/${branch}`)
  let previous
  if (refs) {
    previous = refs.split(/\s/)[0]
    objectId(previous)
    command('fetch', '--no-tags', 'origin', `refs/heads/${branch}`)
  }
  const temporary = resolve(root, `.gallery-rerecord-index-${process.pid}`)
  const env = {...process.env, GIT_INDEX_FILE: temporary,
    GIT_AUTHOR_NAME: 'Harborline Gallery Rerecord', GIT_AUTHOR_EMAIL: 'gallery-rerecord@users.noreply.github.com',
    GIT_COMMITTER_NAME: 'Harborline Gallery Rerecord', GIT_COMMITTER_EMAIL: 'gallery-rerecord@users.noreply.github.com'}
  const indexCommand = (args, input) => execute('git', args, {cwd: root, encoding: 'utf8', input, env,
    stdio: ['pipe', 'pipe', 'pipe']}).trim()
  try {
    indexCommand(['read-tree', head])
    const blob = indexCommand(['hash-object', '-w', '--stdin'], `${JSON.stringify(report, null, 2)}\n`)
    indexCommand(['update-index', '--add', '--cacheinfo', '100644', blob, EVIDENCE_PATH])
    const tree = indexCommand(['write-tree'])
    const parents = previous && previous !== head ? ['-p', previous, '-p', head] : ['-p', head]
    const title = `Phase-4 durable refresh for landed ${head}`
    const commit = indexCommand(['commit-tree', tree, ...parents], `${title}\n\nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>\n`)
    const changed = command('diff-tree', '--no-commit-id', '--name-only', '-r', head, commit).split(/\r?\n/).filter(Boolean)
    requireThat(changed.length === 1 && changed[0] === EVIDENCE_PATH, 'refresh would change more than gate.json')
    // Token stays in the child environment, never a URL, argument, file, stdout or error message.
    const pushEnv = {...env, GIT_CONFIG_COUNT: '1', GIT_CONFIG_KEY_0: 'http.https://github.com/.extraheader',
      GIT_CONFIG_VALUE_0: `AUTHORIZATION: basic ${Buffer.from(`x-access-token:${token}`).toString('base64')}`}
    execute('git', ['push', 'origin', `${commit}:refs/heads/${branch}`],
      {cwd: root, env: pushEnv, stdio: ['ignore', 'pipe', 'pipe']})
    const existing = readApi(`repos/${RECEIPT_REPOSITORY}/pulls?state=open&head=Harborline-Software%3Aevidence%2Fphase-4-rerecord&base=main&per_page=100`)
    requireThat(existing.length <= 1, 'duplicate open refresh PRs')
    const body = {title, body: refreshPrDescription(head, runId)}
    return existing.length ? writeApi('PATCH', `repos/${RECEIPT_REPOSITORY}/pulls/${existing[0].number}`, body)
      : writeApi('POST', `repos/${RECEIPT_REPOSITORY}/pulls`, {...body, head: branch, base: 'main', draft: true})
  } finally { rmSync(temporary, {force: true}); rmSync(`${temporary}.lock`, {force: true}) }
}


if (import.meta.main && process.argv.includes('--release-evidence')) {
  try {
    const offset = process.argv.indexOf('--release-evidence')
    const [mode, directoryArg, approvedCommit] = process.argv.slice(offset + 1)
    const root = resolve(process.env.GITHUB_WORKSPACE ?? '.'), directory = resolve(directoryArg ?? '.')
    const loaded = loadReleaseGatePolicy({root, approvedCommit})
    const sourceRoot = resolve(import.meta.dirname, '../..')
    const output = values => {
      if (process.env.GITHUB_OUTPUT) for (const [key, value] of Object.entries(values))
        writeFileSync(process.env.GITHUB_OUTPUT, `${key}=${value}\n`, {flag: 'a'})
    }
    if (['seal', 'build', 'verify-produced'].includes(mode)) {
      const context = workflowContext(sourceRoot)
      releaseRequire(context.expected.head === approvedCommit && loaded.policy.signerRevision === context.revision
        && process.env.RELEASE_APPROVED_COMMIT === approvedCommit, 'not the designated exact trusted producer')
      const producer = {id: Number(process.env.GITHUB_RUN_ID), attempt: Number(process.env.GITHUB_RUN_ATTEMPT), revision: context.revision}
      const prerequisites = releaseRunPrerequisites({head: approvedCommit, queueId: context.expected.id, actionsAppId: loaded.policy.actionsAppId})
      if (mode === 'seal') {
        const sealed = sealReleaseGateEvidence({root, directory, head: approvedCommit, policySha256: loaded.sha256,
          ...prerequisites, producer, queueCompletedAt: context.run.updated_at})
        output({'inventory-sha256': sealed.inventorySha256, 'receipt-sha256': sealed.receiptSha256, 'companion-sha256': sealed.companionSha256})
      } else if (mode === 'build') {
        const artifactId = Number(process.env.RELEASE_INPUT_ARTIFACT_ID)
        const predicate = buildReleaseGatePredicate({root, directory, policy: loaded.policy, policySha256: loaded.sha256,
          artifact: api(`repos/${RECEIPT_REPOSITORY}/actions/artifacts/${artifactId}`),
          expected: {head: approvedCommit, runId: producer.id, attempt: producer.attempt, artifactId,
            inventorySha256: process.env.RELEASE_INPUT_INVENTORY_SHA256, receiptSha256: process.env.RELEASE_INPUT_RECEIPT_SHA256,
            companionSha256: process.env.RELEASE_INPUT_COMPANION_SHA256,
            sbomArtifactId: Number(process.env.RELEASE_SBOM_ARTIFACT_ID), sbomSha256: process.env.RELEASE_SBOM_SHA256,
            sbomPolicySha256: process.env.RELEASE_SBOM_POLICY_SHA256, sbomTestedTree: process.env.RELEASE_SBOM_TESTED_TREE}})
        writeFileSync(resolve(directory, 'release-predicate.json'), JSON.stringify(predicate))
        output({digest: `sha256:${predicate.receiptSha256}`})
      } else {
        const verified = verifyReleaseGateEvidence({root, directory, approvedCommit, policy: loaded.policy, policySha256: loaded.sha256, allowActiveProducer: true})
        releaseRequire(isDeepStrictEqual(verified.producer, producer), 'signed producer differs from current invocation')
      }
    } else {
      releaseRequire(git(sourceRoot, 'rev-parse', 'HEAD') === loaded.policy.verifierRevision
        && git(sourceRoot, 'status', '--porcelain') === '', 'external verifier differs from current policy')
      if (mode === 'locate') {
        const runs = api(`repos/${RECEIPT_REPOSITORY}/actions/workflows/verify-gallery-receipt.yml/runs?event=workflow_run&head_sha=${loaded.policy.signerRevision}&per_page=100`)
        releaseRequire(Array.isArray(runs.workflow_runs) && runs.total_count === runs.workflow_runs.length && runs.total_count <= 100,
          'producer run census unavailable or truncated')
        let selected
        for (const run of [...runs.workflow_runs].sort((a, b) => b.id - a.id)) {
          if (run.status !== 'completed' || run.conclusion !== 'success' || run.head_sha !== loaded.policy.signerRevision
            || run.event !== 'workflow_run' || run.path !== '.github/workflows/verify-gallery-receipt.yml'
            || run.repository?.full_name !== RECEIPT_REPOSITORY) continue
          const artifacts = api(`repos/${RECEIPT_REPOSITORY}/actions/runs/${run.id}/artifacts?per_page=100`)
          releaseRequire(Array.isArray(artifacts.artifacts) && artifacts.total_count === artifacts.artifacts.length && artifacts.total_count <= 100,
            'producer artifact census unavailable or truncated')
          const match = artifacts.artifacts.filter(item => item.name === `release-gate-receipt-${run.id}-${run.run_attempt}` && !item.expired)
          releaseRequire(match.length <= 1, 'ambiguous signed release artifact')
          if (match.length) { selected = {run, artifact: match[0]}; break }
        }
        releaseRequire(selected && Number.isSafeInteger(selected.artifact.id) && /^sha256:[a-f0-9]{64}$/.test(selected.artifact.digest ?? ''), 'no retained release gate evidence')
        output({'artifact-id': selected.artifact.id, 'producer-run': selected.run.id, 'producer-attempt': selected.run.run_attempt})
      } else if (mode === 'verify') {
        const verified = verifyReleaseGateEvidence({root, directory, approvedCommit, policy: loaded.policy, policySha256: loaded.sha256})
        const selected = api(`repos/${RECEIPT_REPOSITORY}/actions/artifacts/${Number(process.env.RELEASE_SELECTED_ARTIFACT_ID)}`)
        releaseRequire(!selected.expired && selected.workflow_run?.id === verified.producer.id
          && selected.name === `release-gate-receipt-${verified.producer.id}-${verified.producer.attempt}`
          && String(verified.producer.id) === process.env.RELEASE_SELECTED_RUN_ID
          && String(verified.producer.attempt) === process.env.RELEASE_SELECTED_ATTEMPT, 'downloaded signed artifact invocation differs')
        releaseRequire(git(root, 'rev-parse', 'HEAD') === approvedCommit && git(root, 'write-tree') === verified.subject.tree,
          'approved checkout changed before verified receipt installation')
        const gitDir = resolve(root, git(root, 'rev-parse', '--git-dir'))
        writeFileSync(resolve(gitDir, 'harborline-phase4-receipt.json'), readFileSync(resolve(directory, 'release-phase4-receipt.json')))
        writeFileSync(resolve(gitDir, 'harborline-release-receipt.json'), readFileSync(resolve(directory, 'release-receipt.json')))
        process.stdout.write(JSON.stringify({status: 'PASS', subject: verified.subject, producer: verified.producer}) + '\n')
      } else throw new Error('unknown release evidence mode')
    }
  } catch {
    process.stderr.write('release evidence refused: current policy, live invocation, signature or exact bytes could not be established\n')
    process.exitCode = 1
  }
}
