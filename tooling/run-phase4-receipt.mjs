#!/usr/bin/env node
// Vendored from the migration control plane's tooling/run-phase4-receipt.mjs (2026-08-20).
//
// The ONLY producer of .git/harborline-phase4-receipt.json. Platform cannot self-mint an
// acceptable receipt: run-phase-4-gate.mjs run directly stamps mode 'current-index' and the
// verifier accepts only 'exact-staged-tree'. The detached worktree below is the difference --
// it runs the gate against the STAGED tree rather than the working tree.
import {execFileSync, spawn} from 'node:child_process'
import {createHash} from 'node:crypto'
import {mkdtempSync, rmSync, statfsSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'
import {acquirePhase4GateLock} from './phase4-gate-lock.mjs'
import {formatGateFailure} from './gate-failure-report.mjs'
import {cleanUpScratchOnSignal, killProcessTreeSync, sweepStaleScratchTrees, writeScratchPidFile} from './resolve-command.mjs'

// Ticket 289: one prefix, shared with the self-tests and with anything else that ever mints a
// `harborline-platform-receipt-*` scratch tree, so a rename in one place can't silently desync
// the sweep from what it is meant to find.
const RECEIPT_SCRATCH_PREFIX = 'harborline-platform-receipt-'

// Was resolved through migration's workspace contract. This script now lives inside the
// repository it measures, so the root is simply its own parent directory.
const platform = path.resolve(import.meta.dirname, '..')

printDiskFree('start')
// The failing runs are the ones that used to leak, so the end reading has to be printed on every
// exit path (gate failure, thrown error, signal), not only after a receipt is written.
process.on('exit', () => printDiskFree('end'))
for (const removed of sweepStaleScratchTrees(RECEIPT_SCRATCH_PREFIX)) {
  process.stderr.write(`removed stale receipt scratch tree (owner gone, older than 2h): ${removed}\n`)
}

const receiptLock = await acquirePhase4GateLock({repositoryRoot: platform})
const baseHead = git('rev-parse', 'HEAD')
const testedTree = git('write-tree')
// Resolved up front (not just on the pass path) because the failure branch below also needs it,
// to write the full report beside where the receipt itself lands.
const gitDir = execFileSync('git', ['rev-parse', '--git-dir'], {cwd: platform, encoding: 'utf8'}).trim()
const gateReportPath = path.resolve(platform, gitDir, 'harborline-phase4-gate-report.json')
const scratch = mkdtempSync(path.join(tmpdir(), RECEIPT_SCRATCH_PREFIX))
writeScratchPidFile(scratch)
const testedCheckout = path.join(scratch, 'tested-tree')

// Published by runGate so the cleanup below can stop the gate child before deleting the tree it
// is building in; null whenever no gate child is running.
let gateChild = null

function removeScratch() {
  // Ticket 289 review 1: on a signal the gate child (and its npm/dotnet grandchildren) are still
  // writing into `testedCheckout`. Removing the tree under a live writer both fails and lets the
  // orphan recreate what was just deleted, so kill the whole tree and wait for it first.
  if (gateChild) { try { killProcessTreeSync(gateChild.pid) } catch {} }
  try { execFileSync('git', ['worktree', 'remove', '--force', testedCheckout], {cwd: platform, stdio: 'ignore'}) } catch {}
  // Best-effort, like the worktree removal above. Windows returns EPERM deleting the ~28k-file
  // scratch tree even with force, and an unguarded throw here happens INSIDE finally: it masks the
  // gate's own result and skips the receipt write below, so a passing gate reports as a failure
  // that leaves no receipt. Scratch cleanup must never decide whether the gate passed.
  try { rmSync(scratch, {recursive: true, force: true}) } catch {}
}
// Covers the SIGINT/SIGTERM exit paths: cleanup runs once, then re-raises so a kill still kills.
const disposeSignalCleanup = cleanUpScratchOnSignal(removeScratch)

let report
try {
  execFileSync('git', ['worktree', 'add', '--detach', testedCheckout, baseHead], {cwd: platform, stdio: 'ignore'})
  execFileSync('git', ['read-tree', '--reset', '-u', testedTree], {cwd: testedCheckout})
  const gate = await runGate(testedCheckout, baseHead, testedTree)
  if (gate.status !== 0) {
    // The gate still prints its full JSON report to stdout on failure (only process.exitCode is
    // non-zero) -- write it whole to disk and name every failed step, rather than throwing with
    // the last 240 lines of the tail, which is almost always the passing steps that ran after
    // the failure (ticket 277).
    let gateReport
    try { gateReport = JSON.parse(gate.stdout) } catch {}
    if (gateReport) {
      writeFileSync(gateReportPath, `${JSON.stringify(gateReport, null, 2)}\n`)
      throw new Error(formatGateFailure(gateReportPath, gateReport))
    }
    writeFileSync(gateReportPath, gate.stdout)
    const boundedReport = gate.stdout.trimEnd().split('\n').slice(-240).join('\n')
    throw new Error(`phase-4 gate failed with exit code ${gate.status ?? 1} and produced no parseable JSON report; raw output written to ${gateReportPath}\n${boundedReport}`)
  }
  report = JSON.parse(gate.stdout)
  if (report.status !== 'PASS') throw new Error('phase-4 gate did not pass')
} finally {
  // Covers success, gate failure and any thrown error -- disposeSignalCleanup runs removeScratch
  // exactly once (a signal handler may already have run it) and unregisters the signal handlers.
  disposeSignalCleanup()
}
if (git('rev-parse', 'HEAD') !== baseHead || git('write-tree') !== testedTree) {
  throw new Error('Platform HEAD or staged tree changed while the phase-4 gate was running')
}
const receipt = {
  schemaVersion: 3,
  repository: 'harborline-platform',
  phase: 4,
  baseHead,
  testedTree,
  recordedAt: new Date().toISOString(),
  reportSha256: createHash('sha256').update(JSON.stringify(report)).digest('hex'),
  gate: report,
}
writeFileSync(path.resolve(platform, gitDir, 'harborline-phase4-receipt.json'), `${JSON.stringify(receipt, null, 2)}\n`)
process.stdout.write(`${JSON.stringify({status:'PASS', baseHead, testedTree, receipt:'$GIT_COMMON_DIR/harborline-phase4-receipt.json'}, null, 2)}\n`)

function printDiskFree(label) {
  try {
    const stats = statfsSync(platform)
    const freeGiB = (stats.bavail * stats.bsize) / (1024 ** 3)
    process.stderr.write(`disk free (${label}): ${freeGiB.toFixed(2)} GiB\n`)
  } catch (error) {
    process.stderr.write(`disk free (${label}): unavailable (${error.message})\n`)
  }
}

function git(...args) {
  return execFileSync('git', args, {cwd: platform, encoding: 'utf8'}).trim()
}

async function runGate(testedCheckout, baseHead, testedTree) {
  const child = spawn(process.execPath, ['tooling/run-phase-4-gate.mjs', '--phase4-lock-reentry'], {
    cwd: testedCheckout,
    stdio: ['ignore', 'pipe', 'inherit', 'ipc'],
    // Its own process group on POSIX, so removeScratch can kill the gate AND its npm/dotnet
    // grandchildren as one tree. A terminal Ctrl-C no longer reaches it directly; our own SIGINT
    // handler kills it explicitly instead.
    detached: process.platform !== 'win32',
    env: {...process.env, HARBORLINE_BASE_HEAD: baseHead, HARBORLINE_TESTED_TREE: testedTree},
  })
  gateChild = child
  const chunks = []
  let bytes = 0
  let overflow = false
  child.stdout.on('data', chunk => {
    bytes += chunk.length
    if (bytes <= 256 * 1024 * 1024) chunks.push(chunk)
    else {
      overflow = true
      child.kill()
    }
  })
  const exit = new Promise((resolve, reject) => {
    child.once('error', reject)
    child.once('close', (status, signal) => resolve({status, signal}))
  })
  const grant = receiptLock.grantChildReentry(child.pid)
  await new Promise((resolve, reject) => child.send({type: 'phase4-lock-reentry', ...grant}, error => {
    if (error) reject(error)
    else resolve()
  }))
  const result = await exit
  gateChild = null
  if (overflow) throw new Error('phase-4 gate exceeded its 256 MiB output limit')
  return {...result, stdout: Buffer.concat(chunks).toString('utf8')}
}
