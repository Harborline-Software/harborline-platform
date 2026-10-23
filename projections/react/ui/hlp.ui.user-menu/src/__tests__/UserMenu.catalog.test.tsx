import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { defaultStrings } from '@harborline-platform/hlp.ui.default-strings'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { UserMenu } from '../UserMenu'

// Contract: the trigger, panel and sign-out copy comes from the catalog (navigation.userMenu.*), caller
// text overrides it, and no name is ever empty.
const French = { 'navigation.userMenu.trigger': 'Menu du compte de {name}', 'navigation.userMenu.panel': 'Menu du compte', 'navigation.userMenu.signOut': 'Se déconnecter' }
const Arabic = { 'navigation.userMenu.trigger': 'قائمة حساب {name}', 'navigation.userMenu.panel': 'قائمة الحساب', 'navigation.userMenu.signOut': 'تسجيل الخروج' }
const menu = (labels?: React.ComponentProps<typeof UserMenu>['labels']) => <UserMenu identity={{ name: 'Avery' }} labels={labels} onSignOut={() => undefined}/>
const trigger = () => document.querySelector<HTMLButtonElement>('.hl-user-menu__trigger')!
function names() {
  fireEvent.click(trigger())
  const result = { trigger: trigger().getAttribute('aria-label') ?? '', panel: document.querySelector('.hl-user-menu__surface')!.getAttribute('aria-label') ?? '', signOut: document.querySelector('.hl-user-menu__sign-out')!.textContent ?? '' }
  for (const value of Object.values(result)) expect(value.trim()).not.toBe('')
  fireEvent.click(trigger())
  return result
}

describe('UserMenu catalog-backed copy', () => {
  it('takes its default copy from the default catalog without a provider', () => {
    render(menu())
    expect(names()).toEqual({ trigger: defaultStrings['navigation.userMenu.trigger'].replace('{name}', 'Avery'), panel: defaultStrings['navigation.userMenu.panel'], signOut: defaultStrings['navigation.userMenu.signOut'] })
  })

  it('uses the host catalog, follows a French to Arabic change, and caller text overrides it', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{menu()}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ trigger: 'Menu du compte de Avery', panel: 'Menu du compte', signOut: 'Se déconnecter' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{menu()}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ trigger: 'قائمة حساب Avery', panel: 'قائمة الحساب', signOut: 'تسجيل الخروج' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{menu({ signOut: 'Leave' })}</HarborlineLocaleProvider>)
    expect(names().signOut).toBe('Leave')
  })

  it('uses a pseudo-localized catalog under en-XA', () => {
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'navigation.userMenu.signOut': '⟦ Šîĝñ øûţ ··· ⟧' }}>{menu()}</HarborlineLocaleProvider>)
    expect(names().signOut).toBe('⟦ Šîĝñ øûţ ··· ⟧')
  })

  it('never renders an empty name for blank caller text or a blank catalog value', () => {
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'navigation.userMenu.panel': '  ' }}>{menu({ signOut: ' ', trigger: () => '' })}</HarborlineLocaleProvider>)
    expect(names()).toEqual({ trigger: 'Account menu for Avery', panel: 'Account menu', signOut: 'Sign out' })
  })
})
