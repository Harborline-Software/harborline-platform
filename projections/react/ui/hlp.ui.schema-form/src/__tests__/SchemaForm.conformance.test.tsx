import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, form, section, text } from './fixtures'

const noSubmit = () => undefined

describe('SchemaForm revision-1 interface cases', () => {
  it('schema-form.section-order', () => {
    render(<SchemaForm onSubmit={noSubmit} view={form([
      section('Second', [field('b', 'B'), field('a', 'A')]),
      section('First', [field('d', 'D'), field('c', 'C')]),
    ])} />)
    expect([...document.querySelectorAll('[data-schemaform-section]')].map(node => node.getAttribute('data-schemaform-section'))).toEqual(['Second', 'First'])
    expect(screen.getAllByRole('textbox').map(node => node.getAttribute('name'))).toEqual(['b', 'a', 'd', 'c'])
  })

  it('schema-form.hint-registry', () => {
    const custom = vi.fn(args => <input aria-label="Custom" onChange={event => args.onChange(event.currentTarget.value)} value={args.strValue} />)
    render(<SchemaForm controls={{ bespoke: custom }} initialValues={{ x: 'value' }} onSubmit={noSubmit} view={form([section('s', [field('x', 'X', { controlHint: 'bespoke' })])])} />)
    expect(screen.getByRole('textbox', { name: 'Custom' })).toHaveValue('value')
    expect(custom).toHaveBeenCalledOnce()
  })

  it('schema-form.hint-fallback', () => {
    render(<SchemaForm initialValues={{ x: 'fallback' }} onSubmit={noSubmit} view={form([section('s', [field('x', 'Fallback', { controlHint: 'future-string' })])])} />)
    expect(screen.getByRole('textbox', { name: 'Fallback' })).toHaveValue('fallback')
  })

  it('schema-form.unavailable-value', () => {
    render(<SchemaForm initialValues={{ x: { nested: true }, y: { nested: true } }} onSubmit={noSubmit} view={form([section('s', [
      field('x', 'Unknown', { controlHint: 'future-object', valueKind: 'object' }),
      field('y', 'Text object', { controlHint: 'text' }),
    ])])} />)
    expect(document.querySelectorAll('[data-error-code="schema-form.unavailable-value"]')).toHaveLength(2)
    expect(screen.queryByDisplayValue('[object Object]')).toBeNull()
  })

  it('schema-form.rule-visibility', () => {
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ visibility: [
      ['field:hidden', { visible: false, required: false, readOnly: false }],
      ['section:gone', { visible: false, required: false, readOnly: false }],
    ] })) }
    render(<SchemaForm onSubmit={noSubmit} ruleGraph={graph} view={form([
      section('shown', [field('visible', 'Visible'), field('hidden', 'Hidden')]),
      section('gone', [field('gone-field', 'Gone')]),
    ])} />)
    expect(screen.getByRole('textbox', { name: 'Visible' })).toBeInTheDocument()
    expect(screen.queryByText('Hidden')).toBeNull()
    expect(document.querySelector('[data-schemaform-section="gone"]')).toBeNull()
  })

  it('schema-form.rule-required', () => {
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ visibility: [
      ['field:name', { visible: true, required: true, readOnly: false }],
    ] })) }
    render(<SchemaForm onSubmit={noSubmit} ruleGraph={graph} view={form([section('s', [field('name', 'Name')])])} />)
    expect(screen.getByRole('textbox', { name: 'Name' })).toBeRequired()
    expect(screen.getByText('*')).toHaveAttribute('aria-hidden', 'true')
  })

  it('schema-form.rule-readonly', () => {
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ visibility: [
      ['field:name', { visible: true, required: false, readOnly: true }],
    ] })) }
    render(<SchemaForm initialValues={{ name: 'fixed' }} onSubmit={noSubmit} ruleGraph={graph} view={form([section('s', [field('name', 'Name')])])} />)
    expect(screen.getByRole('textbox', { name: 'Name' })).toBeDisabled()
    expect(screen.getByText('Name').closest('.hl-form-field')).toHaveAttribute('data-read-only', 'true')
  })

  it('schema-form.rule-computed', () => {
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ values: [
      ['field:total', { state: 'Resolved', value: 42 }],
    ] })) }
    render(<SchemaForm initialValues={{ total: 0 }} onSubmit={noSubmit} ruleGraph={graph} view={form([section('s', [field('total', 'Total')])])} />)
    expect(screen.getByRole('textbox', { name: 'Total' })).toHaveValue('42')
  })

  it('schema-form.rule-presentation', () => {
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ presentations: [
      ['field:name', { severity: 'warn', badge: text('Review'), styleToken: 'attention' }],
    ] })) }
    render(<SchemaForm onSubmit={noSubmit} ruleGraph={graph} view={form([section('s', [field('name', 'Name')])])} />)
    expect(screen.getByText('Review')).toHaveAttribute('data-severity', 'warn')
    expect(screen.getByText('Name').closest('.hl-form-field')).toHaveAttribute('data-presentation-style-token', 'attention')
  })

  it('schema-form.submit-blocked', async () => {
    const onSubmit = vi.fn()
    const pending: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({
      values: [['field:name', { state: 'Pending' }]], hasPending: true,
    })) }
    const first = render(<SchemaForm onSubmit={onSubmit} ruleGraph={pending} view={form([section('s', [field('name', 'Name')])])} />)
    expect(screen.getByRole('button', { name: 'Submit' })).toBeDisabled()
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await waitFor(() => expect(document.querySelector('[data-error-code="schema-form.submit-blocked"]')).toHaveFocus())
    expect(onSubmit).not.toHaveBeenCalled()
    first.unmount()

    const errored: RuleGraphLike = { evaluateInstance: vi.fn(() => { throw new Error('rule failure') }) }
    render(<SchemaForm onSubmit={onSubmit} ruleGraph={errored} view={form([section('s', [field('name', 'Name')])])} />)
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await waitFor(() => expect(document.querySelector('[data-error-code="schema-form.submit-blocked"]')).toBeInTheDocument())
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('schema-form.validation-inline', async () => {
    render(<SchemaForm initialValues={{ name: '' }} onSubmit={() => ({
      isValid: false,
      errors: [{ jsonPointer: '/name', message: 'Caller-localized inline error', kind: 'Schema' }],
    })} view={form([section('s', [field('name', 'Localized label', { helpText: text('Localized hint') })])])} />)
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await waitFor(() => expect(document.getElementById('name-error')).toHaveTextContent('Caller-localized inline error'))
    expect(screen.getByRole('textbox', { name: 'Localized label' })).toHaveAttribute('aria-describedby', 'name-hint name-error')
  })

  it('schema-form.validation-summary', async () => {
    render(<SchemaForm initialValues={{ name: '' }} onSubmit={() => ({
      isValid: false,
      errors: [
        { jsonPointer: '/group/name', message: 'Nested error', kind: 'Schema' },
        { jsonPointer: '', message: 'Form error', kind: 'Schema' },
      ],
    })} view={form([section('s', [field('name', 'Name')])])} />)
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await screen.findByText('Nested error')
    expect(screen.getByRole('alert')).toHaveTextContent('Nested error')
    expect(screen.getByRole('alert')).toHaveTextContent('Form error')
    expect(document.getElementById('name-error')).toBeNull()
  })

  it('schema-form.summary-focus', async () => {
    render(<SchemaForm onSubmit={() => ({
      isValid: false,
      errors: [{ jsonPointer: '', message: 'Return to errors', kind: 'Schema' }],
    })} view={form([section('s', [field('name', 'Name')])])} />)
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await waitFor(() => expect(screen.getByRole('alert')).toHaveFocus())
  })

  it('schema-form.nested-group-set', () => {
    const groupSibling = { bytes: ['same'] }
    const rootSibling = { untouched: true }
    const onValuesChange = vi.fn()
    render(<SchemaForm
      initialValues={{ group: { child: 'before', sibling: groupSibling }, rootSibling }}
      onSubmit={noSubmit}
      onValuesChange={onValuesChange}
      view={form([section('s', [], { items: [{
        kind: 'group', key: 'group', title: text('Group'), items: [{ kind: 'field', key: 'child', field: field('child', 'Child') }],
      }] })])}
    />)
    fireEvent.change(screen.getByRole('textbox', { name: 'Child' }), { target: { value: 'after' } })
    const candidate = onValuesChange.mock.calls.at(-1)?.[0]
    expect(candidate).toEqual({ group: { child: 'after', sibling: groupSibling }, rootSibling })
    expect(candidate.group.sibling).toBe(groupSibling)
    expect(candidate.rootSibling).toBe(rootSibling)
  })

  it('schema-form.collection-cardinality', () => {
    const onValuesChange = vi.fn()
    render(<SchemaForm
      initialValues={{ lines: [{ value: 'one' }] }}
      onSubmit={noSubmit}
      onValuesChange={onValuesChange}
      view={form([section('s', [], { items: [{
        kind: 'collection', key: 'lines', title: text('Lines'), cardinality: { min: 1, max: 2 },
        items: [{ kind: 'field', key: 'value', field: field('value', 'Value') }],
      }] })])}
    />)
    expect(screen.getByRole('button', { name: 'Remove Item 1' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Add' }))
    expect(screen.getAllByTestId('collection-lines-instance')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: 'Remove Item 1' }))
    expect(screen.getAllByTestId('collection-lines-instance')).toHaveLength(1)
    expect(screen.getByRole('button', { name: 'Remove Item 1' })).toBeDisabled()
    expect(onValuesChange.mock.calls.map(call => call[0].lines.length)).toEqual([2, 1])
  })

  it('schema-form.candidate-complete', async () => {
    const onSubmit = vi.fn()
    const graph: RuleGraphLike = { evaluateInstance: vi.fn(() => evaluation({ visibility: [
      ['field:ruleHidden', { visible: false, required: false, readOnly: false }],
    ] })) }
    render(<SchemaForm
      initialValues={{ visible: 'v', ruleHidden: 'r', nativeHidden: { structured: true } }}
      onSubmit={onSubmit}
      ruleGraph={graph}
      view={form([section('s', [
        field('visible', 'Visible'),
        field('ruleHidden', 'Rule hidden'),
        field('nativeHidden', 'Native hidden', { controlHint: 'hidden', valueKind: 'object' }),
      ])])}
    />)
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledOnce())
    expect(onSubmit).toHaveBeenCalledWith({ visible: 'v', ruleHidden: 'r', nativeHidden: { structured: true } })
  })

  it('schema-form.projection-equivalence', async () => {
    const onSubmit = vi.fn(() => ({ isValid: true, errors: [] }))
    render(<SchemaForm
      initialValues={{ status: 'new', notes: 'Ported', enabled: true }}
      onSubmit={onSubmit}
      strings={{ submit: 'Save' }}
      view={form([section('Main', [
        field('status', 'Status', { controlHint: 'select', options: [{ value: 'new', label: 'New' }, { value: 'done', label: 'Done' }] }),
        field('notes', 'Notes', { controlHint: 'textarea' }),
        field('enabled', 'Enabled', { controlHint: 'boolean-toggle' }),
      ])])}
    />)
    expect(screen.getAllByText(/Status|Notes|Enabled/).map(node => node.textContent)).toEqual(['Status', 'Notes', 'Enabled'])
    expect(screen.getByRole('combobox', { name: 'Status' })).toHaveTextContent('New')
    expect(screen.getByRole('textbox', { name: 'Notes' })).toHaveValue('Ported')
    expect(screen.getByRole('switch', { name: 'Enabled' })).toBeChecked()
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(onSubmit).toHaveBeenCalledWith({ status: 'new', notes: 'Ported', enabled: true }))
    cleanup()
  })
})
