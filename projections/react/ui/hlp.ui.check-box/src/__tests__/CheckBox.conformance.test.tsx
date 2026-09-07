import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { CheckBox, type CheckBoxSize } from '../CheckBox'
import { fixture, sharedCases } from './fixtures'

describe('CheckBox shared fixtures', () => {
  it('check-box.uncontrolled', async () => {
    fixture(sharedCases, 'check-box.uncontrolled')
    const onChange = vi.fn()
    render(<CheckBox aria-label="Include" defaultChecked={false} onChange={onChange} />)
    const control = screen.getByRole('checkbox')
    expect(control).not.toBeChecked()
    await userEvent.setup().click(control)
    expect(control).toBeChecked()
    await userEvent.setup().click(control)
    expect(control).not.toBeChecked()
    expect(onChange.mock.calls.map(call => call[0])).toEqual([true, false])
  })

  it('check-box.controlled', async () => {
    fixture(sharedCases, 'check-box.controlled')
    const onChange = vi.fn()
    render(<CheckBox aria-label="Include" checked={false} onChange={onChange} />)
    const control = screen.getByRole('checkbox')
    await userEvent.setup().click(control)
    expect(onChange).toHaveBeenCalledOnce()
    expect(onChange).toHaveBeenCalledWith(true)
    expect(control).not.toBeChecked()
  })

  it('check-box.mixed', async () => {
    fixture(sharedCases, 'check-box.mixed')
    const onChange = vi.fn()
    render(<CheckBox aria-label="Include" checked="mixed" onChange={onChange} />)
    const control = screen.getByRole('checkbox') as HTMLInputElement
    expect(control.indeterminate).toBe(true)
    expect(control).toHaveAttribute('aria-checked', 'mixed')
    await userEvent.setup().click(control)
    expect(onChange).toHaveBeenCalledWith(true)
    expect(control.indeterminate).toBe(true)
  })

  it('check-box.disabled-noop', async () => {
    fixture(sharedCases, 'check-box.disabled-noop')
    const onChange = vi.fn()
    render(<CheckBox aria-label="Include" disabled onChange={onChange} />)
    const control = screen.getByRole('checkbox')
    await userEvent.setup().click(control)
    expect(control).toBeDisabled()
    expect(onChange).not.toHaveBeenCalled()
  })

  it('check-box.label-placement', async () => {
    fixture(sharedCases, 'check-box.label-placement')
    const { rerender } = render(<CheckBox label="Include" labelPlacement="before" />)
    const before = screen.getByRole('checkbox', { name: 'Include' })
    expect(before.parentElement?.firstElementChild).toHaveTextContent('Include')
    await userEvent.setup().click(screen.getByText('Include'))
    expect(before).toBeChecked()

    rerender(<CheckBox label="Include" labelPlacement="after" />)
    const after = screen.getByRole('checkbox', { name: 'Include' })
    expect(after.parentElement?.lastElementChild).toHaveTextContent('Include')
  })

  it('check-box.required-error', () => {
    fixture(sharedCases, 'check-box.required-error')
    render(<CheckBox aria-label="Include" error required />)
    const control = screen.getByRole('checkbox')
    expect(control).toBeRequired()
    expect(control).toHaveAttribute('aria-required', 'true')
    expect(control).toHaveAttribute('aria-invalid', 'true')
  })

  it('check-box.form-attributes', () => {
    fixture(sharedCases, 'check-box.form-attributes')
    render(<CheckBox describedBy="help" id="include" label="Include" name="include" value="yes" />)
    const control = screen.getByRole('checkbox')
    expect(control).toHaveAttribute('id', 'include')
    expect(control).toHaveAttribute('name', 'include')
    expect(control).toHaveAttribute('value', 'yes')
    expect(control).toHaveAttribute('aria-describedby', 'help')
  })

  it('check-box.sizes', () => {
    fixture(sharedCases, 'check-box.sizes')
    const sizes: CheckBoxSize[] = ['sm', 'md', 'lg']
    const { rerender } = render(<CheckBox aria-label="Include" size="sm" />)
    for (const size of sizes) {
      rerender(<CheckBox aria-label="Include" size={size} />)
      expect(screen.getByRole('checkbox').closest('.hl-check-box')).toHaveAttribute('data-hl-size', size)
    }
  })

  it('check-box.host-attributes', () => {
    fixture(sharedCases, 'check-box.host-attributes')
    render(<CheckBox aria-label="Include row" className="consumer" data-test="x" />)
    const control = screen.getByRole('checkbox', { name: 'Include row' })
    expect(control).toHaveAttribute('data-test', 'x')
    expect(control.closest('.hl-check-box')).toHaveClass('consumer')
  })

  it('check-box.class-parity', () => {
    const declared = fixture(sharedCases, 'check-box.class-parity')
    const expected = declared.expected as { rootClasses: string[], controlClasses: string[] }
    const input = declared.input as { label: string, size: CheckBoxSize, error: boolean }
    const { rerender } = render(<CheckBox error={input.error} label={input.label} size={input.size} />)
    const labelled = screen.getByRole('checkbox')
    expect([...labelled.classList]).toEqual(expected.controlClasses)
    expect([...labelled.closest('.hl-check-box')!.classList]).toEqual(expected.rootClasses)

    // The class list must not depend on whether a label is present: the Blazor lane used to render
    // a hl-check-box__standalone root in that case, which the authority never defined.
    rerender(<CheckBox aria-label={input.label} error={input.error} size={input.size} />)
    const bare = screen.getByRole('checkbox')
    expect([...bare.classList]).toEqual(expected.controlClasses)
    expect([...bare.closest('.hl-check-box')!.classList]).toEqual(expected.rootClasses)
  })

  it('check-box.projection-equivalence', () => {
    fixture(sharedCases, 'check-box.projection-equivalence')
    render(<CheckBox checked="mixed" label="Include" />)
    const control = screen.getByRole('checkbox') as HTMLInputElement
    expect(control.tagName).toBe('INPUT')
    expect(control.type).toBe('checkbox')
    expect(control.indeterminate).toBe(true)
    expect(control).toHaveAccessibleName('Include')
  })
})
