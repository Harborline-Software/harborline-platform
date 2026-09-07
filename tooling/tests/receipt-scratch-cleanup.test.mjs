import assert from 'node:assert/strict'
import { spawn } from 'node:child_process'
import { existsSync, mkdtempSync, rmSync, utimesSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'

import { cleanUpScratchOnSignal, sweepStaleScratchTrees, writeScratchPidFile } from '../resolve-command.mjs'

// Ticket 289: a receipt/gallery/consumer scratch tree that survives a kill, a thrown error or a
// monitor timeout is never cleaned by anything else -- 1,713 orphaned trees, 16.5 GB, on one host.
// These tests exercise the shared sweep directly against fixtures in a temp root, plus one
// end-to-end spawn to prove a SIGTERM mid-run still leaves no tree behind.

const PID_FILE = '.harborline-scratch-owner.pid'
const TWO_HOURS_MS = 2 * 60 * 60 * 1000
const here = dirname(fileURLToPath(import.meta.url))
const sigtermFixture = resolve(here, 'fixtures/receipt-scratch-sigterm-fixture.mjs')

function withRoot(body) {
  const root = mkdtempSync(join(tmpdir(), 'receipt-scratch-cleanup-'))
  try {
    return body(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

function makeTree(root, prefix, { pid, ageMs } = {}) {
  const dir = mkdtempSync(join(root, prefix))
  if (pid !== undefined) writeFileSync(join(dir, PID_FILE), String(pid))
  if (ageMs !== undefined) {
    const past = new Date(Date.now() - ageMs)
    utimesSync(dir, past, past)
  }
  return dir
}

// A pid essentially guaranteed to name no live process, without racing a real one's lifecycle.
const DEAD_PID = 999_999

test('a tree with a dead pid and an old mtime is removed', () => {
  withRoot(root => {
    const dir = makeTree(root, 'harborline-platform-receipt-', { pid: DEAD_PID, ageMs: TWO_HOURS_MS + 60_000 })
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    assert.deepEqual(removed, [dir])
    assert.equal(existsSync(dir), false)
  })
})

test('a tree with a live owner pid is left alone', () => {
  withRoot(root => {
    const dir = makeTree(root, 'harborline-platform-receipt-', { pid: process.pid, ageMs: TWO_HOURS_MS + 60_000 })
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    assert.deepEqual(removed, [])
    assert.equal(existsSync(dir), true)
  })
})

// Review 1 MINOR: a dead receipt's pid can be reused by an unrelated live process, which would
// otherwise pin that one tree forever. Past a day we stop believing the pid.
test('a tree older than the pid-trust horizon is removed even with a live pid', () => {
  withRoot(root => {
    const dir = makeTree(root, 'harborline-platform-receipt-', { pid: process.pid, ageMs: 25 * 60 * 60 * 1000 })
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    assert.deepEqual(removed, [dir])
    assert.equal(existsSync(dir), false)
  })
})

test('a young tree is left alone even with a dead pid', () => {
  withRoot(root => {
    const dir = makeTree(root, 'harborline-platform-receipt-', { pid: DEAD_PID, ageMs: 60_000 })
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    assert.deepEqual(removed, [])
    assert.equal(existsSync(dir), true)
  })
})

test('a tree that only matches a different prefix is left alone', () => {
  withRoot(root => {
    const dir = makeTree(root, 'harborline-feed-x-', { pid: DEAD_PID, ageMs: TWO_HOURS_MS + 60_000 })
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    assert.deepEqual(removed, [])
    assert.equal(existsSync(dir), true)
  })
})

test('writeScratchPidFile writes a pid file the sweep reads back', () => {
  withRoot(root => {
    const dir = mkdtempSync(join(root, 'harborline-platform-receipt-'))
    writeScratchPidFile(dir)
    const past = new Date(Date.now() - (TWO_HOURS_MS + 60_000))
    utimesSync(dir, past, past)
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { root, maxAgeMs: TWO_HOURS_MS })
    // Owned by this test process, which is alive -- left alone.
    assert.deepEqual(removed, [])
    assert.equal(existsSync(dir), true)
  })
})

// Spawns the fixture and returns once it has printed the scratch path it minted -- the tree
// exists at that point, so every assertion below is about what the production helper removed.
async function startFixture(...args) {
  const child = spawn(process.execPath, [sigtermFixture, 'harborline-platform-receipt-', ...args], {
    // The 'throw' mode's stack is expected output, not a test failure: keep it out of the report.
    stdio: ['ignore', 'pipe', args.includes('throw') ? 'ignore' : 'inherit'],
  })
  let scratch = ''
  await new Promise((resolveOutput, reject) => {
    child.stdout.on('data', chunk => {
      scratch += chunk.toString('utf8')
      if (scratch.includes('\n')) resolveOutput()
    })
    child.once('error', reject)
  })
  scratch = scratch.trim()
  const exited = new Promise(resolveExit => child.once('close', (code, signal) => resolveExit({ code, signal })))
  return { child, scratch, exited }
}

async function runSigtermFixture(...args) {
  const { child, scratch, exited } = await startFixture('signal', ...args)
  assert.ok(existsSync(scratch), `expected the fixture's scratch tree to exist before signaling: ${scratch}`)
  child.kill('SIGTERM')
  await exited
  return scratch
}

// The normal-exit path -- success, gate failure, thrown error -- is the ordinary case, and it runs
// on every platform, unlike signal delivery. It is the disposer returned by cleanUpScratchOnSignal
// that removes the tree there, so these two spawn tests are what keep the helper undeletable.
test('the disposer removes the scratch tree when the process exits normally', async () => {
  const { scratch, exited } = await startFixture('normal')
  const { code } = await exited
  assert.equal(code, 0)
  assert.equal(existsSync(scratch), false, `expected ${scratch} to be removed on normal exit`)
})

test('a thrown failure removes the scratch tree and still fails', async () => {
  const { scratch, exited } = await startFixture('throw')
  const { code } = await exited
  assert.notEqual(code, 0, 'the failure must still be a failure')
  assert.equal(existsSync(scratch), false, `expected ${scratch} to be removed after a thrown error`)
})

test('a still-writing gate child does not resurrect the tree', async () => {
  const { scratch, exited } = await startFixture('normal', '--with-child')
  await exited
  assert.equal(existsSync(scratch), false, `expected ${scratch} to be removed with its writer killed first`)
  // The writer wrote every 10ms: if it outlived the cleanup it recreates the tree within this wait.
  await new Promise(resolveWait => setTimeout(resolveWait, 500))
  assert.equal(existsSync(scratch), false, `an orphaned writer recreated ${scratch}`)
})

test('the disposer is idempotent and safe to call after cleanup already ran', () => {
  withRoot(root => {
    const dir = mkdtempSync(join(root, 'harborline-platform-receipt-'))
    let calls = 0
    const dispose = cleanUpScratchOnSignal(() => { calls += 1; rmSync(dir, { recursive: true, force: true }) })
    dispose()
    dispose()
    assert.equal(calls, 1)
    assert.equal(existsSync(dir), false)
    assert.equal(process.listenerCount('SIGTERM'), 0, 'the handlers must be removed on normal exit')
  })
})

// Windows has no real signal delivery: child.kill('SIGTERM') forcibly terminates the process
// (TerminateProcess) without ever running the JS handler -- documented and already relied on by
// phase4-gate-lock.test.mjs's own win32 branch. The safety net there is exactly this ticket's
// start-time sweep: the next run on the host recovers the tree because its pid is now gone. On a
// real POSIX host the in-process handler runs and the tree is gone immediately, before the sweep
// is even needed.
if (process.platform === 'win32') {
  test('Windows SIGTERM forces termination; the next start sweeps the abandoned tree', async () => {
    const scratch = await runSigtermFixture()
    assert.ok(existsSync(scratch), 'forced termination unexpectedly ran the JavaScript cleanup handler')
    const past = new Date(Date.now() - (TWO_HOURS_MS + 60_000))
    utimesSync(scratch, past, past)
    const removed = sweepStaleScratchTrees('harborline-platform-receipt-', { maxAgeMs: TWO_HOURS_MS })
    assert.ok(removed.includes(scratch), `expected the next start's sweep to remove ${scratch}`)
    assert.equal(existsSync(scratch), false)
  })
} else {
  test('a receipt killed mid-run (SIGTERM) leaves no scratch tree behind', async () => {
    const scratch = await runSigtermFixture()
    assert.equal(existsSync(scratch), false, `expected ${scratch} to be removed by the SIGTERM cleanup handler`)
  })
}
