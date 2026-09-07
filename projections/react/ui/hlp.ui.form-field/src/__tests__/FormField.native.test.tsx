import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { FormField } from '../FormField'
import { useFormFieldContext } from '../FormFieldContext'
import { qualityCases } from './fixtures'

function DescribedControl() {
  const { id, describedBy, required, disabled } = useFormFieldContext()
  return (
    <input
      aria-describedby={describedBy}
      disabled={disabled}
      id={id}
      required={required}
    />
  )
}

describe('FormField React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'form-field.quality.label',
      'form-field.quality.description',
      'form-field.quality.alert',
      'form-field.quality.reflow',
      'form-field.quality.caller-copy',
      'form-field.quality.rtl',
      'form-field.quality.pseudo',
      'form-field.quality.light-dark',
      'form-field.quality.tokens',
      'form-field.quality.forced-colors',
      'form-field.quality.visual-parity',
    ])
  })

  it('supplies complete native relationships without rewriting its child', () => {
    render(
      <FormField error="Required" hint="Whole numbers" label="Amount" name="amount" required>
        <DescribedControl />
      </FormField>,
    )
    const input = screen.getByRole('textbox', { name: 'Amount' })
    expect(input).toBeRequired()
    expect(input).toHaveAttribute('aria-describedby', 'amount-hint amount-error')
    for (const reference of (input.getAttribute('aria-describedby') ?? '').split(' ')) {
      expect(document.getElementById(reference)).not.toBeNull()
    }
  })

  it('removes a live error and its description reference when disabled', () => {
    const { rerender } = render(
      <FormField error="Required" hint="Whole numbers" label="Amount" name="amount">
        <DescribedControl />
      </FormField>,
    )
    expect(screen.getByRole('textbox')).toHaveAttribute('aria-describedby', 'amount-hint amount-error')

    rerender(
      <FormField disabled error="Required" hint="Whole numbers" label="Amount" name="amount">
        <DescribedControl />
      </FormField>,
    )
    expect(screen.getByRole('textbox')).toBeDisabled()
    expect(screen.getByRole('textbox')).toHaveAttribute('aria-describedby', 'amount-hint')
    expect(document.getElementById('amount-error')).toBeNull()
  })

  it('preserves caller-owned copy, host attributes, direction, and child identity', () => {
    render(
      <FormField
        aria-label="Amount field"
        className="consumer"
        data-test="x"
        dir="rtl"
        hint={'[!! شرح طويل\nثانٍ !!]'}
        label="المبلغ"
        name="amount"
      >
        <input data-child="untouched" id="amount" />
      </FormField>,
    )
    const root = screen.getByLabelText('Amount field')
    expect(root).toHaveClass('hl-form-field', 'consumer')
    expect(root).toHaveAttribute('data-test', 'x')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('textbox')).toHaveAttribute('data-child', 'untouched')
    expect(screen.getByText((_, element) => element?.textContent === '[!! شرح طويل\nثانٍ !!]')).toBeVisible()
  })

  it('publishes logical, reflow-safe, token, dark-theme, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-form-field-foreground')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('white-space: pre-line')
    expect(css).toContain("[data-theme='dark'] .hl-form-field")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).not.toContain('text-overflow: ellipsis')
  })
})
