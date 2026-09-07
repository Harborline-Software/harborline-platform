import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, expectTypeOf, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { DateField, type DateFieldProps } from '../index'
import { fixture, sharedCases } from './fixtures'

describe('DateField revision-1 shared fixtures', () => {
  it('date-field.native', () => {
    const value = fixture(sharedCases, 'date-field.native')
    const input = value.input as { name: string; value: string }
    render(<DateField {...input} onChange={vi.fn()} aria-label="Date" />)
    const control = screen.getByLabelText('Date')
    expect(control).toHaveAttribute('type', value.expected.type)
    expect(control).toHaveAttribute('id', value.expected.id)
    expect(control).toHaveAttribute('name', value.expected.name)
  })

  it('date-field.raw-roundtrip', () => {
    const value = fixture(sharedCases, 'date-field.raw-roundtrip')
    const input = value.input as { value: string; next: string }
    const onChange = vi.fn()
    render(<DateField name="issued" value={input.value} onChange={onChange} aria-label="Date" />)
    const control = screen.getByLabelText('Date') as HTMLInputElement
    expect(control.value).toBe(value.expected.rendered)
    fireEvent.change(control, { target: { value: input.next } })
    expect(onChange).toHaveBeenLastCalledWith(value.expected.emitted)
    expect(typeof onChange.mock.calls.at(-1)?.[0]).toBe(value.expected.payloadType)
  })

  it('date-field.clear', () => {
    const value = fixture(sharedCases, 'date-field.clear')
    const onChange = vi.fn()
    render(<DateField name="issued" value={String(value.input.value)} onChange={onChange} aria-label="Date" />)
    fireEvent.change(screen.getByLabelText('Date'), {
      target: { value: value.input.next },
    })
    expect(onChange).toHaveBeenLastCalledWith(value.expected.emitted)
  })

  it('date-field.bounds', () => {
    const value = fixture(sharedCases, 'date-field.bounds')
    const input = value.input as { min: string; max: string }
    const { rerender } = render(
      <DateField name="issued" value="" onChange={vi.fn()} min={input.min} max={input.max} aria-label="Date" />,
    )
    const control = screen.getByLabelText('Date')
    expect(control).toHaveAttribute('min', input.min)
    expect(control).toHaveAttribute('max', input.max)
    rerender(<DateField name="issued" value="" onChange={vi.fn()} aria-label="Date" />)
    expect(control).not.toHaveAttribute('min')
    expect(control).not.toHaveAttribute('max')
    expect(value.expected.preserved).toBe(true)
  })

  it('date-field.disabled', async () => {
    const value = fixture(sharedCases, 'date-field.disabled')
    const onChange = vi.fn()
    render(<DateField name="issued" value="" onChange={onChange} disabled aria-label="Date" />)
    const control = screen.getByLabelText('Date')
    expect(control).toBeDisabled()
    await userEvent.setup().type(control, String(value.input.next))
    expect(onChange).toHaveBeenCalledTimes(Number(value.expected.callbacks))
  })

  it('date-field.invalid', () => {
    const value = fixture(sharedCases, 'date-field.invalid')
    render(<DateField name="issued" value="" onChange={vi.fn()} error aria-label="Date" />)
    expect(screen.getByLabelText('Date')).toHaveAttribute(
      'aria-invalid',
      value.expected.ariaInvalid,
    )
  })

  it('date-field.description', () => {
    const value = fixture(sharedCases, 'date-field.description')
    render(
      <FormFieldContext.Provider value={{ describedBy: String(value.expected.ariaDescribedBy) }}>
        <DateField name="issued" value="" onChange={vi.fn()} aria-label="Date" />
      </FormFieldContext.Provider>,
    )
    expect(screen.getByLabelText('Date')).toHaveAttribute(
      'aria-describedby',
      value.expected.ariaDescribedBy,
    )
  })

  it('date-field.naming', () => {
    const value = fixture(sharedCases, 'date-field.naming')
    const input = value.input as { ariaLabel: string; ariaLabelledBy: string }
    render(
      <>
        <span id={input.ariaLabelledBy}>Issued</span>
        <DateField
          name="issued"
          value=""
          onChange={vi.fn()}
          aria-label={input.ariaLabel}
          aria-labelledby={input.ariaLabelledBy}
        />
      </>,
    )
    const control = screen.getByLabelText('Issued')
    expect(control).toHaveAttribute('aria-label', input.ariaLabel)
    expect(control).toHaveAttribute('aria-labelledby', input.ariaLabelledBy)
    expect(value.expected.bothSupported).toBe(true)
  })

  it('date-field.projection-equivalence', () => {
    const value = fixture(sharedCases, 'date-field.projection-equivalence')
    render(
      <FormFieldContext.Provider value={{ describedBy: 'issued-hint' }}>
        <DateField name="issued" value="2026-08-09" onChange={vi.fn()} error aria-label="Issue date" />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByLabelText('Issue date') as HTMLInputElement
    expect({
      type: control.type,
      value: control.value,
      invalid: control.getAttribute('aria-invalid'),
      describedBy: control.getAttribute('aria-describedby'),
    }).toEqual({ type: 'date', value: '2026-08-09', invalid: 'true', describedBy: 'issued-hint' })
    expect(value.expected.rawStringAndAccessibilityEqual).toBe(true)
  })

  it('exports the complete App-compatible prop interface', () => {
    expectTypeOf<DateFieldProps>().toEqualTypeOf<{
      name: string
      value: string
      onChange: (value: string) => void
      min?: string
      max?: string
      disabled?: boolean
      required?: boolean
      error?: boolean
      'aria-label'?: string
      'aria-labelledby'?: string
    }>()
    expect(typeof DateField).toBe('function')
  })

  it('closes the FormField required and disabled propagation gap', () => {
    render(
      <FormFieldContext.Provider value={{ labelId: 'issued-label', required: true, disabled: true }}>
        <DateField name="issued" value="" onChange={vi.fn()} />
      </FormFieldContext.Provider>,
    )
    const control = document.querySelector('input[type="date"]')!
    expect(control).toBeRequired()
    expect(control).toBeDisabled()
    expect(control).toHaveAttribute('aria-labelledby', 'issued-label')
  })
})
