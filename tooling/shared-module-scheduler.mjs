// Shared module orchestration only: the caller owns the unchanged build and module commands.
// Keeping this separate lets selftests control completion without importing the CLI's builds.
const exclusiveModules = ['hlp.ui.chat', 'hlp.ui.data-grid']

export function selectSharedSchedule(value) {
  const mode = value ?? 'original'
  if (mode !== 'original' && mode !== 'exclusive-tail') {
    throw new Error('HARBORLINE_SHARED_SCHEDULE must be original or exclusive-tail')
  }
  return mode
}

function observedOverlap(attempts) {
  const events = attempts.flatMap((attempt, index) => attempt.endOffsetMs > attempt.startOffsetMs
    ? [{at: attempt.startOffsetMs, index, start: true}, {at: attempt.endOffsetMs, index, start: false}]
    : [])
    .sort((left, right) => left.at - right.at)
  const active = new Set()
  const intervals = []
  let previous = events[0]?.at ?? 0
  let maxActiveAttempts = 0
  let overlappingDurationMs = 0
  for (let index = 0; index < events.length;) {
    const at = events[index].at
    // These are half-open intervals. All ends/starts at one instant are applied together,
    // so a completed attempt and its successor do not create an instantaneous overlap.
    if (at > previous) {
      maxActiveAttempts = Math.max(maxActiveAttempts, active.size)
      if (active.size > 1) {
        overlappingDurationMs += at - previous
        intervals.push({
          startOffsetMs: previous,
          endOffsetMs: at,
          activeAttempts: [...active].map(entry => ({
            moduleId: attempts[entry].moduleId,
            attempt: attempts[entry].attempt,
            phase: attempts[entry].phase,
          })),
        })
      }
    }
    while (index < events.length && events[index].at === at) {
      const event = events[index++]
      if (event.start) active.add(event.index)
      else active.delete(event.index)
    }
    previous = at
  }
  return {maxActiveAttempts, overlappingDurationMs, intervals}
}

export function createSharedModuleScheduler({
  moduleIds,
  concurrency,
  mode,
  now = () => performance.now(),
  wallNow = () => new Date().toISOString(),
  onPhase = () => {},
}) {
  const selectedMode = selectSharedSchedule(mode)
  if (!Array.isArray(moduleIds) || moduleIds.some(id => typeof id !== 'string' || id.trim() === '')
    || new Set(moduleIds).size !== moduleIds.length) {
    throw new Error('Shared module IDs must be distinct nonblank strings')
  }
  if (!Number.isInteger(concurrency) || concurrency < 1) {
    throw new Error('Shared module concurrency must be a positive integer')
  }
  const ids = [...moduleIds]
  if (selectedMode === 'exclusive-tail') {
    if (concurrency !== 6) throw new Error('The exclusive-tail experiment requires six ordinary workers')
    if (exclusiveModules.some(id => !ids.includes(id))) {
      throw new Error('The exclusive-tail experiment requires both hlp.ui.chat and hlp.ui.data-grid')
    }
  }
  const phases = selectedMode === 'original'
    ? [{name: 'original-pool', ids, concurrency}]
    : [
      {name: 'ordinary-pool', ids: ids.filter(id => !exclusiveModules.includes(id)), concurrency},
      ...exclusiveModules.map(id => ({name: `exclusive:${id}`, ids: [id], concurrency: 1})),
    ]

  return {
    mode: selectedMode,
    async run(executeModule) {
      const results = new Array(ids.length)
      const resultIndexes = new Map(ids.map((id, index) => [id, index]))
      const attempts = []
      const phaseEvidence = []

      async function executeAttempt(moduleId, attempt, phase) {
        const interval = {moduleId, attempt, phase, startedAt: wallNow(), startOffsetMs: now()}
        attempts.push(interval)
        try {
          const result = await executeModule(moduleId)
          interval.exitCode = result.exitCode
          return result
        } finally {
          // Rejected executors remain rejected; recording an end is not a success fallback.
          interval.endOffsetMs = now()
          interval.endedAt = wallNow()
          interval.durationMs = interval.endOffsetMs - interval.startOffsetMs
        }
      }

      for (const phase of phases) {
        const evidence = {
          name: phase.name,
          moduleIds: [...phase.ids],
          configuredConcurrency: phase.concurrency,
          startedAt: wallNow(),
          startOffsetMs: now(),
        }
        phaseEvidence.push(evidence)
        onPhase({phase: phase.name, boundary: 'start', at: evidence.startedAt, offsetMs: evidence.startOffsetMs})
        let nextIndex = 0
        const workers = Array.from({length: Math.min(phase.concurrency, phase.ids.length)}, async () => {
          while (nextIndex < phase.ids.length) {
            const moduleId = phase.ids[nextIndex++]
            const first = await executeAttempt(moduleId, 1, phase.name)
            if (first.exitCode === 0) {
              results[resultIndexes.get(moduleId)] = {...first, attempts: 1}
              continue
            }
            // Exactly the existing one retry, with the full failed first result retained.
            const second = await executeAttempt(moduleId, 2, phase.name)
            results[resultIndexes.get(moduleId)] = {...second, attempts: 2, firstAttempt: first}
          }
        })
        // Each worker includes its retry. The next phase cannot start while one is live.
        await Promise.all(workers)
        evidence.endOffsetMs = now()
        evidence.endedAt = wallNow()
        evidence.durationMs = evidence.endOffsetMs - evidence.startOffsetMs
        onPhase({phase: phase.name, boundary: 'end', at: evidence.endedAt, offsetMs: evidence.endOffsetMs})
      }

      return {
        results,
        evidence: {
          mode: selectedMode,
          status: 'completed',
          configuredConcurrency: concurrency,
          phases: phaseEvidence,
          attempts,
          observedOverlap: observedOverlap(attempts),
        },
      }
    },
  }
}
