import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { DataGrid, DATA_GRID_MAX_RENDERED_ROWS, DATA_GRID_ROW_HEIGHT } from '../DataGrid'
import type { DataGridChildren, DataGridColumnDef } from '../DataGrid.types'
import { prepareDataGrid } from '../data-grid-model'
import { fixture, performanceCases, sharedCases } from './fixtures'
import { ceilingMs, measureMedian, reportRow } from '../../../hlp.ui.button/src/render-baseline'

interface LargeRow { readonly id: string; readonly [key: `c${number}`]: string }

const getRowId = (row: LargeRow) => row.id
const makeColumns = (count: number): readonly DataGridColumnDef<LargeRow>[] => Array.from({ length: count }, (_, index) => ({
  id: `c${index}`,
  field: `c${index}`,
  header: `Column ${index}`,
  removalPriority: count - index,
}))
const makeRows = (count: number, revision = 0): readonly LargeRow[] => Array.from({ length: count }, (_, rowIndex) => {
  const row = { id: `r${revision}-${rowIndex}` } as LargeRow
  return new Proxy(row, { get: (target, property) => property in target ? target[property as keyof LargeRow] : `${String(property)}:${revision}:${rowIndex}` })
})

describe('DataGrid deterministic Tier-C evidence', () => {
  it('keeps the 10,000 by 20 dataset to a bounded row window with linear normalization', () => {
    fixture(sharedCases, 'data-grid.large-data')
    fixture(performanceCases, 'data-grid.quality.large-data')
    const rows = makeRows(10_000)
    const columns = makeColumns(20)
    const prepared = prepareDataGrid(rows, columns, getRowId)
    expect(prepared.normalizationOperations).toBe(10_020)
    const rendered = render(<DataGrid accessibleName="Large assets" columns={columns} getRowId={getRowId} rows={rows} />)
    const grid = document.querySelector('.hl-data-grid')
    expect(grid).toHaveAttribute('aria-rowcount', '10000')
    expect(document.querySelectorAll('[data-row-id]').length).toBeLessThanOrEqual(DATA_GRID_MAX_RENDERED_ROWS)
    expect(document.querySelectorAll('[data-row-id]')).toHaveLength(DATA_GRID_MAX_RENDERED_ROWS)

    const viewport = document.querySelector('.hl-data-grid__viewport') as HTMLDivElement
    fireEvent.scroll(viewport, { target: { scrollTop: DATA_GRID_ROW_HEIGHT * 9_999 } })
    expect(document.querySelector('[data-row-id="r0-9999"]')).toBeInTheDocument()
    expect(document.querySelectorAll('[data-row-id]').length).toBeLessThanOrEqual(DATA_GRID_MAX_RENDERED_ROWS)
    rendered.unmount()
  }, 30_000)

  it('keeps only replacement 96 without stale rows, groups, or cells', () => {
    fixture(sharedCases, 'data-grid.repeated-update')
    fixture(performanceCases, 'data-grid.quality.repeated-update')
    const columns = makeColumns(20)
    const rendered = render(<DataGrid accessibleName="Updates" columns={columns} getRowId={getRowId} rows={makeRows(256)} />)
    for (let revision = 1; revision <= 96; revision += 1) {
      rendered.rerender(<DataGrid accessibleName="Updates" columns={columns} getRowId={getRowId} rows={makeRows(256, revision)} />)
    }
    expect(document.querySelector('[data-row-id="r96-0"]')).toBeInTheDocument()
    expect(document.querySelector('[data-row-id="r95-0"]')).toBeNull()
    expect(document.querySelector('.hl-data-grid')).toHaveAttribute('aria-rowcount', '256')
    expect(document.querySelectorAll('[data-row-id]')).toHaveLength(DATA_GRID_MAX_RENDERED_ROWS)
    expect(document.querySelectorAll('[data-group-id]')).toHaveLength(0)
  }, 30_000)
})

// Ticket 230 slice 2 fix 4. The Blazor projection rebuilt its whole 10,000-entry projection once
// per property access and regressed the gallery story; this is the same budget in the React lane,
// so a future change that moves projection work into the render path is caught here rather than in
// a 45 s gallery timeout.
//
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
//   no lazy    mac p95 346.8 ms (346.8 259 281 296 298 253.9 292 280 298 294) -> ceiling 700 ms
//   fifty lazy mac p95 366.2 ms (366.2 274 312 326 308 290 300 272.5 315 285) -> ceiling 740 ms
//   macpro (M1 Pro) p95 124.1 ms / 134.5 ms
// DETECTION FLOOR at the fix-2 ceilings: mac 2.0x both rows; macpro 5.6x / 5.5x.
//
// Ticket 265 fix 3 (controller decision). The fix-2 ceiling above was 2x the QUIET p95 of the
// slowest host. On the Windows relocation box that still went red 1-2 runs in 10 whenever a
// platform gate held the machine -- and on that box a gate ALWAYS shares the machine with lanes,
// so a ceiling that only survives a quiet box is a flapping budget. The ceiling below is the
// gate-proof number: it clears every sample observed on Windows across three 10-run windows taken
// WHILE a platform gate ran, and was re-measured 10/10 green under gate load.
//   NEW CEILING 1500 (no lazy) / 1550 (fifty lazy) ms
//   sample: Windows relocation host, under a concurrent platform gate, 30 runs,
//           p95 808.8 / 814.7 ms (max 819.0 / 830.1 ms) -- fix-2 report windows s265-win-final10/p10/p10b
//   detection floor per host (ceiling / that host's p95): Windows-under-gate 1.9 / 1.9x,
//           mac 4.3 / 4.2x, macpro 12.1 / 11.5x
// sensitivity is restored by a quiet serial perf slot in the gate: ticket 268 added the
// gate step `perf-budgets` (tooling/run-perf-budgets.mjs), which runs these rows alone in the
// gate and, when it also measures the box quiet, sets HARBORLINE_PERF_QUIET=1 so `ceilingMs`
// applies the second (tight) argument -- fix 2's 2x-quiet-p95 number -- instead of this one.
const NO_LAZY_CEILING_MS = ceilingMs(1500, 700)
const LAZY_CEILING_MS = ceilingMs(1550, 740)
const BUDGETED_RENDERS = 6
// Six fresh MOUNTS per measurement, not one: a rerender of an identical element is memoised, so a
// single mount plus five rerenders is ~40 ms and its ratio is dominated by measurement noise (five
// quiet Windows runs spanned 0.91-2.06, which overlaps a planted 2x regression). Six mounts build
// the 10,000-entry projection six times, which is the work this row guards, and the ratio narrows.
const BUDGETED_MOUNTS = 6

describe('DataGrid render budget at 10,000 rows', () => {
  const columns = makeColumns(20)
  const rows = makeRows(10_000)
  const lazyChildren: Record<string, DataGridChildren<LargeRow>> = Object.fromEntries(
    Array.from({ length: 50 }, (_, index) => [`r0-${index * 100}`, {
      state: 'loaded' as const,
      count: 2,
      children: [{ id: `lazy-${index}-a` } as LargeRow, { id: `lazy-${index}-b` } as LargeRow],
    }]),
  )

  it.each([
    ['react-data-grid-no-lazy', {} as Record<string, DataGridChildren<LargeRow>>, NO_LAZY_CEILING_MS],
    ['react-data-grid-fifty-lazy', lazyChildren, LAZY_CEILING_MS],
  ] as const)('[PerfBudget] stays within the measured absolute render ceiling: %s', (label, lazy, ceilingMs) => {
    const grid = (
      <DataGrid accessibleName="Large assets" columns={columns} getRowId={getRowId} rows={rows}
        lazyChildren={lazy} onChildrenRequest={() => {}} />
    )
    const { median: measured } = measureMedian(() => {
      for (let mount = 0; mount < BUDGETED_MOUNTS; mount += 1) {
        const rendered = render(grid)
        for (let index = 1; index < BUDGETED_RENDERS; index += 1) rendered.rerender(grid)
        rendered.unmount()
      }
    })
    reportRow(label, measured)
    expect(measured, `${label}: median of ${BUDGETED_MOUNTS} x ${BUDGETED_RENDERS} renders of 10,000 rows was ${measured.toFixed(1)} ms, ceiling ${ceilingMs} ms`)
      .toBeLessThanOrEqual(ceilingMs)
  }, 120_000)
})
