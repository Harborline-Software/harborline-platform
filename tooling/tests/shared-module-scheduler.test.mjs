import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import test from 'node:test'
import {createSharedModuleScheduler, selectSharedSchedule} from '../shared-module-scheduler.mjs'
import {moduleEvidence} from '../shared-module-evidence.mjs'

// Oracles: Chris Q5's six workers / 79 total / 77 ordinary / Chat then Grid / one retry,
// the external catalog roster, and independently controlled completion times and results.
// These executors exercise orchestration only; they are not passing UI or timing evidence.
function deferred() {
  let resolve
  const promise = new Promise(complete => { resolve = complete })
  return {promise, resolve}
}

function result(id, exitCode = 0, stdout = 'module output', stderr = '') {
  return {id, exitCode, durationMs: 17, stdout, stderr}
}

test('original remains the default and retains the general roster and concurrency override', async () => {
  assert.equal(selectSharedSchedule(undefined), 'original')
  assert.equal(selectSharedSchedule('original'), 'original')
  assert.equal(selectSharedSchedule('exclusive-tail'), 'exclusive-tail')
  const input = Object.freeze(['hlp.ui.chat', 'ordinary', 'hlp.ui.data-grid'])
  const release = deferred()
  const calls = []
  let active = 0
  let peak = 0
  const scheduler = createSharedModuleScheduler({moduleIds: input, concurrency: 2})
  const running = scheduler.run(async id => {
    calls.push(id)
    peak = Math.max(peak, ++active)
    await release.promise
    active -= 1
    return result(id)
  })
  const initiallyStarted = [...calls]
  release.resolve()
  const completed = await running
  assert.deepEqual(initiallyStarted, ['hlp.ui.chat', 'ordinary'])
  assert.deepEqual(calls, ['hlp.ui.chat', 'ordinary', 'hlp.ui.data-grid'])
  assert.equal(peak, 2)
  assert.deepEqual(completed.results.map(entry => entry.id), ['hlp.ui.chat', 'ordinary', 'hlp.ui.data-grid'])
  assert.equal(completed.evidence.mode, 'original')
  assert.equal(completed.evidence.configuredConcurrency, 2)
  assert.deepEqual(completed.evidence.phases.map(phase => phase.name), ['original-pool'])

  // Missing designated tail IDs is valid for original mode, including an empty roster.
  const small = await createSharedModuleScheduler({moduleIds: ['only'], concurrency: 3, mode: 'original'})
    .run(async id => result(id))
  assert.deepEqual(small.results.map(entry => entry.id), ['only'])
  const empty = await createSharedModuleScheduler({moduleIds: [], concurrency: 1}).run(() => {
    assert.fail('empty original roster must not launch a module')
  })
  assert.deepEqual(empty.results, [])
})

test('unknown selectors and invalid proposed inputs refuse before any module launch', () => {
  let calls = 0
  const execute = async id => { calls += 1; return result(id) }
  for (const mode of ['', 'Original', 'exclusive', 'unknown']) {
    assert.throws(() => createSharedModuleScheduler({moduleIds: ['one'], concurrency: 6, mode}).run(execute),
      /HARBORLINE_SHARED_SCHEDULE/)
  }
  assert.throws(() => createSharedModuleScheduler({
    moduleIds: ['hlp.ui.chat', 'hlp.ui.data-grid'], concurrency: 5, mode: 'exclusive-tail',
  }).run(execute), /requires six ordinary workers/)
  for (const moduleIds of [['hlp.ui.chat'], ['hlp.ui.data-grid'], ['ordinary']]) {
    assert.throws(() => createSharedModuleScheduler({moduleIds, concurrency: 6, mode: 'exclusive-tail'}).run(execute),
      /requires both hlp.ui.chat and hlp.ui.data-grid/)
  }
  assert.throws(() => createSharedModuleScheduler({
    moduleIds: ['hlp.ui.chat', 'hlp.ui.chat', 'hlp.ui.data-grid'], concurrency: 6, mode: 'exclusive-tail',
  }).run(execute), /distinct nonblank/)
  assert.equal(calls, 0)
})

test('both modes keep all 79 real catalog modules; only the proposed mode has the literal Chat/Grid tail', async () => {
  const catalog = JSON.parse(readFileSync(new URL('../../catalog/modules.yaml', import.meta.url), 'utf8'))
  const moduleIds = Object.entries(catalog.modules)
    .filter(([id, module]) => id.startsWith('hlp.ui.')
      && Object.values(module.projections ?? {}).some(projection => projection.conformanceRunner))
    .map(([id]) => id).sort()
  assert.equal(moduleIds.length, 79)
  assert.ok(moduleIds.includes('hlp.ui.chat'))
  assert.ok(moduleIds.includes('hlp.ui.data-grid'))
  const originalInput = [...moduleIds]
  for (const mode of ['original', 'exclusive-tail']) {
    const calls = []
    const completed = await createSharedModuleScheduler({moduleIds, concurrency: 6, mode})
      .run(async id => { calls.push(id); return result(id) })
    assert.equal(calls.length, 79)
    assert.equal(new Set(calls).size, 79)
    for (const id of originalInput) assert.equal(calls.filter(called => called === id).length, 1)
    if (mode === 'exclusive-tail') {
      assert.deepEqual(calls.slice(-2), ['hlp.ui.chat', 'hlp.ui.data-grid'])
      assert.equal(completed.evidence.phases[0].moduleIds.length, 77)
      assert.deepEqual(completed.evidence.phases.map(phase => phase.name), [
        'ordinary-pool', 'exclusive:hlp.ui.chat', 'exclusive:hlp.ui.data-grid',
      ])
    } else {
      assert.equal(completed.evidence.phases[0].moduleIds.length, 79)
      assert.deepEqual(completed.evidence.phases.map(phase => phase.name), ['original-pool'])
    }
    assert.deepEqual(completed.results.map(entry => entry.id), originalInput)
    assert.deepEqual(moduleIds, originalInput)
    assert.ok(completed.results.every(entry => entry.attempts === 1))
  }
})

test('six ordinary workers fully drain retries before the exclusive Chat retry and then Grid', async () => {
  // Deliberately put heavy IDs before/between ordinary IDs, so input order cannot substitute
  // for the explicit phases. The 77-name fixture is independent of production tail constants.
  const ordinary = Array.from({length: 77}, (_, index) => `ordinary-${String(index).padStart(2, '0')}`)
  const input = Object.freeze(['hlp.ui.data-grid', ...ordinary.slice(0, 5), 'hlp.ui.chat', ...ordinary.slice(5)])
  const releasePool = deferred()
  const releaseOrdinaryRetry = deferred()
  const ordinaryAtRest = deferred()
  const releaseChatRetry = deferred()
  const chatRetryStarted = deferred()
  const calls = []
  const seen = new Map()
  const active = new Set()
  const tailOtherActive = []
  let peak = 0
  let otherOrdinaryFinished = 0
  let ordinaryRetryStarted = false
  const signalOrdinaryAtRest = () => {
    if (otherOrdinaryFinished === 76 && ordinaryRetryStarted) ordinaryAtRest.resolve()
  }
  const running = createSharedModuleScheduler({moduleIds: input, concurrency: 6, mode: 'exclusive-tail'})
    .run(async id => {
      const attempt = (seen.get(id) ?? 0) + 1
      seen.set(id, attempt)
      calls.push({id, attempt})
      if (id === 'hlp.ui.chat' || id === 'hlp.ui.data-grid') {
        tailOtherActive.push({id, attempt, other: [...active]})
      }
      const token = `${id}:${attempt}`
      active.add(token)
      peak = Math.max(peak, active.size)
      try {
        if (id.startsWith('ordinary-')) {
          await releasePool.promise
          if (id === 'ordinary-00') {
            if (attempt === 1) return result(id, 1, 'ordinary first failure')
            ordinaryRetryStarted = true
            signalOrdinaryAtRest()
            await releaseOrdinaryRetry.promise
          }
        } else if (id === 'hlp.ui.chat') {
          if (attempt === 1) return result(id, 2, 'chat first failure')
          chatRetryStarted.resolve()
          await releaseChatRetry.promise
        }
        return result(id)
      } finally {
        active.delete(token)
        if (id.startsWith('ordinary-') && id !== 'ordinary-00') {
          otherOrdinaryFinished += 1
          signalOrdinaryAtRest()
        }
      }
    })
  // Prevent an invariant-breaking implementation from producing an unhandled rejection while
  // these controlled gates are released. Await the original promise below, so it still fails.
  running.catch(() => {})
  const initial = calls.map(entry => entry.id)
  releasePool.resolve()
  await ordinaryAtRest.promise
  const whileOrdinaryRetryHeld = [...calls]
  releaseOrdinaryRetry.resolve()
  await chatRetryStarted.promise
  const whileChatRetryHeld = [...calls]
  releaseChatRetry.resolve()
  const completed = await running

  assert.deepEqual(initial, ['ordinary-00', 'ordinary-01', 'ordinary-02', 'ordinary-03', 'ordinary-04', 'ordinary-05'])
  assert.equal(peak, 6)
  assert.equal(whileOrdinaryRetryHeld.length, 78)
  assert.ok(whileOrdinaryRetryHeld.every(entry => entry.id.startsWith('ordinary-')))
  assert.equal(whileChatRetryHeld.length, 80)
  assert.deepEqual(whileChatRetryHeld.slice(-2), [{id: 'hlp.ui.chat', attempt: 1}, {id: 'hlp.ui.chat', attempt: 2}])
  assert.equal(calls.length, 81)
  assert.deepEqual(calls.slice(-3), [
    {id: 'hlp.ui.chat', attempt: 1}, {id: 'hlp.ui.chat', attempt: 2}, {id: 'hlp.ui.data-grid', attempt: 1},
  ])
  assert.deepEqual(tailOtherActive, [
    {id: 'hlp.ui.chat', attempt: 1, other: []},
    {id: 'hlp.ui.chat', attempt: 2, other: []},
    {id: 'hlp.ui.data-grid', attempt: 1, other: []},
  ])
  assert.deepEqual(completed.results.map(entry => entry.id), [...input])
  assert.equal(completed.results.length, 79)
  assert.equal(completed.results.find(entry => entry.id === 'ordinary-00').firstAttempt.stdout, 'ordinary first failure')
  assert.equal(completed.results.find(entry => entry.id === 'hlp.ui.chat').firstAttempt.stdout, 'chat first failure')
  assert.equal(completed.evidence.attempts.length, 81)
  const ordinaryEnd = completed.evidence.phases[0].endOffsetMs
  const chatAttempts = completed.evidence.attempts.filter(entry => entry.moduleId === 'hlp.ui.chat')
  const gridAttempt = completed.evidence.attempts.find(entry => entry.moduleId === 'hlp.ui.data-grid')
  assert.ok(chatAttempts.every(entry => entry.startOffsetMs >= ordinaryEnd))
  assert.ok(gridAttempt.startOffsetMs >= chatAttempts[1].endOffsetMs)
  assert.ok(completed.evidence.observedOverlap.intervals.every(interval =>
    interval.activeAttempts.every(entry => entry.moduleId.startsWith('ordinary-'))))
})

test('one retry retains the complete failed result and diagnosis even when the second passes', async () => {
  const calls = []
  const attempts = new Map()
  const firstFailure = {
    id: 'recovers', exitCode: 7, durationMs: 123,
    stdout: '{"status":"FAIL","results":[{"caseId":"literal-first-failure","passed":false}]}',
    stderr: 'first attempt stderr',
  }
  const completed = await createSharedModuleScheduler({moduleIds: ['passes', 'recovers', 'fails', 'after'], concurrency: 1})
    .run(async id => {
      const attempt = (attempts.get(id) ?? 0) + 1
      attempts.set(id, attempt)
      calls.push(`${id}:${attempt}`)
      if (id === 'recovers' && attempt === 1) return firstFailure
      if (id === 'fails') return result(id, 9, `failed attempt ${attempt}`)
      return result(id)
    })
  assert.deepEqual(calls, ['passes:1', 'recovers:1', 'recovers:2', 'fails:1', 'fails:2', 'after:1'])
  assert.deepEqual(completed.results.map(entry => entry.exitCode), [0, 0, 9, 0])
  assert.deepEqual(completed.results.map(entry => entry.attempts), [1, 2, 2, 1])
  assert.deepEqual(completed.results[1].firstAttempt, firstFailure)
  assert.equal(completed.results[1].durationMs, 17)
  assert.equal(completed.results[2].firstAttempt.stdout, 'failed attempt 1')
  assert.equal(completed.results[2].stdout, 'failed attempt 2')
  const evidence = moduleEvidence(completed.results[1], true)
  assert.equal(evidence.attempts, 2)
  assert.equal(evidence.firstAttempt.exitCode, 7)
  assert.equal(evidence.firstAttempt.durationMs, 123)
  assert.equal(evidence.firstAttempt.reportStatus, 'FAIL')
  assert.equal(evidence.firstAttempt.failures[0].caseId, 'literal-first-failure')
  assert.equal(evidence.firstAttempt.stderrTail, 'first attempt stderr')
})

test('literal attempt intervals include a failed first attempt and use half-open overlap', async () => {
  const first = deferred()
  const second = deferred()
  const retry = deferred()
  const retryStarted = deferred()
  const clock = [0, 0, 1, 3, 4, 8, 10, 12]
  const dates = Array.from({length: 8}, (_, index) => `2026-10-18T10:00:0${index}.000Z`)
  const phases = []
  let callsA = 0
  const running = createSharedModuleScheduler({
    moduleIds: ['a', 'b'], concurrency: 2,
    now: () => { assert.ok(clock.length > 0); return clock.shift() },
    wallNow: () => { assert.ok(dates.length > 0); return dates.shift() },
    onPhase: event => phases.push(event),
  }).run(id => {
    if (id === 'b') return second.promise
    if (++callsA === 1) return first.promise
    retryStarted.resolve()
    return retry.promise
  })
  first.resolve(result('a', 4))
  await retryStarted.promise
  second.resolve(result('b'))
  // executeAttempt is already awaiting this exact promise: its end is queued before this
  // continuation, so the next literal clock value belongs to b, not the retry.
  await Promise.resolve()
  retry.resolve(result('a'))
  const completed = await running
  assert.deepEqual(completed.evidence.attempts, [
    {moduleId: 'a', attempt: 1, phase: 'original-pool', startedAt: '2026-10-18T10:00:01.000Z', startOffsetMs: 0,
      exitCode: 4, endOffsetMs: 3, endedAt: '2026-10-18T10:00:03.000Z', durationMs: 3},
    {moduleId: 'b', attempt: 1, phase: 'original-pool', startedAt: '2026-10-18T10:00:02.000Z', startOffsetMs: 1,
      exitCode: 0, endOffsetMs: 8, endedAt: '2026-10-18T10:00:05.000Z', durationMs: 7},
    {moduleId: 'a', attempt: 2, phase: 'original-pool', startedAt: '2026-10-18T10:00:04.000Z', startOffsetMs: 4,
      exitCode: 0, endOffsetMs: 10, endedAt: '2026-10-18T10:00:06.000Z', durationMs: 6},
  ])
  assert.deepEqual(completed.evidence.observedOverlap, {
    maxActiveAttempts: 2,
    overlappingDurationMs: 6,
    intervals: [
      {startOffsetMs: 1, endOffsetMs: 3, activeAttempts: [
        {moduleId: 'a', attempt: 1, phase: 'original-pool'}, {moduleId: 'b', attempt: 1, phase: 'original-pool'},
      ]},
      {startOffsetMs: 4, endOffsetMs: 8, activeAttempts: [
        {moduleId: 'b', attempt: 1, phase: 'original-pool'}, {moduleId: 'a', attempt: 2, phase: 'original-pool'},
      ]},
    ],
  })
  assert.deepEqual(phases, [
    {phase: 'original-pool', boundary: 'start', at: '2026-10-18T10:00:00.000Z', offsetMs: 0},
    {phase: 'original-pool', boundary: 'end', at: '2026-10-18T10:00:07.000Z', offsetMs: 12},
  ])
  assert.equal(clock.length, 0)
  assert.equal(dates.length, 0)
  assert.equal(completed.results[0].durationMs, 17)

  // A retry beginning exactly at the failed attempt's end is not overlap with that attempt.
  const touchingTimes = [0, 0, 5, 5, 9, 9]
  let attempt = 0
  const touching = await createSharedModuleScheduler({
    moduleIds: ['touching'], concurrency: 1, now: () => touchingTimes.shift(),
  }).run(async id => result(id, ++attempt === 1 ? 1 : 0))
  assert.equal(touching.evidence.observedOverlap.maxActiveAttempts, 1)
  assert.equal(touching.evidence.observedOverlap.overlappingDurationMs, 0)
  assert.deepEqual(touching.evidence.observedOverlap.intervals, [])
  assert.equal(touchingTimes.length, 0)
})

test('an executor throw or rejection remains a failure and never starts an exclusive tail', async () => {
  for (const reject of [false, true]) {
    const failure = new Error('literal launch refusal')
    const calls = []
    const phases = []
    const running = createSharedModuleScheduler({
      moduleIds: ['broken', 'hlp.ui.chat', 'hlp.ui.data-grid'], concurrency: 6, mode: 'exclusive-tail',
      onPhase: event => phases.push(event),
    }).run(id => {
      calls.push(id)
      if (reject) return Promise.reject(failure)
      throw failure
    })
    await assert.rejects(running, error => error === failure)
    assert.deepEqual(calls, ['broken'])
    assert.deepEqual(phases.map(event => `${event.phase}:${event.boundary}`), ['ordinary-pool:start'])
  }
})
