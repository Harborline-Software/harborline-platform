// Oracles: supplied ADR-0128/0134/0137/0139 and in-toto Statement v1. Fixtures below are
// independently authored protocol documents. Production builds and verifies the statement.
// The CLI boundary double does not claim to test Sigstore cryptography or hosted environment rules.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync, spawnSync} from 'node:child_process'
import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve, dirname} from 'node:path'
import {createHash} from 'node:crypto'
import {runInNewContext} from 'node:vm'
import {pathToFileURL} from 'node:url'
import {buildGalleryPredicate, verifyGalleryReceipt, receiptTree, receiptScope, queueIdentity,
  scopeMatchesGate, receiptCheckPayload, shouldRerecord, refreshPublication, refreshFailureIssue} from '../gates/scan-evidence-provenance.mjs'

const REPO = 'Harborline-Software/harborline-platform'
const SIGNER = `https://github.com/${REPO}/.github/workflows/sign-gallery-receipt.yml@refs/heads/main`
const STEPS = ['root-clean-install', 'dependency-ledger', 'npm-clean-install', 'forms-contracts-clean-install',
  'rule-runtime-clean-install', 'rule-authoring-clean-install', 'copilot-contracts-clean-install', 'dotnet-restore',
  'generation-smoke', 'ui-spec-authority', 'catalog-preflight', 'tooling-selftests', 'sibling-package-origins',
  'prop-vocabulary', 'ui-gate-model', 'build', 'native-tests', 'perf-budgets', 'ui-shared-conformance',
  'package-consumers', 'gallery-gate', 'release-receipt', 'catalog-final']
const GALLERY_STEPS = ['gallery-structure', 'react-gallery-typecheck', 'react-storybook-build',
  'blazor-gallery-clean', 'blazor-gallery-build', 'gallery-tests-typecheck', 'playwright-browser',
  'playwright-browser-launch', 'gallery-accessibility-and-parity']
const CHECKS = {accessibilityScans: 4, ariaParityComparisons: 2, tabWalkLeaveChecks: 2,
  tabWalkReverseChecks: 2, tabWalkObscuredChecks: 2, tabWalkOrderComparisons: 2,
  tabWalkDetectorProofs: 14, visualParityComparisons: 0, reflowChecks: 6, localeProjectionChecks: 0,
  themeFixtureProjectionChecks: 0, themeStateParityComparisons: 0, runtimeThemeSwitchChecks: 2,
  keyboardFocusChecks: 2, forcedColorsChecks: 6, reducedMotionChecks: 8, contextMenuTierBProjectionChecks: 2}
const git = (root, ...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
const put = (root, path, value) => {
  mkdirSync(dirname(resolve(root, path)), {recursive: true})
  writeFileSync(resolve(root, path), typeof value === 'string' ? value : JSON.stringify(value))
}
const clone = value => structuredClone(value)
function fixture(t) {
  const root = mkdtempSync(resolve(tmpdir(), 'gallery-receipt-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  git(root, 'init', '-b', 'main')
  git(root, 'config', 'user.name', 'Receipt Fixture')
  git(root, 'config', 'user.email', 'receipt@example.invalid')
  git(root, 'config', 'core.autocrlf', 'false')
  put(root, 'catalog/modules/hlp.ui.button.json', {moduleId: 'hlp.ui.button', module: {
    presentation: {disposition: 'visual'}, projections: {react: {conformanceRunner: 'fixture'}}}})
  const scenarios = {moduleId: 'hlp.ui.button', qualityFixture: 'conformance/hlp.ui.button/quality-fixtures.yaml',
    scenarios: [{id: 'button.one'}, {id: 'button.two'}]}
  put(root, 'specs/modules/ui/hlp.ui.button/scenarios.json', scenarios)
  put(root, 'gallery/scenarios/hlp.ui.button.json', scenarios)
  put(root, 'gallery/ci-exemptions.json', {pixel: [], wallClock: []})
  put(root, 'conformance/hlp.ui.button/quality-fixtures.yaml', {
    accessibilityCases: [{}], internationalizationCases: [{}], themingCases: [{}]})
  put(root, 'conformance/hlp.ui.button/fixtures.yaml', {cases: [{}]})
  put(root, 'conformance/hlp.ui.button/runners/run-shared.mjs', '// run-ui-module-shared')
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'independent fixture')
  const head = git(root, 'rev-parse', 'HEAD'), revision = head
  const tree = git(root, 'rev-parse', 'HEAD^{tree}')
  const queue = {id: 101, attempt: 2, head}
  const run = {id: 101, run_attempt: 2, head_sha: head, repository: {full_name: REPO},
    head_repository: {full_name: REPO}, event: 'merge_group', status: 'completed', name: 'verify',
    path: '.github/workflows/verify.yml', conclusion: 'success'}
  const annotations = Object.entries(CHECKS).flatMap(([description, count]) =>
    Array.from({length: count}, () => ({type: 'hlp-check', description})))
  const shards = [{schemaVersion: 1, kind: 'gallery-shard', shard: 1, shards: 1, passed: true,
    provenance: {sourceHead: head, runId: '202'}, results: GALLERY_STEPS.map(id => ({id, passed: true})),
    playwrightReport: {stats: {expected: 2, unexpected: 0, skipped: 0, flaky: 0}, suites: [{specs:
      ['button.one', 'button.two'].map((id, i) => ({title: `${id} is accessible and visually conformant`,
        tests: [{status: 'expected', results: [{status: 'passed', annotations: i === 0 ? annotations : []}]}]}))}]}}]
  const gate = {schemaVersion: 3, phase: 4, status: 'PASS', subject: {baseHead: head, testedTree: tree},
    moduleIds: ['hlp.ui.button'], requiredStepIds: STEPS, results: STEPS.map(id => ({id, passed: true})),
    counts: {generationSmokeModules: 1, generationSmokeScenarios: 2, galleryScenarios: 2, qualityCases: 3,
      sharedResults: 2, scenarioBrowserTests: 2, galleryChecks: Object.fromEntries(Object.entries(CHECKS).filter(([, count]) => count > 0))},
    scenarioReconciliation: 'exact', checkReconciliation: 'exact'}
  const now = new Date('2026-11-10T15:01:00Z')
  const producer = {revision, runId: 202, attempt: 1, queueCompletedAt: '2026-11-10T15:00:00Z'}
  const artifacts = [{id: 303, name: 'gallery-run-202-1', digest: `sha256:${'a'.repeat(64)}`}]
  const directory = resolve(root, 'receipt')
  put(root, 'receipt/phase4.json', gate)
  put(root, 'receipt/gallery-shard-1.json', shards[0])
  const artifactFiles = ['phase4.json', 'gallery-shard-1.json'].sort().map(name => ({name,
    sha256: createHash('sha256').update(readFileSync(resolve(directory, name))).digest('hex')}))
  const args = {root, head, artifactFiles, queue: queueIdentity(run, queue), producer, gate, shards, artifacts, now}
  const predicate = buildGalleryPredicate(args)
  const statement = {_type: 'https://in-toto.io/Statement/v1', predicateType: 'https://harborline.software/attestations/gallery-run/v1',
    subject: [{name: 'gallery-tree', digest: {sha256: receiptTree(root, head).sha256}}], predicate}
  const result = {verificationResult: {statement, signature: {certificate: {buildSignerURI: SIGNER,
    buildSignerDigest: revision, buildTrigger: 'workflow_run',
    runInvocationURI: `https://github.com/${REPO}/actions/runs/202/attempts/1`}}, verifiedTimestamps: [{type: 'Tlog'}]}}
  put(root, 'receipt/tree-listing.bin', receiptTree(root, head).listing.toString())
  put(root, 'receipt/receipt.sigstore.json', '{}')
  const calls = []
  const api = path => {
    calls.push(path)
    if (path.endsWith('/101/attempts/2')) return clone(run)
    if (path.endsWith('/202/attempts/1')) return {event: 'workflow_run', head_sha: revision,
      path: '.github/workflows/verify-gallery-receipt.yml', repository: {full_name: REPO}}
    if (path.endsWith('/202/artifacts?per_page=100')) return {artifacts: [{name: 'gallery-receipt-202-1', expired: false}]}
    if (path.endsWith('/artifacts/303')) return {...artifacts[0], expired: false, workflow_run: {id: 202}}
    throw new Error('404')
  }
  const commands = []
  const verify = (options = {}) => verifyGalleryReceipt({root, directory, head, queue, revision, now,
    execute: (command, flags) => { commands.push([command, flags]); return JSON.stringify([result]) }, readApi: api, ...options})
  return {root, head, tree, args, gate, shards, predicate, result, verify, commands, calls, run, api, directory}
}

test('one complete matching receipt is accepted and pins the cryptographic verifier policy', t => {
  const f = fixture(t)
  assert.deepEqual(f.verify(), f.gate)
  const [command, flags] = f.commands[0]
  assert.equal(command, 'gh')
  for (const [flag, value] of [['--repo', REPO], ['--signer-workflow', `${REPO}/.github/workflows/sign-gallery-receipt.yml`],
    ['--signer-digest', f.head], ['--source-ref', 'refs/heads/main'], ['--source-digest', f.head],
    ['--cert-oidc-issuer', 'https://token.actions.githubusercontent.com'],
    ['--predicate-type', 'https://harborline.software/attestations/gallery-run/v1']]) assert.equal(flags[flags.indexOf(flag) + 1], value)
  assert.ok(flags.includes('--deny-self-hosted-runners'))
  assert.ok(f.calls.some(path => path.endsWith('/101/attempts/2')))
  assert.equal(f.predicate.runDetails.value.waitMs, 60000)
  assert.deepEqual(receiptScope(f.root, f.head).scenarios, ['button.one', 'button.two'])
  assert.equal(scopeMatchesGate(receiptScope(f.root, f.head), f.gate), true)
})

for (const [name, mutate] of [
  ['wrong reusable signer', f => { f.result.verificationResult.signature.certificate.buildSignerURI = SIGNER.replace('sign-gallery', 'verify-gallery') }],
  ['wrong signer revision', f => { f.result.verificationResult.signature.certificate.buildSignerDigest = 'b'.repeat(40) }],
  ['wrong build trigger', f => { f.result.verificationResult.signature.certificate.buildTrigger = 'merge_group' }],
  ['wrong invocation', f => { f.result.verificationResult.signature.certificate.runInvocationURI += '0' }],
  ['no transparency proof', f => { f.result.verificationResult.verifiedTimestamps = [] }],
  ['wrong subject tree digest', f => { f.result.verificationResult.statement.subject[0].digest.sha256 = 'c'.repeat(64) }],
  ['SHA1 subject', f => { f.result.verificationResult.statement.subject[0].digest = {sha1: 'a'.repeat(40)} }],
  ['extra digest algorithm', f => { f.result.verificationResult.statement.subject[0].digest.sha1 = 'a'.repeat(40) }],
  ['wrong commit with matching tree claim', f => { f.predicate.buildDefinition.value.externalParameters.gitCommit = 'b'.repeat(40) }],
  ['results claimed signer checked', f => { f.predicate.phase4.assurance = 'signer-checked' }],
  ['unclassified field', f => { f.predicate.unclassified = true }],
  ['missing shard', f => { f.predicate.shardResults.value[0].shards = 2 }],
  ['failed shard', f => { f.predicate.shardResults.value[0].passed = false }],
  ['headless phase4', f => { f.predicate.phase4.value.results.splice(20, 1) }],
  ['reused phase4 step', f => { f.predicate.phase4.value.results[16].reusedFrom = {run: 1} }],
  ['no artifact', f => { f.predicate.artifacts.value = [] }],
]) test(`refuses ${name}`, t => {
  const f = fixture(t)
  mutate(f)
  assert.throws(() => f.verify(), /gallery receipt:/)
})

for (const mutation of [{event: 'pull_request'}, {status: 'in_progress'}, {head_sha: 'a'.repeat(40)},
  {run_attempt: 1}, {id: 100}, {repository: {full_name: 'other/repo'}},
  {head_repository: {full_name: 'other/repo'}}]) test(`REST queue refuses ${JSON.stringify(mutation)}`, t => {
  const f = fixture(t)
  assert.throws(() => f.verify({readApi: path => path.endsWith('/101/attempts/2') ? {...f.run, ...mutation} : f.api(path)}), /queue run/)
})

test('missing/deleted/expired artifacts, invalid signature, modified listing and stale receipt fail closed', t => {
  const f = fixture(t)
  assert.throws(() => f.verify({execute: () => { throw new Error('invalid signature') }}), /invalid signature/)
  assert.throws(() => f.verify({readApi: () => { throw new Error('404') }}), /404/)
  assert.throws(() => f.verify({readApi: path => path.endsWith('/artifacts/303') ? {expired: true} : f.api(path)}), /artifact/)
  assert.throws(() => f.verify({now: new Date('2027-02-10T00:00:00Z')}), /expired/)
  writeFileSync(resolve(f.directory, 'tree-listing.bin'), 'tampered')
  assert.throws(() => f.verify(), /listing/)
  rmSync(resolve(f.directory, 'tree-listing.bin'))
  assert.throws(() => f.verify(), /ENOENT/)
})

test('canonical listing uses NUL-separated byte-sorted ls-tree records and SHA256', t => {
  const f = fixture(t)
  // Separate reference: Git supplies records; Buffer byte sort and crypto calculate the oracle.
  const lines = execFileSync('git', ['ls-tree', '-r', '--full-tree', '-z', f.head], {cwd: f.root})
    .toString().split('\0').filter(Boolean).map(s => Buffer.from(s)).sort(Buffer.compare)
  const expected = Buffer.concat(lines.flatMap(line => [line, Buffer.from([0])]))
  const actual = receiptTree(f.root, f.head)
  assert.deepEqual(actual.listing, expected)
  assert.equal(actual.sha256, createHash('sha256').update(expected).digest('hex'))
})

test('check payload targets queue head and names the retained artifact and timing', t => {
  const f = fixture(t)
  const check = receiptCheckPayload({head: f.head, conclusion: 'success', runId: 202, artifactId: 404, waitMs: 60000, scopeChanged: true})
  assert.equal(check.head_sha, f.head)
  assert.equal(check.name, 'gallery-receipt')
  assert.match(check.output.summary, /404.*60000/)
})

const readWorkflow = name => readFileSync(new URL(`../../.github/workflows/${name}`, import.meta.url), 'utf8')
test('main producer and reusable signer preserve credential and execution boundaries', () => {
  const producer = readWorkflow('verify-gallery-receipt.yml')
  const signer = readWorkflow('sign-gallery-receipt.yml')
  assert.match(producer, /workflow_run:\n    workflows: \[verify\]\n    types: \[completed\]/)
  assert.match(producer, /workflow_run.event == 'merge_group'/)
  for (const id of ['gallery-shard', 'phase-4']) {
    const job = producer.split(`\n  ${id}:\n`)[1].split(/\n  [a-z-]+:\n/)[0]
    assert.match(job, /runs-on: ubuntu-latest/)
    assert.match(job, /contents: read/)
    assert.doesNotMatch(job, /secrets\.|id-token:|environment:|contents: write/)
    assert.match(job, /needs.plan.outputs.revision/)
    assert.match(job, /trusted-orchestration\/tooling/)
  }
  assert.doesNotMatch(producer, /environment: gallery-receipt|GALLERY_RECEIPT_APP_KEY/)
  assert.match(producer, /uses: \.\/\.github\/workflows\/sign-gallery-receipt.yml/)
  assert.equal(signer.match(/environment: gallery-receipt/g).length, 1)
  assert.equal(signer.match(/id-token: write/g).length, 1)
  assert.match(signer, /--receipt build/)
  assert.match(signer, /actions\/attest@v4/)
  assert.ok(signer.indexOf('--receipt verify') < signer.indexOf('--receipt post'))
  assert.doesNotMatch(signer, /ref:.*workflow_run.head_sha|run-phase-4-gate|pnpm install/)
  assert.doesNotMatch(readWorkflow('verify.yml'), /actions\/attest@|GALLERY_RECEIPT_APP_KEY|id-token: write/)
  assert.ok((producer + signer).match(/retention-days: 90/g).length >= 4)
})

// Even a dishonest first-job tally claiming PASS/exact cannot choose the signer's scenario list.
for (const [name, change] of [
  ['drop', report => report.playwrightReport.suites[0].specs.pop()],
  ['add', report => report.playwrightReport.suites[0].specs.push({title: 'rogue is accessible and visually conformant'})],
]) test(`signer rejects raw reports that ${name} a scenario despite a passing runner tally`, t => {
  const f = fixture(t)
  change(f.shards[0])
  assert.throws(() => buildGalleryPredicate(f.args), /raw report|scenarios or outcomes/)
})

test('duplicate shards and a forged reported scenario/check tally are refused', t => {
  const f = fixture(t)
  f.args.shards = [clone(f.shards[0]), clone(f.shards[0])]
  assert.throws(() => buildGalleryPredicate(f.args), /missing shard|invalid/)
  f.args.shards = f.shards
  f.shards[0].playwrightReport.suites[0].specs[0].tests[0].results[0].annotations = []
  assert.throws(() => buildGalleryPredicate(f.args), /check counts/)
})

test('a retained local bundle cannot replace a deleted receipt artifact', t => {
  const f = fixture(t)
  assert.throws(() => f.verify({readApi: path => path.endsWith('/202/artifacts?per_page=100')
    ? {artifacts: []} : f.api(path)}), /receipt artifact/)
})

test('candidate orchestration edits are replaced by the actual main-copy step', t => {
  const f = fixture(t)
  const producer = readWorkflow('verify-gallery-receipt.yml')
  const script = producer.match(/python3 - <<'PY'\n([\s\S]*?)\n          PY/)[1]
    .split('\n').map(line => line.slice(10)).join('\n')
  put(f.root, 'tooling/run-phase-4-gate.mjs', 'candidate gate')
  put(f.root, 'tooling/collect-gallery-shards.mjs', 'candidate count')
  put(f.root, 'tooling/gate-contract.mjs', 'candidate recorder')
  put(f.root, 'tooling/tests/candidate.test.mjs', 'candidate test')
  put(f.root, '.trusted-orchestration/tooling/run-phase-4-gate.mjs', 'main gate')
  put(f.root, '.trusted-orchestration/tooling/collect-gallery-shards.mjs', 'main count')
  put(f.root, '.trusted-orchestration/tooling/gate-contract.mjs', 'main recorder')
  put(f.root, '.trusted-orchestration/tooling/tests/candidate.test.mjs', 'old test')
  execFileSync('python3', ['-c', script], {cwd: f.root})
  assert.equal(readFileSync(resolve(f.root, 'tooling/run-phase-4-gate.mjs'), 'utf8'), 'main gate')
  assert.equal(readFileSync(resolve(f.root, 'tooling/collect-gallery-shards.mjs'), 'utf8'), 'main count')
  assert.equal(readFileSync(resolve(f.root, 'tooling/gate-contract.mjs'), 'utf8'), 'main recorder')
  assert.equal(readFileSync(resolve(f.root, 'tooling/tests/candidate.test.mjs'), 'utf8'), 'candidate test')
})

test('refresh updates one PR, pushes fast-forward and carries only gate.json', t => {
  const f = fixture(t)
  const remote = resolve(f.root, 'remote.git')
  execFileSync('git', ['init', '--bare', remote], {stdio: 'ignore'})
  git(f.root, 'remote', 'add', 'origin', remote)
  git(f.root, 'push', 'origin', 'HEAD:refs/heads/main')
  let existing = [], created = 0, updated = 0
  const writeApi = (method, endpoint, body) => {
    assert.match(body.title, new RegExp(f.head))
    assert.equal(body.body.endsWith('🤖 Generated with [Claude Code](https://claude.com/claude-code).'), true)
    if (method === 'POST') {
      assert.equal(body.head, 'evidence/phase-4-rerecord')
      assert.equal(body.base, 'main')
      assert.equal(body.draft, true)
      created++
      existing = [{number: 7}]
    } else {
      assert.equal(method, 'PATCH')
      assert.match(endpoint, /\/pulls\/7$/)
      updated++
    }
  }
  const publish = report => refreshPublication({root: f.root, head: f.head, report, runId: 202,
    readApi: () => existing, writeApi, token: 'fixture-token-not-a-secret'})
  publish(f.gate)
  const first = git(f.root, 'rev-parse', 'refs/remotes/origin/evidence/phase-4-rerecord')
  publish({...f.gate, refreshedAt: '2026-11-10T16:00:00Z'})
  const second = git(f.root, 'rev-parse', 'refs/remotes/origin/evidence/phase-4-rerecord')
  assert.notEqual(first, second)
  git(f.root, 'merge-base', '--is-ancestor', first, second)
  assert.equal(git(f.root, 'diff', '--name-only', f.head, second), 'docs/evidence/phase-4/gate.json')
  assert.equal(JSON.parse(git(f.root, 'show', `${second}:docs/evidence/phase-4/gate.json`)).subject.baseHead, f.head)
  assert.match(git(f.root, 'log', '-1', '--format=%B', second), /Co-Authored-By: Claude Opus 5\.5 <noreply@anthropic\.com>$/)
  assert.equal(created, 1)
  assert.equal(updated, 1)
})

test('a record-only landing never re-records, and unchanged scope needs no receipt', t => {
  const f = fixture(t)
  put(f.root, 'docs/evidence/phase-4/gate.json', f.gate)
  git(f.root, 'add', 'docs/evidence/phase-4/gate.json')
  git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'record-only')
  const head = git(f.root, 'rev-parse', 'HEAD')
  assert.equal(shouldRerecord(f.root, f.head, head, f.gate), false)
  assert.equal(scopeMatchesGate(receiptScope(f.root, head), f.gate), true)
  put(f.root, 'specs/modules/ui/hlp.ui.button/scenarios.json', {
    scenarios: [{id: 'button.replaced'}, {id: 'button.two'}]})
  const gallery = JSON.parse(readFileSync(resolve(f.root, 'gallery/scenarios/hlp.ui.button.json'), 'utf8'))
  gallery.scenarios[0].id = 'button.replaced'
  put(f.root, 'gallery/scenarios/hlp.ui.button.json', gallery)
  git(f.root, 'add', 'specs', 'gallery')
  git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'same count, different scenario identities')
  assert.equal(shouldRerecord(f.root, head, git(f.root, 'rev-parse', 'HEAD'), f.gate), true)
})

test('refresh failure creates or updates one labelled issue naming landed commit and run', t => {
  const f = fixture(t)
  let existing = [], creates = 0, updates = 0
  const readApi = endpoint => endpoint.includes('/issues?') ? existing : [{name: 'phase-4-rerecord-failure'}]
  const writeApi = (method, endpoint, body) => {
    assert.match(body.body, new RegExp(f.head))
    assert.match(body.body, /actions\/runs\/202/)
    if (method === 'POST') {
      assert.deepEqual(body.labels, ['phase-4-rerecord-failure'])
      creates++
      existing = [{number: 8}]
    } else {
      assert.equal(endpoint.endsWith('/issues/8'), true)
      updates++
    }
  }
  refreshFailureIssue({head: f.head, runId: 202, readApi, writeApi})
  refreshFailureIssue({head: f.head, runId: 202, readApi, writeApi})
  assert.equal(creates, 1)
  assert.equal(updates, 1)
})

test('only the refresh job holds the rerecord environment and App, and no automation queues a PR', () => {
  const workflow = readWorkflow('verify-gallery-receipt.yml')
  const refresh = workflow.split('\n  refresh:\n')[1].split('\n  refresh-alert:\n')[0]
  assert.equal(workflow.match(/environment: gallery-rerecord/g).length, 1)
  assert.match(refresh, /environment: gallery-rerecord/)
  assert.match(refresh, /client-id: \$\{\{ vars.GALLERY_RERECORD_APP_CLIENT_ID \}\}/)
  assert.match(refresh, /private-key: \$\{\{ secrets.GALLERY_RERECORD_APP_KEY \}\}/)
  assert.match(refresh, /permission-contents: write\n          permission-pull-requests: write/)
  assert.doesNotMatch(refresh, /id-token:|permission-checks:|permission-statuses:/)
  assert.match(workflow, /paths-ignore: \[docs\/evidence\/phase-4\/gate.json\]/)
  const source = readFileSync(new URL('../gates/scan-evidence-provenance.mjs', import.meta.url), 'utf8')
  assert.doesNotMatch(source + workflow, /gh pr merge|--auto|enablePullRequestAutoMerge|enqueuePullRequest|--force/)
})

// Run the real CLI in a fixture Git repository. Only the gh process boundary is replaced;
// Git, file reads, provenance/scope checks, receipt reconciliation and mode dispatch remain real.
// A Node preload keeps this portable without shell shims or any live credential/API access.
const GH_BOUNDARY = `
import childProcess from 'node:child_process'
import {syncBuiltinESMExports} from 'node:module'
import {readFileSync, appendFileSync} from 'node:fs'
const realExec = childProcess.execFileSync
const fixture = JSON.parse(readFileSync(process.env.RECEIPT_CLI_FIXTURE, 'utf8'))
const RealDate = Date
globalThis.Date = class extends RealDate {
  constructor(...args) { super(...(args.length ? args : [fixture.now])) }
  static now() { return fixture.now }
}
childProcess.execFileSync = (command, args, options) => {
  if (command !== 'gh') return realExec(command, args, options)
  if (args[0] === 'attestation') {
    appendFileSync(fixture.log, JSON.stringify({command: 'attestation'}) + '\\n')
    if (fixture.rejectVerification) throw new Error('fixture verification refusal')
    return JSON.stringify(fixture.verification)
  }
  if (args[0] !== 'api') throw new Error('unexpected gh command')
  const methodIndex = args.indexOf('--method')
  const method = methodIndex < 0 ? 'GET' : args[methodIndex + 1]
  const endpoint = methodIndex < 0 ? args[1] : args[methodIndex + 2]
  let body
  if (method !== 'GET') {
    const input = args[args.indexOf('--input') + 1]
    body = JSON.parse(input === '-' ? options.input : readFileSync(input, 'utf8'))
  }
  appendFileSync(fixture.log, JSON.stringify({command: 'api', method, endpoint, body}) + '\\n')
  if (fixture.rejectPaths.includes(endpoint)) throw new Error('fixture API refusal')
  if (method !== 'GET') return '{}'
  if (!Object.hasOwn(fixture.routes, endpoint)) throw new Error('unexpected fixture API route')
  return JSON.stringify(fixture.routes[endpoint])
}
syncBuiltinESMExports()
`

function runReceiptCli(f, mode, {durable = f.gate, queueHead = f.head, before = f.head,
  rejectVerification = false, rejectPaths = [], env = {}} = {}) {
  put(f.root, 'docs/evidence/phase-4/gate.json', durable)
  const event = mode === 'refresh-alert' ? {after: f.head, before} : {
    workflow_run: {...f.run, head_sha: queueHead, updated_at: new Date(f.args.now.getTime() - 60000).toISOString()},
  }
  put(f.root, 'receipt-cli/event.json', event)
  put(f.root, 'receipt-cli/gh-boundary.mjs', GH_BOUNDARY)
  put(f.root, 'receipt-cli/requests.jsonl', '')
  const fixturePath = resolve(f.root, 'receipt-cli/fixture.json')
  const base = `repos/${REPO}`
  put(f.root, 'receipt-cli/fixture.json', {log: resolve(f.root, 'receipt-cli/requests.jsonl'),
    now: f.args.now.getTime(), rejectVerification, rejectPaths, verification: [f.result], routes: {
      [`${base}/actions/runs/101/attempts/2`]: {...f.run, head_sha: queueHead},
      [`${base}/actions/runs/202/attempts/1`]: {event: 'workflow_run', head_sha: f.head,
        path: '.github/workflows/verify-gallery-receipt.yml', repository: {full_name: REPO}},
      [`${base}/actions/artifacts/303`]: {id: 303, name: 'gallery-run-202-1', digest: `sha256:${'a'.repeat(64)}`,
        expired: false, workflow_run: {id: 202}},
      [`${base}/actions/runs/202/artifacts?per_page=100`]: {artifacts: [{name: 'gallery-receipt-202-1', expired: false}]},
      [`${base}/rulesets/22915054`]: {rules: [{type: 'merge_queue', parameters: {check_response_timeout_minutes: 120}}]},
      [`${base}/issues?state=open&labels=phase-4-rerecord-failure&per_page=100`]: [],
      [`${base}/labels?per_page=100`]: [{name: 'phase-4-rerecord-failure'}],
    }})
  const cli = resolve(import.meta.dirname, '../gates/scan-evidence-provenance.mjs')
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(resolve(f.root, 'receipt-cli/gh-boundary.mjs')).href,
    cli, '--receipt', mode], {cwd: f.root, encoding: 'utf8', timeout: 15000, env: {
      ...process.env, RECEIPT_CLI_FIXTURE: fixturePath, GITHUB_EVENT_PATH: resolve(f.root, 'receipt-cli/event.json'),
      GITHUB_EVENT_NAME: mode === 'refresh-alert' ? 'push' : 'workflow_run', GITHUB_REF: 'refs/heads/main',
      GITHUB_REPOSITORY: REPO, GITHUB_SHA: f.head, GITHUB_RUN_ID: '202', GITHUB_RUN_ATTEMPT: '1',
      RECEIPT_DIRECTORY: f.directory, RECEIPT_VERIFIED: 'true', RECEIPT_ARTIFACT_ID: '404',
      GITHUB_STEP_SUMMARY: resolve(f.root, 'receipt-cli/summary.md'), ...env,
    }})
  const requests = readFileSync(resolve(f.root, 'receipt-cli/requests.jsonl'), 'utf8').trim()
    .split('\n').filter(Boolean).map(line => JSON.parse(line))
  return {...result, requests}
}

test('refresh alert condition includes failed planning without requiring a changed output', () => {
  const job = readWorkflow('verify-gallery-receipt.yml').split('\n  refresh-alert:\n')[1]
  const condition = job.match(/    if: >-\n((?:      .*\n)+)/)[1].trim()
    .replace(/\bneeds\.([\w-]+)/g, "needs['$1']")
  // This workflow condition uses only the shared JS/GitHub boolean/equality subset; execute the
  // actual expression against literal outcomes instead of asserting source spelling.
  const evaluate = (plan, downstream = {}, event = 'push') => runInNewContext(condition, {
    always: () => true, github: {event_name: event}, needs: {plan,
      ...Object.fromEntries(['ledger-data', 'gallery-shard', 'phase-4', 'refresh']
        .map(id => [id, {result: downstream[id] ?? 'success'}])),
    },
  })
  assert.equal(evaluate({result: 'failure', outputs: {}}), true)
  assert.equal(evaluate({result: 'cancelled', outputs: {}}), true)
  assert.equal(evaluate({result: 'success', outputs: {changed: 'false'}}, {'phase-4': 'skipped'}), false)
  assert.equal(evaluate({result: 'success', outputs: {changed: 'true'}}), false)
  for (const id of ['ledger-data', 'gallery-shard', 'phase-4', 'refresh']) {
    assert.equal(evaluate({result: 'success', outputs: {changed: 'true'}}, {[id]: 'failure'}), true)
  }
  assert.equal(evaluate({result: 'failure', outputs: {}}, {}, 'workflow_run'), false)
})

test('refresh-alert CLI opens the issue despite unreadable provenance and an unusable rerecord base', t => {
  const f = fixture(t)
  const result = runReceiptCli(f, 'refresh-alert', {durable: '{broken durable JSON', before: 'unusable-base'})
  assert.equal(result.status, 0, result.stderr)
  const issues = result.requests.filter(request => request.method === 'POST' && request.endpoint.endsWith('/issues'))
  assert.equal(issues.length, 1)
  assert.deepEqual(issues[0].body.labels, ['phase-4-rerecord-failure'])
  assert.match(issues[0].body.body, new RegExp(f.head))
  assert.match(issues[0].body.body, /actions\/runs\/202/)
})

test('refresh-alert CLI still refuses a push outside main without posting', t => {
  const f = fixture(t)
  const result = runReceiptCli(f, 'refresh-alert', {durable: '{broken', env: {GITHUB_REF: 'refs/heads/other'}})
  assert.equal(result.status, 1)
  assert.equal(result.requests.length, 0)
})

for (const [name, options] of [
  ['unreadable durable evidence', f => ({durable: '{broken'})],
  ['refused durable provenance', f => ({durable: {...f.gate, status: 'FAIL'}})],
  ['unavailable candidate tree', f => ({queueHead: 'b'.repeat(40)})],
  ['invalid bundle', f => ({durable: {...f.gate, counts: {...f.gate.counts, galleryScenarios: 1}}, rejectVerification: true})],
  ['missing retained receipt', f => ({durable: {...f.gate, counts: {...f.gate.counts, galleryScenarios: 1}},
    rejectPaths: [`repos/${REPO}/actions/runs/202/artifacts?per_page=100`]})],
]) test(`post CLI posts a failure check for ${name}`, t => {
  const f = fixture(t)
  const selected = options(f)
  const result = runReceiptCli(f, 'post', selected)
  assert.equal(result.status, 1, result.stderr)
  const checks = result.requests.filter(request => request.method === 'POST' && request.endpoint.endsWith('/check-runs'))
  assert.equal(checks.length, 1, JSON.stringify(result.requests))
  assert.equal(checks[0].body.name, 'gallery-receipt')
  assert.equal(checks[0].body.head_sha, selected.queueHead ?? f.head)
  assert.equal(checks[0].body.status, 'completed')
  assert.equal(checks[0].body.conclusion, 'failure')
  assert.doesNotMatch(result.stderr + checks[0].body.output.summary, /fixture verification refusal|fixture API refusal/)
})

test('post CLI still passes unchanged scope without a receipt verification result', t => {
  const f = fixture(t)
  const result = runReceiptCli(f, 'post', {env: {RECEIPT_VERIFIED: 'false'}})
  assert.equal(result.status, 0, result.stderr)
  const checks = result.requests.filter(request => request.endpoint?.endsWith('/check-runs'))
  assert.equal(checks.length, 1)
  assert.equal(checks[0].body.conclusion, 'success')
  assert.equal(result.requests.some(request => request.command === 'attestation'), false)
})

test('post CLI still requires and re-verifies complete retained evidence for changed scope', t => {
  const f = fixture(t)
  const result = runReceiptCli(f, 'post', {durable: {...f.gate, counts: {...f.gate.counts, galleryScenarios: 1}}})
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.requests.some(request => request.command === 'attestation'), true)
  const checks = result.requests.filter(request => request.endpoint?.endsWith('/check-runs'))
  assert.equal(checks.length, 1)
  assert.equal(checks[0].body.conclusion, 'success')
})

test('post CLI does not post a check when the trusted workflow context cannot be established', t => {
  const f = fixture(t)
  const result = runReceiptCli(f, 'post', {env: {GITHUB_EVENT_NAME: 'pull_request'}})
  assert.equal(result.status, 1)
  assert.equal(result.requests.length, 0)
})
