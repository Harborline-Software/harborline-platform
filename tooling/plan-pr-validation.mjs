#!/usr/bin/env node
// Conservative first slice: preserve early headless behavioral feedback for every other input.
import {execFileSync} from 'node:child_process'
import {appendFileSync, readFileSync} from 'node:fs'

const fastOnlyPaths = new Set([
  'CONTRIBUTING.md', '.github/workflows/verify.yml',
  'tooling/run-pr-preflight.mjs', 'tooling/verify-ci-lanes.mjs', 'tooling/plan-pr-validation.mjs',
  'tooling/tests/ci-verification.test.mjs', 'tooling/tests/ci-load.test.mjs',
  'tooling/tests/collect-gallery-shards.test.mjs',
])

export function requiresPrHeadless(paths) {
  return !Array.isArray(paths) || paths.length === 0 || paths.some(path => !fastOnlyPaths.has(path))
}

if (import.meta.main) {
  let required = true
  try {
    const pr = JSON.parse(readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8')).pull_request
    const base = pr?.base?.sha
    const head = pr?.head?.sha
    if (!/^[0-9a-f]{40}$/.test(base ?? '') || !/^[0-9a-f]{40}$/.test(head ?? '')) throw new Error('missing exact PR commits')
    // No rename compression: deletion of an implementation path still requires behavioral coverage.
    const paths = execFileSync('git', ['diff', '--no-renames', '--name-only', '-z', `${base}...${head}`, '--'],
      {encoding: 'utf8'}).split('\0').filter(Boolean)
    required = requiresPrHeadless(paths)
  } catch (error) {
    console.log(`Unable to classify PR inputs; retaining headless validation: ${error.message}`)
  }
  console.log(required ? 'PR behavioral feedback: headless gate required' : 'PR behavioral feedback: covered CI-flow fixtures only')
  appendFileSync(process.env.GITHUB_OUTPUT, `headless-required=${required}\n`)
}
