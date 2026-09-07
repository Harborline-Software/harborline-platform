import { createRef, useState } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormField } from '../../../hlp.ui.form-field/src/FormField'
import { NumberField } from '../NumberField'
import { qualityCases } from './fixtures'

describe('NumberField React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'number-field.quality.native',
      'number-field.quality.name',
      'number-field.quality.description',
      'number-field.quality.focus',
      'number-field.quality.reflow',
      'number-field.quality.raw-value',
      'number-field.quality.rtl',
      'number-field.quality.pseudo',
      'number-field.quality.light-dark',
      'number-field.quality.tokens',
      'number-field.quality.forced-colors',
      'number-field.quality.visual-parity',
    ])
  })

  it('uses native keyboard editing and emits each browser-owned raw string', async () => {
    const onChange = vi.fn()

    function Harness() {
      const [value, setValue] = useState('')
      return (
        <NumberField
          aria-label="Quantity"
          name="quantity"
          onChange={next => {
            onChange(next)
            setValue(next)
          }}
          value={value}
        />
      )
    }

    render(<Harness />)
    const control = screen.getByRole('spinbutton')
    await userEvent.setup().type(control, '12')
    expect(control).toHaveValue(12)
    expect(onChange.mock.calls.map(call => call[0])).toEqual(['1', '12'])
  })

  it('combines local and inherited required and disabled state with OR semantics', () => {
    const onChange = vi.fn()
    const { rerender } = render(
      <FormField disabled label="Quantity" name="quantity" required>
        <NumberField disabled={false} name="quantity" onChange={onChange} required={false} value="" />
      </FormField>,
    )
    expect(screen.getByRole('spinbutton')).toBeDisabled()
    expect(screen.getByRole('spinbutton')).toBeRequired()
    expect(screen.getByRole('spinbutton')).toHaveAttribute('aria-required', 'true')

    rerender(
      <FormField label="Quantity" name="quantity">
        <NumberField disabled name="quantity" onChange={onChange} required value="" />
      </FormField>,
    )
    expect(screen.getByRole('spinbutton')).toBeDisabled()
    expect(screen.getByRole('spinbutton')).toBeRequired()
  })

  it('does not retain FormField error descriptions after the field becomes disabled', () => {
    const { rerender } = render(
      <FormField error="Required" hint="Whole numbers" label="Quantity" name="quantity">
        <NumberField name="quantity" onChange={() => undefined} value="" />
      </FormField>,
    )
    expect(screen.getByRole('spinbutton')).toHaveAttribute(
      'aria-describedby',
      'quantity-hint quantity-error',
    )

    rerender(
      <FormField disabled error="Required" hint="Whole numbers" label="Quantity" name="quantity">
        <NumberField name="quantity" onChange={() => undefined} value="" />
      </FormField>,
    )
    expect(screen.getByRole('spinbutton')).toHaveAttribute('aria-describedby', 'quantity-hint')
    expect(document.getElementById('quantity-error')).toBeNull()
  })

  it('preserves input host attributes, explicit descriptions, classes, and ref', () => {
    const ref = createRef<HTMLInputElement>()
    render(
      <FormField hint="Whole numbers" label="Quantity" name="quantity">
        <NumberField
          aria-describedby="consumer-help quantity-hint"
          className="consumer"
          data-test="x"
          dir="rtl"
          form="invoice"
          name="quantity"
          onChange={() => undefined}
          ref={ref}
          value=""
        />
      </FormField>,
    )
    const control = screen.getByRole('spinbutton')
    expect(ref.current).toBe(control)
    expect(control).toHaveClass('hl-number-field', 'consumer')
    expect(control).toHaveAttribute('aria-describedby', 'quantity-hint consumer-help')
    expect(control).toHaveAttribute('data-test', 'x')
    expect(control).toHaveAttribute('dir', 'rtl')
    expect(control).toHaveAttribute('form', 'invoice')
  })

  it('preserves caller aria state and never parses or clamps emitted values', () => {
    const onChange = vi.fn()
    render(
      <NumberField
        aria-invalid="grammar"
        aria-label="Quantity"
        max={10}
        min={0}
        name="quantity"
        onChange={onChange}
        value=""
      />,
    )
    const control = screen.getByRole('spinbutton')
    expect(control).toHaveAttribute('aria-invalid', 'grammar')
    fireEvent.change(control, { target: { value: '12' } })
    expect(onChange).toHaveBeenCalledWith('12')
  })

  it('publishes native, logical, reflow-safe, token, dark-theme, and forced-color styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('appearance: auto')
    expect(css).toContain('--hl-number-field-surface')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('padding-inline')
    expect(css).toContain(':focus-visible')
    expect(css).toContain("[data-theme='dark'] .hl-number-field")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).not.toContain('text-overflow: ellipsis')
  })
})
