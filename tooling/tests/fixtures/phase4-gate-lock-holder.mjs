import {existsSync} from 'node:fs'
import {setTimeout as delay} from 'node:timers/promises'

import {acquirePhase4GateLock} from '../../phase4-gate-lock.mjs'

const [repositoryRoot, action = '0', encodedGrant] = process.argv.slice(2)
const [verb, signalPath] = action.split(/:(.*)/s)

// A hold that lasts a FIXED number of milliseconds is a race against the machine, not a test of the
// lock: under load the waiting process had not started by the time the holder released, so it never
// printed "waiting for". A signal file lets the test hold until it has OBSERVED the waiter waiting.
// The cap is a deadlock guard, not a budget, so it is generous.
const SIGNAL_CAP_MS = 120_000
async function waitForSignal(file) {
  const deadline = Date.now() + SIGNAL_CAP_MS
  while (!existsSync(file)) {
    if (Date.now() > deadline) throw new Error(`fixture timed out after ${SIGNAL_CAP_MS}ms waiting for ${file}`)
    await delay(25)
  }
}

const reentryGrant = action === 'IPC_REENTRY'
  ? await new Promise(resolve => process.once('message', resolve))
  : action === 'COPIED_GRANT'
    ? JSON.parse(Buffer.from(encodedGrant, 'base64url').toString('utf8'))
    : undefined
const lock = await acquirePhase4GateLock({
  repositoryRoot,
  command: `test-holder ${process.pid}`,
  waitIntervalMs: 50,
  pollIntervalMs: 10,
  reentryGrant,
  afterAtomicClaim: verb === 'PAUSE_AFTER_CLAIM' ? async () => {
    process.stdout.write(`claim-complete ${process.pid}\n`)
    await waitForSignal(signalPath)
  } : undefined,
})
process.stdout.write(`acquired ${process.pid}\n`)
if (action === 'THROW_UNCAUGHT') throw new Error('fixture uncaught exception after acquisition')
if (verb === 'HOLD') await waitForSignal(signalPath)
else await delay(Number(action) || 0)
if (action !== 'EXIT_WITHOUT_RELEASE') lock.release()
process.stdout.write(`released ${process.pid}\n`)
