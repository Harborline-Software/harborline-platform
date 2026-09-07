import * as React from 'react'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { Switch } from '../Switch'

function UncontrolledSwitch({ changed }: { changed: (checked: boolean) => void }) {
  return <Switch accessibleName="Email alerts" defaultChecked={false} onCheckedChange={changed} />
}

describe('Switch React projection', () => {
  it('supports uncontrolled pointer changes and keeps its accessible name stable', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<UncontrolledSwitch changed={changed} />)
    const control = screen.getByRole('switch', { name: 'Email alerts' })
    expect(control).toHaveAttribute('aria-checked', 'false')
    await user.click(control)
    expect(control).toHaveAttribute('aria-checked', 'true')
    expect(control).toHaveAccessibleName('Email alerts')
    await user.click(control)
    expect(changed.mock.calls).toEqual([[true], [false]])
  })

  it('uses native button activation so Space and Enter each request exactly once', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<Switch accessibleName="Email alerts" checked={false} onCheckedChange={changed} />)
    const control = screen.getByRole('switch')
    control.focus()
    await user.keyboard('[Space]')
    await user.keyboard('[Enter]')
    expect(changed.mock.calls).toEqual([[true], [true]])
  })

  it('associates a visible label, keeps state copy separate, and describes the switch', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<Switch description="Notify every owner" label="Alerts" offText="Disabled" onCheckedChange={changed} onText="Enabled" />)
    const control = screen.getByRole('switch', { name: 'Alerts' })
    expect(screen.getByText('Disabled')).toHaveAttribute('aria-hidden', 'true')
    expect(control).toHaveAccessibleDescription('Notify every owner')
    await user.click(screen.getByText('Alerts'))
    expect(changed).toHaveBeenCalledOnce()
    expect(screen.getByText('Enabled')).toBeInTheDocument()
    expect(control).toHaveAccessibleName('Alerts')
  })

  it('applies ordered FormField metadata with local precedence', () => {
    render(
      <FormFieldContext.Provider value={{ id: 'ambient-switch', labelId: 'ambient-label', describedBy: 'hint error', required: true, disabled: true }}>
        <Switch aria-label="Alerts" description="Notify owners" disabled={false} error required={false} />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByRole('switch')
    expect(control).toHaveAttribute('id', 'ambient-switch')
    expect(control).not.toBeDisabled()
    expect(control).not.toHaveAttribute('aria-required')
    expect(control).toHaveAttribute('aria-invalid', 'true')
    expect(control).toHaveAttribute('aria-describedby', 'hint error ambient-switch-description')
    expect(screen.getByText('!')).toHaveAttribute('aria-hidden', 'true')
  })

  it('participates in forms with exact on/off values and normalizes sizes', () => {
    const { rerender } = render(<Switch accessibleName="Alerts" checked name="alerts" size="small" />)
    const control = screen.getByRole('switch')
    expect(control.parentElement).toHaveAttribute('data-hl-size', 'sm')
    expect(document.querySelector('input[name="alerts"]')).toHaveValue('on')
    rerender(<Switch accessibleName="Alerts" checked={false} name="alerts" size="large" />)
    expect(control.parentElement).toHaveAttribute('data-hl-size', 'lg')
    expect(document.querySelector('input[name="alerts"]')).toHaveValue('off')
  })

  it('native-disables all activation and exposes touch and RTL state', () => {
    const changed = vi.fn()
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'forms.switch.on': 'مفعّل', 'forms.switch.off': 'متوقف' }}>
        <Switch accessibleName="التنبيهات" disabled onCheckedChange={changed} />
      </HarborlineLocaleProvider>,
    )
    const control = screen.getByRole('switch')
    expect(control).toBeDisabled()
    expect(control.parentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByText('متوقف')).toBeInTheDocument()
    fireEvent.click(control)
    fireEvent.keyDown(control, { key: 'Enter' })
    expect(changed).not.toHaveBeenCalled()
  })

  it('rejects unsupported sizes and unnamed switches', () => {
    expect(() => render(<Switch accessibleName="Alerts" size={'xl' as never} />)).toThrow('unsupported-switch-size')
    expect(() => render(<Switch />)).toThrow('accessible-switch-label-required')
  })
})
