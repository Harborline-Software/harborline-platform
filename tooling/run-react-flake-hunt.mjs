#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { cleanUpScratchOnSignal, resolveCommand, runnerEnvironment, sweepStaleScratchTrees, writeScratchPidFile } from './resolve-command.mjs'

// Ticket 289: same scratch-tree lifecycle as the receipt and the generation-smoke step.
const FLAKE_HUNT_SCRATCH_PREFIX = 'harborline-react-flake-hunt-'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')

export function summarizeRuns(runResults, expectedRuns) {
  const tests = new Map()
  const infrastructureFailures = []

  for (const result of runResults) {
    if (result.infrastructureFailure) {
      infrastructureFailures.push(result.infrastructureFailure)
      continue
    }
    for (const assertion of result.assertions) {
      const identity = `${result.moduleId} :: ${assertion.fullName}`
      const entry = tests.get(identity) ?? { moduleId: result.moduleId, test: assertion.fullName, passed: 0, failed: 0, skipped: 0 }
      if (assertion.status === 'passed') entry.passed += 1
      else if (assertion.status === 'failed') entry.failed += 1
      else entry.skipped += 1
      tests.set(identity, entry)
    }
  }

  const perTest = [...tests.values()].map(entry => ({
    ...entry,
    observedRuns: entry.passed + entry.failed + entry.skipped,
    failureRate: entry.failed / expectedRuns,
  })).sort((left, right) => left.moduleId.localeCompare(right.moduleId) || left.test.localeCompare(right.test))
  const incomplete = perTest.filter(entry => entry.observedRuns !== expectedRuns)
  for (const entry of incomplete) {
    infrastructureFailures.push(`${entry.moduleId} :: ${entry.test} appeared in ${entry.observedRuns}/${expectedRuns} runs`)
  }
  const flaky = perTest.filter(entry => entry.passed > 0 && entry.failed > 0)
  const consistentlyFailing = perTest.filter(entry => entry.failed === expectedRuns)
  return { perTest, flaky, consistentlyFailing, infrastructureFailures }
}

function parseArguments(argv) {
  let runs = 20
  const moduleIds = []
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--runs') runs = Number(argv[++index])
    else if (argv[index] === '--module') moduleIds.push(argv[++index])
    else throw new Error(`Unknown argument: ${argv[index]}`)
  }
  if (!Number.isSafeInteger(runs) || runs < 2) throw new Error('--runs must be an integer of at least 2')
  return { runs, moduleIds }
}

function assertionsFromReport(report) {
  if (!Array.isArray(report.testResults)) throw new Error('Vitest JSON report has no testResults array')
  return report.testResults.flatMap(file => {
    if (!Array.isArray(file.assertionResults)) throw new Error('Vitest JSON test result has no assertionResults array')
    return file.assertionResults.map(assertion => ({
      fullName: assertion.fullName ?? [...(assertion.ancestorTitles ?? []), assertion.title].join(' '),
      status: assertion.status,
    }))
  })
}

export function main(argv = process.argv.slice(2)) {
  const { runs, moduleIds: requestedModules } = parseArguments(argv)
  const packageJson = JSON.parse(readFileSync(resolve(reactRoot, 'package.json'), 'utf8'))
  const available = Object.entries(packageJson.scripts)
    .filter(([name, command]) => name.startsWith('test:') && /^vitest run(?:\s|$)/.test(command))
    .map(([name]) => name.slice('test:'.length))
    .sort()
  const moduleIds = requestedModules.length > 0 ? [...new Set(requestedModules)].sort() : available
  const unknown = moduleIds.filter(moduleId => !available.includes(moduleId))
  if (unknown.length > 0) throw new Error(`Unknown React test module(s): ${unknown.join(', ')}`)
  if (moduleIds.length === 0) throw new Error('No React Vitest module scripts found')

  for (const removed of sweepStaleScratchTrees(FLAKE_HUNT_SCRATCH_PREFIX)) {
    process.stderr.write(`removed stale flake-hunt scratch tree (owner gone, older than 2h): ${removed}\n`)
  }
  const directory = mkdtempSync(resolve(tmpdir(), FLAKE_HUNT_SCRATCH_PREFIX))
  writeScratchPidFile(directory)
  const disposeSignalCleanup = cleanUpScratchOnSignal(() => { rmSync(directory, { recursive: true, force: true }) })
  const runResults = []
  try {
    for (let run = 1; run <= runs; run += 1) {
      for (const moduleId of moduleIds) {
        const outputFile = resolve(directory, `${run}-${moduleId}.json`)
        const resolved = resolveCommand('npm', ['run', `test:${moduleId}`, '--', '--reporter=json', '--outputFile', outputFile])
        const result = spawnSync(resolved.executable, resolved.args, {
          cwd: reactRoot,
          encoding: 'utf8',
          maxBuffer: 64 * 1024 * 1024,
          env: { ...process.env, ...runnerEnvironment },
        })
        try {
          const report = JSON.parse(readFileSync(outputFile, 'utf8'))
          const assertions = assertionsFromReport(report)
          if (assertions.length === 0) throw new Error('report contains zero assertions')
          runResults.push({ moduleId, run, exitCode: result.status, assertions })
        } catch (error) {
          const tail = `${result.stdout ?? ''}\n${result.stderr ?? ''}`.trimEnd().split('\n').slice(-20).join('\n')
          runResults.push({ moduleId, run, infrastructureFailure: `${moduleId} run ${run}: ${error.message}\n${tail}`.trimEnd() })
        }
      }
    }
  } finally {
    disposeSignalCleanup()
  }

  const summary = summarizeRuns(runResults, runs)
  const status = summary.infrastructureFailures.length > 0 ? 'ERROR'
    : summary.flaky.length > 0 || summary.consistentlyFailing.length > 0 ? 'FAIL'
      : 'PASS'
  const report = { schemaVersion: 1, diagnostic: 'react-flake-hunt', status, runs, moduleIds, ...summary }
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
  process.exitCode = status === 'PASS' ? 0 : status === 'FAIL' ? 1 : 2
  return report
}

if (import.meta.main) main()
