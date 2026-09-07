#!/usr/bin/env node

// Windows cannot spawn the npm/pnpm `.cmd` shims without `shell: true`, and
// `shell: true` changes argument-quoting semantics inside the exact code the gate
// receipt attests. Instead, resolve those commands to their JavaScript entry points
// and run them through the current Node host: spawn semantics stay identical on
// every platform, and recorded command arrays keep the logical name ('npm', ...)
// because callers translate only at the spawn call itself. Non-Windows platforms
// and every other executable pass through untouched.

import { execFileSync, spawnSync } from 'node:child_process'
import { existsSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { delimiter, dirname, join } from 'node:path'

const isWindows = process.platform === 'win32'
const cache = new Map()

function shimDirectory(name) {
  for (const directory of (process.env.PATH ?? '').split(delimiter)) {
    if (directory && existsSync(join(directory, `${name}.cmd`))) return directory
  }
  return null
}

function npmCliScript() {
  // npm_execpath is set when this process was itself launched via `npm run`.
  const fromEnvironment = process.env.npm_execpath
  if (fromEnvironment && fromEnvironment.endsWith('npm-cli.js') && existsSync(fromEnvironment)) return fromEnvironment
  // Prefer the PATH shim's npm (what typing `npm` runs) over Node's bundled copy.
  const candidates = []
  const shim = shimDirectory('npm')
  if (shim) candidates.push(join(shim, 'node_modules', 'npm', 'bin', 'npm-cli.js'))
  candidates.push(join(dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npm-cli.js'))
  return candidates.find(candidate => existsSync(candidate)) ?? null
}

function entryScript(name) {
  if (name === 'npm') return npmCliScript()
  if (name === 'pnpm') {
    const shim = shimDirectory('pnpm')
    if (shim) {
      const cli = join(shim, 'node_modules', 'pnpm', 'bin', 'pnpm.cjs')
      if (existsSync(cli)) return cli
    }
    return null
  }
  return null
}

export function resolveCommand(executable, args) {
  if (!isWindows || !['npm', 'npx', 'pnpm'].includes(executable)) return { executable, args }
  if (!cache.has(executable)) cache.set(executable, entryScript(executable))
  const script = cache.get(executable)
  if (!script) throw new Error(`unable to resolve ${executable} to a JavaScript entry point on Windows`)
  return { executable: process.execPath, args: [script, ...args] }
}

// The .NET CLI's telemetry sender blocks `dotnet` on macOS: measured on macpro (M1 Pro,
// 2026-09-05) four of eight identical `dotnet test --no-build` spawns took 21.5 s instead of
// 1.8 s, and the stall survived every stdio shape (pipe, ignore, file-backed), so it is the CLI
// waiting on the sender's flush, not a grandchild holding the runner's pipe. With the opt-out
// set, eight of eight took 1.1-1.2 s. It changes nothing a receipt records: recorded command
// arrays are built by callers, and the environment is never hashed into a step entry.
export const runnerEnvironment = { CI: '1', NO_COLOR: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' }

// Ticket 289: the receipt (and the gallery/consumer steps that build their own scratch trees in
// the temp directory) leave a multi-GB tree behind on a failure, a kill, or a monitor timeout --
// nothing else ever removes it (1,713 orphaned trees, 16.5 GB, on one host). Each producer writes
// this file inside its own scratch tree the moment it creates it, and sweeps its own prefix with
// sweepStaleScratchTrees() at start, before making a new tree. A tree is removed only when it is
// BOTH older than maxAgeMs AND its owning pid is gone -- a live owner (even a slow one, on another
// host-day's run) is never touched by a sibling.
const SCRATCH_PID_FILE = '.harborline-scratch-owner.pid'
const TWO_HOURS_MS = 2 * 60 * 60 * 1000
// A pid can be reused by an unrelated live process, and `process.kill(pid, 0)` then reports a dead
// owner's tree as alive forever. Past this age we stop believing the pid: no receipt, gallery or
// consumer step has ever run for a day, so a tree this old is abandoned whatever the pid says.
const PID_TRUST_HORIZON_MS = 24 * 60 * 60 * 1000

export function writeScratchPidFile(scratchDir) {
  writeFileSync(join(scratchDir, SCRATCH_PID_FILE), String(process.pid))
}

function scratchOwnerIsAlive(scratchDir) {
  let pid
  try {
    pid = Number(readFileSync(join(scratchDir, SCRATCH_PID_FILE), 'utf8').trim())
  } catch {
    return false // no pid file: nothing ever claimed ownership, safe to remove once old enough
  }
  if (!Number.isInteger(pid) || pid <= 0) return false
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    return error.code === 'EPERM' // exists, just not ours to signal -- still alive
  }
}

// Registers SIGINT/SIGTERM handlers that run `cleanup()` once, then remove themselves and
// re-raise the signal so the process still dies its normal way (right exit code, no swallowed
// signal). Returns a disposer: callers call it from their own success/failure `finally` so a
// signal arriving after normal completion doesn't run cleanup twice, and a normal exit removes
// the handlers rather than leaving them registered past the work they guard.
export function cleanUpScratchOnSignal(cleanup) {
  let done = false
  const runOnce = () => {
    if (done) return
    done = true
    cleanup()
  }
  const handlers = {}
  for (const signal of ['SIGINT', 'SIGTERM']) {
    handlers[signal] = () => {
      runOnce()
      process.removeListener(signal, handlers[signal])
      process.kill(process.pid, signal)
    }
    process.on(signal, handlers[signal])
  }
  return () => {
    runOnce()
    for (const signal of ['SIGINT', 'SIGTERM']) process.removeListener(signal, handlers[signal])
  }
}

// Ticket 289 review 1: removing the scratch tree while the gate child is still building inside it
// races the writer -- the rmSync fails halfway and the child recreates directories under the path
// just removed, resurrecting the tree the cleanup was there to remove. So kill the whole tree of
// processes first and WAIT for it, synchronously, because the only callers are signal handlers and
// `finally` blocks. The pid must be a detached process-group leader on POSIX (spawn with
// `detached: true`), otherwise `-pid` would name our own group.
export function killProcessTreeSync(pid, { timeoutMs = 5000 } = {}) {
  if (!Number.isInteger(pid) || pid <= 0) return false
  try {
    if (isWindows) execFileSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' })
    else process.kill(-pid, 'SIGTERM')
  } catch {} // already gone, or not ours to signal: the wait below settles it either way
  const idle = new Int32Array(new SharedArrayBuffer(4))
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      process.kill(pid, 0)
    } catch {
      return true
    }
    Atomics.wait(idle, 0, 0, 50)
  }
  return false
}

export function sweepStaleScratchTrees(prefix, { root = tmpdir(), maxAgeMs = TWO_HOURS_MS } = {}) {
  const removed = []
  let entries
  try {
    entries = readdirSync(root)
  } catch {
    return removed
  }
  for (const name of entries) {
    if (!name.startsWith(prefix)) continue
    const dir = join(root, name)
    let stat
    try {
      stat = statSync(dir)
    } catch {
      continue
    }
    const ageMs = Date.now() - stat.mtimeMs
    if (!stat.isDirectory() || ageMs < maxAgeMs) continue
    if (ageMs < PID_TRUST_HORIZON_MS && scratchOwnerIsAlive(dir)) continue
    try {
      rmSync(dir, { recursive: true, force: true })
      removed.push(dir)
    } catch {}
  }
  return removed
}

// Ticket 275: gallery-gate steps had no timeout, so a wedged `npm ci` or `dotnet restore --no-cache`
// looked identical to one that was merely slow -- a node process idling at ~0.1s CPU for hours with
// nothing on stdout. Shared here so run-gallery-gate.mjs and prepare-galleries.mjs read the same
// knob instead of drifting apart. Read on every call (not cached at import time) so a self-test can
// set HARBORLINE_GALLERY_STEP_BUDGET_MS before calling run() without needing a fresh module graph.
// 900s default; override with HARBORLINE_GALLERY_STEP_BUDGET_MS.
export function stepBudgetMs() {
  return Number(process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS ?? 900_000) || 900_000
}

// The spawnSync + budget + resolveCommand plumbing shared by run-gallery-gate.mjs's and
// prepare-galleries.mjs's own private run() functions (each keeps its own breadcrumb text and
// named-timeout error message, since they mean different things to their callers). Pulled out
// here so the plumbing is written once and a self-test can exercise the budget/kill behavior on
// its own, below the two named-failure messages.
export function spawnWithBudget(executable, args, { cwd = process.cwd(), extraEnv = {}, budgetMs = stepBudgetMs() } = {}) {
  const resolved = resolveCommand(executable, args)
  return spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 128 * 1024 * 1024,
    timeout: budgetMs,
    killSignal: 'SIGKILL',
    env: { ...process.env, ...runnerEnvironment, ...extraEnv },
  })
}

// verify-package-fixtures.mjs runs ~20 serial `dotnet restore --force --no-cache` invocations plus
// as many npm install/pack steps. It was measured once at 66 minutes IN TOTAL while every single
// invocation was fine, so the only useful budget here is PER INVOCATION, not over the script as a
// whole: a wedge in restore 14 of 20 gets named, and a merely slow run is left alone. It lives here
// rather than inside verify-package-fixtures.mjs because that file runs its whole body on import,
// so nothing defined in it can be reached by a self-test.
export function runFixtureStep(executable, args, { cwd = process.cwd(), env = {} } = {}) {
  const command = `${executable} ${args.join(' ')}`
  process.stderr.write(`package fixture step started (budget ${stepBudgetMs()}ms): ${command}\n`)
  const result = spawnWithBudget(executable, args, { cwd, extraEnv: env })
  if (result.error?.code === 'ETIMEDOUT') {
    throw new Error(`package fixture step exceeded its ${stepBudgetMs()}ms budget and was killed: ${command}`)
  }
  if (result.status !== 0) {
    throw new Error(`${command} failed (${result.status})\n${result.stdout}\n${result.stderr}`)
  }
  return result.stdout
}
