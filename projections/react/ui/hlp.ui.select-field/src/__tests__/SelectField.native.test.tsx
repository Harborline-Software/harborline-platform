import * as React from 'react'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SelectField, type SelectOption } from '../SelectField'

const options: readonly SelectOption[] = [
  { value: 'active', label: 'Active' },
  { value: 'blocked', label: 'Blocked', disabled: true },
  { value: 'pending', label: 'Pending' },
]

function UncontrolledOpenSelect({ onValueChange = () => undefined }: { onValueChange?: (value: string) => void }) {
  return <SelectField accessibleName="Status" name="status" onValueChange={onValueChange} options={options} value="active" />
}

describe('SelectField React projection', () => {
  it('shows the localized default or caller placeholder and selected label', () => {
    const { rerender } = render(<SelectField accessibleName="Status" name="status" onValueChange={() => undefined} options={options} value="" />)
    expect(screen.getByRole('combobox')).toHaveTextContent('Select…')
    rerender(<SelectField accessibleName="Status" name="status" onValueChange={() => undefined} options={options} placeholder="Choose status" value="" />)
    expect(screen.getByRole('combobox')).toHaveTextContent('Choose status')
    rerender(<SelectField accessibleName="Status" name="status" onValueChange={() => undefined} options={options} value="active" />)
    expect(screen.getByRole('combobox')).toHaveTextContent('Active')
  })

  it('opens a real listbox and requests one value and close transition', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    const opened = vi.fn()
    render(<SelectField accessibleName="Status" name="status" onOpenChange={opened} onValueChange={changed} options={options} value="active" />)
    const trigger = screen.getByRole('combobox', { name: 'Status' })
    await user.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.getAllByRole('option')).toHaveLength(3)
    await user.click(screen.getByRole('option', { name: 'Pending' }))
    expect(changed).toHaveBeenCalledOnce()
    expect(changed).toHaveBeenCalledWith('pending')
    expect(opened.mock.calls).toEqual([[true], [false]])
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('keeps supplied open state host-controlled', async () => {
    const user = userEvent.setup()
    const opened = vi.fn()
    render(<SelectField accessibleName="Status" name="status" onOpenChange={opened} onValueChange={() => undefined} open={false} options={options} value="active" />)
    await user.click(screen.getByRole('combobox'))
    expect(opened).toHaveBeenCalledWith(true)
    expect(screen.getByRole('combobox')).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('navigates enabled options, selects exactly once, and supports typeahead', () => {
    const changed = vi.fn()
    render(<UncontrolledOpenSelect onValueChange={changed} />)
    const trigger = screen.getByRole('combobox')
    fireEvent.keyDown(trigger, { key: 'Enter' })
    fireEvent.keyDown(trigger, { key: 'End' })
    expect(trigger).toHaveAttribute('aria-activedescendant', expect.stringContaining('option-2'))
    fireEvent.keyDown(trigger, { key: 'Enter' })
    expect(changed).toHaveBeenCalledOnce()
    expect(changed).toHaveBeenCalledWith('pending')
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    fireEvent.keyDown(trigger, { key: 'p' })
    expect(trigger).toHaveAttribute('aria-activedescendant', expect.stringContaining('option-2'))
  })

  it('closes on Escape and outside pointer while preserving the correct focus target', () => {
    render(<><UncontrolledOpenSelect /><button type="button">Outside</button></>)
    const trigger = screen.getByRole('combobox')
    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'Enter' })
    fireEvent.keyDown(trigger, { key: 'Escape' })
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
    fireEvent.keyDown(trigger, { key: 'Enter' })
    const outside = screen.getByRole('button', { name: 'Outside' })
    outside.focus()
    fireEvent.pointerDown(outside)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
    expect(outside).toHaveFocus()
  })

  it('applies FormField metadata with local precedence and preserves trigger attributes', () => {
    render(
      <FormFieldContext.Provider value={{ labelId: 'status-label', describedBy: 'status-hint status-error', required: true, disabled: true }}>
        <SelectField data-case="grid" disabled={false} name="status" onValueChange={() => undefined} options={options} required={false} tabIndex={-1} value="active" />
      </FormFieldContext.Provider>,
    )
    const trigger = screen.getByRole('combobox')
    expect(trigger).toHaveAttribute('id', 'status')
    expect(trigger).toHaveAttribute('aria-labelledby', 'status-label')
    expect(trigger).toHaveAttribute('aria-describedby', 'status-hint status-error')
    expect(trigger).not.toBeDisabled()
    expect(trigger).not.toHaveAttribute('aria-required')
    expect(trigger).toHaveAttribute('data-case', 'grid')
    expect(trigger).toHaveAttribute('tabindex', '-1')
  })

  it('native-disables interaction, exposes validation, and follows locale direction', () => {
    const changed = vi.fn()
    const opened = vi.fn()
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'forms.selectPlaceholder': 'اختر…' }}>
        <SelectField accessibleName="الحالة" disabled error name="status" onOpenChange={opened} onValueChange={changed} options={options} required value="" />
      </HarborlineLocaleProvider>,
    )
    const trigger = screen.getByRole('combobox', { name: 'الحالة' })
    expect(trigger).toBeDisabled()
    expect(trigger).toHaveAttribute('aria-invalid', 'true')
    expect(trigger).toHaveAttribute('aria-required', 'true')
    expect(trigger).toHaveTextContent('اختر…')
    expect(trigger.parentElement).toHaveAttribute('dir', 'rtl')
    fireEvent.click(trigger)
    expect(changed).not.toHaveBeenCalled()
    expect(opened).not.toHaveBeenCalled()
  })
})
