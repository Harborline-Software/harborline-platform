#!/usr/bin/env node

import { spawn } from 'node:child_process'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { parseNodeTestCount } from './parse-node-test-count.mjs'
import { resolveCommand, runnerEnvironment } from './resolve-command.mjs'
import { resolvePinnedDotnet } from './resolve-dotnet.mjs'
import {copyCoberturaReport, coverageEnabled} from './coverage.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
const formsTypeScriptRoot = resolve(root, 'projections/typescript/contracts/hlp.contracts.forms')
const copilotContractsRoot = resolve(root, 'projections/typescript/application/hlp.copilot.contracts')
const ruleRuntimeTypeScriptRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-runtime')
const ruleAuthoringTypeScriptRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-authoring')
const dotnet = resolvePinnedDotnet(root)
const buildOnly = process.argv.includes('--build')
const collectCoverage = coverageEnabled()
const blazorTestProject = 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj'

// Every .NET suite the gate runs: [countKey, stepId, project]. Order is load-bearing — it is the
// order `counts` emits after the five TypeScript keys, and validate-repository.mjs and the receipt
// both read that shape. This replaced 24 near-identical `dotnet test` invocations and 24 identical
// `Passed:` parsers; adding a suite is now one row rather than three edits in three places.
// Ticket 268 fix 1. The budgeted performance rows leave this fan-out. `DataGridPerformanceTests`
// ran its two ceiling rows here, beside twenty-five parallel `dotnet test` projects, which is the
// loudest moment of the gate; ticket 265's ceilings are 2x a QUIET p95 and missed under it on both
// Macs. Those rows carry [Trait(Category, PerfBudget)] and are excluded here; the serial
// `perf-budgets` step (tooling/perf-budget-stability.mjs) runs `Category=PerfBudget` and nothing
// else. An optional fourth element is the suite's vstest filter.
const EXCLUDE_PERF_BUDGET = 'Category!=PerfBudget'
const DOTNET_SUITES = [
  ['blazor', 'blazor-native', blazorTestProject, EXCLUDE_PERF_BUDGET],
  ['contracts', 'contracts-identities-native', "projections/dotnet/contracts/hlp.contracts.identities.tests/Harborline.Contracts.Tests.csproj"],
  ['tenancy', 'foundation-tenancy-native', "projections/dotnet/foundation/hlp.foundation.tenancy.tests/Harborline.Foundation.MultiTenancy.Tests.csproj"],
  ['actor', 'foundation-actor-native', "projections/dotnet/foundation/hlp.foundation.actor.tests/Harborline.Foundation.Authorization.Tests.csproj"],
  ['session', 'foundation-session-native', "projections/dotnet/foundation/hlp.foundation.session.tests/Harborline.Foundation.Session.Tests.csproj"],
  ['uiSupport', 'foundation-ui-support-native', "projections/dotnet/foundation/hlp.ui.support.tests/Harborline.Foundation.UI.Tests.csproj"],
  ['schemaValidation', 'kernel-schema-validation-native', "projections/dotnet/kernel/hlp.kernel.schema-validation.tests/Harborline.Kernel.SchemaValidation.Tests.csproj"],
  ['workItems', 'kernel-work-items-native', "projections/dotnet/kernel/hlp.kernel.work-items.tests/Harborline.Kernel.WorkItems.Tests.csproj"],
  ['inspectionReview', 'blocks-inspection-review-native', "projections/dotnet/blocks/hlp.blocks.inspection-review.tests/Harborline.Blocks.InspectionReview.Tests.csproj"],
  ['aggregates', 'blocks-aggregates-native', "projections/dotnet/blocks/hlp.blocks.aggregates.tests/Harborline.Blocks.Aggregates.Tests.csproj"],
  ['relativeChains', 'blocks-relative-chains-native', "projections/dotnet/blocks/hlp.blocks.relative-chains.tests/Harborline.Blocks.RelativeChains.Tests.csproj"],
  ['workflow', 'blocks-workflow-native', "projections/dotnet/blocks/hlp.blocks.workflow.tests/Harborline.Blocks.Workflow.Tests.csproj"],
  ['workflowInterpreter', 'blocks-workflow-interpreter-native', "projections/dotnet/blocks/hlp.blocks.workflow-interpreter.tests/Harborline.Blocks.Workflow.Interpreter.Tests.csproj"],
  ['entityViews', 'blocks-entity-views-native', "projections/dotnet/blocks/hlp.blocks.entity-views.tests/Harborline.Blocks.EntityViews.Tests.csproj"],
  ['foundationScheduling', 'foundation-scheduling-native', "projections/dotnet/foundation/hlp.foundation.scheduling.tests/Harborline.Foundation.Scheduling.Tests.csproj"],
  ['blocksScheduling', 'blocks-scheduling-native', "projections/dotnet/blocks/hlp.blocks.scheduling.tests/Harborline.Blocks.Scheduling.Tests.csproj"],
  ['blocksCalendar', 'blocks-calendar-native', "projections/dotnet/blocks/hlp.blocks.calendar.tests/Harborline.Blocks.Calendar.Tests.csproj"],
  ['blocksReports', 'blocks-reports-native', "projections/dotnet/blocks/hlp.blocks.reports.tests/Harborline.Blocks.Reports.Tests.csproj"],
  ['blocksActivityTimeline', 'blocks-activity-timeline-native', "projections/dotnet/blocks/hlp.blocks.activity-timeline.tests/Harborline.Blocks.ActivityTimeline.Tests.csproj"],
  ['ruleRuntimeDotnet', 'foundation-rule-runtime-native', "projections/dotnet/foundation/hlp.foundation.rule-runtime.tests/Harborline.Foundation.RuleEngine.Tests.csproj"],
  ['ruleAuthoringDotnet', 'foundation-rule-authoring-native', "projections/dotnet/foundation/hlp.foundation.rule-authoring.tests/Harborline.Foundation.RuleAuthoring.Tests.csproj"],
  ['formsDotnet', 'foundation-forms-native', "projections/dotnet/foundation/hlp.foundation.forms.tests/Harborline.Foundation.Forms.Tests.csproj"],
  ['builderDefinitions', 'blocks-builder-definitions-native', "projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj"],
  ['formsEngineDotnet', 'foundation-forms-engine-native', "projections/dotnet/foundation/hlp.foundation.forms-engine.tests/Harborline.Foundation.Forms.Engine.Tests.csproj"],
  ['architecture', 'platform-architecture-native', "projections/dotnet/architecture/hlp.architecture.tests/Harborline.Architecture.Tests.csproj"],
]


function runAttempt(id, executable, args, cwd = root) {
  const started = performance.now()
  return new Promise(resolveResult => {
    const resolved = resolveCommand(executable, args)
    const child = spawn(resolved.executable, resolved.args, {
      cwd,
      env: { ...process.env, ...runnerEnvironment },
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    let stdout = ''
    let stderr = ''
    child.stdout.setEncoding('utf8')
    child.stderr.setEncoding('utf8')
    child.stdout.on('data', chunk => { stdout += chunk })
    child.stderr.on('data', chunk => { stderr += chunk })
    child.on('error', error => {
      resolveResult({
        id,
        command: [executable, ...args],
        exitCode: -1,
        durationMs: Math.round(performance.now() - started),
        passed: false,
        stdout: stdout.trimEnd(),
        stderr: `${stderr}${error.stack ?? error.message}`.trimEnd(),
      })
    })
    child.on('close', code => {
      resolveResult({
        id,
        command: [executable, ...args],
        exitCode: code ?? -1,
        durationMs: Math.round(performance.now() - started),
        passed: code === 0,
        stdout: stdout.trimEnd(),
        stderr: stderr.trimEnd(),
      })
    })
  })
}

async function run(id, executable, args, cwd = root) {
  const first = await runAttempt(id, executable, args, cwd)
  if (buildOnly || first.passed) return { ...first, attempts: 1 }
  const retry = await runAttempt(id, executable, args, cwd)
  return {
    ...retry,
    attempts: 2,
    firstFailure: {
      exitCode: first.exitCode,
      durationMs: first.durationMs,
      stdout: first.stdout.split('\n').slice(-40).join('\n'),
      stderr: first.stderr.split('\n').slice(-40).join('\n'),
    },
  }
}

let results
let coverage = []
if (buildOnly) {
  // The aggregate React declaration build consumes the canonical Forms
  // declaration output, and the rule-authoring typecheck/build consumes the
  // rule-runtime declaration output. Establish both authorities first so clean
  // builds never race a consumer against the compiler writing its dist.
  const [formsTypeScriptBuild, ruleRuntimeTypeScriptBuild] = await Promise.all([
    run('forms-typescript-build', 'npm', ['run', 'build'], formsTypeScriptRoot),
    run('rule-runtime-typescript-build', 'pnpm', ['run', 'build'], ruleRuntimeTypeScriptRoot),
  ])
  results = [formsTypeScriptBuild, ruleRuntimeTypeScriptBuild, ...await Promise.all([
      run('react-typecheck', 'npm', ['run', 'typecheck'], reactRoot),
      run('react-build', 'npm', ['run', 'build'], reactRoot),
      run('forms-typescript-typecheck', 'npm', ['run', 'typecheck'], formsTypeScriptRoot),
      run('rule-runtime-typescript-typecheck', 'pnpm', ['run', 'typecheck'], ruleRuntimeTypeScriptRoot),
      run('rule-authoring-typescript-typecheck', 'pnpm', ['run', 'typecheck'], ruleAuthoringTypeScriptRoot),
      run('rule-authoring-typescript-build', 'pnpm', ['run', 'build'], ruleAuthoringTypeScriptRoot),
      run('copilot-typescript-build', 'npm', ['run', 'build'], copilotContractsRoot),
      run('dotnet-build', dotnet.executable, ['build', 'Harborline.Platform.slnx', '--configuration', 'Release', '--no-restore', '-v:minimal']),
    ])]
} else {
  const [reactResult, formsTypeScriptResult, ruleRuntimeTypeScriptResult, ruleAuthoringTypeScriptResult, copilotTypeScriptResult, dotnetBuild] = await Promise.all([
    run('react-native', 'npm', ['run', 'test:native'], reactRoot),
    run('forms-typescript-native', 'npm', ['run', 'test:native'], formsTypeScriptRoot),
    run('rule-runtime-typescript-native', 'pnpm', ['test'], ruleRuntimeTypeScriptRoot),
    run('rule-authoring-typescript-native', 'pnpm', ['test'], ruleAuthoringTypeScriptRoot),
    run('copilot-typescript-native', 'npm', ['test'], copilotContractsRoot),
    run('dotnet-test-build', dotnet.executable, ['build', 'Harborline.Platform.slnx', '--configuration', 'Release', '--no-restore', '-v:minimal']),
  ])
  const dotnetTests = dotnetBuild.passed
    ? await Promise.all([
      ...DOTNET_SUITES.map(([, id, project, filter]) => run(id, dotnet.executable, [
        'test',
        project,
        '--configuration', 'Release',
        '--no-restore',
        '--no-build',
        '-v:minimal',
        ...(collectCoverage ? ['--settings', 'tooling/coverage.runsettings', '--collect:XPlat Code Coverage', '--results-directory', `artifacts/quality/coverage/${id}/results`] : []),
        ...(filter ? ['--filter', filter] : []),
      ])),
    ])
    : []
  results = [reactResult, formsTypeScriptResult, ruleRuntimeTypeScriptResult, ruleAuthoringTypeScriptResult, copilotTypeScriptResult, dotnetBuild, ...dotnetTests]
  if (collectCoverage) coverage = DOTNET_SUITES
    .filter(([, id]) => results.find(result => result.id === id)?.passed)
    .map(([, id]) => copyCoberturaReport({root, resultsDirectory: resolve(root, 'artifacts/quality/coverage', id, 'results'), suite: id}))
}

let formsTypeScriptTests = 0
let copilotTypeScriptTests = 0
if (!buildOnly) {
  for (const [suiteId, assign] of [
    ['forms-typescript-native', count => { formsTypeScriptTests = count }],
    ['copilot-typescript-native', count => { copilotTypeScriptTests = count }],
  ]) {
    const suite = results.find(result => result.id === suiteId)
    try {
      assign(parseNodeTestCount(suite.stdout, suite.id))
    } catch (error) {
      suite.passed = false
      suite.stderr = [suite.stderr, error instanceof Error ? error.message : String(error)].filter(Boolean).join('\n')
    }
  }
}
const passed = results.every(result => result.passed)
const reactTests = [...(results.find(result => result.id === 'react-native')?.stdout ?? '').matchAll(/Tests\s+(\d+)\s+passed/g)]
  .reduce((total, match) => total + Number(match[1]), 0)
const ruleRuntimeTypeScriptTests = Number(/Tests\s+(\d+)\s+passed/.exec(results.find(result => result.id === 'rule-runtime-typescript-native')?.stdout ?? '')?.[1] ?? 0)
const ruleAuthoringTypeScriptTests = Number(/Tests\s+(\d+)\s+passed/.exec(results.find(result => result.id === 'rule-authoring-typescript-native')?.stdout ?? '')?.[1] ?? 0)
const passedOf = id => Number(/Passed:\s+(\d+)/.exec(results.find(result => result.id === id)?.stdout ?? '')?.[1] ?? 0)
const dotnetCounts = Object.fromEntries(DOTNET_SUITES.map(([key, id]) => [key, passedOf(id)]))
process.stdout.write(`${JSON.stringify({
  schemaVersion: 1,
  status: passed ? 'PASS' : 'FAIL',
  mode: buildOnly ? 'build' : 'native-tests',
  dotnetSdk: dotnet.version,
  // Key order is load-bearing: validate-repository.mjs and the receipt both read this shape.
  // The five TypeScript keys first, then DOTNET_SUITES in table order, then total.
  counts: buildOnly ? undefined : {
    react: reactTests,
    formsTypeScript: formsTypeScriptTests,
    copilotTypeScript: copilotTypeScriptTests,
    ruleRuntimeTypeScript: ruleRuntimeTypeScriptTests,
    ruleAuthoringTypeScript: ruleAuthoringTypeScriptTests,
    ...dotnetCounts,
    total: reactTests + formsTypeScriptTests + copilotTypeScriptTests + ruleRuntimeTypeScriptTests
      + ruleAuthoringTypeScriptTests + Object.values(dotnetCounts).reduce((sum, n) => sum + n, 0),
  },
  coverage: collectCoverage ? coverage : undefined,
  results,
}, null, 2)}\n`)
process.exitCode = passed ? 0 : 1
