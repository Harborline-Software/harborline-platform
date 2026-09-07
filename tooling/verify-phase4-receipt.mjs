#!/usr/bin/env node
// Vendored from the migration control plane's tooling/verify-platform-receipt.mjs (2026-08-20), taking the
// WORKING-TREE version, not HEAD: migration's uncommitted edit drops 'aggregate-compatibility'
// from requiredStepIds to match control ticket 063 phase 2. Vendoring HEAD would demand a gate
// step this repository no longer runs, and every commit would be rejected.
//
// It moved because the migration control plane has no remote and is being deleted; core.hooksPath pointed
// there, and git skips a missing hooksPath silently.
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {existsSync, readFileSync} from 'node:fs'
import path from 'node:path'
import {requiredStepIds} from './gate-contract.mjs'

const platform = process.argv[2]
const changed = execFileSync('git', ['diff', '--cached', '--name-only', '-z'], {cwd: platform, encoding:'utf8'}).split('\0').filter(Boolean)
if (!changed.some(name => name !== 'README.md')) process.exit(0)
// --git-dir, not --git-common-dir: the receipt is per WORKTREE. With the common dir, two worktrees
// running the gate overwrite each other's receipt and the loser's commit is refused (2026-09-03).
const gitDir = execFileSync('git', ['rev-parse', '--git-dir'], {cwd: platform, encoding:'utf8'}).trim()
const receiptPath = path.resolve(platform, gitDir, 'harborline-phase4-receipt.json')
if (!existsSync(receiptPath)) throw new Error('fresh phase-4 receipt required; run npm run receipt:phase4')
const receipt = JSON.parse(readFileSync(receiptPath, 'utf8'))
const head = execFileSync('git', ['rev-parse', 'HEAD'], {cwd: platform, encoding:'utf8'}).trim()
const tree = execFileSync('git', ['write-tree'], {cwd: platform, encoding:'utf8'}).trim()
const gate = receipt.gate
const reportSha256 = gate && createHash('sha256').update(JSON.stringify(gate)).digest('hex')
const validSteps = Array.isArray(gate?.results)
  && requiredStepIds.every((id, index) => gate.results[index]?.id === id && gate.results[index]?.passed === true)
if (receipt.schemaVersion !== 3 || receipt.repository !== 'harborline-platform' || receipt.phase !== 4
    || receipt.baseHead !== head || receipt.testedTree !== tree
    || receipt.reportSha256 !== reportSha256
    || gate?.schemaVersion !== 3 || gate?.phase !== 4 || gate?.status !== 'PASS'
    || gate?.subject?.repository !== 'harborline-platform'
    || gate?.subject?.baseHead !== head || gate?.subject?.testedTree !== tree
    || gate?.subject?.mode !== 'exact-staged-tree'
    || JSON.stringify(gate?.requiredStepIds) !== JSON.stringify(requiredStepIds)
    || !validSteps) {
  // Name the exact failing predicate: the single historic message blamed the tree for
  // every mismatch and cost a wasted resume when the real cause was the step list.
  const mismatches = []
  const expect = (name, actual, expected) => {
    if (actual !== expected) mismatches.push(`${name}: expected ${JSON.stringify(expected)}, found ${JSON.stringify(actual)}`)
  }
  expect('receipt.schemaVersion', receipt.schemaVersion, 3)
  expect('receipt.repository', receipt.repository, 'harborline-platform')
  expect('receipt.phase', receipt.phase, 4)
  expect('receipt.baseHead', receipt.baseHead, head)
  expect('receipt.testedTree', receipt.testedTree, tree)
  expect('receipt.reportSha256', receipt.reportSha256, reportSha256)
  expect('gate.schemaVersion', gate?.schemaVersion, 3)
  expect('gate.phase', gate?.phase, 4)
  expect('gate.status', gate?.status, 'PASS')
  expect('gate.subject.repository', gate?.subject?.repository, 'harborline-platform')
  expect('gate.subject.baseHead', gate?.subject?.baseHead, head)
  expect('gate.subject.testedTree', gate?.subject?.testedTree, tree)
  expect('gate.subject.mode', gate?.subject?.mode, 'exact-staged-tree')
  expect('gate.requiredStepIds', JSON.stringify(gate?.requiredStepIds), JSON.stringify(requiredStepIds))
  if (!validSteps) mismatches.push('gate.results: a required step is missing, misordered, or not passed')
  throw new Error(`phase-4 receipt does not match the staged Platform tree:\n- ${mismatches.join('\n- ') || 'predicate set disagreed without a named mismatch (inspect the receipt manually)'}`)
}
