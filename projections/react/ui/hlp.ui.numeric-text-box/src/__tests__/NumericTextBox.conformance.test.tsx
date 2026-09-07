import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { NumericTextBox } from '../NumericTextBox'
import { fixture, sharedCases } from './fixtures'

function textbox(): HTMLInputElement {
  return screen.getByRole('textbox')
}

describe('NumericTextBox revision-1 shared fixtures', () => {
  it('consumes every frozen case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'numeric-text-box.defaults',
      'numeric-text-box.controlled-value',
      'numeric-text-box.controlled-null',
      'numeric-text-box.uncontrolled-default',
      'numeric-text-box.focus-raw',
      'numeric-text-box.typing-buffer',
      'numeric-text-box.blur-commit',
      'numeric-text-box.enter-commit',
      'numeric-text-box.empty-invalid-commit',
      'numeric-text-box.controlled-request',
      'numeric-text-box.default-value-once',
      'numeric-text-box.external-update',
      'numeric-text-box.focus-blur-order',
      'numeric-text-box.commit-clamp',
      'numeric-text-box.spinner-step',
      'numeric-text-box.spinner-null-anchor',
      'numeric-text-box.spinner-bound-disabled',
      'numeric-text-box.step-not-snap',
      'numeric-text-box.formatting',
      'numeric-text-box.locale-rtl-labels',
      'numeric-text-box.disabled-readonly',
      'numeric-text-box.validation-identity',
      'numeric-text-box.appearance',
      'numeric-text-box.replacement',
      'numeric-text-box.projection-equivalence',
    ])
  })

  it('implements defaults, controlled suppliedness, and one-time uncontrolled defaults', () => {
    fixture(sharedCases, 'numeric-text-box.defaults')
    const rendered = render(<NumericTextBox aria-label="Amount" defaultValue={99} />)
    expect(textbox()).toHaveAttribute('type', 'text')
    expect(textbox()).toHaveAttribute('inputmode', 'decimal')
    expect(textbox()).toHaveValue('99.00')
    expect(screen.getAllByRole('button')).toHaveLength(2)

    rendered.rerender(<NumericTextBox aria-label="Amount" defaultValue={9} value={null} />)
    expect(textbox()).toHaveValue('')
    rendered.rerender(<NumericTextBox aria-label="Amount" defaultValue={9} value={undefined} />)
    expect(textbox()).toHaveValue('')
    rendered.rerender(<NumericTextBox aria-label="Amount" defaultValue={9} />)
    expect(textbox()).toHaveValue('99.00')
  })

  it('uses a raw edit buffer and emits nothing while typing', () => {
    fixture(sharedCases, 'numeric-text-box.focus-raw')
    fixture(sharedCases, 'numeric-text-box.typing-buffer')
    const onChange = vi.fn()
    render(<NumericTextBox aria-label="Amount" onChange={onChange} value={1234.5} />)
    expect(textbox()).toHaveValue('1,234.50')
    fireEvent.focus(textbox())
    expect(textbox()).toHaveValue('1234.5')
    fireEvent.change(textbox(), { target: { value: '12.3' } })
    expect(textbox()).toHaveValue('12.3')
    expect(onChange).not.toHaveBeenCalled()
  })

  it('commits on blur and Enter with value-before-blur ordering and no duplicate blur commit', () => {
    fixture(sharedCases, 'numeric-text-box.blur-commit')
    fixture(sharedCases, 'numeric-text-box.enter-commit')
    fixture(sharedCases, 'numeric-text-box.focus-blur-order')
    const order: string[] = []
    const onChange = vi.fn(() => order.push('value'))
    const onBlur = vi.fn(() => order.push('blur'))
    render(<NumericTextBox aria-label="Amount" onBlur={onBlur} onChange={onChange} value={5} />)
    fireEvent.focus(textbox())
    fireEvent.change(textbox(), { target: { value: '12' } })
    fireEvent.keyDown(textbox(), { key: 'Enter' })
    fireEvent.blur(textbox())
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith(12)
    expect(order).toEqual(['value', 'blur'])
    expect(textbox()).toHaveValue('5.00')
  })

  it('strictly parses complete decimal and exponent strings, clamps, and never step-snaps typing', () => {
    fixture(sharedCases, 'numeric-text-box.empty-invalid-commit')
    fixture(sharedCases, 'numeric-text-box.commit-clamp')
    fixture(sharedCases, 'numeric-text-box.step-not-snap')
    const onChange = vi.fn()
    const rendered = render(<NumericTextBox aria-label="Amount" max={10} min={2} onChange={onChange} step={1} />)
    for (const raw of ['', '12abc', '1', '11', '1.234', '1e1']) {
      fireEvent.focus(textbox())
      fireEvent.change(textbox(), { target: { value: raw } })
      fireEvent.blur(textbox())
    }
    expect(onChange.mock.calls.map(call => call[0])).toEqual([null, null, 2, 10, 2, 10])
    rendered.rerender(<NumericTextBox aria-label="Amount" onChange={onChange} step={1} />)
    fireEvent.focus(textbox())
    fireEvent.change(textbox(), { target: { value: '1.234' } })
    fireEvent.blur(textbox())
    expect(onChange).toHaveBeenLastCalledWith(1.234)
  })

  it('steps from controlled values and a clamped null anchor while disabling active bounds', () => {
    fixture(sharedCases, 'numeric-text-box.spinner-step')
    fixture(sharedCases, 'numeric-text-box.spinner-null-anchor')
    fixture(sharedCases, 'numeric-text-box.spinner-bound-disabled')
    const onChange = vi.fn()
    const rendered = render(<NumericTextBox aria-label="Amount" onChange={onChange} step={2} value={4} />)
    fireEvent.click(screen.getByRole('button', { name: 'Increment' }))
    fireEvent.click(screen.getByRole('button', { name: 'Decrement' }))
    expect(onChange.mock.calls.map(call => call[0])).toEqual([6, 2])

    rendered.rerender(<NumericTextBox aria-label="Amount" min={10} onChange={onChange} value={null} />)
    fireEvent.click(screen.getByRole('button', { name: 'Increment' }))
    expect(onChange).toHaveBeenLastCalledWith(10)
    expect(screen.getByRole('button', { name: 'Decrement' })).toBeDisabled()

    rendered.rerender(<NumericTextBox aria-label="Amount" max={10} onChange={onChange} value={10} />)
    expect(screen.getByRole('button', { name: 'Increment' })).toBeDisabled()
  })

  it('honors plain, currency, and percent formatting including c0 and p0', () => {
    fixture(sharedCases, 'numeric-text-box.formatting')
    const rendered = render(<NumericTextBox aria-label="Amount" locale="en-US" value={1234.5} />)
    expect(textbox()).toHaveValue('1,234.50')
    rendered.rerender(<NumericTextBox aria-label="Amount" format="c0" locale="en-US" value={1234.5} />)
    expect(textbox()).toHaveValue('$1,235')
    rendered.rerender(<NumericTextBox aria-label="Amount" format="c2" locale="en-US" value={1234.5} />)
    expect(textbox()).toHaveValue('$1,234.50')
    rendered.rerender(<NumericTextBox aria-label="Ratio" format="p0" locale="en-US" value={0.125} />)
    expect(textbox()).toHaveValue('13%')
    rendered.rerender(<NumericTextBox aria-label="Ratio" format="p2" locale="en-US" value={0.125} />)
    expect(textbox()).toHaveValue('12.50%')
  })

  it('uses provider direction, explicit formatting locale, and label precedence', () => {
    fixture(sharedCases, 'numeric-text-box.locale-rtl-labels')
    render(
      <HarborlineLocaleProvider
        catalog={{ 'forms.numeric.decrement': 'Lower' }}
        direction="rtl"
        locale="ar-SA"
        numberFormatter={(value, request) => `${request.locale}:${value}`}
      >
        <NumericTextBox aria-label="Amount" incrementLabel="Raise" locale="fr-FR" value={12} />
      </HarborlineLocaleProvider>,
    )
    expect(textbox().closest('.hl-numeric-text-box')).toHaveAttribute('dir', 'rtl')
    expect(textbox()).toHaveValue('fr-FR:12')
    expect(screen.getByRole('button', { name: 'Raise' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lower' })).toBeInTheDocument()
  })

  it('places validation and identity on the input and host attributes on the root', () => {
    fixture(sharedCases, 'numeric-text-box.validation-identity')
    render(
      <NumericTextBox
        aria-describedby="amount-help"
        aria-label="Amount"
        className="consumer"
        data-owner="app"
        error
        id="amount"
        name="amount"
        required
      />,
    )
    expect(textbox()).toHaveAttribute('id', 'amount')
    expect(textbox()).toHaveAttribute('name', 'amount')
    expect(textbox()).toHaveAttribute('aria-describedby', 'amount-help')
    expect(textbox()).toHaveAttribute('aria-required', 'true')
    expect(textbox()).toHaveAttribute('aria-invalid', 'true')
    expect(textbox()).toBeRequired()
    expect(textbox().closest('.hl-numeric-text-box')).toHaveAttribute('data-owner', 'app')
    expect(textbox().closest('.hl-numeric-text-box')).toHaveClass('consumer')
  })

  it('normalizes size aliases and publishes stable appearance vocabulary', () => {
    fixture(sharedCases, 'numeric-text-box.appearance')
    const rendered = render(<NumericTextBox aria-label="Amount" fillMode="flat" rounded="full" size="small" />)
    const root = textbox().closest('.hl-numeric-text-box')
    expect(root).toHaveAttribute('data-size', 'sm')
    expect(root).toHaveAttribute('data-fill-mode', 'flat')
    expect(root).toHaveAttribute('data-rounded', 'full')
    rendered.rerender(<NumericTextBox aria-label="Amount" fillMode="outline" rounded="large" size="large" />)
    expect(root).toHaveAttribute('data-size', 'lg')
    expect(root).toHaveAttribute('data-fill-mode', 'outline')
  })

  it('emits stable configuration errors', () => {
    expect(() => render(<NumericTextBox max={1} min={2} />)).toThrow('invalid-min-max')
    expect(() => render(<NumericTextBox step={0} />)).toThrow('invalid-step')
    expect(() => render(<NumericTextBox decimals={21} />)).toThrow('invalid-decimals')
    expect(() => render(<NumericTextBox min={Number.NaN} />)).toThrow('invalid-bound')
    expect(() => render(<NumericTextBox value={Number.POSITIVE_INFINITY} />)).toThrow('invalid-controlled-value')
  })
})
