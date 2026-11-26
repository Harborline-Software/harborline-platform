// Oracles: supplied ADR-0128/0134/0137/0139 and in-toto Statement v1. Fixtures below are
// independently authored protocol documents. Production builds and verifies the statement.
// The CLI boundary double does not claim to test Sigstore cryptography or hosted environment rules.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync, spawnSync} from 'node:child_process'
import {existsSync, mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve, dirname} from 'node:path'
import {createHash} from 'node:crypto'
import {runInNewContext} from 'node:vm'
import {pathToFileURL} from 'node:url'
import * as releaseEvidence from '../gates/scan-evidence-provenance.mjs'
import {emitReleaseReceipt} from '../release-receipt.mjs'
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

test('release gate production path binds exact bytes and verifies independently before assets', () => {
  assert.equal(typeof releaseEvidence.sealReleaseGateEvidence, 'function')
  assert.equal(typeof releaseEvidence.buildReleaseGatePredicate, 'function')
  assert.equal(typeof releaseEvidence.verifyReleaseGateEvidence, 'function')
  const signer = readWorkflow('sign-gallery-receipt.yml'), producer = readWorkflow('verify-gallery-receipt.yml')
  const release = readWorkflow('release-platform.yml')
  assert.match(signer, /predicate-type: https:\/\/harborline.software\/attestations\/release-gate\/v1/)
  assert.match(signer, /subject-name: phase4-receipt/)
  assert.match(signer, /artifact-ids: \$\{\{ inputs.release-artifact-id \}\}/)
  assert.match(producer, /release-inventory-sha256: \$\{\{ steps.release-seal.outputs.inventory-sha256 \}\}/)
  assert.match(release, /--release-evidence verify/)
  assert.doesNotMatch(release, /run-phase4-receipt.mjs|cp .*staging\/phase4-receipt.json/)
  assert.ok(release.indexOf('--release-evidence verify') < release.indexOf('uses: actions/attest@'))
  assert.match(release, /unproved first-asset live-read boundary/)
})
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
    counts: {nativeTests: 37, generationSmokeModules: 1, generationSmokeScenarios: 2, galleryScenarios: 2, qualityCases: 3,
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

function releaseFixture(t) {
  const f = fixture(t), root = f.root, now = new Date('2026-11-21T17:00:00Z')
  const hash = bytes => createHash('sha256').update(bytes).digest('hex')
  const seed = JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision: '1.0.0', items: [{id: 'fixture'}]})
  put(root, '_shared/packs/platform/platform-pack.export.json', `${seed.slice(0, -1)},"digest":{"algorithm":"sha256","value":"${hash(seed + '\n')}"}}\n`)
  const mainWorkflow = readWorkflow('license-sbom.yml').replaceAll('\r\n', '\n')
  put(root, '.github/workflows/license-sbom.yml', mainWorkflow)
  const repository = resolve(import.meta.dirname, '../..'), copied = new Set()
  const copy = name => {
    if (copied.has(name)) return
    copied.add(name)
    const source = resolve(repository, name), bytes = readFileSync(source, 'utf8')
    put(root, name, bytes)
    for (const match of bytes.matchAll(/from\s+['"](\.[^'"]+)['"]/g))
      copy(resolve(dirname(source), match[1]).slice(repository.length + 1))
  }
  copy('tooling/gates/scan-evidence-provenance.mjs')
  put(root, 'docs/evidence/phase-4/gate.json', {...f.gate, designReview: {expired: []}})
  git(root, 'add', '.'); git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'trusted source A')
  const A = git(root, 'rev-parse', 'HEAD')
  put(root, 'product.txt', 'tested product B')
  put(root, 'tooling/gates/scan-evidence-provenance.mjs', 'import {writeFileSync} from "node:fs"; writeFileSync(process.env.CANDIDATE_MARKER, "executed"); console.log("ADMIT");')
  git(root, 'add', 'product.txt', 'tooling'); git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'tested B')
  const B = git(root, 'rev-parse', 'HEAD'), tree = git(root, 'rev-parse', 'HEAD^{tree}')
  const directory = resolve(root, 'release-inputs')
  const gate = {...f.gate, subject: {baseHead: B, testedTree: tree, mode: 'exact-staged-tree'},
    results: STEPS.map(id => ({id, passed: true, exitCode: 0})), designReview: {expired: []}}
  const policy = {schemaVersion: 1, signerRevision: A, verifierRevision: A, apiRevision: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
    revokedSignerRevisions: [], actionsAppId: 15368, galleryAppId: 333, reviewGateAppId: 111}
  const policySha256 = hash(JSON.stringify(policy))
  put(root, 'release-inputs/phase4.json', gate)
  put(root, 'release-inputs/release-phase4-receipt.json', {schemaVersion: 3, repository: 'harborline-platform', phase: 4,
    baseHead: B, testedTree: tree, gate, reportSha256: hash(JSON.stringify(gate)),
    orchestration: {revision: A, apiRevision: policy.apiRevision}})
  put(root, 'release-inputs/release-receipt.json', emitReleaseReceipt(root, {baseHead: B, testedTree: tree, mode: 'exact-staged-tree'}).toString('utf8'))
  for (let id = 1; id <= 6; id++) {
    const shard = clone(f.shards[0]); shard.shard = id; shard.shards = 6
    shard.provenance = {sourceHead: B, runId: '9002'}
    if (id !== 1) shard.playwrightReport = {stats: {expected: 0, unexpected: 0, skipped: 0, flaky: 0}, suites: []}
    put(root, `release-inputs/gallery-shard-${id}.json`, shard)
  }
  for (const [file, end] of [['license-policy.cjs', 'NODE'], ['syft.yaml', 'SYFT']]) {
    const marker = `cat > "$RUNNER_TEMP/${file}" <<'${end}'\n`
    const body = mainWorkflow.split(marker)[1].split(`\n          ${end}`)[0]
    put(root, `release-inputs/sbom/${file}`, body.split('\n').map(line => line.slice(10)).join('\n') + '\n')
  }
  put(root, 'release-inputs/sbom/sbom.spdx.json', {spdxVersion: 'SPDX-2.3', packages: [{name: 'blocked-package', versionInfo: '1.0.0', licenseDeclared: 'MIT'}]})
  const sbomRecord = {schemaVersion: 1, result: 'PASS', testedCommit: B, testedTree: tree, producerRevision: A, runId: '9002', attempt: '1',
    scanner: {name: 'syft', version: '1.57.2', action: 'e22c389904149dbc22b58101806040fa8d37a610'}}
  for (const [key, file] of [['sbomSha256', 'sbom.spdx.json'], ['policySha256', 'license-policy.cjs'], ['configSha256', 'syft.yaml']])
    sbomRecord[key] = hash(readFileSync(resolve(directory, 'sbom', file)))
  put(root, 'release-inputs/sbom/sbom-evidence.json', sbomRecord)
  const producer = {id: 9002, attempt: 1, revision: A}, queue = {id: 9001, attempt: 2}, sbom = {id: 9003, attempt: 1}
  const sealed = releaseEvidence.sealReleaseGateEvidence({root, directory, head: B, producer, queue, sbom, policySha256,
    queueCompletedAt: '2026-11-21T16:00:00Z', now})
  const artifact = {id: 777, name: 'release-gate-inputs-9002-1', digest: `sha256:${'d'.repeat(64)}`, expired: false, workflow_run: {id: 9002}}
  const repo = {full_name: REPO}, routes = {}
  for (const [id, attempt, name, suite, names] of [[9001, 2, 'verify', 701,
    ['phase-4-gate', ...[1, 2, 3, 4, 5, 6].map(i => `gallery-shard (${i})`), 'gallery-collect', 'verify']], [9003, 1, 'license-sbom', 702, ['sbom']]]) {
    const run = {id, run_attempt: attempt, event: 'merge_group', status: 'completed', conclusion: 'success', head_sha: B,
      path: `.github/workflows/${name}.yml`, name, repository: repo, head_repository: repo, check_suite_id: suite}
    routes[`actions/runs/${id}`] = run; routes[`actions/runs/${id}/attempts/${attempt}`] = run
    const jobs = names.map((name, i) => {
      const jobId = id * 10 + i
      routes[`check-runs/${jobId}`] = {id: jobId, name, status: 'completed', conclusion: 'success', head_sha: B, app: {id: 15368}, check_suite: {id: suite}}
      return {id: jobId, name, run_id: id, head_sha: B, status: 'completed', conclusion: 'success', check_run_url: `https://api.github.com/repos/${REPO}/check-runs/${jobId}`}
    })
    routes[`actions/runs/${id}/attempts/${attempt}/jobs?per_page=100`] = {total_count: jobs.length, jobs}
  }
  routes[`actions/workflows/license-sbom.yml/runs?event=merge_group&head_sha=${B}&per_page=100`] = {total_count: 1, workflow_runs: [routes['actions/runs/9003']]}
  const producerRun = {id: 9002, run_attempt: 1, event: 'workflow_run', path: '.github/workflows/verify-gallery-receipt.yml', head_sha: A,
    repository: repo, status: 'completed', conclusion: 'success'}
  routes['actions/runs/9002'] = producerRun; routes['actions/runs/9002/attempts/1'] = producerRun
  routes['actions/artifacts/777'] = artifact
  routes['actions/artifacts/779'] = {id: 779, name: 'main-sbom-9002-1', digest: `sha256:${'f'.repeat(64)}`, expired: false, workflow_run: {id: 9002}}
  routes['actions/runs/9002/artifacts?per_page=100'] = {total_count: 2, artifacts: [artifact, {id: 888, name: 'release-gate-receipt-9002-1', expired: false}]}
  const readApi = path => { const route = routes[path.replace(`repos/${REPO}/`, '')]; assert.ok(route, path); return clone(route) }
  const expected = {head: B, runId: 9002, attempt: 1, artifactId: 777, ...sealed,
    sbomArtifactId: 779, sbomSha256: sbomRecord.sbomSha256, sbomPolicySha256: sbomRecord.policySha256, sbomTestedTree: tree}
  const args = {root, directory, policy, policySha256, expected, artifact, readApi, now}
  const predicate = releaseEvidence.buildReleaseGatePredicate(args)
  const result = {verificationResult: {statement: {_type: 'https://in-toto.io/Statement/v1',
    predicateType: 'https://harborline.software/attestations/release-gate/v1',
    subject: [{name: 'phase4-receipt', digest: {sha256: hash(readFileSync(resolve(directory, 'release-phase4-receipt.json')))}}], predicate},
    verifiedTimestamps: [{type: 'Tlog'}], signature: {certificate: {buildSignerURI: SIGNER, buildSignerDigest: A,
      sourceRepositoryURI: `https://github.com/${REPO}`, sourceRepositoryRef: 'refs/heads/main', sourceRepositoryDigest: A,
      buildTrigger: 'workflow_run', runnerEnvironment: 'github-hosted', runInvocationURI: `https://github.com/${REPO}/actions/runs/9002/attempts/1`}}}}
  const commands = []
  const verify = () => releaseEvidence.verifyReleaseGateEvidence({root, directory, approvedCommit: B, policy, policySha256, readApi, now,
    execute: (command, flags) => { commands.push([command, flags]); return JSON.stringify([result]) }})
  return {root, directory, A, B, args, sealed, routes, artifact, result, predicate, verify, commands, hash, now, policy, policySha256}
}

test('release attestation binds literal receipt bytes and companion inventory, and invokes the production live verifier', t => {
  const f = releaseFixture(t)
  assert.deepEqual(f.verify(), f.predicate)
  const flags = f.commands[0][1]
  assert.equal(flags[flags.indexOf('--predicate-type') + 1], 'https://harborline.software/attestations/release-gate/v1')
  assert.equal(flags[flags.indexOf('--signer-digest') + 1], f.A)
  assert.ok(flags.includes('--deny-self-hosted-runners'))
  assert.equal(f.predicate.files.length, 13)
  assert.equal(f.predicate.companionSha256, f.hash(readFileSync(resolve(f.directory, 'release-receipt.json'))))
})

test('release signer independently binds fresh read-only SBOM job outputs instead of trusting candidate copies', t => {
  const f = releaseFixture(t)
  for (const [field, value] of [['sbomSha256', '0'.repeat(64)], ['sbomPolicySha256', '0'.repeat(64)],
    ['sbomTestedTree', 'f'.repeat(40)], ['sbomArtifactId', 777], ['artifactId', 778]]) {
    assert.throws(() => releaseEvidence.buildReleaseGatePredicate({...f.args, expected: {...f.args.expected, [field]: value}}), field)
  }
})

test('release signer refuses a changed scanner configuration even after candidate-side inventory hashes are recomputed', t => {
  const f = releaseFixture(t), config = resolve(f.directory, 'sbom/syft.yaml'), recordPath = resolve(f.directory, 'sbom/sbom-evidence.json')
  writeFileSync(config, readFileSync(config, 'utf8') + '# candidate replacement\n')
  const record = JSON.parse(readFileSync(recordPath)); record.configSha256 = f.hash(readFileSync(config))
  writeFileSync(recordPath, JSON.stringify(record))
  const sealed = releaseEvidence.sealReleaseGateEvidence({root: f.root, directory: f.directory, head: f.B,
    producer: f.predicate.producer, queue: f.predicate.queue, sbom: f.predicate.sbom, policySha256: f.policySha256,
    queueCompletedAt: '2026-11-21T16:00:00Z', now: f.now})
  assert.throws(() => releaseEvidence.buildReleaseGatePredicate({...f.args, expected: {...f.args.expected, ...sealed}}), /trusted source/)
})

test('release evidence refuses altered receipt/companion bytes, inventory replacement, extra files, wrong artifact/run and stale producer', async t => {
  const f = releaseFixture(t)
  for (const file of ['release-phase4-receipt.json', 'release-receipt.json', 'release-inputs.json']) {
    await t.test(file, () => {
      const path = resolve(f.directory, file), bytes = readFileSync(path)
      try { writeFileSync(path, Buffer.concat([bytes, Buffer.from('\n')])); assert.throws(f.verify) }
      finally { writeFileSync(path, bytes) }
    })
  }
  put(f.root, 'release-inputs/rogue.txt', 'not in producer outputs')
  assert.throws(f.verify); rmSync(resolve(f.directory, 'rogue.txt'))
  for (const [field, value] of [['id', 778], ['expired', true], ['workflow_run', {id: 9004}]]) {
    const previous = f.artifact[field]
    try { f.artifact[field] = value; assert.throws(f.verify) } finally { f.artifact[field] = previous }
  }
  const run = f.routes['actions/runs/9002']; f.routes['actions/runs/9002'] = {...run, run_attempt: 2, conclusion: 'failure'}
  assert.throws(f.verify); f.routes['actions/runs/9002'] = run
  const statement = f.result.verificationResult.statement
  statement.predicateType = 'https://harborline.software/attestations/gallery-run/v1'
  assert.throws(f.verify)
})

test('actual external release CLI locates exact producer output, verifies it and installs only attested corroboration', t => {
  const f = releaseFixture(t), external = resolve(f.root, '.external-verifier')
  git(f.root, 'worktree', 'add', '--detach', external, f.A)
  const output = resolve(f.root, 'cli-output'), log = resolve(f.root, 'cli-requests'), fixturePath = resolve(f.root, 'cli-data.json')
  const marker = resolve(f.root, 'candidate-executed')
  const producer = f.routes['actions/runs/9002']
  const retained = {id: 888, name: 'release-gate-receipt-9002-1', digest: `sha256:${'e'.repeat(64)}`, expired: false, workflow_run: {id: 9002}}
  f.routes[`actions/workflows/verify-gallery-receipt.yml/runs?event=workflow_run&head_sha=${f.A}&per_page=100`] = {total_count: 1, workflow_runs: [producer]}
  f.routes['actions/artifacts/888'] = retained
  f.routes['actions/runs/9002/artifacts?per_page=100'].artifacts[1] = retained
  put(f.root, 'cli-hook.mjs', GH_BOUNDARY)
  put(f.root, 'cli-requests', '')
  const writeFixture = () => put(f.root, 'cli-data.json', {log, now: f.now.getTime(), rejectVerification: false, rejectPaths: [],
    verification: [f.result], routes: Object.fromEntries(Object.entries(f.routes).map(([path, value]) => [`repos/${REPO}/${path}`, value]))})
  writeFixture()
  put(f.root, 'producer-event.json', {workflow_run: {...f.routes['actions/runs/9001'], updated_at: '2026-11-21T16:00:00Z'}})
  const run = (mode, overrides = {}) => {
    const producing = ['plan', 'build', 'verify-produced'].includes(mode)
    const runId = producing ? '9002' : '9004', revision = producing ? f.A : f.B
    const attempt = overrides.GITHUB_RUN_ATTEMPT ?? '1'
    const snapshot = {schemaVersion: 1, repository: REPO, runId, attempt, revision,
      response: {name: 'RELEASE_GATE_POLICY', value: JSON.stringify(f.policy)}}
    return spawnSync(process.execPath, ['--import', pathToFileURL(resolve(f.root, 'cli-hook.mjs')).href,
    resolve(external, 'tooling/gates/scan-evidence-provenance.mjs'),
    ...(mode === 'plan' ? ['--receipt', 'plan'] : ['--release-evidence', mode, f.directory, f.B])], {
    cwd: producing ? external : f.root, encoding: 'utf8', timeout: 20000, env: {...process.env, RECEIPT_CLI_FIXTURE: fixturePath,
      GITHUB_WORKSPACE: producing ? external : f.root, GITHUB_REPOSITORY: REPO, GITHUB_SHA: revision, GITHUB_RUN_ID: runId, GITHUB_RUN_ATTEMPT: attempt,
      GITHUB_EVENT_NAME: producing ? 'workflow_run' : 'workflow_dispatch', GITHUB_REF: 'refs/heads/main',
      GITHUB_EVENT_PATH: resolve(f.root, 'producer-event.json'), RELEASE_APPROVED_COMMIT: f.B,
      GITHUB_OUTPUT: output, CANDIDATE_MARKER: marker,
      RELEASE_POLICY_ENVELOPE: Buffer.from(JSON.stringify(snapshot)).toString('base64'), RELEASE_POLICY_SHA256: f.policySha256,
      RELEASE_SELECTED_ARTIFACT_ID: '888', RELEASE_SELECTED_RUN_ID: '9002', RELEASE_SELECTED_ATTEMPT: '1', ...overrides}})
  }
  assert.equal(run('plan').status, 0)
  assert.match(readFileSync(output, 'utf8'), /release-eligible=true/)
  const caller = readWorkflow('verify-gallery-receipt.yml').split('\n  sign:\n')[1].split('\n  post:')[0]
  const producerOutputs = {'release-artifact-id': '777', 'release-inventory-sha256': f.sealed.inventorySha256,
    'release-receipt-sha256': f.sealed.receiptSha256, 'release-companion-sha256': f.sealed.companionSha256}
  const inputs = {}
  for (const match of caller.matchAll(/^      ([\w-]+): \$\{\{ needs\.phase-4\.outputs\.([\w-]+) \}\}$/gm)) inputs[match[1]] = producerOutputs[match[2]]
  const sbomOutputs = {'artifact-id': '779', 'sbom-sha256': f.args.expected.sbomSha256,
    'policy-sha256': f.args.expected.sbomPolicySha256, 'tested-tree': f.args.expected.sbomTestedTree}
  for (const match of caller.matchAll(/^      ([\w-]+): \$\{\{ needs\.main-sbom\.outputs\.([\w-]+) \}\}$/gm)) inputs[match[1]] = sbomOutputs[match[2]]
  const buildStep = readWorkflow('sign-gallery-receipt.yml').split('      - id: release-predicate\n')[1].split('      - id: release-attest')[0]
  const buildEnv = {}
  for (const match of buildStep.matchAll(/^          (RELEASE_(?:INPUT|SBOM)_\w+): \$\{\{ inputs\.([\w-]+) \}\}$/gm)) buildEnv[match[1]] = inputs[match[2]]
  const built = run('build', buildEnv)
  assert.equal(built.status, 0, built.stderr)
  assert.deepEqual(JSON.parse(readFileSync(resolve(f.directory, 'release-predicate.json'))), f.predicate)
  assert.notEqual(run('build', {...buildEnv, GITHUB_RUN_ATTEMPT: '2'}).status, 0, 'signer-only rerun cannot reuse earlier producer outputs')
  assert.notEqual(run('build', {...buildEnv, RELEASE_INPUT_COMPANION_SHA256: ''}).status, 0, 'missing named producer digest refuses')
  producer.status = 'in_progress'; producer.conclusion = null; writeFixture()
  const produced = run('verify-produced')
  assert.equal(produced.status, 0, produced.stderr)
  producer.status = 'completed'; producer.conclusion = 'success'; writeFixture()
  const located = run('locate')
  assert.equal(located.status, 0, located.stderr)
  assert.match(readFileSync(output, 'utf8'), /artifact-id=888\nproducer-run=9002\nproducer-attempt=1\n/)
  assert.equal(existsSync(resolve(f.root, '.git/harborline-phase4-receipt.json')), false)
  const verified = run('verify')
  assert.equal(verified.status, 0, verified.stderr)
  assert.deepEqual(readFileSync(resolve(f.root, '.git/harborline-phase4-receipt.json')), readFileSync(resolve(f.directory, 'release-phase4-receipt.json')))
  assert.deepEqual(readFileSync(resolve(f.root, '.git/harborline-release-receipt.json')), readFileSync(resolve(f.directory, 'release-receipt.json')))
  assert.equal(existsSync(marker), false, 'released accept-all verifier never executes')
  f.result.verificationResult.signature.certificate.buildSignerDigest = f.B
  writeFixture()
  const refused = run('verify')
  assert.notEqual(refused.status, 0, 'a wrong signer still refuses through the actual CLI')
  assert.equal(existsSync(marker), false)
})

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
  for (const id of ['gallery-shard', 'candidate-phase-4', 'phase-4']) {
    const job = producer.split(`\n  ${id}:\n`)[1].split(/\n  [a-z-]+:\n/)[0]
    assert.match(job, /runs-on: ubuntu-latest/)
    assert.match(job, /contents: read/)
    assert.doesNotMatch(job, /secrets\.|id-token:|environment:|contents: write/)
    assert.match(job, /needs.plan.outputs.revision/)
    if (id === 'phase-4') assert.match(job, /--receipt collect/)
    else assert.match(job, /trusted-orchestration\/tooling/)
  }
  const poster = producer.split('\n  post:\n')[1].split(/\n  [a-z-]+:\n/)[0]
  assert.match(poster, /environment: gallery-receipt/)
  assert.match(poster, /GALLERY_RECEIPT_APP_KEY/)
  assert.doesNotMatch(poster, /id-token:|attestations:/)
  assert.match(producer, /uses: \.\/\.github\/workflows\/sign-gallery-receipt.yml/)
  assert.doesNotMatch(signer, /environment:|secrets\.|--receipt post/)
  assert.equal(signer.match(/id-token: write/g).length, 1)
  assert.match(signer, /--receipt build/)
  assert.match(signer, /actions\/attest@v4/)
  assert.ok(signer.indexOf('--receipt verify') < signer.indexOf('id: evidence'))
  assert.match(poster, /--receipt post/)
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

test('candidate orchestration edits and additions are replaced by the complete main footprint', t => {
  const f = fixture(t)
  const producer = readWorkflow('verify-gallery-receipt.yml')
  const script = producer.split('\n  gallery-shard:\n')[1].match(/python3 - <<'PY'\n([\s\S]*?)\n          PY/)[1]
    .split('\n').map(line => line.slice(10)).join('\n')
  const repository = resolve(import.meta.dirname, '../..'), copied = new Set()
  const copy = name => {
    if (copied.has(name)) return
    copied.add(name)
    const source = resolve(repository, name), bytes = readFileSync(source)
    put(f.root, name, bytes.toString())
    for (const match of bytes.toString().matchAll(/from\s+['"](\.[^'"]+)['"]/g))
      copy(resolve(dirname(source), match[1]).slice(repository.length + 1))
  }
  copy('tooling/run-phase-4-gate.mjs')
  put(f.root, 'package.json', '{"packageManager":"pnpm@10.0.0"}')
  put(f.root, 'global.json', '{"sdk":{"version":"11.0.100"}}')
  put(f.root, 'tooling/tests/candidate.test.mjs', 'old test')
  git(f.root, 'add', '.')
  git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'main orchestration A')
  const main = git(f.root, 'rev-parse', 'HEAD')
  const mainGate = readFileSync(resolve(f.root, 'tooling/run-phase-4-gate.mjs'), 'utf8')
  put(f.root, 'tooling/run-phase-4-gate.mjs', 'candidate gate')
  put(f.root, 'tooling/collect-gallery-shards.mjs', 'candidate count')
  put(f.root, 'tooling/gate-contract.mjs', 'candidate recorder')
  put(f.root, 'tooling/tests/candidate.test.mjs', 'candidate test')
  put(f.root, 'tooling/rogue.sh', 'exit 0')
  git(f.root, 'add', '.')
  git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'candidate B')
  const tree = git(f.root, 'write-tree')
  git(f.root, 'worktree', 'add', '--detach', resolve(f.root, '.trusted-orchestration'), main)
  const previous = process.env.ORCHESTRATION_REVISION
  process.env.ORCHESTRATION_REVISION = main
  t.after(() => { if (previous === undefined) delete process.env.ORCHESTRATION_REVISION; else process.env.ORCHESTRATION_REVISION = previous })
  execFileSync('python3', ['-c', script], {cwd: f.root})
  assert.equal(readFileSync(resolve(f.root, 'tooling/run-phase-4-gate.mjs'), 'utf8'), mainGate)
  assert.equal(readFileSync(resolve(f.root, 'tooling/tests/candidate.test.mjs'), 'utf8'), 'old test')
  assert.throws(() => readFileSync(resolve(f.root, 'tooling/rogue.sh')), /ENOENT/)
  assert.equal(git(f.root, 'write-tree'), tree)
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
  const response = fixture.routes[endpoint]
  return response?.archiveBase64 ? Buffer.from(response.archiveBase64, 'base64') : JSON.stringify(response)
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

test('current-attempt public policy travels through the actual reusable caller and callee into scope-changing signer planning', t => {
  const f = fixture(t)
  put(f.root, 'gallery/scenarios/hlp.ui.button.json', {moduleId: 'hlp.ui.button', qualityFixture: 'conformance/hlp.ui.button/quality-fixtures.yaml', scenarios: [{id: 'button.one'}, {id: 'button.two'}, {id: 'button.three'}]})
  put(f.root, 'specs/modules/ui/hlp.ui.button/scenarios.json', {moduleId: 'hlp.ui.button', qualityFixture: 'conformance/hlp.ui.button/quality-fixtures.yaml', scenarios: [{id: 'button.one'}, {id: 'button.two'}, {id: 'button.three'}]})
  git(f.root, 'add', 'gallery', 'specs'); git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'changed gallery B')
  const candidate = git(f.root, 'rev-parse', 'HEAD')
  git(f.root, 'checkout', '--detach', f.head)
  const caller = readWorkflow('verify-gallery-receipt.yml').split('\n  sign:\n')[1].split('\n  post:')[0]
  const callee = readWorkflow('sign-gallery-receipt.yml')
  const value = '{"schemaVersion":1,"apiRevision":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}'
  const outputs = {envelope: Buffer.from(JSON.stringify({schemaVersion: 1, repository: REPO,
    runId: '202', attempt: '1', revision: f.head, response: {name: 'RELEASE_GATE_POLICY', value}})).toString('base64'),
    'policy-sha256': createHash('sha256').update(value).digest('hex')}
  const inputs = {}
  for (const match of caller.matchAll(/^      ([\w-]+): \$\{\{ needs\.policy-reader\.outputs\.([\w-]+) \}\}$/gm)) inputs[match[1]] = outputs[match[2]]
  assert.equal(inputs['policy-envelope'], outputs.envelope, 'caller must hand off reader bytes')
  assert.equal(inputs['policy-sha256'], outputs['policy-sha256'], 'caller must hand off reader digest')
  assert.match(caller, /needs: \[[^\]]*policy-reader/)
  const env = {GITHUB_OUTPUT: resolve(f.root, 'signer-outputs'), RELEASE_APPROVED_COMMIT: ''}
  for (const match of callee.matchAll(/^          (RELEASE_POLICY_\w+): \$\{\{ inputs\.([\w-]+) \}\}$/gm)) env[match[1]] = inputs[match[2]]
  assert.equal(env.RELEASE_POLICY_ENVELOPE, outputs.envelope, 'callee must supply planning bytes')
  const result = runReceiptCli(f, 'plan', {queueHead: candidate, env})
  assert.equal(result.status, 0, result.stderr + JSON.stringify(result.requests))
  assert.match(readFileSync(env.GITHUB_OUTPUT, 'utf8'), /changed=true/)
  assert.equal(result.requests.some(request => request.endpoint?.includes('/actions/variables/')), false, 'signer never reads Variables with its default token')
  const missing = runReceiptCli(f, 'plan', {queueHead: candidate, env: {...env, RELEASE_POLICY_SHA256: ''}})
  assert.notEqual(missing.status, 0)
})

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

test('plan actions are pinned and the credentialed ledger checkout uses the trusted workflow SHA directly', () => {
  const workflow = readWorkflow('verify-gallery-receipt.yml')
  const plan = workflow.split('\n  plan:\n')[1].split('\n  ledger-data:\n')[0]
  const ledger = workflow.split('\n  ledger-data:\n')[1].split('\n  gallery-shard:\n')[0]
  const uses = [...plan.matchAll(/^\s*- uses: (.+)$/gm)].map(match => match[1])
  assert.deepEqual(uses, [
    'actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7',
    'actions/setup-node@820762786026740c76f36085b0efc47a31fe5020 # v7',
  ])
  assert.match(ledger, /uses: actions\/checkout@[0-9a-f]{40} # v7\n        with:\n          ref: \$\{\{ github.sha \}\}/)
  assert.doesNotMatch(ledger, /needs\.plan\.outputs\.revision/)
})

function runLedgerCli(f, revision) {
  const source = resolve(f.root, 'control-authority')
  const destination = resolve(f.root, 'ledger-data')
  const log = resolve(f.root, 'ledger-reads.jsonl')
  put(f.root, 'control-authority/research/R-0001-fixture/note.md', 'reach-platform: test\nprivate prose omitted\n')
  put(f.root, 'control-authority/tickets/T-0001-fixture/ticket.md', 'status: READY\n')
  put(f.root, 'ledger-reads.jsonl', '')
  // Observe actual private-tree filesystem access in the child, not just the final exit code.
  put(f.root, 'ledger-observer.mjs', `
import fs from 'node:fs'
import {syncBuiltinESMExports} from 'node:module'
const source = ${JSON.stringify(source)}
const log = ${JSON.stringify(log)}
for (const method of ['readdirSync', 'readFileSync']) {
  const original = fs[method]
  fs[method] = (path, ...args) => {
    if (String(path).startsWith(source)) fs.appendFileSync(log, JSON.stringify({method}) + '\\n')
    return original(path, ...args)
  }
}
syncBuiltinESMExports()
`)
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(resolve(f.root, 'ledger-observer.mjs')).href,
    resolve(import.meta.dirname, '../gates/scan-evidence-provenance.mjs'), '--receipt', 'ledger-data', source, destination],
  {cwd: f.root, encoding: 'utf8', timeout: 15000, env: {...process.env, GITHUB_SHA: revision}})
  const reads = readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(line => JSON.parse(line))
  return {...result, reads, destination}
}

test('ledger-data CLI reads and exports ledger metadata only at the trusted workflow revision', t => {
  const f = fixture(t)
  const result = runLedgerCli(f, f.head)
  assert.equal(result.status, 0, result.stderr)
  assert.ok(result.reads.some(read => read.method === 'readFileSync'))
  assert.equal(readFileSync(resolve(result.destination, 'research/R-0001-fixture/note.md'), 'utf8'), 'reach-platform: test\n')
  assert.equal(readFileSync(resolve(result.destination, 'tickets/T-0001-fixture/ticket.md'), 'utf8'), 'status: READY\n')
})

for (const [label, revision] of [['mismatched', 'b'.repeat(40)], ['missing', undefined], ['malformed', 'not-a-sha']]) {
  test(`ledger-data CLI refuses a ${label} GITHUB_SHA before reading control-authority`, t => {
    const f = fixture(t)
    const result = runLedgerCli(f, revision)
    assert.equal(result.status, 1)
    assert.deepEqual(result.reads, [])
  })
}

test('every external action in the receipt workflow is pinned; only the two same-revision reusable workflows are exempt', () => {
  const workflow = readWorkflow('verify-gallery-receipt.yml')
  const uses = [...workflow.matchAll(/^\s*(?:-\s+)?uses:\s*(.+)$/gm)].map(match => match[1])
  // ADR-0139 decision 3 and ADR-0198 decision 2 require the caller's own main revision.
  const local = ['./.github/workflows/sign-gallery-receipt.yml', './.github/workflows/license-sbom.yml']
  assert.ok(uses.length > 1)
  for (const reference of local) assert.equal(uses.filter(value => value === reference).length, 1)
  for (const reference of uses) {
    if (local.includes(reference)) continue
    assert.match(reference, /^[\w.-]+\/[\w./-]+@[0-9a-f]{40} # v\d+(?:\s.*)?$/,
      `receipt workflow action must use an immutable commit and version comment: ${reference}`)
  }
})

// ADR-0139 d1-2 / ADR-0198 d2,4,9: raw observations cannot author checked facts.
test('trusted split derives inventory and reconciliation from Git data, not candidate tallies', t => {
  assert.equal(typeof releaseEvidence.collectTrustedGateData, 'function')
  const f = fixture(t)
  const reported = {...f.gate, counts: {nativeTests: 37, galleryScenarios: 999}, requiredStepIds: ['forged'],
    results: STEPS.map(id => ({id, passed: true, exitCode: 0}))}
  const collected = releaseEvidence.collectTrustedGateData({...f.args, gate: reported})
  assert.equal(collected.counts.galleryScenarios, 2)
  assert.equal(collected.counts.generationSmokeModules, 1)
  assert.equal(collected.invariants?.expectedSharedResults, 2)
  assert.equal(collected.invariants?.expectedGenerationSmokeModules, 1)
  assert.equal(collected.counts.nativeTests, 37)
  assert.equal(collected.assurance.counts.nativeTests, 'run-reported')
  assert.deepEqual(collected.requiredStepIds, STEPS)
  assert.equal(collected.scenarioReconciliation, 'exact')
  assert.equal(collected.assurance.results, 'run-reported')
  assert.equal(collected.assurance.requiredStepIds, 'signer-checked')
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: {...reported, status: 'FAIL'}}), /reported gate/)
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: {...reported,
    subject: {...reported.subject, testedTree: 'a'.repeat(40)}}}), /subject/)
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: {...reported,
    results: reported.results.slice(1)}}), /fresh.*steps/)
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: reported, shards: []}), /shard/)
  for (const provenance of [{sourceHead: 'b'.repeat(40), runId: '202'}, {sourceHead: f.head, runId: '9001'}])
    assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: reported,
      shards: [{...f.shards[0], provenance}]}), /shard invocation/)
  const incomplete = structuredClone(f.shards); incomplete[0].playwrightReport.suites[0].specs[0].tests[0].results[0].annotations = []
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: reported, shards: incomplete}), /check reconciliation/)
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: {...reported, counts: {nativeTests: 0}}}), /native test count/)
  const failed = structuredClone(reported); failed.results[7].passed = false; failed.results[7].exitCode = 1
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: failed}), /fresh.*steps/)
})

// ADR-0139 d1: candidate code can forge results, but cannot supply main's checked reconciliation.
test('restore-time gallery runner overwrite supplies only untrusted observations', t => {
  const f = fixture(t)
  const raw = structuredClone(f.shards[0])
  raw.playwrightReport.suites[0].specs[0].tests[0].results[0].annotations = []
  raw.counts = {galleryScenarios: 999, galleryChecks: CHECKS}
  put(f.root, 'tooling/run-gallery-gate.mjs', 'throw new Error("original runner must be overwritten")')
  put(f.root, 'overwrite.cjs', `require('node:fs').writeFileSync('tooling/run-gallery-gate.mjs', ${JSON.stringify(`console.log(${JSON.stringify(JSON.stringify(raw))})`)})`)
  put(f.root, 'candidate.proj', '<Project><Target Name="Restore"><Exec Command="node overwrite.cjs" /></Target></Project>')
  const restore = spawnSync('dotnet', ['msbuild', 'candidate.proj', '-t:Restore', '-m:1', '-nr:false'],
    {cwd: f.root, encoding: 'utf8', env: {...process.env, DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1'}})
  assert.equal(restore.status, 0, restore.stderr + restore.stdout)
  const runner = spawnSync(process.execPath, ['tooling/run-gallery-gate.mjs'], {cwd: f.root, encoding: 'utf8'})
  assert.equal(runner.status, 0, runner.stderr)
  const forged = JSON.parse(runner.stdout)
  assert.equal(forged.passed, true) // Literal attacker output; this is not a trusted PASS.
  const reported = {...f.gate, results: STEPS.map(id => ({id, passed: true, exitCode: 0}))}
  assert.throws(() => releaseEvidence.collectTrustedGateData({...f.args, gate: reported, shards: [forged]}),
    /raw check reconciliation failed/)
})

for (const eventName of ['workflow_run', 'push']) test(`trusted collector CLI digests and reconciles raw archives on ${eventName}`, t => {
  const f = fixture(t), work = mkdtempSync(resolve(tmpdir(), 'collector-cli-'))
  t.after(() => rmSync(work, {recursive: true, force: true}))
  // Commit fixture setup so the real CLI enforces a clean main data checkout.
  git(f.root, 'add', '.'); git(f.root, '-c', 'core.hooksPath=', 'commit', '-m', 'trusted fixture data')
  const revision = git(f.root, 'rev-parse', 'HEAD'), head = eventName === 'push' ? revision : f.head
  const gate = {...f.gate, subject: {baseHead: head, testedTree: git(f.root, 'rev-parse', `${head}^{tree}`)},
    counts: {nativeTests: 37, galleryScenarios: 999}, results: STEPS.map(id => ({id, passed: true, exitCode: 0}))}
  const files = [{name: 'candidate-phase4-202-1', file: 'phase4.json', data: gate},
    ...[1, 2, 3, 4, 5, 6].map(id => ({name: `receipt-shard-202-1-${id}`, file: `gallery-shard-${id}.json`,
      data: {...f.shards[0], shard: id, shards: 6, provenance: {sourceHead: head, runId: '202'},
        playwrightReport: id === 1 ? f.shards[0].playwrightReport : {stats: {expected: 0, unexpected: 0, skipped: 0, flaky: 0}, suites: []}}}))]
  const routes = {}, artifacts = []
  for (const [i, input] of files.entries()) {
    const bytes = execFileSync(process.platform === 'win32' ? 'python' : 'python3', ['-c',
      'import sys,io,zipfile;b=io.BytesIO();z=zipfile.ZipFile(b,"w");z.writestr(sys.argv[1],sys.stdin.buffer.read());z.close();sys.stdout.buffer.write(b.getvalue())', input.file],
      {input: JSON.stringify(input.data)})
    artifacts.push({id: i + 10, name: input.name, expired: false, workflow_run: {id: 202},
      digest: 'sha256:' + createHash('sha256').update(bytes).digest('hex')})
    routes[`repos/${REPO}/actions/artifacts/${i + 10}/zip`] = {archiveBase64: bytes.toString('base64')}
  }
  routes[`repos/${REPO}/actions/runs/202/artifacts?per_page=100`] = {total_count: 7, artifacts}
  routes[`repos/${REPO}/actions/runs/101/attempts/2`] = {...f.run, head_sha: head}
  put(work, 'event.json', eventName === 'push' ? {after: head} : {workflow_run: {...f.run, head_sha: head,
    updated_at: '2026-11-10T15:00:00Z'}})
  put(work, 'boundary.mjs', GH_BOUNDARY)
  put(work, 'requests.jsonl', '')
  put(work, 'fixture.json', {routes, log: resolve(work, 'requests.jsonl'), rejectPaths: [], now: f.args.now.getTime()})
  const cli = resolve(import.meta.dirname, '../gates/scan-evidence-provenance.mjs')
  const run = () => spawnSync(process.execPath, ['--import', pathToFileURL(resolve(work, 'boundary.mjs')).href,
    cli, '--receipt', 'collect'], {cwd: f.root, encoding: 'utf8', timeout: 30000, env: {...process.env,
      RECEIPT_CLI_FIXTURE: resolve(work, 'fixture.json'), GITHUB_EVENT_PATH: resolve(work, 'event.json'),
      GITHUB_EVENT_NAME: eventName, GITHUB_REF: 'refs/heads/main', GITHUB_REPOSITORY: REPO,
      GITHUB_SHA: revision, GITHUB_RUN_ID: '202', GITHUB_RUN_ATTEMPT: '1', RUNNER_TEMP: work,
      RECEIPT_DIRECTORY: resolve(work, 'receipt'), RELEASE_APPROVED_COMMIT: ''}})
  const result = run()
  assert.equal(result.status, 0, result.stderr)
  const collected = JSON.parse(readFileSync(resolve(work, 'receipt/phase4.json')))
  assert.equal(collected.counts.galleryScenarios, 2)
  assert.equal(collected.subject.testedTree, git(f.root, 'rev-parse', `${head}^{tree}`))
  assert.equal(collected.assurance.results, 'run-reported')
  // A new invocation with altered archive metadata must fail before collection.
  const originalDigest = artifacts[0].digest
  const save = () => put(work, 'fixture.json', {routes, log: resolve(work, 'requests.jsonl'), rejectPaths: [], now: f.args.now.getTime()})
  artifacts[0].digest = 'sha256:' + '0'.repeat(64); save()
  assert.equal(run().status, 1, 'altered archive bytes refuse')
  artifacts[0].digest = originalDigest
  routes[`repos/${REPO}/actions/runs/202/artifacts?per_page=100`].total_count = 8; save()
  assert.equal(run().status, 1, 'incomplete artifact census refuses')
  routes[`repos/${REPO}/actions/runs/202/artifacts?per_page=100`].total_count = 7
  artifacts.push({...artifacts[0], id: 99})
  routes[`repos/${REPO}/actions/runs/202/artifacts?per_page=100`].total_count = 8; save()
  assert.equal(run().status, 1, 'duplicate artifact name refuses')
  artifacts.pop(); routes[`repos/${REPO}/actions/runs/202/artifacts?per_page=100`].total_count = 7
  const archiveRoute = routes[`repos/${REPO}/actions/artifacts/10/zip`], originalArchive = archiveRoute.archiveBase64
  const extra = execFileSync(process.platform === 'win32' ? 'python' : 'python3', ['-c',
    'import sys,io,zipfile;b=io.BytesIO();z=zipfile.ZipFile(b,"w");z.writestr("phase4.json",sys.stdin.buffer.read());z.writestr("extra.mjs","throw 1");z.close();sys.stdout.buffer.write(b.getvalue())'], {input: JSON.stringify(gate)})
  archiveRoute.archiveBase64 = extra.toString('base64')
  artifacts[0].digest = 'sha256:' + createHash('sha256').update(extra).digest('hex'); save()
  assert.equal(run().status, 1, 'extra archive entry refuses even with correct transport digest')
  archiveRoute.archiveBase64 = originalArchive; artifacts[0].digest = originalDigest; save()
  put(f.root, 'untracked-candidate-helper.mjs', 'throw new Error("must never run")')
  assert.equal(run().status, 1, 'dirty trusted checkout refuses')
})

test('trusted collector writes eligible receipt bytes consumed by the production release signer', t => {
  const f = releaseFixture(t), work = mkdtempSync(resolve(tmpdir(), 'collector-release-'))
  t.after(() => rmSync(work, {recursive: true, force: true}))
  const root = resolve(work, 'main'), directory = resolve(work, 'receipt'), routes = {}
  execFileSync('git', ['clone', '--no-checkout', f.root, root], {stdio: 'pipe'})
  git(root, 'checkout', '--detach', f.A)
  const inputs = [{file: 'phase4.json', name: 'candidate-phase4-9002-1'},
    ...[1, 2, 3, 4, 5, 6].map(id => ({file: `gallery-shard-${id}.json`, name: `receipt-shard-9002-1-${id}`}))]
  const artifacts = []
  for (const [i, input] of inputs.entries()) {
    const bytes = execFileSync(process.platform === 'win32' ? 'python' : 'python3', ['-c',
      'import sys,io,zipfile;b=io.BytesIO();z=zipfile.ZipFile(b,"w");z.writestr(sys.argv[1],sys.stdin.buffer.read());z.close();sys.stdout.buffer.write(b.getvalue())', input.file],
      {input: readFileSync(resolve(f.directory, input.file))})
    artifacts.push({id: i + 10, name: input.name, expired: false, workflow_run: {id: 9002},
      digest: 'sha256:' + f.hash(bytes)})
    routes[`repos/${REPO}/actions/artifacts/${i + 10}/zip`] = {archiveBase64: bytes.toString('base64')}
  }
  routes[`repos/${REPO}/actions/runs/9002/artifacts?per_page=100`] = {total_count: 7, artifacts}
  routes[`repos/${REPO}/actions/runs/9001/attempts/2`] = f.routes['actions/runs/9001/attempts/2']
  put(work, 'event.json', {workflow_run: {...f.routes['actions/runs/9001/attempts/2'], updated_at: '2026-11-21T16:00:00Z'}})
  put(work, 'boundary.mjs', GH_BOUNDARY); put(work, 'requests.jsonl', '')
  put(work, 'fixture.json', {routes, log: resolve(work, 'requests.jsonl'), rejectPaths: [], now: f.now.getTime()})
  const value = JSON.stringify(f.policy), envelope = Buffer.from(JSON.stringify({schemaVersion: 1, repository: REPO,
    runId: '9002', attempt: '1', revision: f.A, response: {name: 'RELEASE_GATE_POLICY', value}})).toString('base64')
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(resolve(work, 'boundary.mjs')).href,
    resolve(import.meta.dirname, '../gates/scan-evidence-provenance.mjs'), '--receipt', 'collect'],
    {cwd: root, encoding: 'utf8', timeout: 30000, env: {...process.env, RECEIPT_CLI_FIXTURE: resolve(work, 'fixture.json'),
      GITHUB_EVENT_PATH: resolve(work, 'event.json'), GITHUB_EVENT_NAME: 'workflow_run', GITHUB_REF: 'refs/heads/main',
      GITHUB_REPOSITORY: REPO, GITHUB_SHA: f.A, GITHUB_RUN_ID: '9002', GITHUB_RUN_ATTEMPT: '1', RUNNER_TEMP: work,
      RECEIPT_DIRECTORY: directory, RELEASE_APPROVED_COMMIT: f.B, RELEASE_POLICY_ENVELOPE: envelope,
      RELEASE_POLICY_SHA256: f.policySha256}})
  assert.equal(result.status, 0, result.stderr)
  for (const name of ['phase4.json', 'release-phase4-receipt.json', 'release-receipt.json'])
    assert.equal(existsSync(resolve(directory, name)), true, `trusted writer must produce ${name}`)
  const receipt = JSON.parse(readFileSync(resolve(directory, 'release-phase4-receipt.json')))
  assert.equal(receipt.baseHead, f.B)
  assert.equal(receipt.reportSha256, f.hash(readFileSync(resolve(directory, 'phase4.json'))))
  assert.equal(receipt.observations.length, 7)
  assert.equal(receipt.gate.assurance.results, 'run-reported')
  for (const file of ['sbom.spdx.json', 'sbom-evidence.json', 'license-policy.cjs', 'syft.yaml'])
    put(directory, `sbom/${file}`, readFileSync(resolve(f.directory, 'sbom', file)).toString('utf8'))
  const sealed = releaseEvidence.sealReleaseGateEvidence({root, directory, head: f.B, producer: {id: 9002, attempt: 1, revision: f.A},
    queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}, policySha256: f.policySha256,
    queueCompletedAt: '2026-11-21T16:00:00Z', now: f.now})
  const predicate = releaseEvidence.buildReleaseGatePredicate({...f.args, root, directory, expected: {...f.args.expected, ...sealed}})
  assert.equal(predicate.receiptSha256, f.hash(readFileSync(resolve(directory, 'release-phase4-receipt.json'))))
  assert.equal(predicate.companionSha256, f.hash(readFileSync(resolve(directory, 'release-receipt.json'))))
})
