import { createRef, useState } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormField } from '../../../hlp.ui.form-field/src/FormField'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { RadioGroup, type RadioOption } from '../RadioGroup'
import { qualityCases } from './fixtures'

const options: RadioOption[] = [
  { value: 'monthly', label: 'Monthly' },
  { value: 'quarterly', label: 'Quarterly' },
  { value: 'annual', label: 'Annual', description: 'Billed once a year' },
]

describe('RadioGroup React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'radio-group.quality.native',
      'radio-group.quality.name',
      'radio-group.quality.description',
      'radio-group.quality.keyboard',
      'radio-group.quality.focus',
      'radio-group.quality.reflow',
      'radio-group.quality.caller-copy',
      'radio-group.quality.rtl',
      'radio-group.quality.pseudo',
      'radio-group.quality.light-dark',
      'radio-group.quality.tokens',
      'radio-group.quality.forced-colors',
      'radio-group.quality.reduced-motion',
      'radio-group.quality.visual-parity',
    ])
  })

  it('uses native radio keyboard behavior through controlled state', async () => {
    function Harness() {
      const [value, setValue] = useState('monthly')
      return <RadioGroup aria-label="Billing" name="billing" onChange={setValue} options={options} value={value} />
    }
    render(<Harness />)
    const monthly = screen.getByRole('radio', { name: 'Monthly' })
    monthly.focus()
    await userEvent.setup().keyboard('{ArrowDown}')
    expect(screen.getByRole('radio', { name: 'Quarterly' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Quarterly' })).toHaveFocus()
  })

  it('combines local and FormField required and disabled metadata', () => {
    const { rerender } = render(
      <FormFieldContext.Provider value={{ required: true, disabled: true }}>
        <RadioGroup aria-label="Billing" name="billing" onChange={vi.fn()} options={options} value="monthly" />
      </FormFieldContext.Provider>,
    )
    const group = screen.getByRole('radiogroup')
    expect(group).toHaveAttribute('aria-required', 'true')
    expect(group).toHaveAttribute('aria-disabled', 'true')
    screen.getAllByRole('radio').forEach(radio => {
      expect(radio).toBeRequired()
      expect(radio).toBeDisabled()
    })

    rerender(
      <FormFieldContext.Provider value={{ required: false, disabled: false }}>
        <RadioGroup aria-label="Billing" disabled name="billing" onChange={vi.fn()} options={options} required value="monthly" />
      </FormFieldContext.Provider>,
    )
    expect(group).toHaveAttribute('aria-required', 'true')
    expect(group).toHaveAttribute('aria-disabled', 'true')
  })

  it('integrates with the public FormField label and descriptions', () => {
    render(
      <FormField error="Choose one" hint="Select a billing interval" label="Billing cycle" name="billing" required>
        <RadioGroup name="billing" onChange={vi.fn()} options={options} value="" />
      </FormField>,
    )
    const group = screen.getByRole('radiogroup', { name: 'Billing cycle' })
    expect(group).toHaveAttribute('aria-describedby', 'billing-hint billing-error')
    expect(group).toHaveAttribute('aria-required', 'true')
    expect(screen.getAllByRole('radio')[0]).toHaveAttribute('id', 'billing')
  })

  it('forwards root host attributes and ref', () => {
    const ref = createRef<HTMLDivElement>()
    render(
      <RadioGroup
        aria-label="Billing"
        className="consumer"
        data-test="x"
        dir="rtl"
        lang="ar"
        name="billing"
        onChange={vi.fn()}
        options={options}
        ref={ref}
        value=""
      />,
    )
    expect(ref.current).toBe(screen.getByRole('radiogroup'))
    expect(ref.current).toHaveClass('consumer')
    expect(ref.current).toHaveAttribute('data-test', 'x')
    expect(ref.current).toHaveAttribute('dir', 'rtl')
    expect(ref.current).toHaveAttribute('lang', 'ar')
  })

  it('publishes logical, token, theme, forced-color, reflow, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-radio-group-foreground')
    expect(css).toContain('margin-inline-start')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('flex-flow: row wrap')
    expect(css).toContain("[data-theme='dark'] .hl-radio-group")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/transition:\s*none/)
  })
})
