import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ActionMenu, type ActionMenuEntry } from '../ActionMenu'
import { fixture, sharedCases } from './fixtures'

const standardItems = (onEdit = vi.fn(), onDelete = vi.fn()): ActionMenuEntry[] => [
  { label: 'Edit', onClick: onEdit },
  { label: 'Unavailable', onClick: vi.fn(), disabled: true },
  { separator: true },
  { label: 'Delete', onClick: onDelete, variant: 'destructive', icon: <span>×</span> },
]

function openWithPointer() {
  fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
  return screen.getByRole('menu')
}

describe('ActionMenu revision-1 shared fixtures', () => {
  it('action-menu.closed', () => {
    fixture(sharedCases, 'action-menu.closed')
    render(<ActionMenu items={standardItems()} />)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'More actions' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('action-menu.default-trigger', () => {
    fixture(sharedCases, 'action-menu.default-trigger')
    render(<ActionMenu items={standardItems()} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    expect(trigger).toHaveAttribute('aria-controls')
  })

  it('action-menu.custom-trigger', () => {
    fixture(sharedCases, 'action-menu.custom-trigger')
    const onClick = vi.fn()
    const onKeyDown = vi.fn()
    render(<ActionMenu items={standardItems()} trigger={<button onClick={onClick} onKeyDown={onKeyDown}>Open tools</button>} />)
    const trigger = screen.getByRole('button', { name: 'Open tools' })
    fireEvent.click(trigger)
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(onClick).toHaveBeenCalledOnce()
    expect(onKeyDown).toHaveBeenCalledOnce()
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
  })

  it('action-menu.pointer-toggle', async () => {
    fixture(sharedCases, 'action-menu.pointer-toggle')
    const user = userEvent.setup()
    render(<ActionMenu items={standardItems()} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    await user.click(trigger)
    expect(screen.getByRole('menu')).toBeInTheDocument()
    await user.click(trigger)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
  })

  it('action-menu.entries', () => {
    fixture(sharedCases, 'action-menu.entries')
    render(<ActionMenu items={[{ label: 'Edit', onClick: vi.fn() }, { separator: true }, { label: 'Delete', onClick: vi.fn() }]} />)
    const menu = openWithPointer()
    expect(within(menu).getAllByRole('menuitem')).toHaveLength(2)
    expect(within(menu).getByRole('separator')).not.toHaveAttribute('tabindex')
  })

  it('action-menu.disabled', () => {
    fixture(sharedCases, 'action-menu.disabled')
    const blocked = vi.fn()
    render(<ActionMenu items={[{ label: 'Unavailable', onClick: blocked, disabled: true }]} />)
    openWithPointer()
    const item = screen.getByRole('menuitem', { name: 'Unavailable' })
    expect(item).toBeDisabled()
    fireEvent.click(item)
    expect(blocked).not.toHaveBeenCalled()
    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  it('action-menu.keyboard-open', () => {
    fixture(sharedCases, 'action-menu.keyboard-open')
    const { unmount } = render(<ActionMenu items={standardItems()} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    for (const [key, expected] of [['ArrowDown', 'Edit'], ['ArrowUp', 'Delete'], ['Home', 'Edit'], ['End', 'Delete']] as const) {
      trigger.focus()
      fireEvent.keyDown(trigger, { key })
      expect(screen.getByRole('menuitem', { name: expected })).toHaveFocus()
      fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    }
    unmount()
  })

  it('action-menu.keyboard-navigation', () => {
    fixture(sharedCases, 'action-menu.keyboard-navigation')
    render(<ActionMenu items={standardItems()} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'End' })
    const edit = screen.getByRole('menuitem', { name: 'Edit' })
    const remove = screen.getByRole('menuitem', { name: 'Delete' })
    expect(remove).toHaveFocus()
    fireEvent.keyDown(remove, { key: 'ArrowDown' })
    expect(edit).toHaveFocus()
    fireEvent.keyDown(edit, { key: 'ArrowUp' })
    expect(remove).toHaveFocus()
    fireEvent.keyDown(remove, { key: 'Home' })
    expect(edit).toHaveFocus()
  })

  it('action-menu.typeahead', () => {
    fixture(sharedCases, 'action-menu.typeahead')
    render(<ActionMenu items={[
      { label: 'Edit', onClick: vi.fn() },
      { label: 'Export', onClick: vi.fn() },
      { label: 'Delete', onClick: vi.fn() },
    ]} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Edit' }), { key: 'd' })
    expect(screen.getByRole('menuitem', { name: 'Delete' })).toHaveFocus()
    fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Delete' }), { key: 'e' })
    expect(screen.getByRole('menuitem', { name: 'Edit' })).toHaveFocus()
    fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Edit' }), { key: 'e' })
    expect(screen.getByRole('menuitem', { name: 'Export' })).toHaveFocus()
  })

  it('action-menu.activation', () => {
    fixture(sharedCases, 'action-menu.activation')
    for (const key of ['Enter', ' '] as const) {
      const selected = vi.fn()
      const { unmount } = render(<ActionMenu items={[{ label: 'Edit', onClick: selected }]} />)
      const trigger = screen.getByRole('button', { name: 'More actions' })
      fireEvent.keyDown(trigger, { key: 'ArrowDown' })
      fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Edit' }), { key })
      expect(selected).toHaveBeenCalledOnce()
      expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      unmount()
    }
  })

  it('action-menu.dismissal', async () => {
    fixture(sharedCases, 'action-menu.dismissal')
    const selected = vi.fn()
    const user = userEvent.setup()
    render(<><ActionMenu items={[{ label: 'Edit', onClick: selected }]} /><button>Outside</button></>)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    fireEvent.keyDown(screen.getByRole('menuitem'), { key: 'Escape' })
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    fireEvent.keyDown(screen.getByRole('menuitem'), { key: 'Tab' })
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()

    await user.click(trigger)
    const outside = screen.getByRole('button', { name: 'Outside' })
    await user.click(outside)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(outside).toHaveFocus()
    expect(selected).not.toHaveBeenCalled()
  })

  it('action-menu.focus-return', async () => {
    fixture(sharedCases, 'action-menu.focus-return')
    const user = userEvent.setup()
    render(<ActionMenu items={[{ label: 'Edit', onClick: vi.fn() }]} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    fireEvent.keyDown(screen.getByRole('menuitem'), { key: 'Escape' })
    await Promise.resolve()
    expect(trigger).toHaveFocus()
    await user.click(trigger)
    await user.click(screen.getByRole('menuitem'))
    await Promise.resolve()
    expect(trigger).toHaveFocus()
  })

  it('action-menu.empty-disabled', () => {
    fixture(sharedCases, 'action-menu.empty-disabled')
    const { rerender } = render(<ActionMenu items={[]} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(trigger).toHaveFocus()
    expect(screen.getByRole('menu')).toHaveAttribute('data-hl-empty', 'true')
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    rerender(<ActionMenu items={[{ label: 'Blocked', onClick: vi.fn(), disabled: true }]} />)
    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(trigger).toHaveFocus()
    expect(screen.getByRole('menuitem')).toBeDisabled()
  })

  it('action-menu.empty-collection', () => {
    const shared = fixture(sharedCases, 'action-menu.empty-collection')
    const sets = shared.input.sets as string[][]
    const expected = shared.expected as {
      menuState: string[]
      emptyMessages: number[]
      menuItems: number[]
      triggerPresent: boolean
      defaultText: string
      overrideText: string
    }
    const build = (set: string[]): ActionMenuEntry[] => set.map(kind =>
      kind === 'separator'
        ? { separator: true as const }
        : { label: 'Blocked', onClick: vi.fn(), disabled: kind === 'disabled' })

    const { rerender } = render(<ActionMenu items={build(sets[0]!)} />)
    sets.forEach((set, index) => {
      rerender(<ActionMenu items={build(set)} />)
      const trigger = screen.getByRole('button', { name: 'More actions' })
      expect(trigger, `set ${index} keeps the trigger`).toBeInTheDocument()
      expect(expected.triggerPresent).toBe(true)
      fireEvent.click(trigger)
      const menu = screen.getByRole('menu')
      expect(menu, `set ${index} state`).toHaveAttribute('data-hl-state', expected.menuState[index])
      expect(document.querySelectorAll('.hl-action-menu__empty'), `set ${index} messages`)
        .toHaveLength(expected.emptyMessages[index]!)
      expect(menu.querySelectorAll('button.hl-action-menu__item'), `set ${index} items`)
        .toHaveLength(expected.menuItems[index]!)
      fireEvent.click(trigger)
    })

    rerender(<ActionMenu items={[]} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByText(expected.defaultText)).toHaveClass('hl-action-menu__empty')
    rerender(<ActionMenu items={[]} empty={shared.input.empty as string} />)
    expect(screen.getByText(expected.overrideText)).toHaveClass('hl-action-menu__empty')
  })

  it('action-menu.empty-collection reads the catalog when the caller declares nothing', () => {
    render(
      <HarborlineLocaleProvider locale="en-US" catalog={{ 'buttons.noActions': 'Nothing available' }}>
        <ActionMenu items={[]} />
      </HarborlineLocaleProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByText('Nothing available')).toHaveClass('hl-action-menu__empty')
  })

  it('action-menu.blankEmptyText', () => {
    const shared = fixture(sharedCases, 'action-menu.blankEmptyText')
    const expected = shared.expected as { defaultText: string }
    render(<ActionMenu items={[]} empty={shared.input.empty as string} />)
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.getByText(expected.defaultText)).toHaveClass('hl-action-menu__empty')
  })

  it('action-menu.empty-collection message never takes focus or invokes', () => {
    const onClick = vi.fn()
    render(<ActionMenu items={[{ separator: true }]} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(trigger).toHaveFocus()
    const message = document.querySelector('.hl-action-menu__empty')!
    expect(message).toHaveAttribute('aria-disabled', 'true')
    fireEvent.click(message)
    expect(onClick).not.toHaveBeenCalled()
    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  it('action-menu.logical-alignment', () => {
    fixture(sharedCases, 'action-menu.logical-alignment')
    const { rerender } = render(<HarborlineLocaleProvider locale="ar-SA"><ActionMenu items={standardItems()} align="left" /></HarborlineLocaleProvider>)
    openWithPointer()
    expect(screen.getByRole('menu').parentElement).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('menu').parentElement).toHaveAttribute('data-hl-align', 'left')
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    rerender(<HarborlineLocaleProvider locale="en-US"><ActionMenu items={standardItems()} align="right" /></HarborlineLocaleProvider>)
    expect(screen.getByRole('button', { name: 'More actions' }).parentElement).toHaveAttribute('data-hl-align', 'right')
  })

  it('action-menu.destructive-icon', () => {
    fixture(sharedCases, 'action-menu.destructive-icon')
    render(<ActionMenu items={standardItems()} />)
    openWithPointer()
    const item = screen.getByRole('menuitem', { name: 'Delete' })
    expect(item).toHaveAttribute('data-hl-variant', 'destructive')
    expect(item.querySelector('[aria-hidden="true"]')).toBeInTheDocument()
  })

  it('action-menu.reopen-reset', () => {
    fixture(sharedCases, 'action-menu.reopen-reset')
    render(<ActionMenu items={standardItems()} />)
    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.keyDown(trigger, { key: 'End' })
    expect(screen.getByRole('menuitem', { name: 'Delete' })).toHaveFocus()
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    fireEvent.keyDown(trigger, { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Edit' })).toHaveFocus()
  })
})
