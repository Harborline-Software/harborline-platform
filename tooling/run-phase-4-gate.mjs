#!/usr/bin/env node

import { execFileSync, spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { cpus } from 'node:os'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import {decideStepReuse, evaluateStepStdout, hashStepInputs, loadPreviousPassEvidence} from './gate-step-evidence.mjs'
import { resolveAppshellFeed } from './resolve-appshell-feed.mjs'
import { resolveCommand, runnerEnvironment } from './resolve-command.mjs'
import { resolvePinnedDotnet } from './resolve-dotnet.mjs'
import {recordPhase4Gate, requiredStepIds} from './gate-contract.mjs'
import {acquirePhase4GateLock} from './phase4-gate-lock.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
if (process.argv.includes('--only')) throw new Error('phase-4 gate refuses --only; package evidence must cover every fixture')
await acquirePhase4GateLock({repositoryRoot: root, reentryGrant: await receiveLockReentryGrant()})
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
const formsContractsRoot = resolve(root, 'projections/typescript/contracts/hlp.contracts.forms')
const ruleRuntimeRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-runtime')
const ruleAuthoringRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-authoring')
const copilotContractsRoot = resolve(root, 'projections/typescript/application/hlp.copilot.contracts')
const dotnet = resolvePinnedDotnet(root)
const results = []
const baseHead = process.env.HARBORLINE_BASE_HEAD
  ?? execFileSync('git', ['rev-parse', 'HEAD'], {cwd: root, encoding: 'utf8'}).trim()
const testedTree = process.env.HARBORLINE_TESTED_TREE
  ?? execFileSync('git', ['write-tree'], {cwd: root, encoding: 'utf8'}).trim()
const gateEvidencePath = resolve(root, 'docs/evidence/phase-4/gate.json')
const previousPass = loadPreviousPassEvidence(gateEvidencePath)
const moduleCatalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8'))
const generationSmokeModuleIds = Object.entries(moduleCatalog.modules)
  .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.') && module.presentation?.disposition === 'visual')
  .map(([moduleId]) => moduleId)
  .sort()
const moduleIds = Object.entries(moduleCatalog.modules)
  .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.')
    && Object.values(module.projections ?? {}).some(projection => projection.conformanceRunner))
  .map(([moduleId]) => moduleId)
  .sort()
const expectedSharedResults = moduleIds.reduce((total, moduleId) => {
  const fixture = JSON.parse(readFileSync(resolve(root, `conformance/${moduleId}/fixtures.yaml`), 'utf8'))
  // A runner delegating to run-ui-module-shared records the React suite ONCE (honesty fix for
  // wave-1 finding platform-appshell-2); a custom per-case runner (button, context-menu drive
  // vitest with -t per fixture id) genuinely produces one React result per case and stays *2.
  const runner = readFileSync(resolve(root, `conformance/${moduleId}/runners/run-shared.mjs`), 'utf8')
  const perCaseReact = !runner.includes('run-ui-module-shared')
  return total + (perCaseReact ? fixture.cases.length * 2 : fixture.cases.length + 1)
}, 0)

// stepEnvironment is deliberately NOT recorded in the step entry: it carries absolute machine
// paths, and the entry is hashed into reportSha256, which the pre-commit receipt check compares.
function run(id, executable, args, cwd = root, json = false, stepEnvironment = {}) {
  const started = performance.now()
  const resolved = resolveCommand(executable, args)
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 128 * 1024 * 1024,
    env: { ...process.env, ...runnerEnvironment, ...stepEnvironment },
  })
  const outcome = evaluateStepStdout({stepId: id, json, status: result.status, stdout: result.stdout})
  const report = outcome.report
  result.status = outcome.status
  if (outcome.failure) result.stderr = [result.stderr, `${id}: ${outcome.failure}`].join('\n')
  const entry = {
    id,
    command: [executable, ...args],
    exitCode: result.status,
    durationMs: Math.round(performance.now() - started),
    passed: result.status === 0,
    report,
    failureOutput: result.status === 0 ? undefined : `${result.stdout}\n${result.stderr}`.trimEnd().split('\n').slice(-80).join('\n'),
  }
  results.push(entry)
  if (!entry.passed) throw new Error(`${id} failed`)
  return report
}

function runReusable(id, executable, args, cwd = root, json = false, stepEnvironment = {}) {
  let inputHash
  let reuseRefusal
  try {
    inputHash = hashStepInputs({repositoryRoot: root, testedTree, stepId: id})
    const decision = decideStepReuse({stepId: id, inputHash, previousPass})
    if (decision.mode === 'reuse') {
      const entry = {
        ...decision.previous,
        durationMs: 0,
        inputHash,
        reuseRefusal: undefined,
        reusedFrom: {
          evidence: 'docs/evidence/phase-4/gate.json',
          testedTree: previousPass.gate.subject.testedTree,
          inputHash: decision.previous.inputHash,
        },
      }
      results.push(entry)
      return entry.report
    }
    reuseRefusal = decision.reason
  } catch (error) {
    reuseRefusal = `input hashing failed: ${error instanceof Error ? error.message : String(error)}`
  }

  try {
    return run(id, executable, args, cwd, json, stepEnvironment)
  } finally {
    const entry = results.at(-1)
    if (entry?.id === id) {
      entry.inputHash = inputHash
      entry.reuseRefusal = reuseRefusal
      entry.reusedFrom = undefined
    }
  }
}

// The app-shell fixture stages two cross-repo tarballs from explicit environment paths and
// verifies their sha256 itself. Resolving them is a PRE-step, not a gate step: adding a step id
// would invalidate every existing receipt (requiredStepIds is asserted in run-phase-4-gate.mjs,
// validate-repository.mjs and verify-phase4-receipt.mjs alike). A resolution failure is recorded
// against the step that needs it, so the cause is named rather than surfacing as an absent step.
function resolveFeedForPackageConsumers() {
  try {
    return resolveAppshellFeed()
  } catch (error) {
    results.push({
      id: 'package-consumers',
      command: ['tooling/resolve-appshell-feed.mjs'],
      exitCode: 1,
      durationMs: 0,
      passed: false,
      failureOutput: error instanceof Error ? error.message : String(error),
    })
    throw error
  }
}

// Ticket 098 phase 3: Playwright --shard, sized to the machine rather than pinned. Measured on this
// sixteen-core host: unsharded 9.0 min, four shards sharing eight workers 5.6 min with 437 expected,
// 0 unexpected and 0 flaky. Four shards at the config's FOUR workers each was faster still (5.2 min)
// and produced fourteen flaky tests and a failure that did not reproduce alone -- shards divide a
// worker budget, they do not multiply one, and a gate that buys wall-clock with reliability has
// traded away the thing it exists to provide.
//
// Derived from cores so the future two-core CI runner gets 1 shard rather than eight workers on two
// cores. Overridable with GALLERY_SHARDS for measurement.
const galleryShards = Number(process.env.GALLERY_SHARDS)
  || Math.max(1, Math.min(4, Math.floor(cpus().length / 4)))

try {
  // The root has its own devDependency (typescript, the parser tooling/gates/render-digest.mjs
  // uses) and nothing installed it: the five sub-project installs below all target
  // subdirectories. A lane worktree has a root node_modules left over from earlier work, so the
  // tooling self-tests passed there and failed in the receipt's detached tested tree, where the
  // five files that reach render-digest.mjs could not resolve 'typescript' (ticket 138 s3).
  run('root-clean-install', 'npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], root)
  run('npm-clean-install', 'npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], reactRoot)
  run('forms-contracts-clean-install', 'npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], formsContractsRoot)
  run('rule-runtime-clean-install', 'pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], ruleRuntimeRoot)
  run('rule-authoring-clean-install', 'pnpm', ['install', '--frozen-lockfile', '--ignore-scripts'], ruleAuthoringRoot)
  run('copilot-contracts-clean-install', 'npm', ['ci', '--ignore-scripts', '--no-audit', '--no-fund'], copilotContractsRoot)
  run('dotnet-restore', dotnet.executable, ['restore', 'Harborline.Platform.slnx', '--force', '--no-cache', '-v:minimal'])
  run('generation-smoke', process.execPath, ['tooling/generate-blazor-smoke.mjs', ...generationSmokeModuleIds.flatMap(moduleId => ['--module', moduleId])], root, true)
  run('ui-spec-authority', process.execPath, ['tooling/sync-ui-spec-authority.mjs', '--check'], root, true)
  run('catalog-preflight', process.execPath, ['tooling/validate-repository.mjs', '--allow-stale-gate'], root, true)
  run('tooling-selftests', process.execPath, ['tooling/run-tooling-selftests.mjs'], root, true)
  run('prop-vocabulary', process.execPath, ['tooling/gates/scan-prop-vocabulary.mjs', '--json'], root, true)
  // A static sweep over source, so it belongs with the cheap checks rather than behind the
  // thirty-eight-minute half. EXPIRED design verdicts fail for every counted UI module,
  // including non-terminal modules. Other unfinished gates remain a recorded worklist.
  run('ui-gate-model', process.execPath, ['tooling/gates/run-ui-gate-model.mjs', '--json'], root, true)
  run('build', process.execPath, ['tooling/run-native.mjs', '--build'], root, true)
  runReusable('native-tests', process.execPath, ['tooling/run-native.mjs'], root, true)
  // Ticket 268: the budgeted performance rows run ONCE MORE here, alone. Inside `native-tests` they
  // share the box with thirty-one concurrent suites and only the loose ceilings can hold; this step
  // is serial in the gate by construction, so when the box also measures quiet the tests apply the
  // tight ceilings. Not reusable: a reused measurement is not a measurement.
  run('perf-budgets', process.execPath, ['tooling/run-perf-budgets.mjs'], root, true)
  runReusable('ui-shared-conformance', process.execPath, ['tooling/run-shared.mjs'], root, true)
  run('package-consumers', process.execPath, ['tooling/verify-package-fixtures.mjs', '--phase-4-gate'], root, true, resolveFeedForPackageConsumers())
  runReusable('gallery-gate', process.execPath, ['tooling/run-gallery-gate.mjs', '--packages-ready'], root, true,
    { GALLERY_SHARDS: String(galleryShards) })
  run('catalog-final', process.execPath, ['tooling/validate-repository.mjs', '--allow-stale-gate'], root, true)
} catch {
  // The failed result is emitted below with its bounded command output.
}

const byId = Object.fromEntries(results.map(result => [result.id, result.report]))
const sharedResults = byId['ui-shared-conformance']?.counts?.passedResults ?? 0
const galleryCounts = byId['gallery-gate']?.counts ?? {}
const passed = results.length === requiredStepIds.length
  && requiredStepIds.every((id, index) => results[index]?.id === id && results[index].passed)
  && byId['ui-shared-conformance']?.counts?.expectedModules === moduleIds.length
  && sharedResults === expectedSharedResults
  && byId['generation-smoke']?.modules === generationSmokeModuleIds.length
  && byId['generation-smoke']?.scenarios > 0
  && byId['generation-smoke']?.scenarios === galleryCounts.scenarios
  && byId['gallery-gate']?.scenarioReconciliation === 'exact'
  && galleryCounts.scenarioBrowserTests === galleryCounts.scenarios
  && byId['gallery-gate']?.checkReconciliation === 'exact'
const report = {
  schemaVersion: 3,
  phase: 4,
  scope: 'HLF-052 App-essential UI modules registered in the Platform catalog',
  subject: {
    repository: 'harborline-platform',
    baseHead,
    testedTree,
    mode: process.env.HARBORLINE_TESTED_TREE ? 'exact-staged-tree' : 'current-index',
  },
  moduleIds,
  requiredStepIds,
  status: passed ? 'PASS' : 'FAIL',
  designReview: byId['ui-gate-model']?.designReview,
  dotnetSdk: dotnet.version,
  counts: {
    sharedResults,
    generationSmokeModules: byId['generation-smoke']?.modules ?? 0,
    generationSmokeScenarios: byId['generation-smoke']?.scenarios ?? 0,
    nativeTests: byId['native-tests']?.counts?.total ?? 0,
    cleanConsumers: byId['package-consumers']?.fixtures?.length ?? 0,
    declaredArtifacts: byId['catalog-final']?.counts?.artifacts ?? 0,
    galleryScenarios: galleryCounts.scenarios ?? 0,
    qualityCases: galleryCounts.qualityCases ?? 0,
    browserTests: galleryCounts.browserTests ?? 0,
    scenarioBrowserTests: galleryCounts.scenarioBrowserTests ?? 0,
    nonScenarioBrowserTests: galleryCounts.nonScenarioBrowserTests ?? 0,
    // Every gallery check family, as recorded by the assertion that ran it. Carried whole so a newly
    // instrumented family reaches the receipt without a second edit here.
    galleryChecks: galleryCounts.checks ?? {},
  },
  // Observations and catalog-derived expectations are carried separately, and labelled, so a reader
  // of a receipt cannot take one for the other (control ticket 100).
  browserOutcomes: byId['gallery-gate']?.browserOutcomes,
  scenarioReconciliation: byId['gallery-gate']?.scenarioReconciliation,
  checkReconciliation: byId['gallery-gate']?.checkReconciliation,
  galleryExpected: byId['gallery-gate']?.expected,
  invariants: {
    expectedSharedResults,
    expectedGenerationSmokeModules: generationSmokeModuleIds.length,
    generationSmokeClaim: 'spec-only-compilation-sufficiency; not visual or behavioral parity',
    prohibitedDependencies: byId['catalog-final']?.checks?.legacyOrSiblingSourceDependencies ?? null,
    configuredRemotes: byId['catalog-final']?.checks?.configuredRemotes ?? null,
    publishableGalleryArtifacts: byId['catalog-final']?.checks?.publishableGalleryArtifacts ?? null,
    packageIdentityAuthorityChanged: false,
    distributionAuthorityRepository: null,
  },
  results,
}
recordPhase4Gate(resolve(root, 'docs/evidence/phase-4/gate.json'), report)
process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
process.exitCode = passed ? 0 : 1

function receiveLockReentryGrant() {
  if (!process.argv.includes('--phase4-lock-reentry')) return undefined
  if (!process.connected) throw new Error('phase-4 gate lock: re-entry requires the receipt parent IPC channel')
  return new Promise((resolvePromise, rejectPromise) => {
    const timeout = setTimeout(() => finish(new Error('phase-4 gate lock: timed out waiting for re-entry grant')), 5_000)
    const onMessage = message => finish(undefined, message)
    const onDisconnect = () => finish(new Error('phase-4 gate lock: receipt parent disconnected before granting re-entry'))
    process.once('message', onMessage)
    process.once('disconnect', onDisconnect)
    function finish(error, message) {
      clearTimeout(timeout)
      process.removeListener('message', onMessage)
      process.removeListener('disconnect', onDisconnect)
      if (error) rejectPromise(error)
      else if (message?.type !== 'phase4-lock-reentry') rejectPromise(new Error('phase-4 gate lock: invalid re-entry grant'))
      else resolvePromise(message)
    }
  })
}
