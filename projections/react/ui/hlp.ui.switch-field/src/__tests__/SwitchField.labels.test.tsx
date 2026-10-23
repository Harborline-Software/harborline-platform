import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { SwitchField } from '../SwitchField'

// Contract: the switch always has a nonempty accessible name, and its on and off state copy follows
// instance, catalog, then fallback precedence.
const French = { 'forms.switch.on': 'Activé', 'forms.switch.off': 'Désactivé' }
const Arabic = { 'forms.switch.on': 'تشغيل', 'forms.switch.off': 'إيقاف' }
const state = () => document.querySelector('.hl-switch__state')!.textContent ?? ''
const field = (props: Partial<React.ComponentProps<typeof SwitchField>> = {}) => <SwitchField checked={false} label="Published" name="published" onCheckedChange={() => undefined} {...props}/>

function accessibleName() {
  const control = screen.getByRole('switch')
  const labelled = control.getAttribute('aria-labelledby')?.split(' ').map(id => document.getElementById(id)?.textContent ?? '').join(' ')
  const name = (control.getAttribute('aria-label') ?? labelled ?? '').trim()
  expect(name).not.toBe('')
  return name
}

describe('SwitchField names and state copy', () => {
  it('uses the fallback state copy without a provider and a nonempty name from the label', () => {
    render(field())
    expect(accessibleName()).toBe('Published')
    expect(state()).toBe('Off')
  })

  it('uses the host catalog, and caller text overrides it', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{field({ checked: true })}</HarborlineLocaleProvider>)
    expect(state()).toBe('Activé')
    view.rerender(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{field({ checked: true, onText: 'Oui' })}</HarborlineLocaleProvider>)
    expect(state()).toBe('Oui')
  })

  it('follows a locale and catalog change', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{field()}</HarborlineLocaleProvider>)
    expect(state()).toBe('Désactivé')
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{field()}</HarborlineLocaleProvider>)
    expect(state()).toBe('إيقاف')
    expect(screen.getByRole('switch').closest('.hl-switch')).toHaveAttribute('dir', 'rtl')
    expect(accessibleName()).toBe('Published')
  })

  it('uses a pseudo-localized catalog under en-XA without truncation', () => {
    const pseudo = '⟦ Öƒƒ ··· ⟧'
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'forms.switch.off': pseudo }}>{field()}</HarborlineLocaleProvider>)
    expect(state()).toBe(pseudo)
  })

  it('never renders blank state copy for blank caller text or a blank catalog value', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{field({ offText: '  ' })}</HarborlineLocaleProvider>)
    expect(state()).toBe('Désactivé')
    view.rerender(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'forms.switch.off': ' ' }}>{field()}</HarborlineLocaleProvider>)
    expect(state()).toBe('Off')
  })

  it('rejects a blank accessible name or a blank label rather than rendering an unnamed switch', () => {
    expect(() => render(<SwitchField aria-label="   " checked={false} name="published" onCheckedChange={() => undefined}/>)).toThrow('accessible-switch-label-required')
    expect(() => render(<SwitchField checked={false} label="  " name="published" onCheckedChange={() => undefined}/>)).toThrow('accessible-switch-label-required')
  })

  it('falls through a blank name or an empty labelledby to the label and the field label', () => {
    const view = render(<SwitchField aria-labelledby="" checked={false} label="Published" name="published" onCheckedChange={() => undefined}/>)
    expect(accessibleName()).toBe('Published')
    view.unmount()
    render(
      <FormFieldContext.Provider value={{ id: 'ambient', labelId: 'field-label', describedBy: undefined, required: false, disabled: false }}>
        <span id="field-label">Enabled</span>
        <SwitchField aria-label="  " checked={false} name="enabled" onCheckedChange={() => undefined}/>
      </FormFieldContext.Provider>,
    )
    expect(accessibleName()).toBe('Enabled')
  })
})
