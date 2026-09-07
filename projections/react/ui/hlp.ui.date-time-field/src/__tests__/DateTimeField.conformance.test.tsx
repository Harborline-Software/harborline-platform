import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, expectTypeOf, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { DateTimeField, type DateTimeFieldProps } from '../index'
import { fixture, sharedCases } from './fixtures'

describe('DateTimeField revision-1 shared fixtures', () => {
  it('date-time-field.native', () => {
    const value = fixture(sharedCases, 'date-time-field.native')
    const input = value.input as { name: string; value: string }
    render(<DateTimeField {...input} onChange={vi.fn()} aria-label="Date and time" />)
    const control = screen.getByLabelText('Date and time')
    expect(control).toHaveAttribute('type', value.expected.type)
    expect(control).toHaveAttribute('id', value.expected.id)
    expect(control).toHaveAttribute('name', value.expected.name)
  })

  it('date-time-field.raw-roundtrip', () => {
    const value = fixture(sharedCases, 'date-time-field.raw-roundtrip')
    const input = value.input as { value: string; next: string }
    const onChange = vi.fn()
    render(<DateTimeField name="starts" value={input.value} onChange={onChange} aria-label="Date and time" />)
    const control = screen.getByLabelText('Date and time') as HTMLInputElement
    expect(control.value).toBe(input.value)
    fireEvent.change(control, { target: { value: input.next } })
    expect(onChange).toHaveBeenLastCalledWith(value.expected.emitted)
    expect(value.expected.timezoneConversion).toBe(false)
  })

  it('date-time-field.clear', () => {
    const value = fixture(sharedCases, 'date-time-field.clear')
    const onChange = vi.fn()
    render(
      <DateTimeField name="starts" value="2026-08-09T14:30" onChange={onChange} aria-label="Date and time" />,
    )
    fireEvent.change(screen.getByLabelText('Date and time'), { target: { value: value.input.next } })
    expect(onChange).toHaveBeenLastCalledWith(value.expected.emitted)
  })

  it('date-time-field.constraints', () => {
    const value = fixture(sharedCases, 'date-time-field.constraints')
    const input = value.input as { min: string; max: string; step: number }
    const { rerender } = render(
      <DateTimeField
        name="starts"
        value=""
        onChange={vi.fn()}
        min={input.min}
        max={input.max}
        step={input.step}
        aria-label="Date and time"
      />,
    )
    const control = screen.getByLabelText('Date and time')
    expect(control).toHaveAttribute('min', input.min)
    expect(control).toHaveAttribute('max', input.max)
    expect(control).toHaveAttribute('step', String(input.step))
    rerender(<DateTimeField name="starts" value="" onChange={vi.fn()} aria-label="Date and time" />)
    expect(control).not.toHaveAttribute('min')
    expect(control).not.toHaveAttribute('max')
    expect(control).not.toHaveAttribute('step')
    expect(value.expected.preserved).toBe(true)
  })

  it('date-time-field.disabled', async () => {
    const value = fixture(sharedCases, 'date-time-field.disabled')
    const onChange = vi.fn()
    render(<DateTimeField name="starts" value="" onChange={onChange} disabled aria-label="Date and time" />)
    const control = screen.getByLabelText('Date and time')
    expect(control).toBeDisabled()
    await userEvent.setup().type(control, '2026-08-09T14:30')
    expect(onChange).toHaveBeenCalledTimes(Number(value.expected.callbacks))
  })

  it('date-time-field.invalid', () => {
    const value = fixture(sharedCases, 'date-time-field.invalid')
    render(<DateTimeField name="starts" value="" onChange={vi.fn()} error aria-label="Date and time" />)
    expect(screen.getByLabelText('Date and time')).toHaveAttribute('aria-invalid', value.expected.ariaInvalid)
  })

  it('date-time-field.description', () => {
    const value = fixture(sharedCases, 'date-time-field.description')
    render(
      <FormFieldContext.Provider value={{ describedBy: String(value.expected.ariaDescribedBy) }}>
        <DateTimeField name="starts" value="" onChange={vi.fn()} aria-label="Date and time" />
      </FormFieldContext.Provider>,
    )
    expect(screen.getByLabelText('Date and time')).toHaveAttribute(
      'aria-describedby',
      value.expected.ariaDescribedBy,
    )
  })

  it('date-time-field.naming', () => {
    const value = fixture(sharedCases, 'date-time-field.naming')
    render(
      <DateTimeField
        name="starts"
        value=""
        onChange={vi.fn()}
        aria-label={String(value.input.ariaLabel)}
      />,
    )
    expect(screen.getByLabelText(String(value.expected.accessibleName))).toBeInTheDocument()
  })

  it('date-time-field.projection-equivalence', () => {
    const value = fixture(sharedCases, 'date-time-field.projection-equivalence')
    render(
      <FormFieldContext.Provider value={{ describedBy: 'starts-hint' }}>
        <DateTimeField
          name="starts"
          value="2026-11-01T01:30"
          onChange={vi.fn()}
          error
          aria-label="Start time"
        />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByLabelText('Start time') as HTMLInputElement
    expect({
      type: control.type,
      value: control.value,
      invalid: control.getAttribute('aria-invalid'),
      describedBy: control.getAttribute('aria-describedby'),
    }).toEqual({
      type: 'datetime-local',
      value: '2026-11-01T01:30',
      invalid: 'true',
      describedBy: 'starts-hint',
    })
    expect(value.expected.rawStringAndAccessibilityEqual).toBe(true)
  })

  it('exports the complete App-compatible prop interface', () => {
    expectTypeOf<DateTimeFieldProps>().toEqualTypeOf<{
      name: string
      value: string
      onChange: (value: string) => void
      min?: string
      max?: string
      step?: number
      disabled?: boolean
      required?: boolean
      error?: boolean
      'aria-label'?: string
      'aria-labelledby'?: string
    }>()
    expect(typeof DateTimeField).toBe('function')
  })

  it('closes the FormField required and disabled propagation gap', () => {
    render(
      <FormFieldContext.Provider value={{ labelId: 'starts-label', required: true, disabled: true }}>
        <DateTimeField name="starts" value="" onChange={vi.fn()} />
      </FormFieldContext.Provider>,
    )
    const control = document.querySelector('input[type="datetime-local"]')!
    expect(control).toBeRequired()
    expect(control).toBeDisabled()
    expect(control).toHaveAttribute('aria-labelledby', 'starts-label')
  })
})
