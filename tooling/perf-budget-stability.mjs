#!/usr/bin/env node
// Ticket 265 fix 2, item 0. THE property a performance budget has to satisfy before its number is
// worth arguing about: on a quiet host, with no regression present, every budgeted row is green on
// every one of N consecutive runs -- and stays green under CPU contention. A row that goes red once
// at rest is a flapping budget whatever its ratio says. Two review rounds rejected ticket 265 for
// exactly that, both times on numbers that a single measurement session had said were fine.
//
// This script runs the budgeted performance rows N times, records every row's measurement from the
// `[perf] row=...` line each test writes to stderr, marks a row RED for a run when that run's
// output carries an assertion failure naming the row OR when that run produced no measurement at
// all, and prints the pass count and the p95 of the measured milliseconds per row.
//
// Ticket 265 fix 4 (review round 3, G2). A run that produced NO `[perf] row=<id>` line is a
// FAILURE, not a pass. Redness used to be `output.includes(`${row}:`)` alone, so a runner that
// crashed, hung out, failed to launch, or simply never contains the row scored green with
// `elapsed: undefined` -- `stable: true` was not proof the row had run. `--self-check` exercises
// exactly that case. The p95 is what the absolute ceilings are derived from (2x the p95
// of the slowest host), so the derivation and the stability check read the same samples.
//
//   node tooling/perf-budget-stability.mjs --runs 10                 # every row, quiet
//   node tooling/perf-budget-stability.mjs --runs 5 --burner 2       # under a two-core burner
//   node tooling/perf-budget-stability.mjs --runs 10 --react-only    # hosts without the dotnet SDK
//   node tooling/perf-budget-stability.mjs --runs 1 --only react-app-shell
//   node tooling/perf-budget-stability.mjs --self-check                   # scoring self-check, no suites
//
// Exit code 0 only if every row was green on every run.

import assert from 'node:assert/strict'
import {spawnSync, spawn} from 'node:child_process'
import {writeFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {resolveCommand, runnerEnvironment} from './resolve-command.mjs'
import {resolvePinnedDotnet} from './resolve-dotnet.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')

// Each target is one runner invocation; `rows` are the budgeted rows it measures. A row id is the
// prefix of every assertion message that row can fail with, which is how a red run is attributed.
//
// Ticket 268 fix 1. Each budgeted row carries a category, and this tool is the ONLY place that runs
// it: the xUnit rows carry [Trait("Category", "PerfBudget")] and are selected with
// `--filter Category=PerfBudget`; the vitest rows carry `[PerfBudget]` in their test name and are
// selected with `-t \[PerfBudget\]`. The parallel fan-outs carry the inverse -- run-native.mjs
// passes `Category!=PerfBudget` and the four perf-bearing scripts in hlp.ui.button/package.json
// pass `-t "^(?!.*\[PerfBudget\])"` -- so a budgeted row is measured in the serial quiet slot and
// nowhere else. tooling/tests/perf-budget-category.test.mjs asserts both halves.
const PERF_BUDGET_CATEGORY = 'Category=PerfBudget'
const PERF_BUDGET_NAME_PATTERN = '\\[PerfBudget\\]'

const targets = [
  {id: 'react-data-grid', lane: 'react', module: 'hlp.ui.data-grid', test: 'DataGrid.performance.test.tsx', rows: ['react-data-grid-no-lazy', 'react-data-grid-fifty-lazy']},
  {id: 'react-app-shell', lane: 'react', module: 'hlp.ui.app-shell', test: 'AppShell.performance.test.tsx', rows: ['react-app-shell']},
  {id: 'react-app-layout', lane: 'react', module: 'hlp.ui.app-layout', test: 'AppLayout.performance.test.tsx', rows: ['react-app-layout']},
  {id: 'react-scheduler', lane: 'react', module: 'hlp.ui.scheduler', test: 'Scheduler.performance.test.tsx', rows: ['react-scheduler']},
  {id: 'react-schema-form', lane: 'react', module: 'hlp.ui.schema-form', test: 'SchemaForm.performance.test.tsx', rows: ['react-schema-form-keystroke-latency', 'react-schema-form-large-form', 'react-schema-form-deep-collection']},
  {id: 'blazor-data-grid', lane: 'blazor', filter: PERF_BUDGET_CATEGORY, rows: ['blazor-data-grid-no-lazy', 'blazor-data-grid-fifty-lazy']},
]

const argv = process.argv.slice(2)
const flag = (name, fallback) => {
  const index = argv.indexOf(`--${name}`)
  return index >= 0 ? argv[index + 1] : fallback
}
const runs = Number(flag('runs', '10'))
const burnerThreads = Number(flag('burner', '0'))
const only = flag('only', undefined)
const jsonPath = flag('json', undefined)
const reactOnly = argv.includes('--react-only')
if (!Number.isInteger(runs) || runs < 1) throw new Error('--runs must be a positive integer')

if (argv.includes('--self-check')) { selfCheck(); process.exit(0) }

const selected = targets.filter(target => {
  if (reactOnly && target.lane !== 'react') return false
  if (only && target.id !== only && !target.rows.includes(only)) return false
  return true
})
if (selected.length === 0) throw new Error('no targets selected')

const dotnet = selected.some(target => target.lane === 'blazor') ? resolvePinnedDotnet(root) : undefined
const burners = burnerThreads > 0 ? startBurners(burnerThreads) : []
const samples = new Map() // row id -> [{run, elapsed, reference, ratio, red}]

try {
  for (let run = 1; run <= runs; run += 1) {
    for (const target of selected) {
      const output = invoke(target)
      for (const row of target.rows) {
        const measured = parseRow(output, row)
        const red = isRed(output, row, measured)
        if (!samples.has(row)) samples.set(row, [])
        samples.get(row).push({run, ...measured, red})
        process.stderr.write(`run ${run} ${row}: elapsed ${measured.elapsed ?? '?'} ratio ${measured.ratio ?? '?'} ${red ? 'RED' : 'green'}\n`)
      }
    }
  }
} finally {
  for (const child of burners) { try { child.kill('SIGKILL') } catch { /* already gone */ } }
}

const report = {
  host: `${process.platform} ${process.arch}`,
  runs,
  burnerThreads,
  rows: [...samples].map(([row, observations]) => summarise(observations, row)),
}
report.stable = stableOf(report.rows)
const text = `${JSON.stringify(report, null, 2)}\n`
process.stdout.write(text)
if (jsonPath) writeFileSync(resolve(jsonPath), text)
process.exitCode = report.stable ? 0 : 1

function invoke(target) {
  const command = target.lane === 'react'
    ? {executable: 'npm', args: ['exec', '--', 'vitest', 'run', '--config', `../${target.module}/vitest.config.ts`, '--root', `../${target.module}`, `src/__tests__/${target.test}`, '-t', PERF_BUDGET_NAME_PATTERN], cwd: resolve(root, 'projections/react/ui/hlp.ui.button')}
    : {executable: dotnet.executable, args: ['test', 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj', '--configuration', 'Release', '--no-build', '-nodeReuse:false', '-maxcpucount:6', '--filter', target.filter, '--logger', 'console;verbosity=detailed'], cwd: root}
  const resolved = resolveCommand(command.executable, command.args)
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd: command.cwd,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    env: {...process.env, ...runnerEnvironment, HARBORLINE_UI_PERFORMANCE: '1'},
  })
  return `${result.stdout ?? ''}\n${result.stderr ?? ''}`
}

// A run is red when the runner reported an assertion failure for the row, or when the run produced
// no measurement for it -- an absent `[perf] row=<id>` line means the row did not run.
function summarise(observations, row) {
  const elapsed = observations.map(observation => observation.elapsed).filter(value => Number.isFinite(value))
  const ratios = observations.map(observation => observation.ratio).filter(value => Number.isFinite(value))
  return {
    row,
    greenRuns: observations.filter(observation => !observation.red).length,
    totalRuns: observations.length,
    elapsedMs: {min: min(elapsed), p95: percentile(elapsed, 0.95), max: max(elapsed), samples: elapsed},
    ratio: ratios.length ? {min: min(ratios), p95: percentile(ratios, 0.95), max: max(ratios), samples: ratios} : undefined,
  }
}

function stableOf(rows) { return rows.every(row => row.greenRuns === row.totalRuns && row.totalRuns > 0) }

function isRed(output, row, measured) {
  return output.includes(`${row}:`) || !Number.isFinite(measured.elapsed)
}

// Negative self-check for the rule above: a row name that never appears in the output must score
// RED and must make the report unstable. Runs no suites.
function selfCheck() {
  const crashed = 'MSB1009: Project file does not exist.\n'
  const missing = parseRow(crashed, 'row-that-does-not-exist')
  assert.equal(missing.elapsed, undefined, 'a crashed run has no measurement')
  assert.equal(isRed(crashed, 'row-that-does-not-exist', missing), true, 'a run with no measurement must be RED')
  assert.equal(summarise([{run: 1, ...missing, red: true}]).greenRuns, 0, 'a red run is not a green run')
  assert.equal(stableOf([summarise([{run: 1, ...missing, red: true}])]), false, 'a row that never ran is not stable')

  const good = '[perf] row=blazor-data-grid-no-lazy elapsed=123.4\n'
  const ok = parseRow(good, 'blazor-data-grid-no-lazy')
  assert.equal(ok.elapsed, 123.4)
  assert.equal(isRed(good, 'blazor-data-grid-no-lazy', ok), false, 'a measured run with no assertion failure is green')
  process.stdout.write('perf-budget-stability self-check: ok\n')
}

function parseRow(output, row) {
  // The React rows print elapsed only (they have no reference); the Blazor row keeps its ratio.
  const match = output.match(new RegExp(`\\[perf\\] row=${row} (?:reference=([\\d.]+) )?elapsed=([\\d.]+)(?: ratio=([\\d.]+))?`))
  if (!match) return {reference: undefined, elapsed: undefined, ratio: undefined}
  return {reference: match[1] ? Number(match[1]) : undefined, elapsed: Number(match[2]), ratio: match[3] ? Number(match[3]) : undefined}
}

function min(values) { return values.length ? Math.min(...values) : undefined }

function max(values) { return values.length ? Math.max(...values) : undefined }

// Nearest-rank p95: index ceil(0.95*n)-1, so with 10 samples it is the MAX and with 30 it is the
// second largest. Stated plainly because a 10-run p95 quoted as a ceiling input is the worst
// observation of that window, not a trimmed one.
function percentile(values, fraction) {
  if (values.length === 0) return undefined
  const sorted = [...values].sort((left, right) => left - right)
  return sorted[Math.min(sorted.length - 1, Math.ceil(fraction * sorted.length) - 1)]
}

// One busy node process per core to occupy, started and killed by pid inside this script's own
// lifetime so no burner outlives the run (the lane rules forbid killing by image name).
function startBurners(count) {
  const source = 'for(;;){let x=0;for(let i=0;i<1e7;i++)x=(x*31+i)%1000003}'
  const children = Array.from({length: count}, () => spawn(process.execPath, ['-e', source], {stdio: 'ignore'}))
  process.stderr.write(`burner pids ${children.map(child => child.pid).join(', ')}\n`)
  return children
}
