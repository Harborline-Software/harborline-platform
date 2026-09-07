import assert from 'node:assert/strict'
import {execFileSync, spawn} from 'node:child_process'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, symlinkSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'
import test from 'node:test'
import {setTimeout as delay} from 'node:timers/promises'

import {acquirePhase4GateLock, resolvePhase4GateLockDirectory} from '../phase4-gate-lock.mjs'

const holderScript = path.resolve(import.meta.dirname, 'fixtures/phase4-gate-lock-holder.mjs')
const repositoryRoot = path.resolve(import.meta.dirname, '../..')

test('the package gate and receipt entry points acquire the shared lock before work', () => {
  const scripts = JSON.parse(readFileSync(path.join(repositoryRoot, 'package.json'), 'utf8')).scripts
  for (const [scriptName, entryPoint, firstWork] of [
    ['gate:phase4', 'tooling/run-phase-4-gate.mjs', 'const reactRoot'],
    ['receipt:phase4', 'tooling/run-phase4-receipt.mjs', "const baseHead = git('rev-parse', 'HEAD')"],
  ]) {
    assert.equal(scripts[scriptName], `node ${entryPoint}`)
    const source = readFileSync(path.join(repositoryRoot, entryPoint), 'utf8')
    assert.match(source, /import \{acquirePhase4GateLock\} from '\.\/phase4-gate-lock\.mjs'/)
    const acquisition = source.indexOf('await acquirePhase4GateLock({repositoryRoot:')
    assert.ok(acquisition >= 0 && acquisition < source.indexOf(firstWork), `${scriptName} acquires too late`)
  }
  const receiptSource = readFileSync(path.join(repositoryRoot, 'tooling/run-phase4-receipt.mjs'), 'utf8')
  const gateSource = readFileSync(path.join(repositoryRoot, 'tooling/run-phase-4-gate.mjs'), 'utf8')
  const lockSource = readFileSync(path.join(repositoryRoot, 'tooling/phase4-gate-lock.mjs'), 'utf8')
  assert.match(receiptSource, /stdio: \['ignore', 'pipe', 'inherit', 'ipc'\]/)
  assert.match(receiptSource, /receiptLock\.grantChildReentry\(child\.pid\)/)
  assert.match(gateSource, /reentryGrant: await receiveLockReentryGrant\(\)/)
  assert.doesNotMatch(`${receiptSource}\n${gateSource}\n${lockSource}`, /HARBORLINE_PHASE4_GATE_LOCK_TOKEN/)
})

test('two concurrent worktree gate invocations serialize', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    // The first holder used to release after a fixed 3,000ms. Under load the second process had not
    // finished starting by then, so it never printed "waiting for" and the test failed for the
    // machine's reason rather than the lock's. It now holds until the waiter has OBSERVABLY waited.
    const release = path.join(repository.scratch, 'release-first')
    const first = runHolder(repository.primary, `HOLD:${release}`)
    await first.waitFor(/acquired/)
    const second = runHolder(repository.linked, 0)
    await second.waitFor(/waiting for test-holder/, 'stderr')
    assert.doesNotMatch(second.stdout(), /acquired/, 'second holder entered before the first released')
    writeFileSync(release, 'release\n')
    assert.equal(await first.exit, 0, first.stderr())
    assert.equal(await second.exit, 0, second.stderr())
    assert.match(second.stdout(), /acquired/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('the receipt lock is re-entrant for its gate child', async () => {
  const repository = createRepositoryWithWorktree()
  const outer = await acquirePhase4GateLock({
    repositoryRoot: repository.primary,
    command: 'receipt-parent',
  })
  try {
    const nested = runHolder(repository.linked, 'IPC_REENTRY', {ipc: true})
    const grant = outer.grantChildReentry(nested.child.pid)
    nested.child.send({type: 'phase4-lock-reentry', ...grant})
    assert.equal(await nested.exit, 0, nested.stderr())
    assert.doesNotMatch(nested.stderr(), /waiting for|taking over stale holder/)
    assert.ok(existsSync(outer.lockDirectory), 'the nested release removed the receipt parent lock')
  } finally {
    outer.release()
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('a copied owner nonce is refused for an unrelated child', async () => {
  const repository = createRepositoryWithWorktree()
  const outer = await acquirePhase4GateLock({repositoryRoot: repository.primary, command: 'receipt-parent'})
  const intended = spawn(process.execPath, ['-e', 'setInterval(() => {}, 1000)'], {stdio: 'ignore'})
  try {
    outer.grantChildReentry(intended.pid)
    const owner = JSON.parse(readFileSync(path.join(outer.lockDirectory, 'owner.json'), 'utf8'))
    const copiedGrant = {holderPid: owner.pid, holderStartId: owner.processStartId, nonce: owner.reentryGrant.nonce}
    const stranger = runHolder(repository.linked, 'COPIED_GRANT', {grant: copiedGrant})
    assert.equal(await stranger.exit, 1)
    assert.match(stranger.stderr(), /child re-entry grant refused/)
    assert.ok(existsSync(outer.lockDirectory), 'the refused child disturbed the receipt parent lock')
  } finally {
    intended.kill()
    await new Promise(resolve => intended.once('exit', resolve))
    outer.release()
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('a paused live winner publishes its owner atomically and is never taken over', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    // Same rule as the serialization test: the pause lasts until the waiter is observed waiting,
    // not for a fixed 3,000ms that a loaded machine outruns.
    const resume = path.join(repository.scratch, 'resume-paused-winner')
    const first = runHolder(repository.primary, `PAUSE_AFTER_CLAIM:${resume}`)
    await first.waitFor(/claim-complete/)
    const lockDirectory = resolvePhase4GateLockDirectory(repository.primary)
    assert.equal(JSON.parse(readFileSync(path.join(lockDirectory, 'owner.json'), 'utf8')).pid, first.child.pid)
    const second = runHolder(repository.linked, 0)
    await second.waitFor(/waiting for test-holder/, 'stderr')
    assert.doesNotMatch(second.stdout(), /acquired/)
    writeFileSync(resume, 'resume\n')
    assert.equal(await first.exit, 0, first.stderr())
    assert.equal(await second.exit, 0, second.stderr())
    assert.match(second.stderr(), /waiting for test-holder/)
    assert.doesNotMatch(second.stderr(), /taking over stale holder/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('a dead holder is taken over with a printed line', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    const lockDirectory = resolvePhase4GateLockDirectory(repository.primary)
    mkdirSync(lockDirectory)
    writeFileSync(path.join(lockDirectory, 'owner.json'), `${JSON.stringify({
      pid: 2_000_000_000,
      startedAt: '2026-09-04T00:00:00.000Z',
      command: 'dead-holder',
      token: 'dead-token',
    })}\n`)
    const replacement = runHolder(repository.linked, 0)
    assert.equal(await replacement.exit, 0, replacement.stderr())
    assert.match(replacement.stderr(), /taking over stale holder dead-holder/)
    assert.match(replacement.stdout(), /acquired/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('a waiter cannot remove the live replacement of the stale instance it inspected', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    const lockDirectory = resolvePhase4GateLockDirectory(repository.primary)
    mkdirSync(lockDirectory)
    writeFileSync(path.join(lockDirectory, 'owner.json'), `${JSON.stringify({
      pid: 2_000_000_000,
      startedAt: '2026-09-04T00:00:00.000Z',
      command: 'dead-instance-a',
      token: 'instance-a',
    })}\n`)
    const resumePath = path.join(repository.scratch, 'resume-waiter-a')
    writeFileSync(resumePath, '')
    const waiterA = runHolder(repository.primary, 200, {
      env: {HARBORLINE_PHASE4_GATE_LOCK_TEST_PAUSE: resumePath},
    })
    await waiterA.waitFor(/stale-inspection-paused/)
    const waiterB = runHolder(repository.linked, 700)
    await waiterB.waitFor(/acquired/)
    assert.equal(JSON.parse(readFileSync(path.join(lockDirectory, 'owner.json'), 'utf8')).pid, waiterB.child.pid)

    writeFileSync(resumePath, 'resume\n')
    await delay(150)
    assert.doesNotMatch(waiterA.stdout(), /acquired/, 'waiter A entered while waiter B held the lock')
    assert.ok(existsSync(lockDirectory), 'waiter A removed waiter B\'s lock')
    assert.equal(JSON.parse(readFileSync(path.join(lockDirectory, 'owner.json'), 'utf8')).pid, waiterB.child.pid,
      'waiter A replaced waiter B as holder')

    assert.equal(await waiterB.exit, 0, waiterB.stderr())
    assert.equal(await waiterA.exit, 0, waiterA.stderr())
    const takeoverOutput = `${waiterA.stderr()}${waiterB.stderr()}`
    assert.equal(takeoverOutput.match(/taking over stale holder/g)?.length ?? 0, 1, takeoverOutput)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('a reused live pid with a different start time is taken over', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    const lockDirectory = resolvePhase4GateLockDirectory(repository.primary)
    mkdirSync(lockDirectory)
    writeFileSync(path.join(lockDirectory, 'owner.json'), `${JSON.stringify({
      pid: process.pid,
      processStartId: 'not-this-process-start',
      startedAt: '2026-09-04T00:00:00.000Z',
      command: 'reused-pid-holder',
      token: 'reused-token',
    })}\n`)
    const replacement = runHolder(repository.linked, 0)
    assert.equal(await replacement.exit, 0, replacement.stderr())
    assert.match(replacement.stderr(), /taking over stale holder reused-pid-holder/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

if (process.platform === 'win32') for (const signal of ['SIGINT', 'SIGTERM']) {
  test(`Windows ${signal} forces termination; the next holder recovers the abandoned lock`, async () => {
    const repository = createRepositoryWithWorktree()
    try {
      const holder = runHolder(repository.primary, 30_000)
      await holder.waitFor(/acquired/)
      const lockDirectory = resolvePhase4GateLockDirectory(repository.primary)
      assert.equal(holder.child.kill(signal), true)
      await holder.exit
      assert.equal(holder.child.signalCode, signal)
      assert.ok(existsSync(lockDirectory), 'forced termination unexpectedly ran the JavaScript release handler')
      assert.equal(JSON.parse(readFileSync(path.join(lockDirectory, 'owner.json'), 'utf8')).pid, holder.child.pid)
      const replacement = runHolder(repository.linked, 0)
      assert.equal(await replacement.exit, 0, replacement.stderr())
      assert.match(replacement.stdout(), /acquired/)
      assert.match(replacement.stderr(), /taking over stale holder test-holder/)
    } finally {
      rmSync(repository.scratch, {recursive: true, force: true})
    }
  })
}

if (process.platform !== 'win32') for (const [signal, exitCode] of [['SIGINT', 130], ['SIGTERM', 143]]) {
  test(`a real OS ${signal} releases before exiting`, async () => {
    const repository = createRepositoryWithWorktree()
    try {
      const holder = runHolder(repository.primary, 30_000)
      await holder.waitFor(/acquired/)
      assert.equal(holder.child.kill(signal), true)
      assert.equal(await holder.exit, exitCode, holder.stderr())
      const replacement = runHolder(repository.linked, 0)
      assert.equal(await replacement.exit, 0, replacement.stderr())
      assert.doesNotMatch(replacement.stderr(), /taking over stale holder/)
    } finally {
      rmSync(repository.scratch, {recursive: true, force: true})
    }
  })
}

test('an uncaught exception after acquisition releases on process exit', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    const holder = runHolder(repository.primary, 'THROW_UNCAUGHT')
    assert.equal(await holder.exit, 1)
    assert.match(holder.stderr(), /fixture uncaught exception after acquisition/)
    const replacement = runHolder(repository.linked, 0)
    assert.equal(await replacement.exit, 0, replacement.stderr())
    assert.doesNotMatch(replacement.stderr(), /taking over stale holder/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('the exit handler releases on normal process exit', async () => {
  const repository = createRepositoryWithWorktree()
  try {
    const holder = runHolder(repository.primary, 'EXIT_WITHOUT_RELEASE')
    assert.equal(await holder.exit, 0, holder.stderr())
    const replacement = runHolder(repository.linked, 0)
    assert.equal(await replacement.exit, 0, replacement.stderr())
    assert.doesNotMatch(replacement.stderr(), /taking over stale holder/)
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

test('every path to one repository resolves to one lock directory', () => {
  // Property: lock identity is the repository, not the spelling of the path used to reach it.
  // A junction (Windows) or a symlink (the macOS /var tmpdir, a symlinked lane) is a second
  // spelling; if it produced a second lock directory, two phase-4 gates could run concurrently.
  const repository = createRepositoryWithWorktree()
  try {
    const link = path.join(path.dirname(repository.scratch), `${path.basename(repository.scratch)}-link`)
    symlinkSync(repository.scratch, link, process.platform === 'win32' ? 'junction' : 'dir')
    try {
      const canonical = resolvePhase4GateLockDirectory(repository.primary)
      for (const spelling of [
        repository.primary,
        repository.linked,
        path.join(link, 'primary'),
        path.join(link, 'linked'),
      ]) assert.equal(resolvePhase4GateLockDirectory(spelling), canonical, `lock identity differs via ${spelling}`)
    } finally {
      rmSync(link, {recursive: true, force: true})
    }
  } finally {
    rmSync(repository.scratch, {recursive: true, force: true})
  }
})

function createRepositoryWithWorktree() {
  const scratch = mkdtempSync(path.join(tmpdir(), 'phase4-gate-lock-'))
  const primary = path.join(scratch, 'primary')
  const linked = path.join(scratch, 'linked')
  git(scratch, 'init', '--initial-branch=main', primary)
  git(primary, 'config', 'user.email', 'gate-lock@example.invalid')
  git(primary, 'config', 'user.name', 'Gate Lock Test')
  writeFileSync(path.join(primary, 'seed.txt'), 'seed\n')
  git(primary, 'add', 'seed.txt')
  git(primary, 'commit', '-m', 'seed')
  git(primary, 'worktree', 'add', '--detach', linked)
  assert.equal(resolvePhase4GateLockDirectory(primary), resolvePhase4GateLockDirectory(linked))
  return {scratch, primary, linked}
}

function git(cwd, ...args) {
  return execFileSync('git', args, {cwd, encoding: 'utf8', stdio: 'pipe'}).trim()
}

function runHolder(repositoryRoot, action, {ipc = false, grant, env = {}} = {}) {
  const args = [holderScript, repositoryRoot, String(action)]
  if (grant) args.push(Buffer.from(JSON.stringify(grant)).toString('base64url'))
  const child = spawn(process.execPath, args, {
    stdio: ipc ? ['ignore', 'pipe', 'pipe', 'ipc'] : ['ignore', 'pipe', 'pipe'],
    env: {...process.env, ...env},
  })
  let stdout = ''
  let stderr = ''
  const waiters = []
  const wake = () => { for (const waiter of waiters.splice(0)) waiter() }
  child.stdout.setEncoding('utf8').on('data', chunk => { stdout += chunk; wake() })
  child.stderr.setEncoding('utf8').on('data', chunk => { stderr += chunk; wake() })
  const exit = new Promise(resolve => child.once('exit', code => { wake(); resolve(code) }))
  return {
    child,
    exit,
    stdout: () => stdout,
    stderr: () => stderr,
    async waitFor(pattern, stream = 'stdout') {
      const read = () => stream === 'stderr' ? stderr : stdout
      while (!pattern.test(read())) {
        if (child.exitCode !== null) throw new Error(`holder exited before ${pattern}: ${stderr}`)
        await new Promise(resolve => waiters.push(resolve))
      }
    },
  }
}
