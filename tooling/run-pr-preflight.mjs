#!/usr/bin/env node
// Preliminary static checks, never a phase-4 receipt or a substitute for the landing gate.
import {spawnSync} from 'node:child_process'
import {existsSync} from 'node:fs'
import {resolve} from 'node:path'

export function toolingTestsPassed(run) {
  const counts = Object.fromEntries(['tests', 'pass', 'fail', 'cancelled', 'skipped', 'todo']
    .map(key => [key, Number(new RegExp(`^# ${key} (\\d+)$`, 'm').exec(run.stdout ?? '')?.[1] ?? NaN)]))
  return run.status === 0 && counts.tests > 0 && counts.pass === counts.tests
    && counts.fail === 0 && counts.cancelled === 0 && counts.skipped === 0 && counts.todo === 0
}

export function runPrPreflight(root, run = spawnSync) {
  const checks = [
    ['consumer-neutral boundary', 'bash', ['eng/verify-boundaries.sh']],
    ['prop vocabulary', process.execPath, ['tooling/gates/scan-prop-vocabulary.mjs', '.']],
    ['recorded evidence provenance', process.execPath, ['tooling/gates/scan-evidence-provenance.mjs', '.']],
    // Existing catalog-preflight mode: checks source policy without demanding an updated full receipt.
    ['catalog preflight', process.execPath, ['tooling/validate-repository.mjs', '--allow-stale-gate']],
    ['CI contract tests', process.execPath, ['--test', '--test-reporter', 'tap',
      'tooling/tests/ci-verification.test.mjs', 'tooling/tests/ci-load.test.mjs',
      'tooling/tests/collect-gallery-shards.test.mjs', 'tooling/tests/evidence-provenance.test.mjs',
      'tooling/tests/prop-vocabulary.test.mjs']],
  ]
  for (const [name, command, args] of checks) {
    if (name === 'CI contract tests' && args.slice(3).some(file => !existsSync(resolve(root, file)))) {
      console.error('Preliminary CI contract tests: FAIL (a declared test file is missing)')
      return false
    }
    const result = run(command, args, {cwd: root, encoding: 'utf8', maxBuffer: 16 * 1024 * 1024})
    if (result.status !== 0 || (name === 'CI contract tests' && !toolingTestsPassed(result))) {
      process.stderr.write(`Preliminary ${name}: FAIL\n${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}\n`)
      return false
    }
    console.log(`Preliminary ${name}: passed`)
  }
  console.log('Preliminary PR preflight passed; no full gate proof produced.')
  return true
}

if (import.meta.main) process.exitCode = runPrPreflight(resolve(import.meta.dirname, '..')) ? 0 : 1
