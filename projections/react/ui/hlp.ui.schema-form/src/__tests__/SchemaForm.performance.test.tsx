import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import { field, fixture, form, performanceCases, section, text } from './fixtures'
import { ceilingMs, measureMedian, reportRow } from '../../../hlp.ui.button/src/render-baseline'

// Ticket 404 follows ticket 265's two-ceiling method. Each value below is the median of
// nine timed rounds after two warm-ups; the serial runner collected ten such medians on
// this quiet Windows host (busy fraction 0.0566). Its nearest-rank p95s were 1.0, 68.6,
// and 46.2 ms, so the quiet ceilings are 2x p95 rounded up: 2, 140, and 100 ms.
//
// The existing quality-fixture budgets stay as the loose values until a slow macOS quiet
// host has supplied the other half of ticket 265's slowest-host derivation. Three ten-run
// two-core-burner windows on this host stayed below them (p95 1.1, 74.9, 53.4 ms), so they
// remain gate-proof rather than made up from one fast box. The serial perf step selects the
// tight number only after it samples a quiet machine.
// DERIVATION (ticket 404 s4, re-derived 2026-09-12 after the first attempt was
// measured on the WRONG HOST). Ticket 265's rule is: tight = 2x the p95 of the
// SLOWEST host that can be sampled quiet; loose = a gate-proof number clearing
// every sample seen while a gate holds that box. The first derivation took both
// from a quiet 16-core Windows box -- the FASTEST host -- and CI red at 537.8 ms
// against a 140 ms ceiling.
//
// All three regimes below are mac16, one machine, ten runs each:
//   quiet (busyFraction 0.008-0.024)  p95  1.5 / 115.8 / 77.9 ms
//   under a concurrent gate (<=0.29)  p95  2.3 / 149.8 / 92.9 ms
//   worst seen in CI (busyFraction 0.133)  1.4 / 537.8 / 337.2 ms
//
// So tight is 2x the quiet p95, and loose clears the worst CI observation with
// room -- not the 149.8 ms of a well-behaved local window, because the CI box at
// the same reported busyFraction was 3.6x slower than that. What separates them
// is work the busyFraction sample does not see.
//
// The 0.133 case is why loose matters here: run-perf-budgets certifies any box
// at busyFraction <= 0.25 as quiet and applies the TIGHT ceilings to it. Nine of
// ten loaded runs measured "quiet" at up to 0.2258. That budget is too
// permissive, and it is a defect in the gate, not in these numbers -- see the
// 404 Log.
const KEYSTROKE_CEILING_MS = ceilingMs(16, 3)
const LARGE_FORM_CEILING_MS = ceilingMs(1100, 232)
const DEEP_COLLECTION_CEILING_MS = ceilingMs(700, 156)

describe('SchemaForm deterministic Tier-C evidence', () => {
  it('[PerfBudget] keeps one keystroke from scaling with form size', () => {
    const value = fixture(performanceCases, 'schema-form.perf.keystroke-latency').expected
    const fields = value.fields as number
    const view = form([section('large', Array.from({ length: fields }, (_, index) => field(`field-${index}`, `Field ${index}`)))])
    // Rendered once: the budget is the cost of a keystroke on an already-mounted
    // form, so each attempt types a distinct value into the same input.
    const rendered = render(<SchemaForm initialValues={{ 'field-0': '' }} onSubmit={() => undefined} view={view} />)
    const input = screen.getByRole('textbox', { name: 'Field 0' })
    let round = 0
    const {median: elapsed} = measureMedian(() => {
      round += 1
      fireEvent.change(input, { target: { value: `n${round}` } })
    })
    rendered.unmount()
    reportRow('react-schema-form-keystroke-latency', elapsed)
    expect(elapsed, `react-schema-form-keystroke-latency: median keystroke on ${fields} fields was ${elapsed.toFixed(1)} ms, ceiling ${KEYSTROKE_CEILING_MS} ms`)
      .toBeLessThanOrEqual(KEYSTROKE_CEILING_MS)
  })

  it('[PerfBudget] renders the frozen 500-field form within budget', () => {
    const value = fixture(performanceCases, 'schema-form.perf.large-form').expected
    const fields = value.fields as number
    const view = form([section('large', Array.from({ length: fields }, (_, index) => field(`field-${index}`, `Field ${index}`)))])
    // Correctness once, untimed: a testing-library query over 500 fields costs more
    // than the render it would be measuring.
    const proof = render(<SchemaForm onSubmit={() => undefined} view={view} />)
    expect(document.querySelectorAll('.hl-form-field')).toHaveLength(fields)
    proof.unmount()
    const {median: elapsed} = measureMedian(() => {
      const rendered = render(<SchemaForm onSubmit={() => undefined} view={view} />)
      rendered.unmount()
    })
    reportRow('react-schema-form-large-form', elapsed)
    expect(elapsed, `react-schema-form-large-form: median render of ${fields} fields was ${elapsed.toFixed(1)} ms, ceiling ${LARGE_FORM_CEILING_MS} ms`)
      .toBeLessThanOrEqual(LARGE_FORM_CEILING_MS)
  })

  it('[PerfBudget] renders the frozen 200-row collection within budget', () => {
    const value = fixture(performanceCases, 'schema-form.perf.deep-collection').expected
    const rows = value.rows as number
    const view = form([section('large', [], { items: [{
      kind: 'collection',
      key: 'rows',
      title: text('Rows'),
      cardinality: { min: 0, max: rows },
      items: [{ kind: 'field', key: 'value', field: field('value', 'Value') }],
    }] })])
    const initialValues = { rows: Array.from({ length: rows }, (_, index) => ({ value: `Row ${index}` })) }
    const proof = render(<SchemaForm initialValues={initialValues} onSubmit={() => undefined} view={view} />)
    expect(screen.getAllByTestId('collection-rows-instance')).toHaveLength(rows)
    proof.unmount()
    const {median: elapsed} = measureMedian(() => {
      const rendered = render(<SchemaForm initialValues={initialValues} onSubmit={() => undefined} view={view} />)
      rendered.unmount()
    })
    reportRow('react-schema-form-deep-collection', elapsed)
    expect(elapsed, `react-schema-form-deep-collection: median render of ${rows}-row collection was ${elapsed.toFixed(1)} ms, ceiling ${DEEP_COLLECTION_CEILING_MS} ms`)
      .toBeLessThanOrEqual(DEEP_COLLECTION_CEILING_MS)
  })
})
