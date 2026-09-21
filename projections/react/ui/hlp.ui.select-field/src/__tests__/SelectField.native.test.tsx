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
  it.each([false, true])('retains active state for acknowledged multiple toggles, searchable=%s', searchable => {
    const changed = vi.fn()
    const view = (value: readonly string[]) => <SelectField multiple searchable={searchable} name="status"
      accessibleName="Status" value={value} options={options.map(option => ({ ...option }))} onValueChange={changed} />
    const { rerender } = render(view(['active']))
    fireEvent.click(screen.getByRole('button', { name: 'Status' }))
    if (searchable) {
      fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'pen' } })
      fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'ArrowDown' })
    } else fireEvent.keyDown(screen.getByRole('listbox'), { key: 'End' })
    fireEvent.keyDown(screen.getByRole('listbox'), { key: ' ' })
    expect(changed.mock.calls).toEqual([[['active', 'pending']]])
    rerender(view(['active', 'pending']))
    expect(screen.getByRole('listbox')).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Pending' }).id)
    if (searchable) expect(screen.getByRole('searchbox')).toHaveValue('pen')
    fireEvent.keyDown(screen.getByRole('listbox'), { key: ' ' })
    expect(changed.mock.calls).toEqual([[['active', 'pending']], [['active']]])
    rerender(view(['active']))
    expect(screen.getByRole('option', { name: 'Pending' })).toHaveAttribute('aria-selected', 'false')
    rerender(view(['pending']))
    expect(screen.getByRole('listbox')).not.toHaveAttribute('aria-activedescendant')
    if (searchable) expect(screen.getByRole('searchbox')).toHaveValue('')
    fireEvent.keyDown(screen.getByRole('listbox'), { key: ' ' })
    expect(changed).toHaveBeenCalledTimes(2)
  })

  it('keeps closed ArrowDown relative to the selected enabled option', () => {
    const changed = vi.fn()
    render(<UncontrolledOpenSelect onValueChange={changed} />)
    const trigger = screen.getByRole('combobox')
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(trigger).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Pending' }).id)
    expect(changed).not.toHaveBeenCalled()
    fireEvent.keyDown(trigger, { key: 'Enter' })
    expect(changed.mock.calls).toEqual([['pending']])
  })

  it('forwards an actual input ref and input events, and lets the caller cancel search keys', () => {
    const ref = React.createRef<HTMLInputElement>()
    const targets: Element[] = []
    const changed = vi.fn()
    const key = vi.fn((event: React.KeyboardEvent<HTMLInputElement>) => {
      targets.push(event.currentTarget)
      if (event.key === 'ArrowDown') event.preventDefault()
    })
    render(<SelectField searchable ref={ref} name="status" value="" options={options} onValueChange={changed}
      onFocus={event => targets.push(event.currentTarget)} onBlur={event => targets.push(event.currentTarget)} onKeyDown={key} />)
    const input = screen.getByRole('combobox')
    expect(ref.current).toBe(input)
    expect(ref.current).toBeInstanceOf(HTMLInputElement)
    fireEvent.focus(input)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    expect(key).toHaveBeenCalledOnce()
    expect(input).not.toHaveAttribute('aria-activedescendant')
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(changed).not.toHaveBeenCalled()
    fireEvent.keyDown(input, { key: 'ArrowUp' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(changed.mock.calls).toEqual([['pending']])
    fireEvent.blur(input)
    expect(targets.length).toBeGreaterThanOrEqual(6)
    expect(targets.every(target => target === input)).toBe(true)
  })

  it.each(['single', 'multiple', 'searchable-multiple'] as const)('keeps %s refs and host events on the button', mode => {
    const ref = React.createRef<HTMLButtonElement>()
    const targets: Element[] = []
    const events = {
      onFocus: (event: React.FocusEvent<HTMLButtonElement>) => { targets.push(event.currentTarget) },
      onBlur: (event: React.FocusEvent<HTMLButtonElement>) => { targets.push(event.currentTarget) },
      onKeyDown: (event: React.KeyboardEvent<HTMLButtonElement>) => { targets.push(event.currentTarget); event.preventDefault() },
    }
    render(mode === 'single'
      ? <SelectField {...events} ref={ref} name="status" value="active" options={options} onValueChange={() => {}} />
      : <SelectField {...events} ref={ref} multiple searchable={mode === 'searchable-multiple'} name="status" value={['active']} options={options} onValueChange={() => {}} />)
    const trigger = screen.getByRole(mode === 'single' ? 'combobox' : 'button')
    expect(ref.current).toBe(trigger)
    expect(ref.current).toBeInstanceOf(HTMLButtonElement)
    fireEvent.focus(trigger)
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(screen.queryByRole('listbox')).toBeNull()
    fireEvent.blur(trigger)
    expect(targets).toEqual([trigger, trigger, trigger])
  })

  it.each([false, true])('multiple search=%s opens on a selected enabled member without toggling or filtering by selection', searchable => {
    const changed = vi.fn()
    const { rerender } = render(<SelectField multiple searchable={searchable} name="status" value={['blocked', 'pending']} options={options} onValueChange={changed} />)
    const trigger = screen.getByRole('button')
    fireEvent.click(trigger)
    const list = screen.getByRole('listbox')
    expect(screen.getAllByRole('option')).toHaveLength(3)
    expect(list).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Pending' }).id)
    if (searchable) {
      expect(screen.getByRole('searchbox')).toHaveValue('')
      fireEvent.keyDown(screen.getByRole('searchbox'), { key: 'ArrowDown' })
      expect(list).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Pending' }).id)
    }
    expect(list).toHaveFocus()
    expect(changed).not.toHaveBeenCalled()
    fireEvent.keyDown(list, { key: 'Escape' })
    rerender(<SelectField multiple searchable={searchable} name="status" value={['blocked', 'unknown']} options={options} onValueChange={changed} />)
    fireEvent.click(trigger)
    expect(screen.getByRole('listbox')).toHaveAttribute('aria-activedescendant', screen.getByRole('option', { name: 'Active' }).id)
    expect(changed).not.toHaveBeenCalled()
  })

  it.each([false, true])('multiple search=%s keeps required/read-only on its listbox and preserves trigger metadata', searchable => {
    render(<SelectField multiple searchable={searchable} open required readOnly error name="status" accessibleName="Status"
      aria-describedby="hint error" value={['pending']} options={options} onValueChange={() => {}} />)
    const trigger = screen.getByRole('button', { name: 'Status' })
    const list = screen.getByRole('listbox', { name: 'Status' })
    expect(trigger).not.toHaveAttribute('aria-required')
    expect(trigger).not.toHaveAttribute('aria-readonly')
    expect(trigger).toHaveAttribute('aria-describedby', 'hint error')
    expect(trigger).toHaveAttribute('aria-invalid', 'true')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(trigger).toHaveAttribute('aria-controls', list.id)
    expect(list).toHaveAttribute('aria-required', 'true')
    expect(list).toHaveAttribute('aria-readonly', 'true')
  })

  it('external popup dismissal discards the draft before reopening', () => {
    const changed = vi.fn()
    const view = (open: boolean) => <SelectField searchable open={open} name="status" value="active" options={options} onValueChange={changed} />
    const { rerender } = render(view(true))
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'pen' } })
    rerender(view(false))
    rerender(view(true))
    expect(screen.getByRole('combobox')).toHaveValue('Active')
    expect(changed).not.toHaveBeenCalled()
  })
  it('filters 40,000 labels literally with a bounded DOM and refuses empty or inactive Enter', () => {
    const changed = vi.fn()
    const many = Array.from({ length: 40000 }, (_, index) => ({ value: String(index), label: 'Member ' + String(index).padStart(5, '0') }))
    render(<SelectField searchable name="members" accessibleName="Members" value="" options={many} onValueChange={changed} />)
    const input = screen.getByRole('combobox')
    fireEvent.focus(input)
    expect(screen.getAllByRole('option')).toHaveLength(25)
    fireEvent.change(input, { target: { value: 'MEMBER 3999' } })
    expect(screen.getAllByRole('option')).toHaveLength(10)
    expect(fireEvent.keyDown(input, { key: 'Enter' })).toBe(false)
    fireEvent.change(input, { target: { value: '.*' } })
    expect(screen.queryAllByRole('option')).toHaveLength(0)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(changed).not.toHaveBeenCalled()
  })

  it('preserves editing and IME, dismisses drafts and cancels active membership on external updates', async () => {
    const changed = vi.fn()
    const user = userEvent.setup()
    const view = (items = options, value = 'active') => <><SelectField searchable name="status" accessibleName="Status" value={value} options={items} onValueChange={changed} /><button>Outside</button></>
    const { rerender } = render(view())
    const input = screen.getByRole('combobox')
    await user.click(input)
    fireEvent.change(input, { target: { value: 'pen' } })
    for (const key of ['Home', 'End', ' ', 'ArrowLeft', 'ArrowRight']) expect(fireEvent.keyDown(input, { key })).toBe(true)
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.compositionStart(input)
    fireEvent.keyDown(input, { key: 'Enter', isComposing: true })
    fireEvent.compositionEnd(input)
    expect(changed).not.toHaveBeenCalled()
    fireEvent.keyDown(input, { key: 'Escape' })
    expect(input).toHaveValue('Active')
    expect(input).toHaveFocus()
    expect(screen.queryByRole('listbox')).toBeNull()
    fireEvent.change(input, { target: { value: 'pen' } })
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    rerender(view([{ value: 'active', label: 'Active' }]))
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(input).toHaveValue('Active')
    expect(changed).not.toHaveBeenCalled()
    rerender(view(options, 'pending'))
    expect(input).toHaveValue('Pending')
    fireEvent.change(input, { target: { value: 'act' } })
    await user.tab()
    expect(screen.getByRole('button', { name: 'Outside' })).toHaveFocus()
    expect(input).toHaveValue('Pending')
    await user.click(input)
    fireEvent.change(input, { target: { value: 'act' } })
    await user.click(screen.getByRole('button', { name: 'Outside' }))
    expect(screen.queryByRole('listbox')).toBeNull()
    expect(input).toHaveValue('Pending')
  })

  it.each([false, true])('multiple search=%s uses a button and named listbox, toggles once without mutating and leaves Tab free', async searchable => {
    const changed = vi.fn()
    const values = Object.freeze(['unknown', 'active'])
    const user = userEvent.setup()
    render(<><SelectField multiple searchable={searchable} name="status" accessibleName="Status" value={values} options={options} onValueChange={changed} /><button>Outside</button></>)
    const trigger = screen.getByRole('button', { name: 'Status' })
    expect(screen.queryByRole('combobox')).toBeNull()
    await user.click(trigger)
    const list = screen.getByRole('listbox', { name: 'Status' })
    expect(list).toHaveAttribute('aria-multiselectable', 'true')
    expect(trigger).toHaveAttribute('aria-haspopup', 'listbox')
    if (searchable) {
      const search = screen.getByRole('searchbox')
      expect(list).not.toContainElement(search)
      expect(search).toHaveFocus()
      expect(screen.getAllByRole('option')).toHaveLength(3)
      fireEvent.change(search, { target: { value: 'pen' } })
      expect(fireEvent.keyDown(search, { key: 'Enter' })).toBe(false)
      expect(changed).not.toHaveBeenCalled()
      fireEvent.keyDown(search, { key: 'ArrowDown' })
      expect(list).toHaveFocus()
    } else {
      expect(list).toHaveFocus()
      fireEvent.keyDown(list, { key: 'End' })
    }
    fireEvent.keyDown(list, { key: ' ' })
    expect(changed.mock.calls).toEqual([[['unknown', 'active', 'pending']]])
    expect(values).toEqual(['unknown', 'active'])
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Pending' })).toHaveAttribute('aria-selected', 'false')
    fireEvent.keyDown(list, { key: 'Escape' })
    expect(trigger).toHaveFocus()
    await user.click(trigger)
    await user.tab()
    expect(screen.getByRole('button', { name: 'Outside' })).toHaveFocus()
  })

  it('multiple toggles remove the last selection, retain unknown identities and refuse disabled and read-only choices', () => {
    const changed = vi.fn()
    const { rerender } = render(<SelectField multiple open name="status" accessibleName="Status" value={['active']} options={options} onValueChange={changed} />)
    fireEvent.click(screen.getByRole('option', { name: 'Blocked' }))
    expect(changed).not.toHaveBeenCalled()
    fireEvent.click(screen.getByRole('option', { name: /Active/ }))
    expect(changed.mock.calls).toEqual([[[]]])
    rerender(<SelectField multiple readOnly open name="status" accessibleName="Status" value={[]} options={options} onValueChange={changed} />)
    fireEvent.click(screen.getByRole('option', { name: 'Pending' }))
    expect(changed).toHaveBeenCalledTimes(1)
  })

  it('rejects invalid bounds and duplicate controlled values', () => {
    for (const maxVisibleOptions of [0, -1, 1.5, Infinity, NaN]) {
      expect(() => render(<SelectField searchable name="status" value="" options={options} maxVisibleOptions={maxVisibleOptions} onValueChange={() => {}} />)).toThrow('invalid-select-option-limit')
    }
    expect(() => render(<SelectField multiple name="status" value={['active', 'active']} options={options} onValueChange={() => {}} />)).toThrow('duplicate-selected-value')
  })
  it('keeps an editable search draft separate from controlled selection and commits only a current match', () => {
    const changed = vi.fn()
    render(<SelectField searchable accessibleName="Status" name="status" value="active" options={options} onValueChange={changed} />)
    const input = screen.getByRole('combobox')
    expect(input.tagName).toBe('INPUT')
    fireEvent.change(input, { target: { value: 'PEN' } })
    expect(screen.getAllByRole('option')).toHaveLength(1)
    expect(changed).not.toHaveBeenCalled()
    fireEvent.keyDown(input, { key: 'ArrowDown' })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(changed.mock.calls).toEqual([['pending']])
    expect(input).toHaveValue('Active')
  })
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
