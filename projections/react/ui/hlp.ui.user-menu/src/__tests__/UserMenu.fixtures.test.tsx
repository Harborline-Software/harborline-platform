import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { UserMenu, userInitials, type UserMenuItem } from '../UserMenu'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// projection-equivalence compares lanes. rtl-touch asserts the touch marker and direction; the
// 44 CSS-pixel minimum itself is stylesheet behaviour left to the gallery's reflow checks.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['user-menu.projection-equivalence']
// The trigger, panel and sign-out names, read with the surface open, then closed again.
function menuNames() {
  const trigger = document.querySelector<HTMLButtonElement>('.hl-user-menu__trigger')!
  fireEvent.click(trigger)
  const names = { trigger: trigger.getAttribute('aria-label'), panel: document.querySelector('.hl-user-menu__surface')!.getAttribute('aria-label'), signOut: document.querySelector('.hl-user-menu__sign-out')!.textContent }
  fireEvent.click(trigger)
  return names
}
const ada = { name: 'Ada Lovelace' }
const trigger = () => document.querySelector<HTMLButtonElement>('.hl-user-menu__trigger')!
const action = (id: string, label: string, onActivate = vi.fn(), extra: Partial<UserMenuItem> = {}) => ({ id, kind: 'action', label, onActivate, ...extra }) as UserMenuItem

describe('UserMenu shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'user-menu.identity': ({ input, expected }) => {
    render(<UserMenu identity={input as { name: string }} defaultOpen items={[action('profile', 'Profile')]}/>)
    const header = document.querySelector('.hl-user-menu__identity')!
    expect([input.name, input.email, input.role].every(value => header.textContent!.includes(value))).toBe(expected.allIdentityFieldsPreserved)
    expect(trigger().getAttribute('aria-label')).toContain(expected.triggerNameIncludes)
  },
  'user-menu.initials': ({ input, expected }) => {
    expect(input.names.map((name: string) => userInitials(name))).toEqual(expected.initials)
  },
  'user-menu.avatar': ({ input, expected }) => {
    render(<UserMenu identity={{ name: 'Ada Lovelace', avatarUri: input.avatarUri }}/>)
    const name = trigger().getAttribute('aria-label')
    const image = document.querySelector('.hl-user-menu__avatar img')
    expect(image !== null).toBe(expected.initiallyImage)
    fireEvent.error(image!)
    expect(document.querySelector('.hl-user-menu__avatar img') === null && document.querySelector('.hl-user-menu__avatar')!.textContent === 'AL').toBe(expected.fallbackInitials)
    expect(trigger().getAttribute('aria-label') === name).toBe(expected.triggerNameStable)
  },
  'user-menu.closed': ({ input, expected }) => {
    render(<UserMenu identity={ada} open={input.open} items={[action('profile', 'Profile')]}/>)
    expect(trigger().getAttribute('aria-expanded') === 'true').toBe(expected.triggerExpanded)
    expect(document.querySelector('.hl-user-menu__surface') !== null).toBe(expected.surfaceMounted)
  },
  'user-menu.toggle': ({ input, expected }) => {
    render(<UserMenu identity={ada} items={[action('profile', 'Profile')]}/>)
    const states: boolean[] = []
    for (let index = 0; index < input.activations; index++) {
      fireEvent.click(trigger())
      states.push(trigger().getAttribute('aria-expanded') === 'true')
      expect(document.querySelectorAll('.hl-user-menu__surface').length <= 1).toBe(expected.singleSurface)
    }
    expect(states).toEqual(expected.openStates)
  },
  'user-menu.simple-pattern': async ({ input, expected }) => {
    const items: UserMenuItem[] = input.kinds.map((kind: string, index: number) => kind === 'separator' ? { id: `s${index}`, kind } : kind === 'link' ? { id: `l${index}`, kind, label: `Link ${index}`, destination: '/x' } : action(`a${index}`, `Action ${index}`))
    render(<UserMenu identity={ada} defaultOpen items={items}/>)
    expect(document.querySelector('.hl-user-menu__surface')!.getAttribute('role')).toBe(expected.surfaceRole)
    await waitFor(() => expect(screen.getAllByRole('menuitem')[0]).toHaveFocus())
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' })
    expect(screen.getAllByRole('menuitem')[1]!.matches(':focus')).toBe(expected.completeKeyboardModel)
  },
  'user-menu.custom-pattern': async ({ expected }) => {
    render(<UserMenu identity={ada} defaultOpen items={[{ id: 'theme', kind: 'custom', content: <label>Theme<select aria-label="Theme"><option>Light</option></select></label> }]}/>)
    expect(document.querySelector('.hl-user-menu__surface')!.getAttribute('role')).toBe(expected.surfaceRole)
    expect(screen.getByRole('combobox', { name: 'Theme' }) !== null && screen.queryByRole('menuitem') === null).toBe(expected.embeddedNativeSemantics)
  },
  'user-menu.ordered-entries': ({ input, expected }) => {
    const items: UserMenuItem[] = input.ids.map((id: string) => id.startsWith('separator') ? { id, kind: 'separator' } : action(id, id))
    render(<UserMenu identity={ada} defaultOpen items={items}/>)
    const rendered = Array.from(document.querySelectorAll('.hl-user-menu__items > *')).map(node => node.tagName === 'HR' ? input.ids.find((id: string) => id.startsWith('separator')) : node.textContent)
    expect(rendered).toEqual(expected.renderOrder)
  },
  'user-menu.selection': ({ input, expected }) => {
    const invoked: string[] = []
    render(<UserMenu identity={ada} defaultOpen items={[action(input.activate, 'Profile', vi.fn(() => invoked.push(input.activate)))]}/>)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Profile' }))
    expect(invoked).toEqual(expected.invocations)
    expect(document.querySelector('.hl-user-menu__surface') === null).toBe(expected.closed)
  },
  'user-menu.keep-open': ({ input, expected }) => {
    const invoked: string[] = []
    render(<UserMenu identity={ada} defaultOpen items={[action(input.activate, 'Pin', vi.fn(() => invoked.push(input.activate)), { closeOnSelect: input.closeOnSelect } as Partial<UserMenuItem>)]}/>)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Pin' }))
    expect(invoked).toEqual(expected.invocations)
    expect(document.querySelector('.hl-user-menu__surface') !== null).toBe(expected.open)
  },
  'user-menu.custom-keep-open': ({ input, expected }) => {
    const changed = vi.fn()
    render(<UserMenu identity={ada} defaultOpen items={[{ id: input.customControl, kind: 'custom', closeOnSelect: input.closeOnSelect, content: <button type="button" onClick={changed}>Theme</button> }]}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Theme' }))
    expect(changed.mock.calls.length === 1).toBe(expected.nativeControlActivated)
    expect(document.querySelector('.hl-user-menu__surface') !== null).toBe(expected.open)
  },
  'user-menu.disabled': async ({ input, expected }) => {
    const billing = vi.fn()
    render(<UserMenu identity={ada} defaultOpen items={[action('profile', 'Profile'), action(input.disabled, 'Billing', billing, { disabled: true } as Partial<UserMenuItem>), { id: 'docs', kind: 'link', label: 'Docs', destination: '/docs' }, { id: 'help', kind: 'link', label: 'Help', destination: '/help', disabled: true }]}/>)
    const disabled = screen.getByRole('menuitem', { name: 'Billing' })
    expect(disabled !== null).toBe(expected.visible)
    await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus())
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Docs' }).matches(':focus')).toBe(expected.navigationSkips)
    expect(disabled).toBeDisabled()
    fireEvent.click(disabled)
    expect(billing).toHaveBeenCalledTimes(expected.invocations)
    // A disabled link keeps no destination, so neither pointer nor keyboard can navigate.
    expect(document.querySelectorAll('[role=menuitem][href]').length - 1).toBe(expected.navigations)
  },
  'user-menu.sign-out': ({ expected }) => {
    const signOut = vi.fn()
    render(<UserMenu identity={ada} defaultOpen onSignOut={signOut} items={[action('profile', 'Profile')]}/>)
    const entries = Array.from(document.querySelectorAll('.hl-user-menu__items > button'))
    expect(entries.at(-1)!.textContent === 'Sign out').toBe(expected.appendedAfterItems)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Sign out' }))
    expect(signOut).toHaveBeenCalledTimes(expected.invocations)
    expect(document.querySelector('.hl-user-menu__surface') === null).toBe(expected.closed)
  },
  'user-menu.dismissal-focus': async ({ input, expected }) => {
    const outcomes: Record<string, { closed: boolean; focus: string }> = {}
    for (const method of input.methods) {
      const view = render(<><UserMenu identity={ada} items={[action('profile', 'Profile')]}/><button type="button">Outside</button></>)
      fireEvent.click(trigger())
      await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus())
      const outside = screen.getByRole('button', { name: 'Outside' })
      if (method === 'Escape') fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Profile' }), { key: 'Escape' })
      else if (method === 'trigger-toggle') fireEvent.click(trigger())
      else { fireEvent.pointerDown(outside); outside.focus() }
      await new Promise(resolve => setTimeout(resolve, 0))
      outcomes[method] = { closed: document.querySelector('.hl-user-menu__surface') === null, focus: document.activeElement === trigger() ? 'trigger' : document.activeElement === outside ? 'outside' : 'other' }
      view.unmount()
    }
    expect(Object.values(outcomes).every(outcome => outcome.closed)).toBe(expected.allClose)
    expect(outcomes['Escape']!.focus === 'trigger' && outcomes['trigger-toggle']!.focus === 'trigger').toBe(expected.escapeAndToggleRestoreTrigger)
    expect(outcomes['outside-pointer']!.focus === 'outside').toBe(expected.outsideTargetRetainsFocus)
  },
  'user-menu.keyboard-menu': async ({ expected }) => {
    const profile = vi.fn()
    render(<UserMenu identity={ada} defaultOpen onSignOut={vi.fn()} items={[action('profile', 'Profile', profile), { id: 'docs', kind: 'link', label: 'Docs', destination: '/docs' }]}/>)
    const menu = screen.getByRole('menu')
    await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus())
    const focused = () => (document.activeElement as HTMLElement).textContent
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    const roving = focused() === 'Docs' && screen.getByRole('menuitem', { name: 'Docs' }).tabIndex === 0
    fireEvent.keyDown(menu, { key: 'End' })
    fireEvent.keyDown(menu, { key: 'ArrowDown' })
    const wraps = focused() === 'Profile'
    fireEvent.keyDown(menu, { key: 'ArrowUp' })
    const wrapsBack = focused() === 'Sign out'
    fireEvent.keyDown(menu, { key: 'p' })
    const typeahead = focused() === 'Profile'
    // Activation by the real Enter key on the focused item.
    await userEvent.setup().keyboard('{Enter}')
    expect(roving).toBe(expected.rovingFocus)
    expect(wraps && wrapsBack).toBe(expected.wraps)
    expect(typeahead).toBe(expected.typeahead)
    expect(profile.mock.calls.length === 1).toBe(expected.activationExactlyOnce)
  },
  'user-menu.keyboard-dialog': async ({ expected }) => {
    const onOpenChange = vi.fn()
    render(<UserMenu identity={ada} defaultOpen onOpenChange={onOpenChange} items={[{ id: 'theme', kind: 'custom', content: <><button type="button">First</button><button type="button">Second</button></> }]}/>)
    await waitFor(() => expect(screen.getByRole('button', { name: 'First' })).toHaveFocus())
    const arrow = fireEvent.keyDown(screen.getByRole('button', { name: 'First' }), { key: 'ArrowDown' })
    expect(screen.getByRole('button', { name: 'First' }).matches(':focus') && arrow).toBe(expected.noInventedMenuNavigation)
    const tab = fireEvent.keyDown(screen.getByRole('button', { name: 'First' }), { key: 'Tab' })
    expect(tab && document.querySelector('.hl-user-menu__surface') !== null).toBe(expected.nativeTabOrder)
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onOpenChange.mock.calls.at(-1)?.[0] === false).toBe(expected.escapeCloses)
  },
  'user-menu.placement': ({ input, expected }) => {
    const seen = new Set<string>()
    for (const placement of input.placements) for (const alignment of input.alignments) {
      const view = render(<UserMenu identity={ada} placement={placement} alignment={alignment}/>)
      const root = view.container.querySelector('.hl-user-menu')!
      seen.add(`${root.getAttribute('data-placement')}|${root.getAttribute('data-alignment')}`)
      view.unmount()
    }
    expect(seen.size === input.placements.length * input.alignments.length).toBe(expected.allDistinct)
    const view = render(<UserMenu identity={ada}/>)
    const root = view.container.querySelector('.hl-user-menu')!
    expect({ placement: root.getAttribute('data-placement'), alignment: root.getAttribute('data-alignment') }).toEqual(expected.defaults)
  },
  'user-menu.rtl-touch': ({ input, expected }) => {
    const view = render(<UserMenu identity={ada} direction={input.direction} canShowRail={input.canShowRail}/>)
    expect(view.container.querySelector('.hl-user-menu')!.getAttribute('dir') === 'rtl').toBe(expected.logicalAlignment)
    // Touch sizing applies exactly when the rail cannot be shown; the pixel minimum is stylesheet behaviour.
    expect(trigger().getAttribute('data-hl-touch') === 'true').toBe(!input.canShowRail)
  },
  'user-menu.localization': ({ input, expected }) => {
    render(<UserMenu identity={ada} defaultOpen onSignOut={vi.fn()} labels={{ trigger: name => input.triggerLabel.replace('{name}', name), panel: input.panelLabel, signOut: input.signOutLabel }} items={[action('profile', 'Profile')]}/>)
    const controlled = trigger().getAttribute('aria-label') === input.triggerLabel.replace('{name}', 'Ada Lovelace')
      && screen.getByRole('menu').getAttribute('aria-label') === input.panelLabel
      && screen.getByRole('menuitem', { name: input.signOutLabel }) !== null
    expect(controlled).toBe(expected.allChromeCallerControlled)
  },
  'user-menu.catalog-defaults': ({ input, expected }) => {
    const menu = <UserMenu identity={{ name: input.name }} onSignOut={vi.fn()}/>
    const view = render(menu)
    expect(menuNames()).toEqual(expected.defaultCatalog)
    view.rerender(<HarborlineLocaleProvider locale={input.hostCatalog.locale} catalog={input.hostCatalog.entries}>{menu}</HarborlineLocaleProvider>)
    expect(menuNames()).toEqual(expected.hostCatalog)
  },
  'user-menu.caller-text-exact': ({ input, expected }) => {
    render(<HarborlineLocaleProvider locale={input.hostCatalog.locale} catalog={input.hostCatalog.entries}><UserMenu identity={{ name: input.name }} labels={input.labels} onSignOut={vi.fn()}/></HarborlineLocaleProvider>)
    const { panel, signOut } = menuNames()
    expect({ panel, signOut }).toEqual(expected)
  },
  'user-menu.invalid-input': ({ input, expected }) => {
    const silence = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const errors: string[] = []
    const capture = (node: React.ReactElement) => { try { render(node); errors.push('none') } catch (error) { errors.push((error as Error).message) } }
    try {
      capture(<UserMenu identity={{ name: input.name }}/>)
      capture(<UserMenu identity={ada} items={input.duplicateIds.map((id: string) => action(id, id))}/>)
      capture(<UserMenu identity={ada} items={[{ id: 'docs', kind: 'link', label: 'Docs', destination: '' }]}/>)
    } finally { silence.mockRestore() }
    expect(input.invalidEntry).toBe('link-without-destination')
    expect(errors).toEqual(expected.errors)
  },
}
