import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, fixture, form, performanceCases, section, text } from './fixtures'

// The budgets below are the committed quality profile's and are asserted unchanged.
// Each is measured as the fastest of ATTEMPTS runs: this suite is the only React
// module with wall-clock assertions and it runs inside a gate that executes six
// module runners in parallel, so a single sample measures the scheduler as much as
// the renderer. The minimum is the honest answer to "can the implementation meet
// this budget", and it cannot mask a regression — a genuinely slower renderer is
// slower in every attempt.
const ATTEMPTS = 5

function fastest(attempt: () => number): number {
  let best = Number.POSITIVE_INFINITY
  for (let index = 0; index < ATTEMPTS; index += 1) best = Math.min(best, attempt())
  return best
}

describe('SchemaForm deterministic Tier-C evidence', () => {
  it('evaluates the rule graph at most once per value change', () => {
    const budget = fixture(performanceCases, 'schema-form.perf.rule-reevaluation').expected.maxGraphEvaluationsPerChange as number
    const evaluateInstance = vi.fn(() => evaluation())
    const graph: RuleGraphLike = { evaluateInstance }
    render(<SchemaForm initialValues={{ name: '' }} onSubmit={() => undefined} ruleGraph={graph} view={form([section('s', [field('name', 'Name')])])} />)
    const before = evaluateInstance.mock.calls.length
    fireEvent.change(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'next' } })
    expect(evaluateInstance.mock.calls.length - before).toBeLessThanOrEqual(budget)
  })

  it('keeps one keystroke from scaling with form size', () => {
    const value = fixture(performanceCases, 'schema-form.perf.keystroke-latency').expected
    const fields = value.fields as number
    const budget = value.budgetMs as number
    const view = form([section('large', Array.from({ length: fields }, (_, index) => field(`field-${index}`, `Field ${index}`)))])
    // Rendered once: the budget is the cost of a keystroke on an already-mounted
    // form, so each attempt types a distinct value into the same input.
    const rendered = render(<SchemaForm initialValues={{ 'field-0': '' }} onSubmit={() => undefined} view={view} />)
    const input = screen.getByRole('textbox', { name: 'Field 0' })
    let attempt = 0
    const elapsed = fastest(() => {
      attempt += 1
      const start = performance.now()
      fireEvent.change(input, { target: { value: `n${attempt}` } })
      return performance.now() - start
    })
    rendered.unmount()
    // eslint-disable-next-line no-console
    console.log(`[measure] keystroke on ${fields} fields: ${elapsed.toFixed(2)}ms (best of ${ATTEMPTS})`)
    expect(elapsed).toBeLessThanOrEqual(budget)
  })

  it('renders the frozen 500-field form within budget', () => {
    const value = fixture(performanceCases, 'schema-form.perf.large-form').expected
    const fields = value.fields as number
    const budget = value.budgetMs as number
    const view = form([section('large', Array.from({ length: fields }, (_, index) => field(`field-${index}`, `Field ${index}`)))])
    // Correctness once, untimed: a testing-library query over 500 fields costs more
    // than the render it would be measuring.
    const proof = render(<SchemaForm onSubmit={() => undefined} view={view} />)
    expect(document.querySelectorAll('.hl-form-field')).toHaveLength(fields)
    proof.unmount()
    const elapsed = fastest(() => {
      const start = performance.now()
      const rendered = render(<SchemaForm onSubmit={() => undefined} view={view} />)
      const measured = performance.now() - start
      rendered.unmount()
      return measured
    })
    // eslint-disable-next-line no-console
    console.log(`[measure] render ${fields} fields: ${elapsed.toFixed(2)}ms (best of ${ATTEMPTS})`)
    expect(elapsed).toBeLessThanOrEqual(budget)
  })

  it('renders the frozen 200-row collection within budget', () => {
    const value = fixture(performanceCases, 'schema-form.perf.deep-collection').expected
    const rows = value.rows as number
    const budget = value.budgetMs as number
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
    const elapsed = fastest(() => {
      const start = performance.now()
      const rendered = render(<SchemaForm initialValues={initialValues} onSubmit={() => undefined} view={view} />)
      const measured = performance.now() - start
      rendered.unmount()
      return measured
    })
    // eslint-disable-next-line no-console
    console.log(`[measure] render ${rows}-row collection: ${elapsed.toFixed(2)}ms (best of ${ATTEMPTS})`)
    expect(elapsed).toBeLessThanOrEqual(budget)
  })
})
