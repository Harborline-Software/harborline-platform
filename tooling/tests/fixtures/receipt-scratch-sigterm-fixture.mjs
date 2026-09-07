// Ticket 289: a minimal stand-in for run-phase4-receipt.mjs's own scratch-tree lifecycle -- mints
// a scratch tree under the same prefix, writes its pid file, installs the same signal cleanup
// (optionally with a gate-child stand-in writing into the tree), then either idles (so the test
// can signal it), exits normally, or throws. The self-test spawns this, waits for the scratch
// path on stdout, and asserts what the production helper left behind.
//
// Usage: fixture <prefix> [signal|normal|throw] [--with-child]
import { spawn } from 'node:child_process'
import { existsSync, mkdtempSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'

import { cleanUpScratchOnSignal, killProcessTreeSync, writeScratchPidFile } from '../../resolve-command.mjs'

const [prefix, mode = 'signal'] = process.argv.slice(2)
const withChild = process.argv.includes('--with-child')

const scratch = mkdtempSync(join(tmpdir(), prefix))
writeScratchPidFile(scratch)

// Stands in for the phase-4 gate child: keeps writing into the scratch tree until it is killed,
// so a cleanup that deletes the tree without stopping its writer sees the tree come back.
const writerSource =
  "const {mkdirSync,writeFileSync}=require('node:fs');const d=process.argv[1];" +
  "setInterval(()=>{try{mkdirSync(d+'/writer',{recursive:true});writeFileSync(d+'/writer/'+Date.now()+'.txt','x')}catch{}},10);" +
  // Self-terminates, so a mutation that skips the kill leaves a noisy test rather than a permanent
  // orphan writing into the temp directory.
  "setTimeout(()=>process.exit(0),15000)"
const child = withChild
  ? spawn(process.execPath, ['-e', writerSource, scratch], { stdio: 'ignore', detached: process.platform !== 'win32' })
  : null
// The fixture must be able to exit on its own even when the cleanup never kills the writer (that is
// exactly the mutation these tests have to catch), so the writer never holds the event loop open.
if (child) child.unref()

// Synchronous, because the cleanup this fixture exists to exercise is itself synchronous (it runs
// from a signal handler and from `finally`).
function sleepSync(ms) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, ms)
}

// A grandchild does not outlive this process on Windows, so the "did the cleanup stop the writer
// first" question has to be answered from INSIDE the fixture, while it is still running: wait until
// the writer is demonstrably writing, clean up, then look again.
if (child) {
  const deadline = Date.now() + 10_000
  while (!existsSync(join(scratch, 'writer')) && Date.now() < deadline) sleepSync(20)
  if (!existsSync(join(scratch, 'writer'))) {
    process.stderr.write('fixture: the writer child never started\n')
    process.exit(2)
  }
}

const disposeSignalCleanup = cleanUpScratchOnSignal(() => {
  if (child) killProcessTreeSync(child.pid)
  rmSync(scratch, { recursive: true, force: true })
})

process.stdout.write(`${scratch}\n`)

if (mode === 'signal') {
  setInterval(() => {}, 1000)
} else {
  try {
    if (mode === 'throw') throw new Error('fixture failed after minting its scratch tree')
  } finally {
    disposeSignalCleanup()
  }
  if (child) {
    // The writer wrote every 10ms: if the cleanup removed the tree without killing it first, it
    // recreates the tree well inside this window.
    sleepSync(300)
    if (existsSync(scratch)) {
      process.stderr.write(`fixture: the writer child recreated ${scratch} after cleanup\n`)
      process.exit(3)
    }
  }
}
