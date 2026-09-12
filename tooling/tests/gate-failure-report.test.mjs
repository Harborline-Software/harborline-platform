import assert from 'node:assert/strict'
import test from 'node:test'

import {collectFailedSteps, formatGateFailure} from '../gate-failure-report.mjs'

// Twenty passing steps, one ordinary failed leaf step (exitCode 1, real output), and one grouped
// step (like `native-tests`) whose nested `report.results` holds a spawn failure. The two failure
// shapes below are copied verbatim from real producers, not invented:
// - `rule-runtime-clean-install` is the exact entry captured on macpro
//   (~/logs/s265-gate-repro.json) for a spawnSync (run-phase-4-gate.mjs) step that never started:
//   exitCode: null, failureOutput: "undefined\nundefined" (stdout/stderr coerced by the template
//   literal that builds it -- there is no `error` field).
// - `blazor-native` matches run-native.mjs's spawn() `error` handler: exitCode: -1, no `error`
//   field either, the real ENOENT text folded into `stderr`.
function fixtureReport() {
  const passing = Array.from({length: 20}, (_, index) => ({
    id: `passing-step-${index}`,
    exitCode: 0,
    durationMs: 100,
    passed: true,
  }))
  return {
    status: 'FAIL',
    results: [
      ...passing,
      {
        id: 'rule-runtime-clean-install',
        command: ['pnpm', 'install', '--frozen-lockfile'],
        exitCode: 1,
        durationMs: 4200,
        passed: false,
        failureOutput: Array.from({length: 30}, (_, index) => `install-error-line-${index}`).join('\n'),
        failureEvidencePath: '.claude/gate-evidence/rule-runtime-clean-install.log',
      },
      {
        id: 'native-tests',
        exitCode: 1,
        durationMs: 9000,
        passed: false,
        report: {
          status: 'FAIL',
          results: [
            {id: 'react-native', exitCode: 0, durationMs: 500, passed: true, stdout: 'ok'},
            {
              id: 'blazor-native',
              command: ['missing-dotnet', 'test', 'Harborline.UIAdapters.Blazor.Tests.csproj'],
              exitCode: -1,
              durationMs: 12,
              passed: false,
              stdout: '',
              stderr: 'Error: spawn missing-dotnet ENOENT\n    at ChildProcess._handle.onexit (node:internal/child_process:283:19)',
            },
          ],
        },
      },
    ],
  }
}

// Real spawnSync-shaped spawn failure, unmodified from the macpro repro.
function spawnSyncSpawnFailureStep() {
  return {
    id: 'rule-runtime-clean-install',
    command: ['pnpm', 'install', '--frozen-lockfile', '--ignore-scripts'],
    exitCode: null,
    durationMs: 1,
    passed: false,
    failureOutput: 'undefined\nundefined',
  }
}

test('collectFailedSteps names the failed leaf and the spawn failure, not the passing steps or the group', () => {
  const failed = collectFailedSteps(fixtureReport().results)
  const ids = failed.map(step => step.id)
  assert.deepEqual(ids.sort(), ['blazor-native', 'rule-runtime-clean-install'])
})

test('formatGateFailure message carries the report path, both failure shapes, and no passing step id', () => {
  const message = formatGateFailure('/tmp/harborline-phase4-gate-report.json', fixtureReport())
  assert.match(message, /\/tmp\/harborline-phase4-gate-report\.json/)
  assert.match(message, /rule-runtime-clean-install/)
  assert.match(message, /install-error-line-0/)
  assert.match(message, /full output: \.claude\/gate-evidence\/rule-runtime-clean-install\.log/)
  assert.match(message, /blazor-native/)
  assert.match(message, /ENOENT/)
  assert.match(message, /missing-dotnet test/)
  for (let index = 0; index < 20; index += 1) {
    assert.doesNotMatch(message, new RegExp(`passing-step-${index}\\b`))
  }
  assert.doesNotMatch(message, /native-tests/)
  assert.doesNotMatch(message, /undefined/)
})

test('a real spawnSync ENOENT (exitCode null, placeholder "undefined\\nundefined" output) prints the command, not the placeholder', () => {
  const message = formatGateFailure('/tmp/report.json', {results: [spawnSyncSpawnFailureStep()]})
  assert.match(message, /rule-runtime-clean-install: exitCode=null command: pnpm install --frozen-lockfile --ignore-scripts/)
  assert.doesNotMatch(message, /undefined/)
})

test('a gate that fails on an invariant with every step passed names the counts, not a bare "no step" line', () => {
  const report = {
    counts: {nativeTests: 12, declaredArtifacts: 40},
    scenarioReconciliation: 'mismatch',
    results: [{id: 'ok', exitCode: 0, durationMs: 5, passed: true}],
  }
  const message = formatGateFailure('/tmp/report.json', report)
  assert.match(message, /counts=.*"nativeTests":12/)
  assert.match(message, /scenarioReconciliation=mismatch/)
})

test('formatGateFailure first takes the HEAD of a long failureOutput, not the tail (the bug being fixed)', () => {
  const message = formatGateFailure('/tmp/report.json', fixtureReport())
  assert.match(message, /install-error-line-19/)
  assert.doesNotMatch(message, /install-error-line-20/)
})

test('a grouping step marked failed with no failed children is reported as a fallback', () => {
  const report = {
    results: [
      {
        id: 'orphan-group',
        exitCode: 1,
        durationMs: 10,
        passed: false,
        failureOutput: 'the group itself failed with no failing child',
        report: {results: [{id: 'inner-ok', exitCode: 0, durationMs: 1, passed: true}]},
      },
    ],
  }
  const failed = collectFailedSteps(report.results)
  assert.deepEqual(failed.map(step => step.id), ['orphan-group'])
})

test('a truncated string report names its failed grouping step and says nested results are unavailable', () => {
  const report = {
    results: [
      {
        id: 'native-tests',
        command: ['dotnet', 'test'],
        exitCode: 1,
        durationMs: 9000,
        passed: false,
        failureOutput: 'native test runner output',
        report: 'child report contents\n[truncated after 16384 characters]',
      },
    ],
  }

  const message = formatGateFailure('/tmp/report.json', report)
  assert.deepEqual(collectFailedSteps(report.results).map(step => step.id), ['native-tests'])
  assert.match(message, /native-tests/)
  assert.match(message, /step report was truncated; nested results are unavailable/)
})

test('an object report still renders its nested failed result exactly as before', () => {
  const report = {
    results: [
      {
        id: 'native-tests',
        exitCode: 1,
        durationMs: 9000,
        passed: false,
        report: {
          results: [
            {
              id: 'react-native',
              command: ['npm', 'test'],
              exitCode: 1,
              durationMs: 15,
              passed: false,
              failureOutput: 'nested failure output',
            },
          ],
        },
      },
    ],
  }

  assert.equal(
    formatGateFailure('/tmp/report.json', report),
    'phase-4 gate report: /tmp/report.json\nreact-native: exitCode=1 durationMs=15 command: npm test\nnested failure output',
  )
})

test('a nested failed child inherits its enclosing step full-output pointer', () => {
  const report = {
    results: [{
      id: 'native-tests',
      passed: false,
      failureEvidencePath: '.claude/gate-evidence/native-tests.log',
      report: {results: [{
        id: 'blazor-native',
        exitCode: 1,
        durationMs: 15,
        passed: false,
        failureOutput: 'nested output',
      }]},
    }],
  }

  const message = formatGateFailure('/tmp/report.json', report)
  assert.match(message, /blazor-native/)
  assert.match(message, /full output: \.claude\/gate-evidence\/native-tests\.log/)
})
