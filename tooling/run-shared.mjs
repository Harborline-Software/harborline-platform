#!/usr/bin/env node

import {spawn} from 'node:child_process'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {readFileSync} from 'node:fs'
import {cpus, loadavg, freemem, totalmem} from 'node:os'
import {resolveCommand, runnerEnvironment} from './resolve-command.mjs'
import {resolvePinnedDotnet} from './resolve-dotnet.mjs'
import {moduleEvidence} from './shared-module-evidence.mjs'
import {createSharedModuleScheduler, writeSharedScheduleReport} from './shared-module-scheduler.mjs'
import {loadModuleCatalog} from './module-catalog.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
const catalog = loadModuleCatalog(root)
const modules = Object.entries(catalog.modules)
  .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.')
    && Object.values(module.projections ?? {}).some(projection => projection.conformanceRunner))
  .map(([moduleId]) => moduleId)
  .sort()
const requestedConcurrency = Number.parseInt(process.env.HARBORLINE_SHARED_CONCURRENCY ?? '6', 10)
const concurrency = Number.isFinite(requestedConcurrency)
  ? Math.max(1, Math.min(requestedConcurrency, modules.length))
  : 6
const observationOrigin = performance.now()
const observationNow = () => performance.now() - observationOrigin
const phaseResources = []
const scheduler = createSharedModuleScheduler({
  moduleIds: modules,
  concurrency,
  mode: process.env.HARBORLINE_SHARED_SCHEDULE,
  now: observationNow,
  onPhase: recordPhaseResources,
})
// Validate the selector/experiment before resolving a toolchain or launching any setup.
const dotnet = resolvePinnedDotnet(root)

function observed(read) {
  try {
    return {available: true, value: read()}
  } catch {
    return {available: false, value: null}
  }
}

function recordPhaseResources(phase) {
  phaseResources.push({
    ...phase,
    hostCpu: observed(() => {
      const entries = cpus()
      return {logicalCount: entries.length, models: [...new Set(entries.map(entry => entry.model))]}
    }),
    loadAverage: observed(loadavg),
    freeMemoryBytes: observed(freemem),
    totalMemoryBytes: observed(totalmem),
  })
}

function execute(id, executable, args, cwd = root, extraEnv = {}) {
  const started = performance.now()
  return new Promise(resolveResult => {
    const resolved = resolveCommand(executable, args)
    const child = spawn(resolved.executable, resolved.args, {
      cwd,
      env: {...process.env, ...runnerEnvironment, ...extraEnv},
      stdio: ['ignore', 'pipe', 'pipe'],
    })
    let stdout = ''
    let stderr = ''
    child.stdout.setEncoding('utf8')
    child.stderr.setEncoding('utf8')
    child.stdout.on('data', chunk => { stdout += chunk })
    child.stderr.on('data', chunk => { stderr += chunk })
    child.on('error', error => resolveResult({
      id,
      exitCode: -1,
      durationMs: Math.round(performance.now() - started),
      stdout,
      stderr: `${stderr}${error.stack ?? error.message}`,
    }))
    child.on('close', code => resolveResult({
      id,
      exitCode: code ?? -1,
      durationMs: Math.round(performance.now() - started),
      stdout,
      stderr,
    }))
  })
}

recordPhaseResources({phase: 'setup', boundary: 'start', at: new Date().toISOString(), offsetMs: observationNow()})
const setup = await Promise.all([
  execute('react-aggregate-build', 'npm', ['run', 'build'], reactRoot),
  execute('dotnet-solution-build', dotnet.executable, [
    'build', 'Harborline.Platform.slnx', '--configuration', 'Release', '-v:minimal',
  ]),
])
recordPhaseResources({phase: 'setup', boundary: 'end', at: new Date().toISOString(), offsetMs: observationNow()})
const setupPassed = setup.every(result => result.exitCode === 0)

let moduleResults = []
let scheduleEvidence = {mode: scheduler.mode, status: 'not-started', configuredConcurrency: concurrency, attempts: []}
if (setupPassed) {
  const scheduled = await scheduler.run(moduleId => execute(
    moduleId,
    process.execPath,
    [`conformance/${moduleId}/runners/run-shared.mjs`],
    root,
    {HARBORLINE_SHARED_SKIP_BUILD: '1'},
  ))
  moduleResults = scheduled.results
  scheduleEvidence = scheduled.evidence
}

const reports = []
for (const result of moduleResults) {
  if (result.exitCode !== 0) continue
  try {
    reports.push(JSON.parse(result.stdout))
  } catch {
    result.exitCode = 1
    result.stderr += '\nshared runner did not emit one JSON report'
  }
}
const passedResults = reports.reduce((total, report) => total + (report.counts?.passedResults ?? 0), 0)
const expectedResults = reports.reduce((total, report) => total + (report.counts?.expectedResults ?? 0), 0)
const passed = setupPassed
  && moduleResults.length === modules.length
  && moduleResults.every(result => result.exitCode === 0)
  && reports.length === modules.length
  && reports.every(report => report.status === 'PASS')
  && passedResults === expectedResults

const report = {
  schemaVersion: 1,
  status: passed ? 'PASS' : 'FAIL',
  strategy: scheduler.mode === 'original'
    ? 'one shared build followed by bounded parallel read-only module runners'
    : 'one shared build, ordinary module pool drained, then exclusive Chat and Grid module runners',
  concurrency,
  schedule: {
    ...scheduleEvidence,
    clock: 'monotonic milliseconds since shared-runner observation start',
    phaseResources,
    resourceScope: 'host OS observations, not effective CPU/memory quotas or causal contention evidence',
  },
  dotnetSdk: dotnet.version,
  moduleIds: modules,
  counts: {
    modules: reports.length,
    expectedModules: modules.length,
    expectedResults,
    passedResults,
  },
  setup: setup.map(result => ({
    id: result.id,
    exitCode: result.exitCode,
    durationMs: result.durationMs,
    passed: result.exitCode === 0,
    failureOutput: result.exitCode === 0 ? undefined : `${result.stdout}\n${result.stderr}`.trim().split('\n').slice(-60).join('\n'),
  })),
  modules: moduleResults.map(result => {
    // A module runner that calls runUiModuleShared without assigning its return value to
    // process.exitCode exits 0 even when its own report says FAIL. Trusting the exit code alone
    // therefore records a failing module as passed and drops every failing case, leaving the
    // aggregate count as the only evidence that anything went wrong. The report's own status is
    // the authority here, and its failing results carry the diagnosis the count can only imply.
    const report = reports.find(entry => entry.moduleId === result.id)
    const passed = result.exitCode === 0 && report?.status !== 'FAIL'
    return {
      moduleId: result.id,
      exitCode: result.exitCode,
      durationMs: result.durationMs,
      passed,
      counts: report?.counts,
      executionIntervals: scheduleEvidence.attempts.filter(attempt => attempt.moduleId === result.id),
      // A failing module's report is still on its stdout; the evidence names its failing cases
      // instead of the passing tail of the pretty-printed results array.
      ...moduleEvidence(result, passed),
    }
  }),
}
process.stdout.write(writeSharedScheduleReport(root, report))
process.exitCode = passed ? 0 : 1
