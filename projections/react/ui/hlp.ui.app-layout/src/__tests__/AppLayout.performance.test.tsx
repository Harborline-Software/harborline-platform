import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppLayout } from '../AppLayout'
import { ceilingMs, measureMedian, reportRow } from '../../../hlp.ui.button/src/render-baseline'

// Ticket 254 sweep: same rule as AppShell -- vitest's flat 5,000ms default is not a budget.
//
// The vitest timeout stays flat and generous on purpose: it is a hang guard, not the budget.
const ITEMS = 256
const UPDATES = 96
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
//   mac p95 348.5 ms (335 331 336 330 327.8 333 340 348.5 329 333) -> ceiling 700 ms
//   macpro (M1 Pro) p95 165.0 ms
// DETECTION FLOOR, stated honestly: mac 2.0x, macpro 4.2x.
//
// Ticket 265 fix 3 (controller decision). The fix-2 ceiling above was 2x the QUIET p95 of the
// slowest host. On the Windows relocation box that still went red 1-2 runs in 10 whenever a
// platform gate held the machine -- and on that box a gate ALWAYS shares the machine with lanes,
// so a ceiling that only survives a quiet box is a flapping budget. The ceiling below is the
// gate-proof number: it clears every sample observed on Windows across three 10-run windows taken
// WHILE a platform gate ran, and was re-measured 10/10 green under gate load.
//   NEW CEILING 1700 ms
//   sample: Windows relocation host, under a concurrent platform gate, 30 runs,
//           p95 649.9 ms (max 678.6 ms) -- fix-2 report windows s265-win-final10/p10/p10b
//   detection floor per host (ceiling / that host's p95): Windows-under-gate 2.6x,
//           mac 4.9x, macpro 10.3x
// sensitivity is restored by a quiet serial perf slot in the gate: ticket 268 added the
// gate step `perf-budgets` (tooling/run-perf-budgets.mjs), which runs these rows alone in the
// gate and, when it also measures the box quiet, sets HARBORLINE_PERF_QUIET=1 so `ceilingMs`
// applies the second (tight) argument -- fix 2's 2x-quiet-p95 number -- instead of this one.
const ABSOLUTE_CEILING_MS = ceilingMs(1700, 700)

describe('AppLayout deterministic structural performance', () => {
  it('[PerfBudget] keeps one navigation subtree for 256 items and only the latest of 96 updates', () => {
    const items = Array.from({ length: ITEMS }, (_, index) => <a key={index} href={`#item-${index}`}>Item {index}</a>)
    const renderLoop = () => {
      const view = render(<AppLayout body={<div>Body 0</div>} header={<div>Header 0</div>} sideNav={items} railCapable/>)
      for (let update = 1; update <= UPDATES; update += 1) view.rerender(<AppLayout body={<div>Body {update}</div>} header={<div>Header {update}</div>} sideNav={items} railCapable={update % 2 === 0}/>)
      return view
    }
    const { median: measured } = measureMedian(() => { renderLoop().unmount() })
    reportRow('react-app-layout', measured)
    // One unmeasured pass for the structural assertions the budget rides on.
    renderLoop()
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(screen.getAllByRole('link')).toHaveLength(ITEMS)
    expect(screen.getByText('Body 96')).toBeInTheDocument()
    expect(screen.queryByText('Body 95')).toBeNull()
    expect(screen.getByText('Header 96')).toBeInTheDocument()
    expect(measured, `react-app-layout: median of ${1 + UPDATES} renders of ${ITEMS} items was ${measured.toFixed(1)} ms, ceiling ${ABSOLUTE_CEILING_MS} ms`)
      .toBeLessThanOrEqual(ABSOLUTE_CEILING_MS)
  }, 120_000)

  it('emits at most one close request for a drawer-to-rail transition', () => {
    const changed = vi.fn()
    const view = render(<AppLayout body="Body" sideNav="Navigation" railCapable={false} mobileNavOpen onMobileNavOpenChange={changed}/>)
    for (let update = 0; update < 96; update += 1) view.rerender(<AppLayout body={`Body ${update}`} sideNav="Navigation" railCapable={update >= 48} mobileNavOpen onMobileNavOpenChange={changed}/>)
    expect(changed.mock.calls.filter(([open]) => open === false)).toHaveLength(1)
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
