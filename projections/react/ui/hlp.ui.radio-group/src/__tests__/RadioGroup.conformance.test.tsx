import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { RadioGroup, type RadioGroupOrientation, type RadioOption } from '../RadioGroup'
import { fixture, sharedCases } from './fixtures'

const options: RadioOption[] = [
  { value: 'monthly', label: 'Monthly' },
  { value: 'quarterly', label: 'Quarterly' },
  { value: 'annual', label: 'Annual', description: 'Billed once a year' },
]

describe('RadioGroup shared fixtures', () => {
  it('radio-group.native', () => {
    fixture(sharedCases, 'radio-group.native')
    render(<RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="" />)
    expect(screen.getByRole('radiogroup', { name: 'Billing' })).toBeInTheDocument()
    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(3)
    radios.forEach(radio => expect(radio).toHaveAttribute('name', 'billing'))
  })

  it('radio-group.controlled', () => {
    fixture(sharedCases, 'radio-group.controlled')
    render(<RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="quarterly" />)
    expect(screen.getByRole('radio', { name: 'Quarterly' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Monthly' })).not.toBeChecked()
    expect(screen.getByRole('radio', { name: 'Annual' })).not.toBeChecked()
  })

  it('radio-group.select', async () => {
    fixture(sharedCases, 'radio-group.select')
    const onChange = vi.fn()
    render(<RadioGroup aria-label="Billing" name="billing" onChange={onChange} options={options} value="monthly" />)
    await userEvent.setup().click(screen.getByRole('radio', { name: 'Quarterly' }))
    expect(onChange).toHaveBeenCalledOnce()
    expect(onChange).toHaveBeenCalledWith('quarterly')
    expect(screen.getByRole('radio', { name: 'Monthly' })).toBeChecked()
  })

  it('radio-group.option-disabled', async () => {
    fixture(sharedCases, 'radio-group.option-disabled')
    const onChange = vi.fn()
    const disabledOptions = options.map(option => option.value === 'annual' ? { ...option, disabled: true } : option)
    render(<RadioGroup aria-label="Billing" name="billing" onChange={onChange} options={disabledOptions} value="monthly" />)
    const annual = screen.getByRole('radio', { name: 'Annual' })
    expect(annual).toBeDisabled()
    await userEvent.setup().click(annual)
    expect(onChange).not.toHaveBeenCalled()
  })

  it('radio-group.group-disabled', async () => {
    fixture(sharedCases, 'radio-group.group-disabled')
    const onChange = vi.fn()
    render(<RadioGroup aria-label="Billing" disabled name="billing" onChange={onChange} options={options} value="monthly" />)
    screen.getAllByRole('radio').forEach(radio => expect(radio).toBeDisabled())
    await userEvent.setup().click(screen.getByRole('radio', { name: 'Quarterly' }))
    expect(onChange).not.toHaveBeenCalled()
  })

  it('radio-group.description', () => {
    fixture(sharedCases, 'radio-group.description')
    render(<RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="" />)
    const annual = screen.getByRole('radio', { name: 'Annual' })
    expect(screen.getByText('Billed once a year')).toBeVisible()
    expect(annual).toHaveAccessibleName('Annual')
    expect(annual).toHaveAccessibleDescription('Billed once a year')
  })

  it('radio-group.orientation', () => {
    fixture(sharedCases, 'radio-group.orientation')
    const { rerender } = render(
      <RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="" />,
    )
    const group = screen.getByRole('radiogroup')
    expect(group).toHaveAttribute('data-hl-orientation', 'vertical')
    expect(group).toHaveClass('hl-radio-group--vertical')
    rerender(
      <RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} orientation="horizontal" value="" />,
    )
    expect(group).toHaveAttribute('data-hl-orientation', 'horizontal')
    expect(group).toHaveClass('hl-radio-group--horizontal')
  })

  it('radio-group.invalid', () => {
    fixture(sharedCases, 'radio-group.invalid')
    render(<RadioGroup aria-label="Billing" error name="billing" onChange={vi.fn()} options={options} value="" />)
    expect(screen.getByRole('radiogroup')).toHaveAttribute('aria-invalid', 'true')
  })

  it('radio-group.form-field', () => {
    fixture(sharedCases, 'radio-group.form-field')
    render(
      <FormFieldContext.Provider value={{ labelId: 'billing-label', describedBy: 'billing-hint billing-error' }}>
        <span id="billing-label">Billing</span>
        <RadioGroup name="billing" onChange={vi.fn()} options={options} value="" />
      </FormFieldContext.Provider>,
    )
    const group = screen.getByRole('radiogroup', { name: 'Billing' })
    expect(group).toHaveAttribute('aria-labelledby', 'billing-label')
    expect(group).toHaveAttribute('aria-describedby', 'billing-hint billing-error')
  })

  it('radio-group.required', () => {
    fixture(sharedCases, 'radio-group.required')
    render(
      <FormFieldContext.Provider value={{ required: true }}>
        <RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="" />
      </FormFieldContext.Provider>,
    )
    expect(screen.getByRole('radiogroup')).toHaveAttribute('aria-required', 'true')
    screen.getAllByRole('radio').forEach(radio => expect(radio).toBeRequired())
  })

  it('radio-group.class-parity', () => {
    const declared = fixture(sharedCases, 'radio-group.class-parity')
    const input = declared.input as { error: boolean, orientation: RadioGroupOrientation, options: RadioOption[] }
    const expected = declared.expected as Record<string, string[]> & { descriptionOutsideLabel: boolean }
    const cases = input.options
    render(
      <RadioGroup aria-label="Billing" error={input.error} name="billing" onChange={vi.fn()} options={cases} orientation={input.orientation} value="" />,
    )

    expect([...screen.getByRole('radiogroup').classList]).toEqual(expected.rootClasses)
    for (const option of cases) {
      const control = screen.getByRole('radio', { name: option.label })
      const label = control.closest('label')!
      const wrapper = control.closest('.hl-radio-group__option')!
      expect([...control.classList]).toEqual(expected.controlClasses)
      expect([...wrapper.classList]).toEqual(expected.optionClasses)
      expect([...label.classList]).toEqual(
        option.disabled ? expected.disabledLabelClasses : expected.labelClasses,
      )
      expect([...label.querySelector('span')!.classList]).toEqual(expected.labelTextClasses)
    }

    // The Blazor lane used to nest the description in a hl-radio-group__copy span inside the
    // label; the authority spaces it as a sibling of the label inside the option.
    const described = cases.find(option => option.description)!
    const description = screen.getByText(described.description!)
    expect([...description.classList]).toEqual(expected.descriptionClasses)
    expect(description.parentElement).toHaveClass('hl-radio-group__option')
    if (expected.descriptionOutsideLabel) expect(description.closest('label')).toBeNull()
  })

  it('radio-group.projection-equivalence', () => {
    fixture(sharedCases, 'radio-group.projection-equivalence')
    render(<RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="quarterly" />)
    const group = screen.getByRole('radiogroup', { name: 'Billing' })
    expect(group).toHaveAttribute('data-hl-orientation', 'vertical')
    expect(screen.getAllByRole('radio')).toHaveLength(options.length)
    expect(screen.getByRole('radio', { name: 'Quarterly' })).toBeChecked()
  })
})
