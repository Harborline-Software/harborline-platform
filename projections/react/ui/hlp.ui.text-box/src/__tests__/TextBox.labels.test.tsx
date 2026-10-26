import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { TextBox } from '../TextBox'

// Contract: clear and reveal labels follow instance, catalog, then fallback precedence, and every
// action has a nonempty accessible name.
const French = { 'common.clear': 'Effacer', 'forms.textBox.showPassword': 'Afficher le mot de passe', 'forms.textBox.hidePassword': 'Masquer le mot de passe' }
const Arabic = { 'common.clear': 'امسح', 'forms.textBox.showPassword': 'اعرض كلمة المرور', 'forms.textBox.hidePassword': 'أخفِ كلمة المرور' }

function names() {
  const clear = document.querySelector('[data-hl-action="clear"]')?.getAttribute('aria-label') ?? ''
  const reveal = document.querySelector('[data-hl-action="reveal"]')?.getAttribute('aria-label') ?? ''
  expect(clear.trim()).not.toBe('')
  expect(reveal.trim()).not.toBe('')
  return { clear, reveal }
}
const box = (props: Partial<React.ComponentProps<typeof TextBox>> = {}) => <TextBox aria-label="Secret" clearButton showReveal type="password" defaultValue="hunter2" {...props}/>

describe('TextBox action labels', () => {
  it('uses the default catalog without a provider or caller text, and the hide label once revealed', () => {
    render(box())
    expect(names()).toEqual({ clear: 'Clear', reveal: 'Show password' })
    fireEvent.click(document.querySelector('[data-hl-action="reveal"]')!)
    expect(names().reveal).toBe('Hide password')
  })

  it('uses the host catalog, and caller text overrides it', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{box()}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ clear: 'Effacer', reveal: 'Afficher le mot de passe' })
    view.rerender(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{box({ clearLabel: 'Vider', showPasswordLabel: 'Voir' })}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ clear: 'Vider', reveal: 'Voir' })
  })

  it('follows a locale and catalog change', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{box()}</HarborlineLocaleProvider>)
    expect(names().clear).toBe('Effacer')
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{box()}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ clear: 'امسح', reveal: 'اعرض كلمة المرور' })
    expect(document.querySelector('input')).toHaveAttribute('dir', 'rtl')
  })

  it('uses a pseudo-localized catalog under en-XA without truncation', () => {
    const pseudo = '⟦ Çļëåŗ ··· ⟧'
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'common.clear': pseudo }}>{box()}</HarborlineLocaleProvider>)
    expect(names().clear).toBe(pseudo)
  })

  it('never renders an empty name for blank caller text or a blank catalog value', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{box({ clearLabel: '', showPasswordLabel: '   ' })}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ clear: 'Effacer', reveal: 'Afficher le mot de passe' })
    view.rerender(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.clear': '  ' }}>{box()}</HarborlineLocaleProvider>)
    expect(names().clear).toBe('Clear')
  })
})

describe('TextBox caller name', () => {
  // A blank caller name is no name (WCAG 4.1.2): the FormField label still names the input, as Switch does.
  const field = (props: Partial<React.ComponentProps<typeof TextBox>>) => (
    <FormFieldContext.Provider value={{ id: 'email', labelId: 'email-label' }}><span id="email-label">Email</span><TextBox {...props}/></FormFieldContext.Provider>
  )
  it.each([' ', '\t\n', ''])('ignores a blank aria-label (%j) so the FormField label names the input', name => {
    render(field({ 'aria-label': name }))
    const input = document.querySelector('input')!
    expect([input.getAttribute('aria-label'), input.getAttribute('aria-labelledby')]).toEqual([null, 'email-label'])
    expect(input).toHaveAccessibleName('Email')
  })
  it('keeps a nonblank aria-label exactly, over the FormField label', () => {
    render(field({ 'aria-label': ' Work email ' }))
    const input = document.querySelector('input')!
    expect([input.getAttribute('aria-label'), input.getAttribute('aria-labelledby')]).toEqual([' Work email ', null])
  })
  it('lets an explicit aria-labelledby win over a blank aria-label', () => {
    render(<><span id="own">Own</span>{field({ 'aria-label': ' ', 'aria-labelledby': 'own' })}</>)
    expect(document.querySelector('input')).toHaveAccessibleName('Own')
  })
})
