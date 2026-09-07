import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AppShell } from '../AppShell'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import { navigationFixture, type TestWorkspace } from './navigation-fixture'
import { ceilingMs, measureMedian, reportRow } from '../../../hlp.ui.button/src/render-baseline'

// Ticket 254: this test used to run on vitest's flat 5,000ms default, which is not a budget for
// anything -- it is the same number whether the loop renders one item or a million.
//
// The vitest timeout stays flat and generous on purpose: it is a hang guard, not the budget.
const GROUPS = 16
const ITEMS_PER_GROUP = 16
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
//   mac p95 1264.3 ms (1264.3 1119 1115 1100 1087.7 1113 1092 1101 1126 1109) -> ceiling 2530 ms
//   macpro (M1 Pro) p95 578.9 ms
// DETECTION FLOOR, stated honestly. 2x on the slowest host detects only an Nx regression on a
// faster one, N = ceiling / that host p95: mac 2.0x, macpro 4.4x.
//
// Ticket 265 fix 3 (controller decision). The fix-2 ceiling above was 2x the QUIET p95 of the
// slowest host. On the Windows relocation box that still went red 1-2 runs in 10 whenever a
// platform gate held the machine -- and on that box a gate ALWAYS shares the machine with lanes,
// so a ceiling that only survives a quiet box is a flapping budget. The ceiling below is the
// gate-proof number: it clears every sample observed on Windows across three 10-run windows taken
// WHILE a platform gate ran, and was re-measured 10/10 green under gate load.
//   NEW CEILING 7800 ms
//   sample: Windows relocation host, under a concurrent platform gate, 30 runs,
//           p95 3387.6 ms (max 3595.3 ms) -- fix-2 report windows s265-win-final10/p10/p10b
//   detection floor per host (ceiling / that host's p95): Windows-under-gate 2.3x,
//           mac 6.2x, macpro 13.5x
// sensitivity is restored by a quiet serial perf slot in the gate: ticket 268 added the
// gate step `perf-budgets` (tooling/run-perf-budgets.mjs), which runs these rows alone in the
// gate and, when it also measures the box quiet, sets HARBORLINE_PERF_QUIET=1 so `ceilingMs`
// applies the second (tight) argument -- fix 2's 2x-quiet-p95 number -- instead of this one.
const ABSOLUTE_CEILING_MS = ceilingMs(7800, 2530)

describe('AppShell deterministic structural performance', () => {
  it('[PerfBudget] renders 256 items across groups with threads and keeps one navigation subtree over 96 updates', () => {
    const groups = Array.from({ length: GROUPS }, (_, g) => ({ id: `g${g}`, label: `Group ${g}`, items: Array.from({ length: ITEMS_PER_GROUP }, (_, i) => ({ id: `g${g}-i${i}`, label: `Item ${g}.${i}`, threads: i === 0 ? [{ id: `g${g}-t`, label: `Session ${g}` }] : undefined })) }))
    const ws: TestWorkspace[] = [{ id: 'w', label: 'W', groups }]
    const navigation = navigationFixture(ws)
    const roleVocabulary = RoleVocabulary.fromApi([])
    const renderLoop = () => {
      const view = render(<AppShell shellId="perf" {...navigation} roleVocabulary={roleVocabulary} heldRoles={{roles:[]}} body={<div>Body 0</div>} railCapable groupCap={16} />)
      for (let update = 1; update <= UPDATES; update += 1) view.rerender(<AppShell shellId="perf" {...navigation} roleVocabulary={roleVocabulary} heldRoles={{roles:[]}} body={<div>Body {update}</div>} railCapable groupCap={16} collapsed={false} />)
      return view
    }
    const { median: measured } = measureMedian(() => { renderLoop().unmount() })
    reportRow('react-app-shell', measured)
    // One unmeasured pass for the structural assertions the budget rides on.
    renderLoop()
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(screen.getAllByText(/^Item /)).toHaveLength(GROUPS * ITEMS_PER_GROUP)
    expect(screen.getByText('Body 96')).toBeInTheDocument()
    expect(screen.queryByText('Body 95')).toBeNull()
    expect(measured, `react-app-shell: median of ${1 + UPDATES} renders of ${GROUPS * ITEMS_PER_GROUP} items was ${measured.toFixed(1)} ms, ceiling ${ABSOLUTE_CEILING_MS} ms`)
      .toBeLessThanOrEqual(ABSOLUTE_CEILING_MS)
  }, 120_000)
})
