import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { UserMenu } from '../UserMenu'

// Proves the default copy is read from the default catalog, not an English literal: the catalog's value
// is replaced here, and the component must show the replacement with no provider and no caller text, and
// also when the host catalog's value is blank (the component's own fallback, not the provider's).
vi.mock('@harborline-platform/hlp.ui.default-strings', async original => {
  const actual = await original<typeof import('@harborline-platform/hlp.ui.default-strings')>()
  return { ...actual, defaultStrings: { ...actual.defaultStrings, 'navigation.userMenu.signOut': 'Catalog sign out', 'navigation.userMenu.trigger': 'Catalog menu for {name}' } }
})

describe('UserMenu default copy comes from the default catalog', () => {
  it('shows the default catalog value with no provider', () => {
    render(<UserMenu identity={{ name: 'Avery' }} onSignOut={() => undefined}/>)
    expect(document.querySelector('.hl-user-menu__trigger')!.getAttribute('aria-label')).toBe('Catalog menu for Avery')
    fireEvent.click(document.querySelector('.hl-user-menu__trigger')!)
    expect(document.querySelector('.hl-user-menu__sign-out')!.textContent).toBe('Catalog sign out')
  })

  it('falls back to the default catalog value when the host value is blank', () => {
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'navigation.userMenu.signOut': ' ', 'navigation.userMenu.trigger': '' }}><UserMenu identity={{ name: 'Avery' }} onSignOut={() => undefined}/></HarborlineLocaleProvider>)
    expect(document.querySelector('.hl-user-menu__trigger')!.getAttribute('aria-label')).toBe('Catalog menu for Avery')
    fireEvent.click(document.querySelector('.hl-user-menu__trigger')!)
    expect(document.querySelector('.hl-user-menu__sign-out')!.textContent).toBe('Catalog sign out')
  })
})
