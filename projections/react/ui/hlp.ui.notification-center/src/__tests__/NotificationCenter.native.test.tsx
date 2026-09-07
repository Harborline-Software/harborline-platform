import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

const items: NotificationCenterItem[] = [
  { id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal', read: false },
  { id: 'r1', title: 'Export ready', timestamp: 'Earlier', kind: 'result', read: false },
]
describe('NotificationCenter React projection', () => {
  it('shows bounded badge, opens a dialog, and invokes one proposal action before close', () => {
    const confirm = vi.fn()
    render(<NotificationCenter items={items} onConfirm={confirm}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    expect(screen.getByRole('dialog', { name: 'Notifications' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Confirm: Approve export' }))
    expect(confirm).toHaveBeenCalledOnce()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('filters without reordering and keeps informational actions open', () => {
    const action = vi.fn()
    render(<NotificationCenter items={items} onAction={action}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    fireEvent.click(screen.getByRole('tab', { name: 'Result' }))
    expect(screen.queryByText('Approve export')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Export ready' }))
    expect(action).toHaveBeenCalledWith(items[1])
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('uses an authored icon for item dismissal', () => {
    render(<NotificationCenter items={items} onDismiss={() => {}}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    const dismiss = screen.getByRole('button', { name: 'Dismiss: Export ready' })
    expect(dismiss.querySelector('svg')).toBeInTheDocument()
    expect(dismiss).not.toHaveTextContent('×')
  })
})
