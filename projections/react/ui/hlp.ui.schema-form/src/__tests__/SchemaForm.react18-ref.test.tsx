import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'

import { compile, FormRuleGraph, type RuleDefinition } from '@harborline-software/rule-engine'

vi.mock('@harborline-platform/hlp.ui.button', async () => {
  const React = await import('react')
  return {
    Button(props: React.ButtonHTMLAttributes<HTMLButtonElement> & {
      loading?: boolean
      variant?: string
      ref?: React.Ref<HTMLButtonElement>
    }) {
      const { children, loading: _loading, ref: _react19OnlyRef, variant: _variant, ...buttonProps } = props
      return React.createElement('button', buttonProps, children)
    },
  }
})

import { SchemaForm } from '../SchemaForm'
import type { RuleGraphLike } from '../SchemaForm.types'
import { field, form, section } from './fixtures'
import { admitEnvironment as admitTestEnvironment, builtInFunctions as testBuiltIns, fieldReadEffect as testFieldRead, lentGrammar as testGrammar } from '@harborline-software/rule-engine'
// The fixture host's borrower environment (T-590 rules-eng-26): Forms-shaped, every register key, render phase.
const testAdmission = admitTestEnvironment({ borrower: 'react-rule-graph-fixture', grammar: testGrammar, variables: { field: 'form field', row: 'form row' }, operations: testBuiltIns.map((f) => f.key), effects: [testFieldRead], missingValues: 'missing-field-reads-null', timeSource: 'evaluated-at', timeZone: 'utc', phases: { AuthoringValidation: false, PublishValidation: false, Render: true, Submission: true, Run: false, SignOff: false }, replay: 'deterministic' }).forPhase('Render')

it('recovers focus through the owned submit DOM element when a React 18 plain-function Button drops refs', async () => {
  const graph: RuleGraphLike = new FormRuleGraph(compile([{
    id: 'hide-trigger',
    tier: 'JsonLogic',
    scope: 'Field',
    scopeTarget: 'trigger',
    expression: { '!=': [{ var: 'trigger' }, 'hide'] },
    action: 'Visibility',
  }] satisfies RuleDefinition[]), () => new Date('2026-06-30T00:00:00.000Z'), testAdmission)
  render(<SchemaForm
    initialValues={{ trigger: '', retained: '' }}
    onSubmit={vi.fn()}
    ruleGraph={graph}
    view={form([section('s', [field('trigger', 'Trigger'), field('retained', 'Retained')])])}
  />)

  const input = screen.getByRole('textbox', { name: 'Trigger' })
  input.focus()
  fireEvent.change(input, { target: { value: 'hide' } })

  await waitFor(() => expect(screen.getByRole('button', { name: 'Submit' })).toHaveFocus())
  expect(screen.queryByRole('textbox', { name: 'Trigger' })).toBeNull()
})
