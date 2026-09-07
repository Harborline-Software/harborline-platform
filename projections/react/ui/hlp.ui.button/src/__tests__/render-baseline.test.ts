// Ticket 268. `ceilingMs` is the whole tight/loose mechanism: the gate's `perf-budgets` step sets
// HARBORLINE_PERF_QUIET=1 only when it has the gate to itself AND measured the box quiet, and the
// budgeted rows read the flag through this one function. Its branch is asserted here rather than
// only through a perf row, because a perf row that silently applies the loose ceiling in a quiet
// slot is green either way -- exactly the failure this ticket exists to remove.
import { afterEach, describe, expect, it } from 'vitest'
import { ceilingMs } from '../render-baseline'

const original = process.env.HARBORLINE_PERF_QUIET

afterEach(() => {
  if (original === undefined) delete process.env.HARBORLINE_PERF_QUIET
  else process.env.HARBORLINE_PERF_QUIET = original
})

describe('ceilingMs', () => {
  it('applies the tight ceiling only for the exact quiet signal the gate step sets', () => {
    process.env.HARBORLINE_PERF_QUIET = '1'
    expect(ceilingMs(1700, 700)).toBe(700)
  })

  it.each(['0', '', 'true', 'yes', ' 1'])('applies the loose ceiling for %o', value => {
    process.env.HARBORLINE_PERF_QUIET = value
    expect(ceilingMs(1700, 700)).toBe(1700)
  })

  it('applies the loose ceiling when the signal is absent -- the safe direction', () => {
    delete process.env.HARBORLINE_PERF_QUIET
    expect(ceilingMs(1700, 700)).toBe(1700)
  })
})
