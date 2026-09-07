import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { FormField } from '../FormField'
import { useFormFieldContext } from '../FormFieldContext'
import { fixture, sharedCases } from './fixtures'

function ContextProbe() {
  const context = useFormFieldContext()
  return <output data-testid="context">{JSON.stringify(context)}</output>
}

describe('FormField shared fixtures', () => {
  it('form-field.label', () => {
    fixture(sharedCases, 'form-field.label')
    render(<FormField label="Amount" name="amount"><input id="amount" /></FormField>)
    const label = screen.getByText('Amount').closest('label')
    expect(label).toHaveAttribute('id', 'amount-label')
    expect(label).toHaveAttribute('for', 'amount')
    expect(screen.getByRole('textbox', { name: 'Amount' })).toHaveAttribute('id', 'amount')
  })

  it('form-field.required', () => {
    fixture(sharedCases, 'form-field.required')
    render(<FormField label="Amount" name="amount" required><ContextProbe /></FormField>)
    expect(screen.getByText('*')).toHaveAttribute('aria-hidden', 'true')
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({ required: true })
  })

  it('form-field.hint', () => {
    fixture(sharedCases, 'form-field.hint')
    render(<FormField hint="Whole numbers" label="Amount" name="amount"><ContextProbe /></FormField>)
    expect(screen.getByText('Whole numbers')).toHaveAttribute('id', 'amount-hint')
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({
      describedBy: 'amount-hint',
    })
  })

  it('form-field.error', () => {
    fixture(sharedCases, 'form-field.error')
    render(<FormField error="Required" label="Amount" name="amount"><ContextProbe /></FormField>)
    expect(screen.getByRole('alert')).toHaveAttribute('id', 'amount-error')
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({
      describedBy: 'amount-error',
    })
  })

  it('form-field.hint-error-order', () => {
    fixture(sharedCases, 'form-field.hint-error-order')
    render(
      <FormField error="Required" hint="Whole numbers" label="Amount" name="amount">
        <ContextProbe />
      </FormField>,
    )
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({
      describedBy: 'amount-hint amount-error',
    })
  })

  it('form-field.disabled', () => {
    fixture(sharedCases, 'form-field.disabled')
    render(
      <FormField disabled error="Required" hint="Whole numbers" label="Amount" name="amount">
        <ContextProbe />
      </FormField>,
    )
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(document.getElementById('amount-error')).not.toBeInTheDocument()
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({
      describedBy: 'amount-hint',
      disabled: true,
    })
  })

  it('form-field.context', () => {
    fixture(sharedCases, 'form-field.context')
    render(<FormField disabled label="Amount" name="amount" required><ContextProbe /></FormField>)
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toEqual({
      id: 'amount',
      labelId: 'amount-label',
      required: true,
      disabled: true,
    })
  })

  it('form-field.multiline-hint', () => {
    fixture(sharedCases, 'form-field.multiline-hint')
    render(<FormField hint={'Line one\nLine two'} label="Amount" name="amount"><input id="amount" /></FormField>)
    expect(screen.getByText((_, element) => element?.textContent === 'Line one\nLine two')).toHaveTextContent('Line one Line two')
  })

  it('form-field.projection-equivalence', () => {
    fixture(sharedCases, 'form-field.projection-equivalence')
    render(
      <FormField error="Required" hint="Whole numbers" label="Amount" name="amount" required>
        <ContextProbe />
      </FormField>,
    )
    expect(screen.getByText('Amount').closest('label')).toHaveAttribute('for', 'amount')
    expect(screen.getByRole('alert')).toHaveAttribute('id', 'amount-error')
    expect(JSON.parse(screen.getByTestId('context').textContent ?? '{}')).toMatchObject({
      id: 'amount',
      labelId: 'amount-label',
      describedBy: 'amount-hint amount-error',
      required: true,
      disabled: false,
    })
  })
})
