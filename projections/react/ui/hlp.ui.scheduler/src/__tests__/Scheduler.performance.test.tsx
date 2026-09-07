import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Scheduler } from '../Scheduler'
import { SCHEDULER_RECURRENCE_CAP, expandRecurrence } from '../SchedulerRecurrence'
import type { SchedulerEvent, SchedulerViewType } from '../Scheduler.types'
import { performanceCases } from './fixtures'
import { ceilingMs, measureMedian, reportRow } from '../../../hlp.ui.button/src/render-baseline'

const now = new Date('2026-08-11T09:30:00')

function event(index: number): SchedulerEvent {
  const start = new Date(2026, 7, 11 + index % 28, 8 + index % 10, 0)
  return { id: `e-${index}`, title: `Event ${index}`, start, end: new Date(start.getTime() + 30 * 60_000) }
}

// Ticket 254 sweep: vitest's flat 5,000ms default is not a budget for anything.
//
// The vitest timeout stays flat and generous on purpose: it is a hang guard, not the budget.
const REPLACEMENTS = 96
// Ticket 265 fix 2 (review round 2 REJECT closed). The ratio to a synthetic reference is GONE.
// Round 2 measured the reference itself moving 3.4x run to run on one host, which flapped rows red
// at rest with no regression present; a denominator that noisy cannot be a gate and no re-derived k
// fixes a wrong statistic. What is left is ONE absolute ceiling, derived by measurement, not typed.
//
// DERIVATION (tooling/perf-budget-stability.mjs, 10 consecutive runs per host, ticket 265 fix-2
// report). The ceiling is 2x the p95 of the SLOWEST host that could be sampled QUIET, which is the
// 2016 MacBook (`mac`). The Windows relocation host could not be sampled quiet -- a platform gate
// held the box for the whole measurement window and cost 3-4x -- so its samples are reported in the
// fix-2 report but are NOT the derivation; see the stated gap there.
//   mac p95 650.7 ms (650.7 600 609 594.7 623 616 624 610 609 613) -> ceiling 1300 ms
//   macpro (M1 Pro) p95 224.2 ms
// DETECTION FLOOR, stated honestly: mac 2.0x, macpro 5.8x.
//
// Ticket 265 fix 3 (controller decision). The fix-2 ceiling above was 2x the QUIET p95 of the
// slowest host. On the Windows relocation box that still went red 1-2 runs in 10 whenever a
// platform gate held the machine -- and on that box a gate ALWAYS shares the machine with lanes,
// so a ceiling that only survives a quiet box is a flapping budget. The ceiling below is the
// gate-proof number: it clears every sample observed on Windows across three 10-run windows taken
// WHILE a platform gate ran, and was re-measured 10/10 green under gate load.
//   NEW CEILING 3200 ms
//   sample: Windows relocation host, under a concurrent platform gate, 30 runs,
//           p95 1565.7 ms (max 1621.7 ms) -- fix-2 report windows s265-win-final10/p10/p10b
//   detection floor per host (ceiling / that host's p95): Windows-under-gate 2.0x,
//           mac 4.9x, macpro 14.3x
// sensitivity is restored by a quiet serial perf slot in the gate: ticket 268 added the
// gate step `perf-budgets` (tooling/run-perf-budgets.mjs), which runs these rows alone in the
// gate and, when it also measures the box quiet, sets HARBORLINE_PERF_QUIET=1 so `ceilingMs`
// applies the second (tight) argument -- fix 2's 2x-quiet-p95 number -- instead of this one.
const ABSOLUTE_CEILING_MS = ceilingMs(3200, 1300)

describe('Scheduler Tier-C performance', () => {
  it('consumes every performance fixture', () => {
    expect(performanceCases.map(value => value.id)).toEqual([
      'scheduler.quality.large-data',
      'scheduler.quality.recurrence-cap',
      'scheduler.quality.replacement',
    ])
  })

  it('renders 256 unique visible events with constant toolbar and bounded cells', () => {
    const events = Array.from({ length: 256 }, (_, index) => event(index))
    const rendered = render(<Scheduler data={events} defaultDate={now} defaultView="agenda" now={now} readOnly />)
    expect(document.querySelectorAll('[data-event-key]')).toHaveLength(256)
    expect(new Set([...document.querySelectorAll('[data-event-key]')].map(node => node.getAttribute('data-event-key'))).size).toBe(256)
    expect(document.querySelectorAll('.hl-scheduler__toolbar')).toHaveLength(1)
    rendered.unmount()
  })

  it('hard-caps unbounded recurrence at 1000 occurrences', () => {
    const master: SchedulerEvent = { id: 'series', title: 'Daily', start: new Date('2020-01-01T09:00:00Z'), end: new Date('2020-01-01T10:00:00Z'), recurrenceRule: 'FREQ=DAILY' }
    const values = expandRecurrence(master, new Date('2020-01-01T00:00:00Z'), new Date('2040-01-01T00:00:00Z'))
    expect(values).toHaveLength(SCHEDULER_RECURRENCE_CAP)
  })

  it('[PerfBudget] replaces data, view, and date 96 times without stale nodes or structural growth', () => {
    const views: readonly SchedulerViewType[] = ['day', 'week', 'month', 'agenda']
    const renderLoop = () => {
      const first = event(0)
      const rendered = render(<Scheduler data={[first]} defaultDate={now} defaultView="agenda" now={now} readOnly />)
      for (let index = 1; index <= REPLACEMENTS; index += 1) {
        const next = event(index)
        rendered.rerender(<Scheduler data={[next]} now={now} readOnly selectedDate={next.start} view={views[index % views.length]} />)
      }
      return rendered
    }
    const { median: measured } = measureMedian(() => { renderLoop().unmount() })
    reportRow('react-scheduler', measured)
    // One unmeasured pass for the structural assertions the budget rides on.
    renderLoop()
    expect(document.querySelectorAll('.hl-scheduler__toolbar')).toHaveLength(1)
    expect(document.querySelectorAll('[data-scheduler-overlay]')).toHaveLength(0)
    expect(document.querySelectorAll('[data-event-key]')).toHaveLength(1)
    expect(document.querySelector('[data-event-key]')).toHaveAttribute('data-event-key', expect.stringContaining('e-96'))
    expect(measured, `react-scheduler: median of ${1 + REPLACEMENTS} replacement renders was ${measured.toFixed(1)} ms, ceiling ${ABSOLUTE_CEILING_MS} ms`)
      .toBeLessThanOrEqual(ABSOLUTE_CEILING_MS)
  }, 120_000)
})
