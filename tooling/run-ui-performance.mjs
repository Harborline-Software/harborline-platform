#!/usr/bin/env node

import {spawnSync} from 'node:child_process'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {resolveCommand, runnerEnvironment} from './resolve-command.mjs'
import {resolvePinnedDotnet} from './resolve-dotnet.mjs'
import {writeUiPerformanceProfile} from './ui-performance-profile.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const profiles = {
  'hlp.ui.empty-state': {root: 'hlp.ui.empty-state', test: 'EmptyState.performance.test.tsx', blazor: 'EmptyStatePerformanceNativeTests', scale: {updates: 96, maximumTextState: true}, invariants: ['bounded-dom-or-markup', 'latest-state-visible', 'no-duplicate-actions']},
  'hlp.ui.table': {root: 'hlp.ui.table', test: 'Table.performance.test.tsx', blazor: 'TablePerformanceTests', scale: {updates: 96, rows: 256}, invariants: ['exact-row-count', 'latest-content-visible', 'no-stale-rows']},
  'hlp.ui.window': {root: 'hlp.ui.window', test: 'Window.performance.test.tsx', blazor: 'WindowPerformanceTests', scale: {updates: 96, childNodes: 256}, invariants: ['exact-child-count', 'latest-state-visible', 'no-stale-callbacks']},
  'hlp.ui.app-layout': {root: 'hlp.ui.app-layout', test: 'AppLayout.performance.test.tsx', blazor: 'AppLayoutPerformanceTests', scale: {updates: 96, navigationItems: 256, viewportTransitions: 96}, invariants: ['single-navigation-subtree', 'latest-content-visible', 'no-duplicate-or-stale-drawer-callbacks']},
  'hlp.ui.chart': {root: 'hlp.ui.chart', test: 'Chart.performance.test.tsx', blazor: 'ChartPerformanceTests', scale: {updates: 96, categories: 256, series: 2}, invariants: ['exact-accessible-row-count', 'latest-series-visible', 'linear-normalization']},
  'hlp.ui.chat': {root: 'hlp.ui.chat', test: 'Chat.performance.test.tsx', blazor: 'ChatPerformanceTests', scale: {updates: 96, messages: 256}, invariants: ['exact-message-count', 'latest-message-visible', 'no-stale-composer-state']},
  'hlp.ui.data-grid': {root: 'hlp.ui.data-grid', test: 'DataGrid.performance.test.tsx', blazor: 'DataGridPerformanceTests', scale: {updates: 96, rows: 10000, columns: 20}, invariants: ['bounded-row-window', 'declared-row-count', 'last-row-reachable']},
  'hlp.ui.gantt': {root: 'hlp.ui.gantt', test: 'Gantt.performance.test.tsx', blazor: 'GanttPerformanceTests', scale: {updates: 96, tasks: 256, dependencies: 320}, invariants: ['exact-task-count', 'exact-dependency-count', 'latest-schedule-visible']},
  'hlp.ui.numeric-text-box': {root: 'hlp.ui.numeric-text-box', test: 'NumericTextBox.performance.test.tsx', blazor: 'NumericTextBoxPerformanceTests', scale: {updates: 96, rawCharacters: 256}, invariants: ['constant-control-structure', 'latest-value-visible', 'no-stale-callbacks']},
  'hlp.ui.scheduler': {root: 'hlp.ui.scheduler', test: 'Scheduler.performance.test.tsx', blazor: 'SchedulerPerformanceTests', scale: {updates: 96, events: 256, recurrenceCap: 1000}, invariants: ['bounded-visible-event-structure', 'latest-schedule-visible', 'bounded-recurrence-expansion']},
  // The only profile whose React file is not named *.performance.test.tsx: schema-form already owns
  // that name for the quality profile's millisecond budgets. This harness records
  // wallClockBudgetUsed: false, so its file has to be the deterministic one, not the budgets one.
  'hlp.ui.schema-form': {root: 'hlp.ui.schema-form', test: 'SchemaForm.deterministic.test.tsx', blazor: 'SchemaFormPerformanceTests', scale: {updates: 96, fields: 256}, invariants: ['exact-field-count', 'latest-value-visible', 'no-stale-callbacks']},
}
const dotnet = resolvePinnedDotnet(root)
const moduleFlag = process.argv.indexOf('--module')
const requestedModuleId = moduleFlag >= 0 ? process.argv[moduleFlag + 1] : undefined
if (moduleFlag >= 0 && !requestedModuleId) throw new Error('missing deterministic UI performance module after --module')
if (requestedModuleId && !profiles[requestedModuleId]) throw new Error(`unsupported deterministic UI performance module: ${requestedModuleId}`)
const emitFlag = process.argv.indexOf('--emit')
const requestedEmitPath = emitFlag >= 0 ? process.argv[emitFlag + 1] : undefined
if (emitFlag >= 0 && (!requestedEmitPath || requestedEmitPath.startsWith('--'))) throw new Error('missing UI performance profile path after --emit')
const emitPath = requestedEmitPath ? resolve(requestedEmitPath) : undefined

const moduleIds = requestedModuleId ? [requestedModuleId] : Object.keys(profiles)
const reports = moduleIds.map(runProfile)
const passed = reports.every(report => report.status === 'PASS')
const report = requestedModuleId ? reports[0] : {
  schemaVersion: 1,
  status: passed ? 'PASS' : 'FAIL',
  method: 'deterministic-structural-bound',
  wallClockBudgetUsed: false,
  counts: {modules: reports.length, passed: reports.filter(item => item.status === 'PASS').length},
  modules: reports,
}
process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
if (emitPath) {
  if (passed) writeUiPerformanceProfile(emitPath, reports)
  else process.stderr.write('UI performance profile was not emitted because one or more profile executions failed.\n')
}
process.exitCode = passed ? 0 : 1

function runProfile(moduleId) {
  const profile = profiles[moduleId]
  const executions = [
    run('react', 'npm', [
      'exec', '--', 'vitest', 'run',
      '--config', `../${profile.root}/vitest.config.ts`,
      '--root', `../${profile.root}`,
      `src/__tests__/${profile.test}`,
    ], resolve(root, 'projections/react/ui/hlp.ui.button')),
    run('blazor', dotnet.executable, [
      'test', 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj',
      '--configuration', 'Release',
      '--filter', `FullyQualifiedName~${profile.blazor}`,
      '-v:minimal',
    ], root),
  ]
  const profilePassed = executions.every(execution => execution.exitCode === 0)
  return {
    schemaVersion: 1,
    moduleId,
    status: profilePassed ? 'PASS' : 'FAIL',
    method: 'deterministic-structural-bound',
    wallClockBudgetUsed: false,
    scale: profile.scale,
    invariants: profile.invariants,
    projections: executions.map(execution => ({id: execution.id, status: execution.exitCode === 0 ? 'PASS' : 'FAIL'})),
    executions,
  }
}

function run(id, executable, args, cwd) {
  const started = performance.now()
  const resolved = resolveCommand(executable, args)
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    // Marks the run as this stage's rather than the shared stage's. A module whose deterministic
    // file would distort a wall-clock budget collected from the same root excludes it unless this
    // is set; the other profiles ignore it.
    env: {...process.env, ...runnerEnvironment, HARBORLINE_UI_PERFORMANCE: '1'},
  })
  return {
    id,
    exitCode: result.status ?? 1,
    durationMs: Math.round(performance.now() - started),
    failureOutput: result.status === 0 ? undefined : `${result.stdout}\n${result.stderr}`.trim().split('\n').slice(-80).join('\n'),
  }
}
