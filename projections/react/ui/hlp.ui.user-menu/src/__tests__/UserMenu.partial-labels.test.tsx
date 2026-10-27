import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { UserMenu } from '../UserMenu'

// Contract (interface revision 3, G6), React twin of the Blazor UserMenuPartialLabelsTests: each label the caller
// omits, or supplies blank, comes from the catalog (host, then default); every other label is kept exactly.
const French = { 'navigation.userMenu.trigger': 'Menu du compte de {name}', 'navigation.userMenu.panel': 'Menu du compte', 'navigation.userMenu.signOut': 'Se déconnecter' }
const Arabic = { 'navigation.userMenu.trigger': 'قائمة حساب {name}', 'navigation.userMenu.panel': 'قائمة الحساب', 'navigation.userMenu.signOut': 'تسجيل الخروج' }
type Labels = React.ComponentProps<typeof UserMenu>['labels']
const localized = (locale: string, catalog: Record<string, string>, labels?: Labels) =>
  <HarborlineLocaleProvider locale={locale} catalog={catalog}><UserMenu identity={{ name: 'Avery' }} labels={labels} onSignOut={() => undefined}/></HarborlineLocaleProvider>
const trigger = () => document.querySelector<HTMLButtonElement>('.hl-user-menu__trigger')!
// Sign-out text is read untrimmed.
function names() {
  fireEvent.click(trigger())
  const result = [trigger().getAttribute('aria-label'), document.querySelector('.hl-user-menu__surface')!.getAttribute('aria-label'), document.querySelector('.hl-user-menu__sign-out')!.textContent]
  fireEvent.click(trigger())
  return result
}

describe('UserMenu partial label overrides', () => {
  it('keeps the supplied field and takes the rest from the Arabic catalog', () => {
    const view = render(localized('ar-SA', Arabic, { signOut: 'Leave' }))
    expect(names()).toEqual(['قائمة حساب Avery', 'قائمة الحساب', 'Leave'])
    view.rerender(localized('ar-SA', Arabic, { trigger: name => `Who is ${name}?` }))
    expect(names()).toEqual(['Who is Avery?', 'قائمة الحساب', 'تسجيل الخروج'])
  })

  it('falls back to the catalog for an omitted or blank field', () => {
    const view = render(localized('ar-SA', Arabic, { trigger: name => `Account of ${name}`, signOut: ' ' }))
    expect(names()).toEqual(['Account of Avery', 'قائمة الحساب', 'تسجيل الخروج'])
    view.rerender(localized('ar-SA', Arabic, { trigger: () => '\t', panel: '', signOut: 'Leave' }))
    expect(names()).toEqual(['قائمة حساب Avery', 'قائمة الحساب', 'Leave'])
    view.rerender(localized('ar-SA', Arabic, {}))
    expect(names()).toEqual(['قائمة حساب Avery', 'قائمة الحساب', 'تسجيل الخروج'])
  })

  it('updates the fallback fields on a locale change and keeps the explicit one', () => {
    const labels = { signOut: 'Leave' }
    const view = render(localized('fr-FR', French, labels))
    expect(names()).toEqual(['Menu du compte de Avery', 'Menu du compte', 'Leave'])
    view.rerender(localized('ar-SA', Arabic, labels))
    expect(names()).toEqual(['قائمة حساب Avery', 'قائمة الحساب', 'Leave'])
  })

  it('keeps a fully specified English labels object in English under an Arabic catalog', () => {
    render(localized('ar-SA', Arabic, { trigger: name => `Account menu for ${name}`, panel: 'Account menu', signOut: 'Sign out' }))
    expect(names()).toEqual(['Account menu for Avery', 'Account menu', 'Sign out'])
  })
})
