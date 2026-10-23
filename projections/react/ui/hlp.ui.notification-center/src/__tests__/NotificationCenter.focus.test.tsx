import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import * as React from 'react'
import { describe, expect, it } from 'vitest'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

// Contract: focus is never stranded. An action that removes its own control settles focus inside the
// dialog once the host applies the change, even in a later render, and never takes back focus the user
// moved elsewhere.
const initial: NotificationCenterItem[] = [
  { id: 'r1', title: 'Export ready', timestamp: 'Now', kind: 'result', read: false },
  { id: 'r2', title: 'Import done', timestamp: 'Earlier', kind: 'result', read: false },
  { id: 'r3', title: 'Backup done', timestamp: 'Yesterday', kind: 'result', read: true },
]

function Host({ late = false }: { late?: boolean }) {
  const [items, setItems] = React.useState(initial)
  const [queued, setQueued] = React.useState<(() => void) | null>(null)
  const apply = (change: () => void) => (late ? setQueued(() => change) : change())
  return (
    <>
      <NotificationCenter
        items={items}
        onDismiss={item => apply(() => setItems(current => current.filter(entry => entry.id !== item.id)))}
        onMarkAllRead={() => apply(() => setItems(current => current.map(entry => ({ ...entry, read: true }))))}
      />
      <button type="button" onClick={() => { queued?.(); setQueued(null) }}>Apply</button>
    </>
  )
}

const open = () => fireEvent.click(screen.getByRole('button', { name: /^Notifications/ }))
const dialog = () => screen.getByRole('dialog')

describe('NotificationCenter focus settles inside the dialog', () => {
  it('moves focus to the next row when a dismissed item is removed', async () => {
    render(<Host/>)
    open()
    const dismiss = screen.getByRole('button', { name: 'Dismiss: Export ready' })
    dismiss.focus()
    await userEvent.setup().keyboard('[Enter]')
    expect(screen.queryByText('Export ready')).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Dismiss: Import done' }))
  })

  it('moves focus to the previous row when the last item is dismissed', async () => {
    render(<Host/>)
    open()
    screen.getByRole('button', { name: 'Dismiss: Backup done' }).focus()
    await userEvent.setup().keyboard('[Enter]')
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Dismiss: Import done' }))
  })

  it('moves focus into the dialog when Mark all read removes itself', async () => {
    render(<Host/>)
    open()
    screen.getByRole('button', { name: 'Mark all read' }).focus()
    await userEvent.setup().keyboard('[Enter]')
    expect(screen.queryByRole('button', { name: 'Mark all read' })).toBeNull()
    expect(dialog().contains(document.activeElement)).toBe(true)
    expect(document.activeElement).not.toBe(document.body)
  })

  it('settles focus when a host applies the removal in a later render, only if focus was lost', () => {
    render(<Host late/>)
    open()
    const dismiss = screen.getByRole('button', { name: 'Dismiss: Export ready' })
    dismiss.focus()
    fireEvent.click(dismiss)
    expect(document.activeElement).toBe(dismiss)
    // The host applies the change later; the dismissed control is removed and focus falls to the page.
    act(() => { (document.activeElement as HTMLElement).blur(); screen.getByRole('button', { name: 'Apply' }).click() })
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Dismiss: Import done' }))
  })

  it('never takes back focus the user moved elsewhere', () => {
    render(<Host late/>)
    open()
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss: Export ready' }))
    const elsewhere = screen.getByRole('button', { name: 'Apply' })
    elsewhere.focus()
    act(() => elsewhere.click())
    expect(document.activeElement).toBe(elsewhere)
  })
})
