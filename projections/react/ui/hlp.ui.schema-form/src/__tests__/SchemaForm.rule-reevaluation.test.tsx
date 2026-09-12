import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, fixture, form, performanceCases, section } from './fixtures'

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
})
