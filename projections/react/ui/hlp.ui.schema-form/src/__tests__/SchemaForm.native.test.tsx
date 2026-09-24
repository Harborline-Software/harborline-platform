import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { compile, FormRuleGraph, type RuleDefinition } from '@harborline-software/rule-engine'

import { SchemaForm } from '../SchemaForm'
import { DEFAULT_CONTROLS } from '../controls'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, form, qualityCases, section, text } from './fixtures'

const hideTriggerGraph = (): RuleGraphLike => new FormRuleGraph(compile([{
  id: 'hide-trigger',
  tier: 'JsonLogic',
  scope: 'Field',
  scopeTarget: 'trigger',
  expression: { '!=': [{ var: 'trigger' }, 'hide'] },
  action: 'Visibility',
}] satisfies RuleDefinition[]), () => new Date('2026-06-30T00:00:00.000Z'))

describe('SchemaForm React projection', () => {
  it('schema-form.host-readonly-unavailable', () => {
    const stringify = vi.fn(() => 'must-not-render')
    const structured = { nested: true, toString: stringify }
    const view = form([section('details', [
      field('unknown', 'Unknown', { controlHint: 'future-object', valueKind: 'object' }),
      field('text', 'Text'),
      field('secret', 'Secret', { isSensitive: true }),
    ])])
    const cut = render(<SchemaForm view={view} values={{ unknown: structured, text: structured, secret: structured }} readOnly onSubmit={vi.fn()} />)
    expect(cut.container.querySelectorAll('[data-error-code="schema-form.unavailable-value"]')).toHaveLength(2)
    expect(cut.container.querySelector('#secret')?.textContent).toBe('Hidden')
    expect(stringify).not.toHaveBeenCalled()
    expect(cut.container.querySelectorAll('input,select,textarea,button')).toHaveLength(0)
  })
  it('renders every visible built-in hint as static harbor inspection output', () => {
    const visible = [
      ['text', 'vesselName', 'MV North Star'],
      ['textarea', 'inspectionScope', 'Annual hull and machinery inspection.'],
      ['number', 'grossTonnage', 18425.5],
      ['integer', 'crewCount', 24],
      ['select', 'inspectionType', 'annual'],
      ['multiselect', 'systemsReviewed', ['navigation', 'fire']],
      ['checkbox', 'documentsVerified', true],
      ['boolean', 'masterAttested', true],
      ['boolean-toggle', 'followUpRequired', false],
      ['date', 'inspectionDate', '2026-09-16'],
      ['datetime', 'inspectionStarted', '2026-09-16T09:30:00-04:00'],
      ['time', 'highTide', '14:45'],
      ['currency', 'estimatedCost', 48750.5],
      ['percentage', 'completion', 92.5],
      ['phone', 'agentPhone', '+1 410 555 0142'],
      ['email', 'agentEmail', 'port.agent@example.test'],
      ['url', 'certificateUrl', 'https://records.example.test/inspections/HLI-2048'],
      ['readonly', 'applicationStatus', 'Approved for certificate issuance'],
    ] as const
    const fields = visible.map(([hint, name]) => field(name, name, {
      controlHint: hint,
      ...(hint === 'select' ? { options: [{ value: 'annual', label: text('Annual safety inspection') }] } : {}),
      ...(hint === 'multiselect' ? { options: [
        { value: 'navigation', label: text('Navigation') },
        { value: 'fire', label: text('Fire suppression') },
      ] } : {}),
    }))
    fields.push(field('transportToken', 'Transport token', { controlHint: 'hidden' }))
    const values = Object.fromEntries([...visible.map(([, name, value]) => [name, value]), ['transportToken', 'kept']])
    const cut = render(<SchemaForm view={form([section('inspection', fields)])} values={values} readOnly onSubmit={vi.fn()} />)

    const expected = {
      vesselName: 'MV North Star', inspectionScope: 'Annual hull and machinery inspection.', grossTonnage: '18425.5', crewCount: '24',
      inspectionType: 'Annual safety inspection', systemsReviewed: 'Navigation, Fire suppression', documentsVerified: 'true',
      masterAttested: 'true', followUpRequired: 'false', inspectionDate: '2026-09-16', inspectionStarted: '2026-09-16T09:30:00-04:00',
      highTide: '14:45', estimatedCost: '48750.5', completion: '92.5', agentPhone: '+1 410 555 0142',
      agentEmail: 'port.agent@example.test', certificateUrl: 'https://records.example.test/inspections/HLI-2048',
      applicationStatus: 'Approved for certificate issuance',
    }
    for (const [id, value] of Object.entries(expected)) expect(cut.container.querySelector(`#${id}`)).toHaveTextContent(value)
    expect(cut.container.querySelector('input[type="hidden"][name="transportToken"]')).toHaveValue('kept')
    expect(screen.queryByText('Transport token')).toBeNull()
    expect(cut.container.querySelectorAll('input:not([type="hidden"]),select,textarea,button[type="submit"]')).toHaveLength(0)
  })

  it('admits only declared scalar multiselect values in host read-only mode', () => {
    const stringify = vi.fn(() => 'must-not-render')
    const options = [{ value: 'navigation', label: text('Navigation') }]
    const view = form([section('inspection', [
      field('undeclared', 'Undeclared', { controlHint: 'multiselect', options }),
      field('structured', 'Structured', { controlHint: 'multiselect', options }),
    ])])
    const cut = render(<SchemaForm view={view} values={{ undeclared: ['fire'], structured: [{ toString: stringify }] }} readOnly onSubmit={vi.fn()} />)
    expect(cut.container.querySelectorAll('[data-error-code="schema-form.unavailable-value"]')).toHaveLength(2)
    expect(stringify).not.toHaveBeenCalled()
  })
  it('suppresses sensitive and unreadable hidden fields before hidden serialization', () => {
    const stringify = vi.fn(() => 'must-not-render')
    const view = form([section('details', [
      field('sensitive', 'Sensitive', { controlHint: 'hidden', isSensitive: true }),
      field('unreadable', 'Unreadable', { controlHint: 'hidden', isReadable: false }),
      field('structured', 'Structured', { controlHint: 'hidden' }),
    ])])
    const structured = { toString: stringify }
    const cut = render(<SchemaForm view={view} values={{ sensitive: structured, unreadable: 'secret', structured }} readOnly onSubmit={vi.fn()} />)
    expect(cut.container).not.toHaveTextContent('Sensitive')
    expect(cut.container).not.toHaveTextContent('Unreadable')
    expect(cut.container).not.toHaveTextContent('Structured')
    expect(cut.container.querySelectorAll('input[type="hidden"]')).toHaveLength(1)
    expect(cut.container.querySelector('input[type="hidden"][name="structured"]')).toHaveValue('')
    expect(stringify).not.toHaveBeenCalled()
  })

  it('uses the first matching label for duplicate multiselect option values', () => {
    const options = [
      { value: 'navigation', label: text('Navigation') },
      { value: 'navigation', label: text('Duplicate') },
    ]
    const view = form([section('inspection', [field('systems', 'Systems', { controlHint: 'multiselect', options })])])
    const cut = render(<SchemaForm view={view} values={{ systems: ['navigation'] }} readOnly onSubmit={vi.fn()} />)
    expect(cut.container.querySelector('#systems')).toHaveTextContent('Navigation')
  })
  it('schema-form.host-readonly', () => {
    const submit = vi.fn()
    const changed = vi.fn()
    const action = vi.fn()
    const values = Object.freeze({ title: 'Published' })
    const graph: RuleGraphLike = { evaluateInstance: () => evaluation({ values: [['field:title', { state: 'Resolved', value: 'Computed' }]] }) }
    const view = form([section('details', [field('title', 'Title')]), section('actions', [], { items: [
      { kind: 'action', key: 'open', action: { kind: 'open-url', label: text('Open'), url: 'https://example.test' } },
    ] })])
    const cut = render(<SchemaForm view={view} values={values} ruleGraph={graph} readOnly onSubmit={submit} onValuesChange={changed} onBlockAction={action} />)
    expect(cut.container.querySelector('output')?.textContent).toBe('Computed')
    expect(cut.container.querySelectorAll('input,select,textarea,button[type=submit]')).toHaveLength(0)
    fireEvent.click(screen.getByRole('button', { name: 'Open' }))
    expect(action).not.toHaveBeenCalled()
    fireEvent.submit(cut.container.querySelector('form')!)
    expect(submit).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
    expect(values.title).toBe('Published')
    cut.rerender(<SchemaForm view={view} values={{ title: 'Published' }} onSubmit={submit} />)
    expect(screen.getByRole('textbox')).toHaveValue('Published')
    expect(screen.getByRole('button', { name: 'Submit' })).toBeEnabled()
  })
  it('consumes every frozen projection-native quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'schema-form.quality.field-labelling',
      'schema-form.quality.error-association',
      'schema-form.quality.summary-focus',
      'schema-form.quality.hidden-not-announced',
      'schema-form.quality.reflow',
      'schema-form.quality.tab-order',
      'schema-form.quality.collection-controls',
      'schema-form.quality.focus-survives-rehide',
      'schema-form.quality.caller-copy',
      'schema-form.quality.rtl',
      'schema-form.quality.pseudo',
      'schema-form.quality.currency-format',
      'schema-form.quality.date-format',
      'schema-form.quality.number-format',
      'schema-form.quality.light-dark',
      'schema-form.quality.tokens',
      'schema-form.quality.forced-colors',
      'schema-form.quality.visual-parity',
    ])
  })

  it('publishes the built-in control hints including runtime domain editors', () => {
    expect(Object.keys(DEFAULT_CONTROLS).sort()).toEqual([
      'boolean', 'boolean-toggle', 'checkbox', 'choicelist', 'currency', 'date', 'datetime', 'email',
      'hidden', 'integer', 'multiselect', 'none', 'number', 'percentage', 'phone', 'radiogroup', 'readonly',
      'recordpicker', 'select', 'singlevalue', 'taxonomypicker', 'text', 'textarea', 'time', 'url',
    ])
  })

  it('keeps caller copy, direction, control order, and hidden values projection-native', () => {
    const view = form([section('details', [
      field('first', 'الاسم', { helpText: text('[!! مساعدة موسعة !!]', 'ar'), label: text('الاسم', 'ar') }),
      field('secret', 'سر', { controlHint: 'hidden', label: text('سر', 'ar') }),
      field('last', 'البريد', { controlHint: 'email', label: text('البريد', 'ar') }),
    ], { title: text('التفاصيل', 'ar') })], 'النموذج')
    render(
      <SchemaForm
        initialValues={{ first: 'أ', secret: 'kept', last: 'x@example.test' }}
        localeChain={['ar']}
        onSubmit={vi.fn()}
        strings={{ submit: '[!! حفظ التغييرات !!]' }}
        view={{ ...view, title: text('النموذج', 'ar') }}
      />,
    )
    expect(screen.getByRole('form', { name: 'النموذج' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByRole('textbox').map(control => control.getAttribute('name'))).toEqual(['first', 'last'])
    expect(screen.getByText('[!! مساعدة موسعة !!]')).toBeVisible()
    expect(screen.getByRole('button', { name: '[!! حفظ التغييرات !!]' })).toBeInTheDocument()
    expect(screen.queryByText('سر')).toBeNull()
    expect(document.querySelector('input[type="hidden"][name="secret"]')).toHaveValue('kept')
  })

  it('associates server errors with their controls and focuses the summary', async () => {
    const onSubmit = vi.fn(() => ({
      isValid: false,
      errors: [{ jsonPointer: '/name', message: 'الاسم مطلوب', kind: 'Schema' as const }],
    }))
    render(<SchemaForm initialValues={{ name: '' }} onSubmit={onSubmit} view={form([section('s', [field('name', 'الاسم')])])} />)
    fireEvent.submit(screen.getByRole('form', { name: 'Fixture form' }))
    await waitFor(() => expect(document.getElementById('name-error')).toHaveTextContent('الاسم مطلوب'))
    expect(screen.getByRole('textbox', { name: 'الاسم' })).toHaveAttribute('aria-describedby', 'name-error')
    // Submission resolves a promise, so the validation state lands outside act and the focus
    // effect commits on a later tick than the error text this test already waited for. Awaiting
    // the focus separately asserts the same invariant without racing the effect that satisfies it.
    await waitFor(() => expect(document.querySelector('.hl-schema-form__summary')).toHaveFocus())
  })

  it('moves focus predictably when a rule re-hides the active field', async () => {
    const graph = hideTriggerGraph()
    render(<SchemaForm initialValues={{ trigger: '' }} onSubmit={vi.fn()} ruleGraph={graph} view={form([section('s', [field('trigger', 'Trigger')])])} />)
    const input = screen.getByRole('textbox', { name: 'Trigger' })
    input.focus()
    fireEvent.change(input, { target: { value: 'hide' } })
    await waitFor(() => expect(screen.getByText('This form has no fields to display.')).toHaveFocus())
    expect(screen.queryByRole('textbox', { name: 'Trigger' })).toBeNull()
  })

  it('uses locale-sensitive sibling controls for currency, dates, and numbers', () => {
    render(
      <SchemaForm
        initialValues={{ money: 1234.5, date: '2026-08-13', count: 1234 }}
        localeChain={['de-DE']}
        onSubmit={vi.fn()}
        view={form([section('s', [
          field('money', 'Currency', { controlHint: 'currency', config: { currencyCode: 'EUR' } }),
          field('date', 'Date', { controlHint: 'date' }),
          field('count', 'Number', { controlHint: 'number' }),
        ])])}
      />,
    )
    expect(screen.getByRole('textbox', { name: 'Currency' })).toHaveValue('1.234,50 €')
    expect(screen.getByLabelText('Date')).toHaveAttribute('type', 'date')
    expect(screen.getByLabelText('Number')).toHaveAttribute('type', 'number')
  })

  it('publishes logical, reflow-safe, token, dark-theme, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-schema-form-foreground')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('padding-inline')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain("[data-theme='dark'] .hl-schema-form")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).not.toMatch(/margin-(left|right)|padding-(left|right)|#[0-9a-f]{3,8}/i)
    expect(css).not.toContain('text-overflow: ellipsis')
  })
})
