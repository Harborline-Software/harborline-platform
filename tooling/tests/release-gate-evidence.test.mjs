// Oracles: T-1138 release.shared-signer-policy; ADR-0198 decision 5.
import assert from 'node:assert/strict'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, symlinkSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, join, relative, resolve} from 'node:path'
import {pathToFileURL} from 'node:url'
import {execFileSync, spawnSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import test from 'node:test'
import * as evidence from '../gates/scan-evidence-provenance.mjs'
import * as mainGate from '../run-phase-4-gate.mjs'
import {hashStepInputs} from '../gate-step-evidence.mjs'
import {requiredStepIds as actualRequiredStepIds} from '../gate-contract.mjs'

// ADR-0198 required-check trust inventory requires the complete phase-4 contract.
// Independent literal baseline: platform main 0924f437 gate-contract.mjs; never derived at runtime.
const EXPECTED_RELEASE_STEPS = ['root-clean-install', 'dependency-ledger', 'npm-clean-install',
  'forms-contracts-clean-install', 'rule-runtime-clean-install', 'rule-authoring-clean-install',
  'copilot-contracts-clean-install', 'dotnet-restore', 'generation-smoke', 'ui-spec-authority',
  'catalog-preflight', 'tooling-selftests', 'sibling-package-origins', 'prop-vocabulary', 'ui-gate-model',
  'build', 'native-tests', 'perf-budgets', 'ui-shared-conformance', 'package-consumers', 'gallery-gate',
  'release-receipt', 'catalog-final']
test('independent required inventory detects an omitted production obligation', () => {
  assert.deepEqual(actualRequiredStepIds, EXPECTED_RELEASE_STEPS)
})

const workflow = name => readFileSync(new URL(`../../.github/workflows/${name}.yml`, import.meta.url), 'utf8')
const job = (text, name) => text.split(`\n  ${name}:\n`)[1]?.split(/\n  [a-z][a-z0-9-]*:\n/)[0]

// R-0224: outputs are current-attempt planning data, not a first-asset live read.
test('R-0224: policy outputs bind repository, run, attempt, source and exact policy bytes', () => {
  assert.equal(typeof evidence.readPolicySnapshot, 'function')
  const context = {repository: 'Harborline-Software/harborline-platform', runId: '9002', attempt: '1', revision: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'}
  const response = {name: 'RELEASE_GATE_POLICY', value: '{"schemaVersion":1}'}
  const snapshot = {...context, schemaVersion: 1, response}
  const digest = createHash('sha256').update(response.value).digest('hex')
  const read = value => evidence.readPolicySnapshot({envelope: Buffer.from(JSON.stringify(value)).toString('base64'), digest, context})
  assert.deepEqual(read(snapshot), response)
  for (const field of ['repository', 'runId', 'attempt', 'revision'])
    assert.throws(() => read({...snapshot, [field]: 'other'}), /policy snapshot/)
  assert.throws(() => read({...snapshot, response: {...response, value: '{}'}}), /policy snapshot/)
  assert.throws(() => evidence.readPolicySnapshot({envelope: '', digest, context}), /policy snapshot/)
})

test('R-0224: readers isolate the Variables credential and earlier outputs cannot activate assets', () => {
  const release = workflow('release-platform'), producer = workflow('verify-gallery-receipt')
  for (const [text, names] of [[release, ['policy-stage', 'policy-publish']], [producer, ['policy-reader']]]) {
    for (const name of names) {
      const reader = job(text, name)
      assert.ok(reader, name)
      assert.match(reader, /environment: release-policy-reader/)
      assert.match(reader, /POLICY_READ_TOKEN: \$\{\{ secrets.RELEASE_POLICY_READ_TOKEN \}\}/)
      assert.match(reader, /GH_TOKEN="\$POLICY_READ_TOKEN" gh api/)
      assert.doesNotMatch(reader, /actions\/checkout|node tooling|pnpm|id-token: write|attestations: write/)
    }
  }
  for (const name of ['stage', 'publish']) assert.doesNotMatch(job(release, name), /secrets.RELEASE_POLICY_READ_TOKEN|actions\/variables\/RELEASE_GATE_POLICY/)
  assert.doesNotMatch(job(producer, 'plan'), /secrets.RELEASE_POLICY_READ_TOKEN/)
  const publish = job(release, 'publish')
  assert.ok(publish.indexOf('Refuse assets without a proved isolated final live read') < publish.indexOf('uses: actions/attest@'))
  assert.match(publish, /unproved first-asset live-read boundary[\s\S]*?exit 1/)
})

test('R-0224: actual isolated reader makes fresh credentialed requests, transfers only data and refuses denial or missing credentials', t => {
  const reader = job(workflow('release-platform'), 'policy-stage')
  const script = reader.split('        run: |\n')[1].split('\n').map(line => line.slice(10)).join('\n')
  const directory = mkdtempSync(join(tmpdir(), 'policy-reader-'))
  t.after(() => rmSync(directory, {recursive: true, force: true}))
  const context = {repository: 'Harborline-Software/harborline-platform', runId: '9004', attempt: '1', revision: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'}
  const run = (name, {token = 'fixture-variables-read', denied = false, value = '{"schemaVersion":1}'} = {}) => {
    const output = join(directory, name), calls = join(directory, 'calls')
    const prelude = `gh() {
      test "$GH_TOKEN" = fixture-variables-read || return 22
      test "$DENIED" = false || return 23
      printf 'request\\n' >> "$CALLS"
      printf '%s' "$API_RESPONSE"
    }\n`
    const result = spawnSync('bash', ['-c', prelude + script], {encoding: 'utf8', env: {...process.env,
      GH_TOKEN: 'fixture-actions-read', POLICY_READ_TOKEN: token, DENIED: String(denied), CALLS: calls,
      API_RESPONSE: JSON.stringify({name: 'RELEASE_GATE_POLICY', value}), GITHUB_OUTPUT: output,
      GITHUB_REPOSITORY: context.repository, GITHUB_RUN_ID: context.runId, GITHUB_RUN_ATTEMPT: context.attempt, GITHUB_SHA: context.revision}})
    const bytes = existsSync(output) ? readFileSync(output, 'utf8') : ''
    assert.doesNotMatch(bytes + result.stdout + result.stderr, /fixture-variables-read|fixture-actions-read/)
    return {result, bytes}
  }
  const first = run('first')
  assert.equal(first.result.status, 0, first.result.stderr)
  const values = Object.fromEntries(first.bytes.trim().split('\n').map(line => { const i = line.indexOf('='); return [line.slice(0, i), line.slice(i + 1)] }))
  assert.equal(evidence.readPolicySnapshot({envelope: values.envelope, digest: values['policy-sha256'], context}).value, '{"schemaVersion":1}')
  const next = run('next', {value: '{"schemaVersion":1,"revoked":true}'})
  assert.equal(next.result.status, 0, next.result.stderr)
  assert.notEqual(first.bytes, next.bytes)
  assert.equal(readFileSync(join(directory, 'calls'), 'utf8'), 'request\nrequest\n')
  for (const [name, options] of [['missing', {token: ''}], ['denied', {denied: true}], ['malformed', {value: 'not JSON'}]]) {
    const failed = run(name, options)
    assert.notEqual(failed.result.status, 0, name)
    assert.equal(failed.bytes, '', name)
  }
})

test('R-0225: every owned test-bearing gate provisions pinned Syft and exports its path before execution', () => {
  for (const [file, name] of [['verify', 'phase-4-gate'], ['verify-gallery-receipt', 'gallery-shard'],
    ['verify-gallery-receipt', 'phase-4']]) {
    const text = job(workflow(file), name)
    assert.match(text, /id: test-syft/)
    assert.match(text, /uses: anchore\/sbom-action\/download-syft@e22c389904149dbc22b58101806040fa8d37a610/)
    assert.match(text, /syft-version: v1\.57\.2/)
    assert.match(text, /HARBORLINE_TEST_SYFT=\$\{\{ steps.test-syft.outputs.cmd \}\}/)
    assert.ok(text.indexOf('id: test-syft') < text.indexOf('node tooling/run-')
      || text.indexOf('node tooling/run-') === -1)
  }
})

test('release.main-orchestration-inputs: producer selects immutable API data and invokes the main receipt entry outside the subject', () => {
  const producer = workflow('verify-gallery-receipt')
  for (const name of ['gallery-shard', 'phase-4']) {
    const steps = job(producer, name)
    assert.match(steps, /API_REVISION: \$\{\{ needs.plan.outputs.api-revision \}\}/)
    assert.match(steps, /git checkout --detach "\$API_REVISION"/)
    assert.doesNotMatch(steps, /git clone --quiet https:/)
    assert.ok(steps.indexOf('package-manager-cache: false') < steps.indexOf('--prepare-trusted-orchestration'))
    assert.ok(steps.indexOf('--prepare-trusted-orchestration') < steps.indexOf('pnpm/action-setup@'))
  }
  const phase4 = job(producer, 'phase-4')
  assert.match(phase4, /HARBORLINE_SUBJECT_ROOT: \$\{\{ github.workspace \}\}/)
  assert.match(phase4, /node \.trusted-orchestration\/tooling\/run-phase4-receipt.mjs/)
  assert.match(phase4, /HARBORLINE_ORCHESTRATION_REVISION: \$\{\{ needs.plan.outputs.revision \}\}/)
  assert.match(phase4, /HARBORLINE_API_REVISION: \$\{\{ needs.plan.outputs.api-revision \}\}/)
})

function copyProductionClosure(destination, entry) {
  const repository = resolve(import.meta.dirname, '../..'), copied = new Set()
  const copy = name => {
    if (copied.has(name)) return
    copied.add(name)
    const source = resolve(repository, name), bytes = readFileSync(source)
    mkdirSync(dirname(join(destination, name)), {recursive: true})
    writeFileSync(join(destination, name), bytes)
    for (const match of bytes.toString().matchAll(/from\s+['"](\.[^'"]+)['"]/g))
      copy(relative(repository, resolve(dirname(source), match[1])))
  }
  copy(entry)
}

function orchestrationFixture(t) {
  const root = mkdtempSync(join(tmpdir(), 'release-orchestration-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  const put = (name, bytes) => { mkdirSync(dirname(join(root, name)), {recursive: true}); writeFileSync(join(root, name), bytes) }
  const git = (...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
    env: {...process.env, GIT_AUTHOR_NAME: 'Orchestration Fixture', GIT_COMMITTER_NAME: 'Orchestration Fixture',
      GIT_AUTHOR_EMAIL: 'fixture@example.invalid', GIT_COMMITTER_EMAIL: 'fixture@example.invalid'}}).trim()
  git('init', '-b', 'main')
  copyProductionClosure(root, 'tooling/run-phase4-receipt.mjs')
  copyProductionClosure(root, 'tooling/run-phase-4-gate.mjs')
  put('tooling/main-gate-logic.mjs', readFileSync(join(root, 'tooling/run-phase-4-gate.mjs')))
  put('tooling/run-phase-4-gate.mjs', `
    import {appendFileSync, readFileSync, writeFileSync} from 'node:fs'; import {resolve} from 'node:path';
    import * as mainLogic from './main-gate-logic.mjs';
    import {createReusableStepRunner} from './main-gate-logic.mjs';
    import {loadPreviousPassEvidence} from './gate-step-evidence.mjs';
    const requiredStepIds = ${JSON.stringify(EXPECTED_RELEASE_STEPS)};
    export {trustedOrchestrationInventory, applyTrustedOrchestration, createTrustedStepExecutor} from './main-gate-logic.mjs';
    if (import.meta.main) process.once('message', () => {
      const root = resolve(process.env.HARBORLINE_GATE_ROOT ?? resolve(import.meta.dirname, '..')), results = [], reports = new Map();
      const dispatch = mainLogic.createPhase4StepDispatcher({repositoryRoot: root,
        sourceRoot: resolve(import.meta.dirname, '..'), revision: process.env.HARBORLINE_ORCHESTRATION_REVISION,
        results, reports, failureEvidenceDirectory: resolve(root, '.claude/gate-evidence')});
      const run = id => {
        appendFileSync(process.env.ORCHESTRATION_LOG, id + '\\n');
        if (id === 'dotnet-restore' && process.env.RESTORE_CANARY === '1')
          return dispatch(id, 'dotnet', ['msbuild', 'projections/restore.proj', '-t:Restore', '-m:1', '-nr:false', '-v:quiet']);
        if (id === 'native-tests') return dispatch(id, process.execPath, ['tooling/later-helper.mjs']);
        return dispatch(id, process.execPath, ['-e', 'process.exit(0)']);
      };
      const reuse = createReusableStepRunner({env: process.env, repositoryRoot: root,
        testedTree: process.env.HARBORLINE_TESTED_TREE,
        previousPass: loadPreviousPassEvidence(resolve(root, 'docs/evidence/phase-4/gate.json')),
        results, reports, run});
      let status = 'PASS';
      try { for (const id of requiredStepIds) id === 'native-tests' ? reuse(id, 'fixture', [], root, true) : run(id); }
      catch { status = 'FAIL'; }
      // A child can return a well-shaped PASS after its final dispatch. Only the parent's
      // independently measured acceptance check can reject these changed bytes.
      if (process.env.MAIN_REPORT_MUTATION === 'parent')
        writeFileSync(resolve(root, 'tooling/later-helper.mjs'), 'process.exit(0)');
      if (process.env.MAIN_REPORT_MUTATION === 'omit') results.pop();
      if (process.env.MAIN_REPORT_MUTATION === 'reused') results[0].reusedFrom = {source: 'cached-pass'};
      console.log(JSON.stringify({schemaVersion: 3, phase: 4, status, requiredStepIds, results,
        subject: {repository: 'harborline-platform', baseHead: process.env.HARBORLINE_BASE_HEAD,
          testedTree: process.env.HARBORLINE_TESTED_TREE, mode: 'exact-staged-tree'}, designReview: {expired: []}}));
      process.exitCode = status === 'PASS' ? 0 : 1; process.disconnect();
    });
  `)
  put('tooling/later-helper.mjs', 'process.exit(process.env.MAIN_SHOULD_FAIL === \"1\" ? 1 : 0)');
  put('package.json', '{"packageManager":"pnpm@10.0.0"}')
  put('pnpm-lock.yaml', 'lockfileVersion: 9.0\n')
  put('global.json', '{"sdk":{"version":"11.0.100"}}')
  put('gallery/tests/package.json', '{"name":"fixture-gallery","private":true}')
  put('tooling/tests/main-input.test.mjs', 'export const role = "main";\n')
  put('projections/product.cs', '// product A\n')
  const seed = JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision: '1.0.0', items: [{id: 'fixture'}]})
  const seedDigest = createHash('sha256').update(`${seed}\n`).digest('hex')
  put('_shared/packs/platform/platform-pack.export.json', `${seed.slice(0, -1)},"digest":{"algorithm":"sha256","value":"${seedDigest}"}}\n`)
  git('add', '.'); git('commit', '-m', 'trusted A')
  const A = git('rev-parse', 'HEAD'), treeA = git('rev-parse', 'HEAD^{tree}')
  const fakeReport = `({schemaVersion:3,phase:4,status:'PASS',requiredStepIds:${JSON.stringify(EXPECTED_RELEASE_STEPS)},
    results:${JSON.stringify(EXPECTED_RELEASE_STEPS)}.map(id=>({id,passed:true,exitCode:0,report:{tests:1,pass:1}})),
    subject:{repository:'harborline-platform',baseHead:process.env.HARBORLINE_BASE_HEAD,
      testedTree:process.env.HARBORLINE_TESTED_TREE,mode:'exact-staged-tree'},designReview:{expired:[]}})`
  put('tooling/run-phase-4-gate.mjs', `import {appendFileSync} from 'node:fs'; process.once('message',()=>{
    appendFileSync(process.env.ORCHESTRATION_LOG,'candidate-gate\\n'); console.log(JSON.stringify(${fakeReport})); process.disconnect(); });`)
  put('tooling/gate-contract.mjs', 'export const requiredStepIds = [];\n')
  put('tooling/collect-gallery-shards.mjs', 'export const forged = true;\n')
  put('tooling/tests/main-input.test.mjs', 'export const role = "candidate";\n')
  put('tooling/rogue.sh', '#!/usr/bin/env bash\nexit 0\n')
  put('tooling/rogue.csproj', '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>')
  put('coverage.runsettings', '<RunSettings/>')
  put('.npmrc', 'node-options=--import ./tooling/rogue-loader.mjs\n')
  put('package.json', '{"packageManager":"pnpm@99.0.0"}')
  put('projections/product.cs', '// candidate product/test input must run\n')
  // Literal adversarial MSBuild target: write an unstaged success helper during restore.
  put('projections/restore.proj', `<Project><Target Name="Restore"><WriteLinesToFile Condition="'$(OVERWRITE_HELPER)' == '1'" File="../tooling/later-helper.mjs" Lines="process.exit(0)" Overwrite="true" /></Target></Project>`)

  git('add', '.')
  // Cache setup is not the assertion oracle: the independently stated obligation is that main's
  // failing native step executes once even when the existing reuse implementation finds a match.
  const inputHash = hashStepInputs({repositoryRoot: root, testedTree: git('write-tree'), stepId: 'native-tests'})
  put('docs/evidence/phase-4/gate.json', JSON.stringify({schemaVersion: 3, phase: 4, status: 'PASS',
    subject: {baseHead: A, testedTree: treeA}, designReview: {expired: []},
    results: [{id: 'native-tests', passed: true, exitCode: 0, inputHash, report: {tests: 1, pass: 1}}]}))
  git('add', '.'); git('commit', '-m', 'candidate B')
  const B = git('rev-parse', 'HEAD'), treeB = git('rev-parse', 'HEAD^{tree}')
  const source = join(root, '.trusted-orchestration')
  git('worktree', 'add', '--detach', source, A)
  return {root, source, A, B, treeB, git, put}
}

test('release.main-orchestration-inputs: complete footprint replaces all tooling/config formats, deletes additions and preserves candidate product inputs', t => {
  assert.equal(typeof mainGate.trustedOrchestrationInventory, 'function')
  assert.equal(typeof mainGate.applyTrustedOrchestration, 'function')
  const f = orchestrationFixture(t)
  const inventory = mainGate.trustedOrchestrationInventory({repositoryRoot: f.root, sourceRoot: f.source, revision: f.A})
  mainGate.applyTrustedOrchestration({repositoryRoot: f.root, sourceRoot: f.source, inventory})
  assert.equal(readFileSync(join(f.root, 'package.json'), 'utf8'), '{"packageManager":"pnpm@10.0.0"}')
  assert.equal(readFileSync(join(f.root, 'tooling/tests/main-input.test.mjs'), 'utf8'), 'export const role = "main";\n')
  assert.equal(readFileSync(join(f.root, 'projections/product.cs'), 'utf8'), '// candidate product/test input must run\n')
  for (const name of ['tooling/rogue.sh', 'tooling/rogue.csproj', 'coverage.runsettings', '.npmrc'])
    assert.equal(existsSync(join(f.root, name)), false, name)
  assert.equal(f.git('write-tree'), f.treeB, 'overlay must never relabel the original candidate Git tree')
  assert.equal(inventory.revision, f.A)
  assert.equal(inventory.testedTree, f.treeB)
  assert.ok(inventory.subjectInputs.some(entry => entry.path === 'projections/product.cs' && entry.mode === '100644'))
  mainGate.applyTrustedOrchestration({repositoryRoot: f.root, sourceRoot: f.source, inventory})
  assert.equal(f.git('write-tree'), f.treeB, 'repeated application preserves the same subject')
})

test('release.main-orchestration-inputs: a candidate tooling link is removed without following it', t => {
  const f = orchestrationFixture(t), outside = join(f.root, 'outside')
  mkdirSync(outside); writeFileSync(join(outside, 'sentinel'), 'untouched')
  renameSync(join(f.root, 'tooling'), join(f.root, 'saved-candidate-tooling'))
  symlinkSync(outside, join(f.root, 'tooling'), process.platform === 'win32' ? 'junction' : 'dir')
  const inventory = mainGate.trustedOrchestrationInventory({repositoryRoot: f.root, sourceRoot: f.source, revision: f.A})
  mainGate.applyTrustedOrchestration({repositoryRoot: f.root, sourceRoot: f.source, inventory})
  assert.equal(readFileSync(join(outside, 'sentinel'), 'utf8'), 'untouched')
  assert.equal(readFileSync(join(f.root, 'tooling/tests/main-input.test.mjs'), 'utf8'), 'export const role = "main";\n')
})

test('release.main-orchestration-inputs: a candidate config parent link refuses before writing outside the subject', t => {
  const f = orchestrationFixture(t), outside = join(f.root, 'outside')
  mkdirSync(outside); writeFileSync(join(outside, 'sentinel'), 'untouched')
  renameSync(join(f.root, 'gallery'), join(f.root, 'saved-candidate-gallery'))
  symlinkSync(outside, join(f.root, 'gallery'), process.platform === 'win32' ? 'junction' : 'dir')
  const inventory = mainGate.trustedOrchestrationInventory({repositoryRoot: f.root, sourceRoot: f.source, revision: f.A})
  assert.throws(() => mainGate.applyTrustedOrchestration({repositoryRoot: f.root, sourceRoot: f.source, inventory}), /candidate parent redirects/)
  assert.equal(readFileSync(join(outside, 'sentinel'), 'utf8'), 'untouched')
  assert.equal(existsSync(join(outside, 'tests/package.json')), false)
})

test('release.main-orchestration-inputs: a dirty main executable checkout refuses the inventory', t => {
  const f = orchestrationFixture(t)
  writeFileSync(join(f.source, 'tooling/injected.sh'), 'exit 0')
  assert.throws(() => mainGate.trustedOrchestrationInventory({repositoryRoot: f.root, sourceRoot: f.source, revision: f.A}), /main checkout is dirty/)
})

for (const [label, fail, mutation] of [['failure refuses', true, ''], ['positive passes', false, ''],
  ['incomplete inventory refuses', false, 'omit'], ['reused result refuses', false, 'reused'],
  ['wrong API refuses', false, 'api'], ['restore overwrite refuses', false, 'overwrite'], ['underlying failure refuses', true, 'underlying'], ['parent integrity refuses', false, 'parent']]) test(`release.main-orchestration-inputs: actual receipt internal checkout runs main fresh steps (${label})`, t => {
  assert.equal(typeof mainGate.trustedOrchestrationInventory, 'function')
  assert.equal(typeof mainGate.createPhase4StepDispatcher, 'function', 'production dispatch seam required')
  const f = orchestrationFixture(t)
  const inventory = mainGate.trustedOrchestrationInventory({repositoryRoot: f.root, sourceRoot: f.source, revision: f.A})
  mainGate.applyTrustedOrchestration({repositoryRoot: f.root, sourceRoot: f.source, inventory})
  const log = join(f.root, 'orchestration.log'), hook = join(f.root, 'forge-hook.mjs'), scratch = join(f.root, 'runner-scratch')
  mkdirSync(scratch)
  writeFileSync(log, '')
  writeFileSync(hook, `import cp from 'node:child_process'; import {writeFileSync} from 'node:fs'; import {syncBuiltinESMExports} from 'node:module';
    const actual = cp.execFileSync;
    cp.execFileSync = (command,args,options) => {
      if (command === 'git' && args[0] === 'fetch') {
        actual('git',['update-ref','refs/remotes/origin/main',process.env.FIXTURE_MAIN],options);
        const file = actual('git',['rev-parse','--path-format=absolute','--git-path','FETCH_HEAD'],{...options,encoding:'utf8'}).trim();
        writeFileSync(file,process.env.FIXTURE_MAIN+"\\t\\tbranch 'main' of fixture\\n"); return ''; }
      return actual(command,args,options); }; syncBuiltinESMExports();`)
  const result = spawnSync(process.execPath, ['--import', pathToFileURL(hook).href, join(f.source, 'tooling/run-phase4-receipt.mjs')],
    {encoding: 'utf8', timeout: 60000, cwd: f.root, env: {...process.env,
      TEMP: scratch, TMP: scratch, TMPDIR: scratch, FIXTURE_MAIN: f.A, ORCHESTRATION_LOG: log,
      MAIN_SHOULD_FAIL: fail ? '1' : '0', HARBORLINE_SUBJECT_ROOT: f.root,
      MAIN_REPORT_MUTATION: mutation, RESTORE_CANARY: ['overwrite', 'underlying'].includes(mutation) ? '1' : '0',
      OVERWRITE_HELPER: mutation === 'overwrite' ? '1' : '0', DOTNET_CLI_USE_MSBUILD_SERVER: '0', MSBUILDDISABLENODEREUSE: '1',
      HARBORLINE_ORCHESTRATION_REVISION: f.A, HARBORLINE_RECEIPT_RUN: '0',
      HARBORLINE_API_REPO: f.source, HARBORLINE_API_REVISION: mutation === 'api' ? f.B : f.A,
    }})
  const refused = fail || !!mutation
  assert.equal(result.status, refused ? 1 : 0, result.stdout + result.stderr)
  const calls = readFileSync(log, 'utf8').split('\n')
  assert.equal(calls.filter(id => id === 'native-tests').length, ['api', 'overwrite'].includes(mutation) ? 0 : 1, mutation === 'overwrite' ? 'restore overwrite must stop production dispatch before native-tests' : 'cached PASS cannot skip the actual main native step')
  assert.equal(calls.filter(id => id === 'candidate-gate').length, 0, 'fresh internal Git checkout cannot execute candidate orchestration')
  assert.equal(existsSync(join(f.root, '.git/harborline-phase4-receipt.json')), !refused)
  assert.equal(existsSync(join(f.root, '.git/harborline-release-receipt.json')), !refused)
  if (mutation === 'parent') assert.match(result.stderr, /trusted orchestration: runtime input changed: tooling\/later-helper.mjs/)
  if (mutation === 'overwrite') {
    assert.equal(calls.filter(id => id === 'dotnet-restore').length, 1, 'actual MSBuild restore target must execute')
    assert.match(result.stderr, /trusted orchestration: runtime input changed: tooling\/later-helper.mjs/)
  }
  if (!refused) {
    const receipt = JSON.parse(readFileSync(join(f.root, '.git/harborline-phase4-receipt.json')))
    assert.equal(receipt.testedTree, f.treeB)
    assert.equal(receipt.orchestration.revision, f.A)
  }
})

test('release.main-required-computations: reusable SBOM scans data with fixed main configuration and retains byte digests', () => {
  const sbom = workflow('license-sbom')
  assert.match(sbom, /workflow_call:/)
  assert.match(sbom, /tested-commit:/)
  assert.match(sbom, /syft-version: v1\.57\.2/)
  assert.match(sbom, /config: \$\{\{ runner.temp \}\}\/syft.yaml/)
  assert.match(sbom, /path: subject/)
  assert.match(sbom, /persist-credentials: false/)
  assert.match(sbom, /upload-artifact: false/)
  assert.match(sbom, /upload-release-assets: false/)
  assert.match(sbom, /retention-days: 90/)
  assert.match(sbom, /sbom-sha256:/)
  assert.match(sbom, /policy-sha256:/)
  assert.doesNotMatch(job(sbom, 'sbom'), /secrets\.|id-token:|attestations:|npm install|pnpm install/)
})

test('release.gate-evidence-attested: main coordinates designated release work and requires its fresh SBOM job', () => {
  const producer = workflow('verify-gallery-receipt')
  const computation = job(producer, 'main-sbom')
  assert.ok(computation, 'main-side SBOM computation required')
  assert.match(computation, /uses: \.\/\.github\/workflows\/license-sbom.yml/)
  assert.match(computation, /release-eligible == 'true'/)
  assert.match(computation, /tested-commit: \$\{\{ needs.plan.outputs.head \}\}/)
  assert.doesNotMatch(computation, /secrets:|id-token:|attestations:|steps:/)
  assert.match(job(producer, 'phase-4'), /needs.main-sbom.result == 'success'/)
  assert.match(job(producer, 'plan'), /RELEASE_APPROVED_COMMIT: \$\{\{ vars.RELEASE_APPROVED_COMMIT \}\}/)
})

// Oracles: R-0225 mandatory preprovisioned Syft; absence is a failure, never a skip.
test('required scanner cannot silently disappear when its executable input is absent', () => {
  const env = {...process.env}; delete env.HARBORLINE_TEST_SYFT; delete env.NODE_TEST_CONTEXT;
  const result = spawnSync(process.execPath, ['--test', '--test-reporter=tap',
    '--test-name-pattern=actual fresh scanner refuses GPL', import.meta.filename], {env, encoding: 'utf8'});
  assert.notEqual(result.status, 0, result.stdout + result.stderr);
  assert.match(result.stdout, /# skipped 0/);
  assert.match(result.stdout, /HARBORLINE_TEST_SYFT.*required/);
})

// Exercises the same pinned Syft scanner and main-authored policy/configuration as the workflow.
// CI provisions this required executable before tests; tests never install or download it.
test('release.main-required-computations: actual fresh scanner refuses GPL despite forged MIT and candidate exclusions; MIT admits',
  t => {
    assert.ok(process.env.HARBORLINE_TEST_SYFT, 'HARBORLINE_TEST_SYFT is required (CI must provision Syft v1.57.2)')
    const version = spawnSync(process.env.HARBORLINE_TEST_SYFT, ['version', '-o', 'json'], {encoding: 'utf8', timeout: 15000})
    assert.equal(version.status, 0, 'required Syft version probe failed')
    assert.equal(JSON.parse(version.stdout).version, '1.57.2', 'required pinned Syft version')
    const text = workflow('license-sbom')
    const config = text.match(/cat > "\$RUNNER_TEMP\/syft.yaml" <<'SYFT'\n([\s\S]*?)\n          SYFT/)
    const policy = text.match(/cat > "\$RUNNER_TEMP\/license-policy.cjs" <<'NODE'\n([\s\S]*?)\n          NODE/)
    assert.ok(config, 'main scanner configuration required')
    assert.ok(policy, 'main deny policy required')
    const root = mkdtempSync(join(tmpdir(), 'release-sbom-'))
    t.after(() => rmSync(root, {recursive: true, force: true}))
    const subject = join(root, 'candidate')
    mkdirSync(join(subject, 'node_modules/blocked-package'), {recursive: true})
    writeFileSync(join(root, 'syft.yaml'), config[1].split('\n').map(line => line.slice(10)).join('\n'))
    writeFileSync(join(root, 'policy.cjs'), policy[1].split('\n').map(line => line.slice(10)).join('\n'))
    writeFileSync(join(subject, '.syft.yaml'), 'exclude: ["**/node_modules/**"]\n')
    writeFileSync(join(subject, 'license-sbom.yml'), 'run: exit 0\n')
    writeFileSync(join(subject, 'sbom.spdx.json'), JSON.stringify({packages: [{name: 'blocked-package', licenseDeclared: 'MIT'}]}))
    writeFileSync(join(subject, 'package.json'), JSON.stringify({name: 'fixture-root', version: '1.0.0', dependencies: {'blocked-package': '1.0.0'}}))
    const run = license => {
      writeFileSync(join(subject, 'node_modules/blocked-package/package.json'), JSON.stringify({name: 'blocked-package', version: '1.0.0', license}))
      const report = join(root, 'fresh.spdx.json')
      const scan = spawnSync(process.env.HARBORLINE_TEST_SYFT,
        ['scan', `dir:${subject}`, '--config', join(root, 'syft.yaml'), '-o', `spdx-json=${report}`],
        {encoding: 'utf8', cwd: subject, timeout: 60000, env: {...process.env, SYFT_CHECK_FOR_APP_UPDATE: 'false'}})
      assert.equal(scan.status, 0, scan.stderr)
      const packages = JSON.parse(readFileSync(report)).packages
      assert.ok(packages.some(item => item.name === 'blocked-package' && item.versionInfo === '1.0.0'
        && (item.licenseDeclared === license || item.licenseConcluded === license)), JSON.stringify(packages))
      return spawnSync(process.execPath, [join(root, 'policy.cjs'), report], {encoding: 'utf8'})
    }
    const denied = run('GPL-3.0-only')
    assert.equal(denied.status, 1, denied.stderr)
    assert.match(denied.stdout, /DENY blocked-package@1\.0\.0/)
    const admitted = run('MIT')
    assert.equal(admitted.status, 0, admitted.stderr)
    for (const bytes of ['not-json', '{}', '{"spdxVersion":"SPDX-2.3","packages":[]}']) {
      writeFileSync(join(root, 'invalid.spdx.json'), bytes)
      const invalid = spawnSync(process.execPath, [join(root, 'policy.cjs'), join(root, 'invalid.spdx.json')], {encoding: 'utf8'})
      assert.equal(invalid.status, 1, `invalid or empty SPDX must refuse: ${bytes}`)
    }
    const missing = spawnSync(process.execPath, [join(root, 'policy.cjs'), join(root, 'missing.spdx.json')], {encoding: 'utf8'})
    assert.equal(missing.status, 1, 'missing SPDX must refuse')
  })

// Independent REST documents: T-1138 run/attempt literals and ADR-0198 decision 2.
function prerequisites(head = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb') {
  const repository = {full_name: 'Harborline-Software/harborline-platform'}
  const queue = {id: 9001, run_attempt: 2, event: 'merge_group', status: 'completed', conclusion: 'success',
    head_sha: head, path: '.github/workflows/verify.yml', name: 'verify', repository, head_repository: repository, check_suite_id: 701}
  const sbom = {...queue, id: 9003, run_attempt: 1, check_suite_id: 702, name: 'license-sbom', path: '.github/workflows/license-sbom.yml'}
  const names = ['phase-4-gate', 'gallery-shard (1)', 'gallery-shard (2)', 'gallery-shard (3)',
    'gallery-shard (4)', 'gallery-shard (5)', 'gallery-shard (6)', 'gallery-collect', 'verify']
  const jobs = names.map((name, i) => ({id: i + 1, name, status: 'completed', conclusion: 'success',
    head_sha: head, run_id: 9001, check_run_url: `https://api.github.com/repos/Harborline-Software/harborline-platform/check-runs/${i + 1}`}))
  const sbomJobs = [{...jobs[0], id: 20, name: 'sbom', run_id: 9003,
    check_run_url: 'https://api.github.com/repos/Harborline-Software/harborline-platform/check-runs/20'}]
  const checks = Object.fromEntries([...jobs, ...sbomJobs].map(item => [item.id, {id: item.id,
    name: item.name, head_sha: head, status: 'completed', conclusion: 'success', app: {id: 15368},
    check_suite: {id: item.run_id === 9001 ? 701 : 702}}]))
  const documents = {
    'actions/runs/9001': queue, 'actions/runs/9001/attempts/2': queue,
    'actions/runs/9001/attempts/2/jobs?per_page=100': {total_count: 9, jobs},
    ['actions/workflows/license-sbom.yml/runs?event=merge_group&head_sha=' + head + '&per_page=100']: {total_count: 1, workflow_runs: [sbom]},
    'actions/runs/9003': sbom, 'actions/runs/9003/attempts/1': sbom,
    'actions/runs/9003/attempts/1/jobs?per_page=100': {total_count: 1, jobs: sbomJobs},
    ...Object.fromEntries(Object.entries(checks).map(([id, check]) => [`check-runs/${id}`, check])),
  }
  const readApi = path => {
    const key = path.replace('repos/Harborline-Software/harborline-platform/', '')
    assert.ok(Object.hasOwn(documents, key), `unexpected API lookup ${key}`)
    return structuredClone(documents[key])
  }
  const verify = () => {
    assert.equal(typeof evidence.releaseRunPrerequisites, 'function', 'release run prerequisite verifier required')
    return evidence.releaseRunPrerequisites({head, queueId: 9001, actionsAppId: 15368, readApi})
  }
  return {head, queue, sbom, jobs, sbomJobs, checks, documents, readApi, verify}
}

function invocation(t, {verifierFixture = false} = {}) {
  const root = mkdtempSync(join(tmpdir(), 'release-invocation-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  const git = (...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8',
    env: {...process.env, GIT_AUTHOR_NAME: 'Release Fixture', GIT_COMMITTER_NAME: 'Release Fixture',
      GIT_AUTHOR_EMAIL: 'fixture@example.invalid', GIT_COMMITTER_EMAIL: 'fixture@example.invalid'},
    stdio: ['ignore', 'pipe', 'pipe']}).trim()
  git('init', '-b', 'main')
  if (verifierFixture) {
    const repository = resolve(import.meta.dirname, '../..'), copied = new Set()
    const copy = name => {
      if (copied.has(name)) return
      copied.add(name)
      const source = resolve(repository, name), bytes = readFileSync(source)
      mkdirSync(dirname(join(root, name)), {recursive: true})
      writeFileSync(join(root, name), bytes)
      for (const match of bytes.toString().matchAll(/from\s+['"](\.[^'"]+)['"]/g))
        copy(relative(repository, resolve(dirname(source), match[1])))
    }
    copy('tooling/gates/scan-evidence-provenance.mjs')
  }
  const commits = []
  for (const value of ['A', 'B', 'C']) {
    writeFileSync(join(root, 'product.txt'), value)
    if (verifierFixture && value === 'B') {
      writeFileSync(join(root, 'tooling/gates/scan-evidence-provenance.mjs'),
        'import {writeFileSync} from "node:fs"; writeFileSync(process.env.CANDIDATE_MARKER, "executed"); console.log("ADMIT");\n')
      writeFileSync(join(root, 'release-gate-policy.json'), '{"acceptAll":true}')
    }
    git('add', '.'); git('commit', '-m', value)
    commits.push(git('rev-parse', 'HEAD'))
  }
  const [A, B, C] = commits
  const unrelated = git('commit-tree', git('rev-parse', `${A}^{tree}`), '-m', 'unrelated')
  const f = prerequisites(B)
  const producerRun = {id: 9002, run_attempt: 1, event: 'workflow_run', status: 'completed', conclusion: 'success',
    head_sha: A, path: '.github/workflows/verify-gallery-receipt.yml', repository: {full_name: 'Harborline-Software/harborline-platform'}}
  f.documents['actions/runs/9002'] = producerRun
  f.documents['actions/runs/9002/attempts/1'] = producerRun
  const certificate = {
    buildSignerURI: 'https://github.com/Harborline-Software/harborline-platform/.github/workflows/sign-gallery-receipt.yml@refs/heads/main',
    buildSignerDigest: A, buildTrigger: 'workflow_run', runnerEnvironment: 'github-hosted',
    sourceRepositoryURI: 'https://github.com/Harborline-Software/harborline-platform',
    sourceRepositoryRef: 'refs/heads/main', sourceRepositoryDigest: A,
    runInvocationURI: 'https://github.com/Harborline-Software/harborline-platform/actions/runs/9002/attempts/1',
  }
  const input = {root, approvedCommit: B, testedCommit: B, testedTree: git('rev-parse', `${B}^{tree}`),
    acceptedSignerRevision: A, actionsAppId: 15368, queue: {id: 9001, attempt: 2},
    sbom: {id: 9003, attempt: 1}, producer: {id: 9002, attempt: 1, revision: A}, certificate, readApi: f.readApi}
  const source = sha => {
    input.acceptedSignerRevision = sha; input.producer.revision = sha
    certificate.buildSignerDigest = sha; certificate.sourceRepositoryDigest = sha; producerRun.head_sha = sha
  }
  const verify = () => {
    assert.equal(typeof evidence.verifyReleaseInvocation, 'function', 'independent release invocation verifier required')
    return evidence.verifyReleaseInvocation(input)
  }
  return {...f, root, A, B, C, unrelated, input, producerRun, certificate, source, verify}
}

function externalPolicy(f) {
  const policy = {schemaVersion: 1, verifierRevision: f.A, signerRevision: f.A, apiRevision: f.A,
    revokedSignerRevisions: [], actionsAppId: 15368, galleryAppId: 333, reviewGateAppId: 111}
  const response = {name: 'RELEASE_GATE_POLICY', value: JSON.stringify(policy)}
  f.documents['actions/variables/RELEASE_GATE_POLICY'] = response
  return {policy, response, load: () => {
    assert.equal(typeof evidence.loadReleaseGatePolicy, 'function', 'external policy loader required')
    return evidence.loadReleaseGatePolicy({root: f.root, approvedCommit: f.B, readApi: f.readApi})
  }}
}

test('release.verifier-policy-outside-release: release bootstraps only the owner-pinned external executable closure', t => {
  const release = workflow('release-platform')
  const block = release.match(/      - name: Bootstrap current external release verifier\n([\s\S]*?)\n      - /)
  assert.ok(block, 'external verifier bootstrap step required')
  const script = block[1].split('        run: |\n')[1].split('\n').map(line => line.replace(/^          /, '')).join('\n')
  const f = invocation(t, {verifierFixture: true}), p = externalPolicy(f)
  const released = join(f.root, 'released')
  execFileSync('git', ['worktree', 'add', '--detach', released, f.B], {cwd: f.root, stdio: 'ignore'})
  const responsePath = join(f.root, 'owner-variable.json'), hookPath = join(f.root, 'api-hook.mjs')
  writeFileSync(hookPath, `import cp from 'node:child_process'; import {readFileSync} from 'node:fs'; import {syncBuiltinESMExports} from 'node:module';
    const actual = cp.execFileSync; cp.execFileSync = (command, args, options) => {
      if (command !== 'gh') return actual(command, args, options);
      if (args[0] !== 'api' || args[1] !== 'repos/Harborline-Software/harborline-platform/actions/variables/RELEASE_GATE_POLICY') throw new Error('unexpected API');
      return readFileSync(process.env.OWNER_VARIABLE, 'utf8'); }; syncBuiltinESMExports();`)
  const prelude = `git() {
      if [ "$1" = fetch ]; then command git cat-file -e "$4^{commit}";
      elif [ "$1" = worktree ] && [ "$2" = add ] && [ "$DIRTY_VERIFIER" = true ]; then command git "$@"; printf 'candidate addition' > "$4/untracked.txt";
      else command git "$@"; fi; }
    gh() { cat "$OWNER_VARIABLE"; }
    node() {
      if [ "$INSIDE_SNAPSHOT" = true ]; then "$NODE_BINARY" --import "$API_HOOK" "$1" "$2" "$3" "$4" "$GITHUB_WORKSPACE/candidate-policy.json";
      else "$NODE_BINARY" --import "$API_HOOK" "$@"; fi; }
    set -euo pipefail
  `
  const run = (label, policy, {dirty = false, inside = false, eventHead = f.B, badSnapshot = false} = {}) => {
    const runner = join(f.root, label), marker = join(runner, 'candidate-executed')
    mkdirSync(runner)
    writeFileSync(responsePath, JSON.stringify({name: 'RELEASE_GATE_POLICY', value: JSON.stringify(policy)}))
    const result = spawnSync('bash', ['-c', prelude + script], {cwd: released, encoding: 'utf8', env: {
      ...process.env, GITHUB_WORKSPACE: released, RUNNER_TEMP: runner, APPROVED_COMMIT: f.B,
      GITHUB_SHA: eventHead,
      REPOSITORY: 'Harborline-Software/harborline-platform', NODE_BINARY: process.execPath,
      OWNER_VARIABLE: responsePath, API_HOOK: pathToFileURL(hookPath).href,
      GITHUB_REPOSITORY: 'Harborline-Software/harborline-platform', GITHUB_RUN_ID: '9004', GITHUB_RUN_ATTEMPT: '1',
      RELEASE_POLICY_ENVELOPE: Buffer.from(JSON.stringify({schemaVersion: 1,
        repository: 'Harborline-Software/harborline-platform', runId: '9004', attempt: '1', revision: eventHead,
        response: {name: 'RELEASE_GATE_POLICY', value: JSON.stringify(policy)}})).toString('base64'),
      RELEASE_POLICY_SHA256: badSnapshot ? '0'.repeat(64) : createHash('sha256').update(JSON.stringify(policy)).digest('hex'),
      CANDIDATE_MARKER: marker, GITHUB_OUTPUT: join(runner, 'outputs'),
      DIRTY_VERIFIER: String(dirty), INSIDE_SNAPSHOT: String(inside),
    }})
    assert.equal(existsSync(marker), false, 'released-tree accept-all verifier must never execute')
    return {result, runner}
  }
  const valid = run('valid', p.policy)
  assert.equal(valid.result.status, 0, valid.result.stdout + valid.result.stderr)
  assert.equal(JSON.parse(readFileSync(join(valid.runner, 'release-policy.json'))).signerRevision, f.A)
  const revoked = run('revoked', {...p.policy, revokedSignerRevisions: [f.A]})
  assert.notEqual(revoked.result.status, 0, 'fresh revoked owner policy must refuse')
  const equal = run('equal', {...p.policy, verifierRevision: f.B})
  assert.notEqual(equal.result.status, 0, 'verifier at released commit must refuse before loading it')
  const dirty = run('dirty', p.policy, {dirty: true})
  assert.notEqual(dirty.result.status, 0, 'external verifier checkout must stay clean')
  const inside = run('inside', p.policy, {inside: true})
  assert.notEqual(inside.result.status, 0, 'policy snapshot cannot be stored in the released checkout')
  const wrongSnapshot = run('wrong-snapshot', p.policy, {badSnapshot: true})
  assert.notEqual(wrongSnapshot.result.status, 0, 'unbound reader bytes refuse without an Actions-token fallback')
  const advanced = run('advanced', p.policy, {eventHead: f.C})
  assert.notEqual(advanced.result.status, 0, 'main-tip dispatch guard must refuse B after event SHA C')
})

test('release.verifier-policy-outside-release: current owner policy pins exact prior-main verifier/signer/API revisions and its byte digest', t => {
  const f = invocation(t), p = externalPolicy(f)
  writeFileSync(join(f.root, 'release-gate-policy.json'), '{"signerRevision":"candidate-selected","acceptAll":true}')
  const loaded = p.load()
  assert.deepEqual(loaded.policy, p.policy)
  assert.equal(loaded.sha256, createHash('sha256').update(p.response.value).digest('hex'))
  assert.equal(loaded.bytes.toString(), p.response.value)
})

test('release.main-orchestration-inputs: gallery input planning obtains a full API pin from current owner configuration', t => {
  const f = invocation(t), p = externalPolicy(f)
  assert.equal(typeof evidence.loadTrustedApiInput, 'function')
  const readApi = path => f.documents[path.replace('repos/Harborline-Software/harborline-platform/', '')]
  assert.deepEqual(evidence.loadTrustedApiInput({readApi}), {apiRevision: f.A,
    policySha256: createHash('sha256').update(p.response.value).digest('hex')})
  p.response.value = JSON.stringify({...p.policy, apiRevision: 'main'})
  assert.throws(() => evidence.loadTrustedApiInput({readApi}), /release policy:/)
  p.response.value = '{}'
  assert.throws(() => evidence.loadTrustedApiInput({readApi}), /release policy:/)
})

for (const [label, mutate] of [
  ['missing policy', (f, p) => { delete f.documents['actions/variables/RELEASE_GATE_POLICY'] }],
  ['malformed policy', (f, p) => { p.response.value = 'not JSON' }],
  ['wrong variable', (f, p) => { p.response.name = 'CANDIDATE_POLICY' }],
  ['unsupported schema', (f, p) => { p.policy.schemaVersion = 2 }],
  ['verifier at release', (f, p) => { p.policy.verifierRevision = f.B }],
  ['descendant verifier', (f, p) => { p.policy.verifierRevision = f.C }],
  ['unresolvable verifier', (f, p) => { p.policy.verifierRevision = 'ffffffffffffffffffffffffffffffffffffffff' }],
  ['signer at release', (f, p) => { p.policy.signerRevision = f.B }],
  ['revoked signer', (f, p) => { p.policy.revokedSignerRevisions = [f.A] }],
  ['missing revocations', (f, p) => { delete p.policy.revokedSignerRevisions }],
  ['malformed revocations', (f, p) => { p.policy.revokedSignerRevisions = ['main'] }],
  ['invalid API pin', (f, p) => { p.policy.apiRevision = 'main' }],
  ['unknown Actions identity', (f, p) => { p.policy.actionsAppId = 0 }],
  ['unknown gallery identity', (f, p) => { delete p.policy.galleryAppId }],
  ['unknown review identity', (f, p) => { p.policy.reviewGateAppId = '111' }],
  ['Actions used as review publisher', (f, p) => { p.policy.reviewGateAppId = 15368 }],
]) test(`release.verifier-policy-outside-release: ${label} refuses`, t => {
  const f = invocation(t), p = externalPolicy(f)
  mutate(f, p)
  if (label !== 'malformed policy') p.response.value = JSON.stringify(p.policy)
  assert.throws(p.load, /release policy:/)
})

test('release.verifier-policy-outside-release: a fresh policy read observes revocation instead of reusing the previous snapshot', t => {
  const f = invocation(t), p = externalPolicy(f)
  assert.equal(p.load().policy.signerRevision, f.A)
  p.response.value = JSON.stringify({...p.policy, revokedSignerRevisions: [f.A]})
  assert.throws(p.load, /release policy: signer is revoked/)
})

test('release.gate-evidence-exact-run: A -> B -> C admits source A, tested/released B, queue 9001/2 and producer 9002/1', t => {
  const f = invocation(t)
  assert.deepEqual(f.verify(), {queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}, producer: {id: 9002, attempt: 1}})
})

test('release.gate-evidence-exact-run: source equality, descendant, unrelated and absent Git history refuse', async t => {
  const f = invocation(t)
  for (const [label, source] of [['equal', f.B], ['descendant', f.C], ['unrelated', f.unrelated], ['missing', 'ffffffffffffffffffffffffffffffffffffffff']]) {
    await t.test(label, () => {
      f.source(source)
      assert.throws(f.verify, /release invocation:/)
    })
  }
})

for (const [label, mutate] of [
  ['another signer', f => { f.certificate.buildSignerURI = 'https://github.com/other/repo/.github/workflows/sign.yml@refs/heads/main' }],
  ['SHA-qualified signer URI', f => { f.certificate.buildSignerURI = f.certificate.buildSignerURI.replace('refs/heads/main', f.A) }],
  ['wrong signer digest', f => { f.certificate.buildSignerDigest = f.C }],
  ['producer differs from policy', f => { f.input.producer.revision = f.C }],
  ['source differs from signer', f => { f.certificate.sourceRepositoryDigest = f.B }],
  ['wrong source ref', f => { f.certificate.sourceRepositoryRef = 'refs/pull/1/merge' }],
  ['wrong repository', f => { f.certificate.sourceRepositoryURI = 'https://github.com/other/repo' }],
  ['self-hosted', f => { f.certificate.runnerEnvironment = 'self-hosted' }],
  ['missing source facts', f => { delete f.certificate.sourceRepositoryDigest }],
  ['PR certificate', f => { f.certificate.buildTrigger = 'pull_request' }],
  ['manual certificate', f => { f.certificate.buildTrigger = 'workflow_dispatch' }],
  ['release-generated certificate', f => { f.certificate.buildTrigger = 'workflow_dispatch'; f.certificate.runInvocationURI = 'https://github.com/Harborline-Software/harborline-platform/actions/runs/9004/attempts/1' }],
  ['wrong invocation', f => { f.certificate.runInvocationURI = 'https://github.com/Harborline-Software/harborline-platform/actions/runs/9001/attempts/2' }],
  ['different tested SHA despite a claimed matching tree', f => { f.input.testedCommit = f.A }],
  ['wrong tree', f => { f.input.testedTree = 'ffffffffffffffffffffffffffffffffffffffff' }],
  ['old queue attempt', f => { f.input.queue.attempt = 1 }],
  ['old SBOM attempt', f => { f.input.sbom.attempt = 2 }],
  ['new producer attempt', f => { f.documents['actions/runs/9002'] = {...f.producerRun, run_attempt: 2, conclusion: 'failure'} }],
  ['new successful producer attempt', f => { f.documents['actions/runs/9002'] = {...f.producerRun, run_attempt: 2} }],
  ['pending producer', f => { f.producerRun.status = 'in_progress'; f.producerRun.conclusion = null }],
  ['wrong producer source', f => { f.producerRun.head_sha = f.B }],
  ['wrong producer workflow', f => { f.producerRun.path = '.github/workflows/release-platform.yml' }],
  ['wrong producer event', f => { f.producerRun.event = 'workflow_dispatch' }],
]) test(`release.gate-evidence-exact-run: ${label} refuses`, t => {
  const f = invocation(t)
  mutate(f)
  assert.throws(f.verify, /release invocation:/)
})

test('release.gate-evidence-attested: successful exact-source queue and SBOM prerequisites are recorded separately', () => {
  const f = prerequisites()
  assert.deepEqual(f.verify(), {queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}})
})

test('release.gate-evidence-attested: pending SBOM is polled before prerequisite admission', async () => {
  const f = prerequisites()
  f.sbom.status = 'in_progress'
  assert.equal(typeof evidence.waitForReleaseRunPrerequisites, 'function')
  let clock = 0, waits = 0
  const result = await evidence.waitForReleaseRunPrerequisites({
    head: f.head, queueId: 9001, actionsAppId: 15368,
    readApi: path => structuredClone(f.documents[path.replace('repos/Harborline-Software/harborline-platform/', '')]),
    now: () => clock, maxWaitMs: 200, pollMs: 100,
    wait: async ms => { waits++; clock += ms; f.sbom.status = 'completed' },
  })
  assert.equal(waits, 1)
  assert.deepEqual(result, {queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}})
})

test('release.gate-evidence-attested: unfinished prerequisites time out without an admission', async () => {
  const f = prerequisites()
  f.sbom.status = 'in_progress'
  assert.equal(typeof evidence.waitForReleaseRunPrerequisites, 'function')
  let clock = 0
  await assert.rejects(() => evidence.waitForReleaseRunPrerequisites({
    head: f.head, queueId: 9001, actionsAppId: 15368,
    readApi: path => structuredClone(f.documents[path.replace('repos/Harborline-Software/harborline-platform/', '')]),
    now: () => clock, maxWaitMs: 200, pollMs: 100, wait: async ms => { clock += ms },
  }), /release prerequisites: timeout/)
  assert.equal(clock, 200)
})

for (const state of ['in_progress', 'failure', 'skipped', 'cancelled', 'timed_out']) {
  test(`release.gate-evidence-attested: SBOM ${state} cannot admit receipt production`, () => {
    const f = prerequisites()
    if (state === 'in_progress') f.sbom.status = state
    else f.sbom.conclusion = state
    assert.throws(f.verify, /release prerequisites:/)
  })
}

for (const name of ['phase-4-gate', 'gallery-shard (1)', 'gallery-shard (2)', 'gallery-shard (3)',
  'gallery-shard (4)', 'gallery-shard (5)', 'gallery-shard (6)', 'gallery-collect', 'verify']) {
  for (const conclusion of ['failure', 'skipped', 'cancelled', null]) {
    test(`release.gate-evidence-attested: ${name} ${conclusion} refuses despite aggregate success`, () => {
      const f = prerequisites()
      f.jobs.find(item => item.name === name).conclusion = conclusion
      assert.throws(f.verify, /release prerequisites:/)
    })
  }
}

for (const [name, mutate] of [
  ['missing job', f => { f.jobs.pop(); f.documents['actions/runs/9001/attempts/2/jobs?per_page=100'].total_count = 8 }],
  ['duplicate job', f => { f.jobs.push(f.jobs[0]); f.documents['actions/runs/9001/attempts/2/jobs?per_page=100'].total_count = 10 }],
  ['other workflow', f => { f.queue.path = '.github/workflows/fake.yml' }],
  ['wrong event', f => { f.queue.event = 'pull_request' }],
  ['wrong head', f => { f.queue.head_sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }],
  ['latest queue failed', f => {
    const latest = {...f.queue, run_attempt: 3, conclusion: 'failure'}
    f.documents['actions/runs/9001'] = latest
    f.documents['actions/runs/9001/attempts/3'] = latest
    f.documents['actions/runs/9001/attempts/3/jobs?per_page=100'] = {total_count: 9, jobs: f.jobs}
  }],
  ['unknown check source', f => { f.checks[1].app.id = 222 }],
  ['wrong check suite', f => { f.checks[1].check_suite.id = 999 }],
  ['same-name forged check on wrong head', f => { f.checks[1].head_sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }],
  ['job from another run', f => { f.jobs[0].run_id = 9002 }],
  ['foreign check URL', f => { f.jobs[0].check_run_url = 'https://example.invalid/check-runs/1' }],
  ['unreadable API', f => { delete f.documents['actions/runs/9001'] }],
]) test(`release.gate-evidence-attested: ${name} refuses`, () => {
  const f = prerequisites()
  mutate(f)
  assert.throws(f.verify, /release prerequisites:/)
})

test('release.shared-signer-policy: signing has no App credential or publishing environment', () => {
  const signer = workflow('sign-gallery-receipt')
  assert.doesNotMatch(signer, /secrets:|secrets\.|environment:|create-github-app-token|--receipt post/)
  assert.equal((signer.match(/id-token: write/g) ?? []).length, 1)
  assert.equal((signer.match(/attestations: write/g) ?? []).length, 1)
  assert.doesNotMatch(signer, /contents: write|checks: write|statuses: write|pnpm install|run-phase-4-gate/)
})

test('release.shared-signer-policy: the calling job is step-free and passes no secrets', () => {
  const caller = job(workflow('verify-gallery-receipt'), 'sign')
  assert.ok(caller)
  assert.match(caller, /uses: \.\/\.github\/workflows\/sign-gallery-receipt.yml/)
  assert.match(caller, /id-token: write/)
  assert.match(caller, /attestations: write/)
  assert.doesNotMatch(caller, /steps:|secrets:|environment:/)
})

test('release.shared-signer-policy: a separate main poster checks evidence with no signing rights', () => {
  const poster = job(workflow('verify-gallery-receipt'), 'post')
  assert.ok(poster, 'dedicated App poster must be a separate job')
  assert.match(poster, /environment: gallery-receipt/)
  assert.match(poster, /ref: \$\{\{ github.sha \}\}/)
  assert.match(poster, /GALLERY_RECEIPT_APP_KEY/)
  assert.match(poster, /--receipt post/)
  assert.doesNotMatch(poster, /id-token:|attestations:|contents: write|run-phase-4-gate|pnpm install/)
  assert.match(poster, /needs\.sign\.outputs\.artifact-id/)
})
