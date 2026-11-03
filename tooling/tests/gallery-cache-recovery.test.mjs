import assert from 'node:assert/strict'
import childProcess from 'node:child_process'
import fs from 'node:fs'
import { syncBuiltinESMExports } from 'node:module'
import test from 'node:test'

import { removeCacheWithRetry } from '../prepare-galleries.mjs'

function mockRecovery(t, flag, remove) {
  const previous = process.env.HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN
  if (flag === undefined) delete process.env.HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN
  else process.env.HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN = flag
  const removal = t.mock.method(fs, 'rmSync', remove)
  const shutdown = t.mock.method(childProcess, 'spawnSync', () => ({ status: 0 }))
  let now = 0
  t.mock.method(Date, 'now', () => { now += 1000; return now })
  syncBuiltinESMExports()
  t.after(() => {
    t.mock.restoreAll()
    syncBuiltinESMExports()
    if (previous === undefined) delete process.env.HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN
    else process.env.HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN = previous
  })
  return { removal, shutdown }
}

for (const code of ['EPERM', 'EBUSY']) {
  test(`opt-out refuses ${code} before global shutdown and preserves its cause`, t => {
    const cause = Object.assign(new Error('literal locked cache'), { code })
    const { removal, shutdown } = mockRecovery(t, '1', () => { throw cause })

    assert.throws(() => removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' }), error => {
      assert.equal(error.cause, cause)
      assert.match(error.message, /HARBORLINE_FORBID_GLOBAL_BUILD_SERVER_SHUTDOWN=1/)
      assert.match(error.message, /no shutdown was attempted/)
      assert.ok(error.message.includes(code))
      return true
    })
    assert.equal(removal.mock.callCount(), 1)
    assert.equal(shutdown.mock.callCount(), 0)
  })

  test(`default ${code} recovery shuts down once and retries removal`, t => {
    let attempts = 0
    const { removal, shutdown } = mockRecovery(t, undefined, () => {
      attempts += 1
      if (attempts < 3) throw Object.assign(new Error('literal locked cache'), { code })
    })

    removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' })

    assert.equal(removal.mock.callCount(), 3)
    assert.equal(shutdown.mock.callCount(), 1)
    assert.deepEqual(shutdown.mock.calls[0].arguments.slice(0, 2), ['mock-dotnet', ['build-server', 'shutdown']])
  })
}

test('a flag value other than 1 retains default recovery', t => {
  let attempts = 0
  const { removal, shutdown } = mockRecovery(t, '0', () => {
    attempts += 1
    if (attempts === 1) throw Object.assign(new Error('literal locked cache'), { code: 'EBUSY' })
  })

  removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' })

  assert.equal(removal.mock.callCount(), 2)
  assert.equal(shutdown.mock.callCount(), 1)
})

test('nonbusy removal errors retain their exact identity without shutdown', t => {
  const cause = Object.assign(new Error('literal bad path'), { code: 'EINVAL' })
  const { removal, shutdown } = mockRecovery(t, '1', () => { throw cause })

  assert.throws(() => removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' }), error => error === cause)
  assert.equal(removal.mock.callCount(), 1)
  assert.equal(shutdown.mock.callCount(), 0)
})

test('successful removal keeps its recursive force options and never shuts down', t => {
  const { removal, shutdown } = mockRecovery(t, '1', () => {})

  removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' })

  assert.deepEqual(removal.mock.calls.map(call => call.arguments), [['literal-cache', { recursive: true, force: true }]])
  assert.equal(shutdown.mock.callCount(), 0)
})

test('default busy recovery retains its attempt limit and single shutdown', t => {
  const { removal, shutdown } = mockRecovery(t, undefined, () => {
    throw Object.assign(new Error('literal locked cache'), { code: 'EBUSY' })
  })

  assert.throws(() => removeCacheWithRetry('literal-cache', { executable: 'mock-dotnet' }), /could not be removed after 6 attempts \(EBUSY\)/)
  assert.equal(removal.mock.callCount(), 6)
  assert.equal(shutdown.mock.callCount(), 1)
})
