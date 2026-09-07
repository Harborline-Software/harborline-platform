import {execFileSync} from 'node:child_process'
import {randomUUID} from 'node:crypto'
import {mkdirSync, readFileSync, realpathSync, renameSync, rmSync, statSync, writeFileSync} from 'node:fs'
import path from 'node:path'

const LOCK_DIRECTORY_NAME = 'harborline-phase4-gate.lock'
const OWNER_FILE_NAME = 'owner.json'

// Test-only pause seam: when HARBORLINE_PHASE4_GATE_LOCK_TEST_PAUSE names an empty regular file
// or a FIFO, stale takeover pauses after inspection until the file is populated or the FIFO is written.
// The seam is a no-op when the variable is unset or does not name either kind of filesystem entry.

export function resolvePhase4GateLockDirectory(repositoryRoot) {
  // realpath: git answers a RELATIVE `.git` for a primary worktree and an already-resolved ABSOLUTE
  // path for a linked one. Resolving the relative answer against a symlinked/junctioned caller path
  // (a macOS /var tmpdir, a junctioned lane) yields a second lock directory for the same repository,
  // and mutual exclusion is silently lost. Canonicalise the root the way git canonicalises its own.
  const canonicalRoot = realpathSync(repositoryRoot)
  const commonDir = execFileSync('git', ['rev-parse', '--git-common-dir'], {cwd: canonicalRoot, encoding: 'utf8'}).trim()
  return path.join(path.resolve(canonicalRoot, commonDir), LOCK_DIRECTORY_NAME)
}

export async function acquirePhase4GateLock({repositoryRoot, command = [process.execPath, ...process.argv.slice(1)].join(' '),
  waitIntervalMs = 60_000, pollIntervalMs = 250, reentryGrant, afterAtomicClaim} = {}) {
  const lockDirectory = resolvePhase4GateLockDirectory(repositoryRoot)
  const ownerPath = path.join(lockDirectory, OWNER_FILE_NAME)
  if (reentryGrant) {
    const inheritedOwner = readOwner(ownerPath)
    const validGrant = reentryGrant.holderPid === process.ppid
      && inheritedOwner?.pid === reentryGrant.holderPid
      && inheritedOwner.processStartId === reentryGrant.holderStartId
      && inheritedOwner.reentryGrant?.pid === process.pid
      && inheritedOwner.reentryGrant?.nonce === reentryGrant.nonce
      && isOwnerAlive(inheritedOwner)
    if (!validGrant) throw new Error('phase-4 gate lock: child re-entry grant refused')
    replaceOwner(ownerPath, inheritedOwner, {...inheritedOwner, reentryGrant: undefined})
    return Object.freeze({lockDirectory, release() {}})
  }

  const token = randomUUID()
  const processStartId = readProcessStartId(process.pid)
  if (processStartId === null) throw new Error(`phase-4 gate lock: cannot read start time for pid ${process.pid}`)
  const owner = Object.freeze({pid: process.pid, processStartId, startedAt: new Date().toISOString(), command, token})
  let nextWaitingLineAt = 0
  while (true) {
    const candidateDirectory = `${lockDirectory}.candidate-${process.pid}-${token}`
    try {
      mkdirSync(candidateDirectory, {mode: 0o700})
      try {
        writeFileSync(path.join(candidateDirectory, OWNER_FILE_NAME), `${JSON.stringify(owner, null, 2)}\n`, {flag: 'wx'})
        renameSync(candidateDirectory, lockDirectory)
        break
      } catch (error) {
        rmSync(candidateDirectory, {recursive: true, force: true})
        if (['EEXIST', 'ENOTEMPTY', 'EPERM'].includes(error?.code)) {
          // Another fully populated candidate won the atomic rename.
        } else {
          throw error
        }
      }
    } catch (error) {
      if (error?.code === 'EEXIST') {
        rmSync(candidateDirectory, {recursive: true, force: true})
      } else {
        throw error
      }
    }

    const currentOwner = readOwner(ownerPath)
    const observedDirectory = readDirectoryIdentity(lockDirectory)
    if (currentOwner && !isOwnerAlive(currentOwner)) {
      pauseAfterStaleInspectionForTest()
      if (claimAndRemoveStaleLock(lockDirectory, ownerPath, currentOwner, observedDirectory)) {
        process.stderr.write(`phase-4 gate lock: taking over stale holder ${describeOwner(currentOwner)}\n`)
        continue
      }
    }
    if (Date.now() >= nextWaitingLineAt) {
      process.stderr.write(`phase-4 gate lock: waiting for ${describeOwner(currentOwner)}\n`)
      nextWaitingLineAt = Date.now() + waitIntervalMs
    }
    await delay(pollIntervalMs)
  }

  let released = false
  const exitHandler = () => release()
  const signalHandler = signal => {
    release()
    process.exit(signal === 'SIGINT' ? 130 : 143)
  }
  const sigintHandler = () => signalHandler('SIGINT')
  const sigtermHandler = () => signalHandler('SIGTERM')
  process.once('exit', exitHandler)
  process.once('SIGINT', sigintHandler)
  process.once('SIGTERM', sigtermHandler)
  await afterAtomicClaim?.()

  function release() {
    if (released) return
    released = true
    process.removeListener('exit', exitHandler)
    process.removeListener('SIGINT', sigintHandler)
    process.removeListener('SIGTERM', sigtermHandler)
    if (readOwner(ownerPath)?.token === token) rmSync(lockDirectory, {recursive: true, force: true})
  }
  function grantChildReentry(childPid) {
    if (!Number.isInteger(childPid) || childPid <= 0) throw new Error('phase-4 gate lock: invalid child pid')
    const currentOwner = readOwner(ownerPath)
    if (currentOwner?.token !== token) throw new Error('phase-4 gate lock: current process no longer owns the lock')
    const grant = Object.freeze({holderPid: process.pid, holderStartId: processStartId, nonce: randomUUID()})
    replaceOwner(ownerPath, currentOwner, {...currentOwner, reentryGrant: {pid: childPid, nonce: grant.nonce}})
    return grant
  }
  return Object.freeze({lockDirectory, grantChildReentry, release})
}

function readOwner(ownerPath) {
  try {
    return JSON.parse(readFileSync(ownerPath, 'utf8'))
  } catch (error) {
    if (error?.code === 'ENOENT' || error instanceof SyntaxError) return null
    throw error
  }
}

function isPidAlive(pid) {
  if (!Number.isInteger(pid) || pid <= 0) return false
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    return error?.code === 'EPERM'
  }
}

function isOwnerAlive(owner) {
  if (!isPidAlive(owner?.pid)) return false
  if (typeof owner.processStartId !== 'string') return true
  const liveStartId = readProcessStartId(owner.pid)
  return liveStartId === null || liveStartId === owner.processStartId
}

function readProcessStartId(pid) {
  try {
    if (process.platform === 'linux') {
      const stat = readFileSync(`/proc/${pid}/stat`, 'utf8')
      return `linux:${stat.slice(stat.lastIndexOf(')') + 2).split(' ')[19]}`
    }
    if (process.platform === 'win32') {
      const ticks = execFileSync('powershell.exe', ['-NoLogo', '-NoProfile', '-NonInteractive', '-Command',
        `(Get-Process -Id ${pid} -ErrorAction Stop).StartTime.ToUniversalTime().Ticks`],
      {encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], windowsHide: true}).trim()
      return `win32:${ticks}`
    }
    const started = execFileSync('ps', ['-o', 'lstart=', '-p', String(pid)],
      {encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore']}).trim()
    return started ? `${process.platform}:${started}` : null
  } catch {
    return null
  }
}

function replaceOwner(ownerPath, expectedOwner, replacementOwner) {
  const currentOwner = readOwner(ownerPath)
  if (currentOwner?.token !== expectedOwner.token) throw new Error('phase-4 gate lock: owner changed during update')
  const temporaryOwnerPath = `${ownerPath}.${process.pid}.${randomUUID()}.tmp`
  try {
    writeFileSync(temporaryOwnerPath, `${JSON.stringify(replacementOwner, null, 2)}\n`, {flag: 'wx'})
    renameSync(temporaryOwnerPath, ownerPath)
  } finally {
    rmSync(temporaryOwnerPath, {force: true})
  }
}

function claimAndRemoveStaleLock(lockDirectory, ownerPath, observedOwner, observedDirectory) {
  const claimDirectory = path.join(lockDirectory, 'takeover-claim')
  try {
    mkdirSync(claimDirectory)
  } catch (error) {
    if (error?.code === 'EEXIST' || error?.code === 'ENOENT') return false
    throw error
  }
  const confirmedOwner = readOwner(ownerPath)
  const sameDeadOwner = observedOwner && JSON.stringify(confirmedOwner) === JSON.stringify(observedOwner)
    && sameDirectory(readDirectoryIdentity(lockDirectory), observedDirectory)
    && !isOwnerAlive(confirmedOwner)
  if (sameDeadOwner) {
    rmSync(lockDirectory, {recursive: true, force: true})
    return true
  }
  rmSync(claimDirectory, {recursive: true, force: true})
  return false
}

function readDirectoryIdentity(lockDirectory) {
  try {
    const {birthtimeMs, dev, ino, mtimeMs} = statSync(lockDirectory)
    return {birthtimeMs, dev, ino, mtimeMs}
  } catch (error) {
    if (error?.code === 'ENOENT') return null
    throw error
  }
}

function sameDirectory(left, right) {
  return left !== null && right !== null
    && left.dev === right.dev && left.ino === right.ino && left.birthtimeMs === right.birthtimeMs
}

function pauseAfterStaleInspectionForTest() {
  const pausePath = process.env.HARBORLINE_PHASE4_GATE_LOCK_TEST_PAUSE
  if (!pausePath) return
  const pauseTarget = statSync(pausePath, {throwIfNoEntry: false})
  if (!pauseTarget?.isFile() && !pauseTarget?.isFIFO()) return
  process.stdout.write(`stale-inspection-paused ${process.pid}\n`)
  if (pauseTarget.isFIFO()) {
    readFileSync(pausePath)
    return
  }
  while (statSync(pausePath, {throwIfNoEntry: false})?.size === 0) {
    Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, 10)
  }
}

function describeOwner(owner) {
  return owner ? `${owner.command} (pid ${owner.pid}) since ${owner.startedAt}` : 'an unknown process with no owner record'
}

function delay(milliseconds) {
  return new Promise(resolve => setTimeout(resolve, milliseconds))
}
