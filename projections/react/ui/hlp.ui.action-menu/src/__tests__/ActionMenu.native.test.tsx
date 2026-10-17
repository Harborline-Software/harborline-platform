import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ActionMenu, type ActionMenuEntry } from '../ActionMenu'
import { qualityCases } from './fixtures'

describe('ActionMenu React projection quality', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'action-menu.quality.pattern',
      'action-menu.quality.keyboard',
      'action-menu.quality.focus',
      'action-menu.quality.dismissal',
      'action-menu.quality.forced-colors',
      'action-menu.quality.reflow',
      'action-menu.quality.locales',
      'action-menu.quality.rtl',
      'action-menu.quality.pseudo',
      'action-menu.quality.themes',
      'action-menu.quality.tokens',
      'action-menu.quality.contrast',
      'action-menu.quality.reduced-motion',
      'action-menu.quality.visual-parity',
    ])
  })

  it('resolves default trigger copy from the locale catalog', () => {
    render(
      <HarborlineLocaleProvider locale="en-XA" catalog={{ 'buttons.moreActions': '[!! More actions expanded !!]' }}>
        <ActionMenu items={[{ label: 'Edit', onClick: vi.fn() }]} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('button', { name: '[!! More actions expanded !!]' })).toBeInTheDocument()
  })

  it('rejects an empty effective default trigger name', () => {
    expect(() => render(
      <HarborlineLocaleProvider catalog={{ 'buttons.moreActions': ' ' }}>
        <ActionMenu items={[]} />
      </HarborlineLocaleProvider>,
    )).toThrow('accessible-name-required')
  })

  it('styles a custom trigger while preserving its caller class and accessible name', () => {
    render(<ActionMenu trigger={<button className="consumer">Review certification</button>} items={[]} />)
    expect(screen.getByRole('button', { name: 'Review certification' })).toHaveClass(
      'hl-action-menu__trigger',
      'hl-action-menu__trigger--custom',
      'consumer',
    )
  })

  it('distinguishes forward and backward navigation across three enabled entries', () => {
    // Interface: arrows wrap/skip disabled entries and separators; Home/End use boundaries.
    const invoked = vi.fn()
    const items: ActionMenuEntry[] = [
      { label: 'Edit', onClick: invoked },
      { label: 'Blocked', onClick: invoked, disabled: true },
      { label: 'Export', onClick: invoked },
      { separator: true },
      { label: 'Delete', onClick: invoked },
    ]
    render(<ActionMenu items={items} />)
    fireEvent.keyDown(screen.getByRole('button', { name: 'More actions' }), { key: 'ArrowDown' })
    const edit = screen.getByRole('menuitem', { name: 'Edit' })
    const exportItem = screen.getByRole('menuitem', { name: 'Export' })
    const remove = screen.getByRole('menuitem', { name: 'Delete' })
    expect(edit).toHaveFocus()
    fireEvent.keyDown(edit, { key: 'ArrowDown' })
    expect(exportItem).toHaveFocus()
    fireEvent.keyDown(exportItem, { key: 'ArrowUp' })
    expect(edit).toHaveFocus()
    fireEvent.keyDown(edit, { key: 'ArrowUp' })
    expect(remove).toHaveFocus()
    fireEvent.keyDown(remove, { key: 'ArrowDown' })
    expect(edit).toHaveFocus()
    fireEvent.keyDown(edit, { key: 'End' })
    expect(remove).toHaveFocus()
    fireEvent.keyDown(remove, { key: 'Home' })
    expect(edit).toHaveFocus()
    expect(screen.getByRole('menuitem', { name: 'Blocked' })).toBeDisabled()
    expect(screen.getByRole('separator')).not.toHaveAttribute('tabindex')
    for (const item of screen.getAllByRole('menuitem')) expect(item).toHaveAttribute('tabindex', '-1')
    expect(invoked).not.toHaveBeenCalled()
  })

  it('lets Tab leave the menu for the next outside control without restoring trigger focus', async () => {
    // Interface dismissal oracle: Tab closes without trapping or returning focus.
    const invoked = vi.fn()
    const user = userEvent.setup()
    render(<><ActionMenu items={[{ label: 'Edit', onClick: invoked }]} /><button>Outside</button></>)
    await user.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByRole('menuitem', { name: 'Edit' })).toHaveFocus()
    // Keep keydown and user-event's Tab default together before React removes the active item.
    await act(async () => { await user.tab() })
    await Promise.resolve()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Outside' })).toHaveFocus()
    expect(invoked).not.toHaveBeenCalled()
  })

  it('uses the current enabled entries and callbacks after an items update', async () => {
    const oldInvoked = vi.fn()
    const newInvoked = vi.fn()
    const { rerender } = render(<ActionMenu items={[
      { label: 'Alpha', onClick: oldInvoked },
      { label: 'Bravo', onClick: oldInvoked },
      { label: 'Charlie', onClick: oldInvoked },
    ]} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'End' })
    expect(screen.getByRole('menuitem', { name: 'Charlie' })).toHaveFocus()
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    await Promise.resolve()
    rerender(<ActionMenu items={[
      { label: 'Alpha', onClick: oldInvoked, disabled: true },
      { label: 'Delta', onClick: newInvoked },
      { separator: true },
      { label: 'Echo', onClick: newInvoked },
    ]} />)
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    const delta = screen.getByRole('menuitem', { name: 'Delta' })
    const echo = screen.getByRole('menuitem', { name: 'Echo' })
    expect(delta).toHaveFocus()
    fireEvent.keyDown(delta, { key: 'End' })
    expect(echo).toHaveFocus()
    fireEvent.keyDown(echo, { key: 'ArrowUp' })
    expect(delta).toHaveFocus()
    fireEvent.keyDown(delta, { key: 'Enter' })
    await Promise.resolve()
    expect(newInvoked).toHaveBeenCalledOnce()
    expect(oldInvoked).not.toHaveBeenCalled()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('moves its exclusive home when the declared scope changes', () => {
    // disclosure-v1.json: a scope has one home, and releasing it permits another owner.
    const items = [{ label: 'Edit', onClick: vi.fn() }]
    const moved = render(<ActionMenu items={items} scopeId="row-A" />)
    moved.rerender(<ActionMenu items={items} scopeId="row-B" />)
    const released = render(<ActionMenu items={items} scopeId="row-A" />)
    expect(screen.getAllByRole('button', { name: 'More actions' })).toHaveLength(2)
    expect(() => render(<ActionMenu items={items} scopeId="row-B" />)).toThrow('action-menu-scope-taken')
    released.unmount()
    moved.unmount()
  })

  it('toggles a text-node trigger with linked ARIA and dismisses Escape on that trigger', async () => {
    const invoked = vi.fn()
    render(<ActionMenu items={[{ label: 'Edit', onClick: invoked }]} trigger="Tools" />)
    const trigger = screen.getByRole('button', { name: 'Tools' })
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    fireEvent.click(trigger)
    const menu = screen.getByRole('menu')
    expect(menu.id).not.toBe('')
    expect(trigger).toHaveAttribute('aria-controls', menu.id)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    fireEvent.click(trigger)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Edit' })).toHaveFocus()
    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'Escape' })
    await Promise.resolve()
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
    expect(invoked).not.toHaveBeenCalled()
  })

  it('publishes logical, token, theme, forced-color, reflow, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-action-menu-surface')
    expect(css).toContain("[data-hl-align='left']")
    expect(css).toContain('inset-inline-start')
    expect(css).toContain('inset-inline-end')
    expect(css).toContain('max-inline-size: calc(100vi - 2rem)')
    expect(css).toContain("[data-theme='dark'] .hl-action-menu")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
