#!/usr/bin/env node
// Conservative first slice: preserve early headless behavioral feedback for every other input.
// Base-sourced policy (owner 2026-10-01): verify.yml runs THIS file from the PR's base commit, through
// tooling/run-base-policy.mjs, so a PR cannot widen the list below for itself. Workflow files and the policy
// scripts are deliberately absent from it.
import {execFileSync} from 'node:child_process'

const fastOnlyPaths = new Set([
  'CONTRIBUTING.md',
  'tooling/tests/ci-verification.test.mjs', 'tooling/tests/ci-load.test.mjs',
  'tooling/tests/collect-gallery-shards.test.mjs',
])

export function requiresPrHeadless(paths) {
  return !Array.isArray(paths) || paths.length === 0 || paths.some(path => !fastOnlyPaths.has(path))
}

// Usage: plan-pr-validation.mjs <base-sha> <head-sha>, run inside a repository holding both commits. Prints
// exactly `true` or `false`; any doubt exits non-zero and the wrapper keeps the headless gate.
if (import.meta.main) {
  const [base, head] = process.argv.slice(2)
  if (!/^[0-9a-f]{40}$/.test(base ?? '') || !/^[0-9a-f]{40}$/.test(head ?? '')) {
    console.error('usage: plan-pr-validation.mjs <base-sha> <head-sha>')
    process.exit(1)
  }
  // PROC-0001: every deletion/rename requires headless, including allowlisted paths. No rename
  // compression means even a rename between two allowed paths includes a deletion. Keep statuses;
  // path-only output cannot distinguish an allowed modification from an allowed deletion.
  const changes = execFileSync('git', ['diff', '--no-renames', '--name-status', '-z', `${base}...${head}`, '--'],
    {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).split('\0')
  if (changes.pop() !== '' || changes.length % 2 !== 0) throw new Error('unreadable Git change-status records')
  const paths = []
  let structuralChange = false
  for (let index = 0; index < changes.length; index += 2) {
    if (!['A', 'M'].includes(changes[index]) || !changes[index + 1]) structuralChange = true
    paths.push(changes[index + 1])
  }
  console.log(String(structuralChange || requiresPrHeadless(paths)))
}
