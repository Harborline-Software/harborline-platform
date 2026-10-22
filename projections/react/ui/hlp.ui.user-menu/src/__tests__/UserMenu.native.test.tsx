import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { UserMenu, userInitials, type UserMenuItem } from '../UserMenu'

const identity = { name: 'Ada Byron Lovelace', email: 'ada@example.test', role: 'Analyst' }

describe('UserMenu React projection', () => {
  it('renders stable identity, grapheme initials, and complete simple-menu keyboard behavior', async () => {
    const profile = vi.fn()
    const items: UserMenuItem[] = [
      { id: 'profile', kind: 'action', label: 'Profile', onActivate: profile },
      { id: 'billing', kind: 'action', label: 'Billing', disabled: true, onActivate: vi.fn() },
      { id: 'settings', kind: 'link', label: 'Settings', destination: '/settings' },
    ]
    render(<UserMenu identity={identity} items={items} canShowRail={false}/>)
    expect(userInitials(identity.name)).toBe('AL')
    const trigger = screen.getByRole('button', { name: 'Account menu for Ada Byron Lovelace' })
    expect(trigger).toHaveAttribute('data-hl-touch', 'true')
    fireEvent.click(trigger)
    const menu = screen.getByRole('menu', { name: 'Account menu' })
    await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus())
    fireEvent.keyDown(menu, { key: 'End' })
    expect(screen.getByRole('menuitem', { name: 'Settings' })).toHaveFocus()
    fireEvent.keyDown(menu, { key: 'p' })
    expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Profile' }))
    expect(profile).toHaveBeenCalledOnce()
    expect(screen.queryByRole('menu')).toBeNull()
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('uses dialog semantics for custom native content and keeps it open on activation', () => {
    render(<UserMenu identity={identity} items={[{ id: 'theme', kind: 'custom', closeOnSelect: false, content: <label>Theme <select><option>Dark</option></select></label> }]}/>)
    fireEvent.click(screen.getByRole('button', { name: /Account menu for/ }))
    expect(screen.getByRole('dialog', { name: 'Account menu' })).toBeInTheDocument()
    expect(screen.getByRole('combobox', { name: 'Theme' })).toBeInTheDocument()
    expect(screen.queryByRole('menu')).toBeNull()
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'Dark' } })
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('supports keep-open actions, sign out ordering, and Escape focus restoration', async () => {
    const pin = vi.fn()
    const signOut = vi.fn()
    render(<UserMenu identity={identity} items={[{ id: 'pin', kind: 'action', label: 'Pin', closeOnSelect: false, onActivate: pin }]} onSignOut={signOut}/>)
    const trigger = screen.getByRole('button', { name: /Account menu for/ })
    fireEvent.click(trigger)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Pin' }))
    expect(pin).toHaveBeenCalledOnce()
    expect(screen.getByRole('menu')).toBeInTheDocument()
    expect(screen.getAllByRole('menuitem').at(-1)).toHaveTextContent('Sign out')
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  // A control inside custom content owns its Escape first; the surface closes only on an Escape
  // nothing inside it handled (WAI-ARIA APG dialog pattern).
  it('lets a control inside custom content handle Escape before the surface closes', () => {
    const onOpenChange = vi.fn()
    render(<UserMenu identity={identity} defaultOpen onOpenChange={onOpenChange} items={[{ id: 'theme', kind: 'custom', content: <input aria-label="Theme search" onKeyDown={event => { if (event.key === 'Escape') event.preventDefault() }}/> }]}/>)
    fireEvent.keyDown(screen.getByRole('textbox', { name: 'Theme search' }), { key: 'Escape' })
    expect(onOpenChange).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledExactlyOnceWith(false)
  })

  // Tab out of a nonmodal menu closes it (WAI-ARIA APG menu button). Focus moves to the trigger first
  // so the browser's Tab continues from there instead of from a removed item; Shift+Tab lands on it.
  it('closes the menu when Tab or Shift+Tab leaves it', async () => {
    const onOpenChange = vi.fn()
    const items: UserMenuItem[] = [
      { id: 'profile', kind: 'action', label: 'Profile', onActivate: vi.fn() },
      { id: 'settings', kind: 'link', label: 'Settings', destination: '/settings' },
    ]
    render(<UserMenu identity={identity} items={items} onOpenChange={onOpenChange}/>)
    const trigger = screen.getByRole('button', { name: 'Account menu for Ada Byron Lovelace' })
    for (const shiftKey of [false, true]) {
      fireEvent.click(trigger)
      await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Profile' })).toHaveFocus())
      const tab = fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Profile' }), { key: 'Tab', shiftKey })
      expect(screen.queryByRole('menu')).toBeNull()
      expect(onOpenChange).toHaveBeenLastCalledWith(false)
      expect(trigger).toHaveFocus()
      expect(tab).toBe(!shiftKey)
    }
  })

  // The trigger announces what it opens (WAI-ARIA APG menu button), as the Blazor trigger does.
  it('announces the popup kind on the trigger', () => {
    const view = render(<UserMenu identity={identity} items={[{ id: 'profile', kind: 'action', label: 'Profile', onActivate: vi.fn() }]}/>)
    expect(screen.getByRole('button', { name: /^Account menu for/ })).toHaveAttribute('aria-haspopup', 'menu')
    view.rerender(<UserMenu identity={identity} items={[{ id: 'theme', kind: 'custom', content: <span>Theme</span> }]}/>)
    expect(screen.getByRole('button', { name: /^Account menu for/ })).toHaveAttribute('aria-haspopup', 'dialog')
  })
})
