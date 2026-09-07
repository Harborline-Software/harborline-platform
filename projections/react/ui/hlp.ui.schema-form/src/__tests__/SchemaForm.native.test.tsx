import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { SchemaForm } from '../SchemaForm'
import { DEFAULT_CONTROLS } from '../controls'
import type { RuleGraphLike } from '../SchemaForm.types'
import { evaluation, field, form, qualityCases, section, text } from './fixtures'

describe('SchemaForm React projection', () => {
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

  it('publishes exactly the revision-1 built-in control hints', () => {
    expect(Object.keys(DEFAULT_CONTROLS).sort()).toEqual([
      'boolean', 'boolean-toggle', 'checkbox', 'currency', 'date', 'datetime', 'email',
      'hidden', 'integer', 'multiselect', 'number', 'percentage', 'phone', 'readonly',
      'select', 'text', 'textarea', 'time', 'url',
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
    const graph: RuleGraphLike = {
      evaluateInstance: vi.fn(instance => evaluation({
        visibility: instance.fields.trigger === 'hide'
          ? [['field:trigger', { visible: false, required: false, readOnly: false }]]
          : [],
      })),
    }
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
