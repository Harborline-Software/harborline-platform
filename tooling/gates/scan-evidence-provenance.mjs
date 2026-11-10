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
import {resolve} from 'node:path'

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

if (import.meta.main && !process.argv.includes('--receipt')) {
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

export function buildGalleryPredicate({root, head, queue, producer, gate, shards, artifacts, artifactFiles, now = new Date()}) {
  const tree = receiptTree(root, head)
  const scope = receiptScope(root, head)
  requireThat(gate?.schemaVersion === 3 && gate.phase === 4 && gate.status === 'PASS'
    && gate.subject?.baseHead === head && gate.subject?.testedTree === tree.gitTree, 'phase-4 subject or status differs')
  const steps = requiredStepIds.includes('gallery-gate') ? requiredStepIds : [...requiredStepIds.slice(0, -2), 'gallery-gate', ...requiredStepIds.slice(-2)]
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

function api(path) {
  return JSON.parse(execFileSync('gh', ['api', path], {encoding: 'utf8', maxBuffer: 32 * 1024 * 1024,
    stdio: ['ignore', 'pipe', 'pipe']}))
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
          writeFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\nhead=${head}\nrevision=${head}\n`, {flag: 'a'})
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
        writeFileSync(process.env.GITHUB_OUTPUT, `changed=${changed}\nhead=${expected.head}\nrevision=${revision}\n`, {flag: 'a'})
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
