#!/usr/bin/env node

import {spawn} from 'node:child_process'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {readFileSync} from 'node:fs'
import {resolveCommand, runnerEnvironment} from './resolve-command.mjs'
import {resolvePinnedDotnet} from './resolve-dotnet.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
const catalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8'))
const modules = Object.entries(catalog.modules)
  .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.')
    && Object.values(module.projections ?? {}).some(projection => projection.conformanceRunner))
  .map(([moduleId]) => moduleId)
  .sort()
const dotnet = resolvePinnedDotnet(root)
const requestedConcurrency = Number.parseInt(process.env.HARBORLINE_SHARED_CONCURRENCY ?? '6', 10)
const concurrency = Number.isFinite(requestedConcurrency)
  ? Math.max(1, Math.min(requestedConcurrency, modules.length))
  : 6

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

async function executeModules(moduleIds) {
  const results = new Array(moduleIds.length)
  let nextIndex = 0
  const workers = Array.from({length: Math.min(concurrency, moduleIds.length)}, async () => {
    while (nextIndex < moduleIds.length) {
      const index = nextIndex++
      const moduleId = moduleIds[index]
      const first = await execute(
        moduleId,
        process.execPath,
        [`conformance/${moduleId}/runners/run-shared.mjs`],
        root,
        {HARBORLINE_SHARED_SKIP_BUILD: '1'},
      )
      if (first.exitCode === 0) {
        results[index] = {...first, attempts: 1}
        continue
      }
      // One retry, mirroring run-native.mjs: a test host can die on teardown (observed as
      // 0xC0000005 after its own summary printed Failed: 0) under parallel dotnet load. A
      // genuine failure fails twice; both attempts are recorded so a retried pass is visible.
      const second = await execute(
        moduleId,
        process.execPath,
        [`conformance/${moduleId}/runners/run-shared.mjs`],
        root,
        {HARBORLINE_SHARED_SKIP_BUILD: '1'},
      )
      results[index] = {...second, attempts: 2, firstAttempt: {exitCode: first.exitCode, durationMs: first.durationMs}}
    }
  })
  await Promise.all(workers)
  return results
}

const setup = await Promise.all([
  execute('react-aggregate-build', 'npm', ['run', 'build'], reactRoot),
  execute('dotnet-solution-build', dotnet.executable, [
    'build', 'Harborline.Platform.slnx', '--configuration', 'Release', '-v:minimal',
  ]),
])
const setupPassed = setup.every(result => result.exitCode === 0)

let moduleResults = []
if (setupPassed) {
  moduleResults = await executeModules(modules)
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

process.stdout.write(`${JSON.stringify({
  schemaVersion: 1,
  status: passed ? 'PASS' : 'FAIL',
  strategy: 'one shared build followed by bounded parallel read-only module runners',
  concurrency,
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
    const failures = []
    for (const entry of report?.results ?? []) {
      if (entry.passed || failures.some(seen => seen.projection === entry.projection)) continue
      failures.push(entry)
    }
    return {
      moduleId: result.id,
      exitCode: result.exitCode,
      durationMs: result.durationMs,
      passed,
      counts: report?.counts,
      failureOutput: passed
        ? undefined
        : failures.length > 0
          ? JSON.stringify(failures, null, 2)
          : `${result.stdout}\n${result.stderr}`.trim().split('\n').slice(-80).join('\n'),
    }
  }),
}, null, 2)}\n`)
process.exitCode = passed ? 0 : 1
