import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormField } from '../../../hlp.ui.form-field/src/FormField'
import { NumberField } from '../NumberField'
import { fixture, sharedCases } from './fixtures'

describe('NumberField shared fixtures', () => {
  it('number-field.native', () => {
    fixture(sharedCases, 'number-field.native')
    render(<NumberField aria-label="Quantity" name="quantity" onChange={() => undefined} value="" />)
    const control = screen.getByRole('spinbutton')
    expect(control).toHaveAttribute('type', 'number')
    expect(control).toHaveAttribute('id', 'quantity')
    expect(control).toHaveAttribute('name', 'quantity')
  })

  it('number-field.numeric-value', () => {
    fixture(sharedCases, 'number-field.numeric-value')
    render(<NumberField aria-label="Quantity" name="quantity" onChange={() => undefined} value={42} />)
    expect(screen.getByRole('spinbutton')).toHaveValue(42)
  })

  it('number-field.raw-string', () => {
    fixture(sharedCases, 'number-field.raw-string')
    const onChange = vi.fn()
    render(<NumberField aria-label="Quantity" name="quantity" onChange={onChange} value="100.50" />)
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '100.5' } })
    expect(onChange).toHaveBeenCalledOnce()
    expect(onChange).toHaveBeenCalledWith('100.5')
    expect(typeof onChange.mock.calls[0][0]).toBe('string')
  })

  it('number-field.empty', () => {
    fixture(sharedCases, 'number-field.empty')
    const onChange = vi.fn()
    const { rerender } = render(
      <NumberField aria-label="Quantity" name="quantity" onChange={onChange} value="" />,
    )
    expect(screen.getByRole('spinbutton')).toHaveValue(null)

    rerender(<NumberField aria-label="Quantity" name="quantity" onChange={onChange} value="5" />)
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '' } })
    expect(onChange).toHaveBeenCalledWith('')
  })

  it('number-field.constraints', () => {
    fixture(sharedCases, 'number-field.constraints')
    render(
      <NumberField aria-label="Quantity" max={100} min={0} name="quantity" onChange={() => undefined} step={0.5} value="" />,
    )
    expect(screen.getByRole('spinbutton')).toHaveAttribute('min', '0')
    expect(screen.getByRole('spinbutton')).toHaveAttribute('max', '100')
    expect(screen.getByRole('spinbutton')).toHaveAttribute('step', '0.5')
  })

  it('number-field.disabled', async () => {
    fixture(sharedCases, 'number-field.disabled')
    const onChange = vi.fn()
    render(<NumberField aria-label="Quantity" disabled name="quantity" onChange={onChange} value="" />)
    const control = screen.getByRole('spinbutton')
    expect(control).toBeDisabled()
    await userEvent.setup().type(control, '2')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('number-field.invalid', () => {
    fixture(sharedCases, 'number-field.invalid')
    render(<NumberField aria-label="Quantity" error name="quantity" onChange={() => undefined} value="" />)
    expect(screen.getByRole('spinbutton')).toHaveAttribute('aria-invalid', 'true')
  })

  it('number-field.description', () => {
    fixture(sharedCases, 'number-field.description')
    render(
      <FormField error="Required" hint="Whole numbers" label="Quantity" name="quantity">
        <NumberField name="quantity" onChange={() => undefined} value="" />
      </FormField>,
    )
    expect(screen.getByRole('spinbutton', { name: 'Quantity' })).toHaveAttribute(
      'aria-describedby',
      'quantity-hint quantity-error',
    )
  })

  it('number-field.tab-order', () => {
    fixture(sharedCases, 'number-field.tab-order')
    render(<NumberField aria-label="Quantity" name="quantity" onChange={() => undefined} tabIndex={-1} value="" />)
    expect(screen.getByRole('spinbutton')).toHaveAttribute('tabindex', '-1')
  })

  it('number-field.projection-equivalence', () => {
    fixture(sharedCases, 'number-field.projection-equivalence')
    const onChange = vi.fn()
    render(<NumberField aria-label="Quantity" name="quantity" onChange={onChange} step={0.5} value="3.5" />)
    const control = screen.getByRole('spinbutton', { name: 'Quantity' })
    fireEvent.change(control, { target: { value: '4.25' } })
    expect(control.tagName).toBe('INPUT')
    expect(onChange).toHaveBeenCalledWith('4.25')
  })
})
