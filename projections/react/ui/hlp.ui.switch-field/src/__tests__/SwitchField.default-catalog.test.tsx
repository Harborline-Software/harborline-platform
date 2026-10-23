import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SwitchField } from '../SwitchField'

// Proves the default copy is read from the default catalog, not an English literal: the catalog's value
// is replaced here, and the component must show the replacement with no provider and no caller text, and
// also when the host catalog's value is blank (the component's own fallback, not the provider's).
vi.mock('@harborline-platform/hlp.ui.default-strings', async original => {
  const actual = await original<typeof import('@harborline-platform/hlp.ui.default-strings')>()
  return { ...actual, defaultStrings: { ...actual.defaultStrings, 'forms.switch.on': 'Catalog on', 'forms.switch.off': 'Catalog off' } }
})

describe('Switch default copy comes from the default catalog', () => {
  it('shows the default catalog value with no provider', () => {
    const view = render(<SwitchField checked label="Published" name="published" onCheckedChange={() => undefined}/>)
    expect(document.querySelector('.hl-switch__state')!.textContent).toBe('Catalog on')
    view.rerender(<SwitchField checked={false} label="Published" name="published" onCheckedChange={() => undefined}/>)
    expect(document.querySelector('.hl-switch__state')!.textContent).toBe('Catalog off')
  })

  it('falls back to the default catalog value when the host value is blank', () => {
    const host = (checked: boolean) => <HarborlineLocaleProvider locale="fr-FR" catalog={{ 'forms.switch.on': ' ', 'forms.switch.off': '' }}><SwitchField checked={checked} label="Published" name="published" onCheckedChange={() => undefined}/></HarborlineLocaleProvider>
    const view = render(host(true))
    expect(document.querySelector('.hl-switch__state')!.textContent).toBe('Catalog on')
    view.rerender(host(false))
    expect(document.querySelector('.hl-switch__state')!.textContent).toBe('Catalog off')
  })
})
