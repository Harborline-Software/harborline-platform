import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

// Contract: caller labels override every piece of chrome, and no control or region is ever unnamed;
// blank caller text keeps the default. (Catalog-backed defaults are blocked: the module declares no
// locale-provider or default-strings dependency, and the catalog has no notification-center keys.)
const items: NotificationCenterItem[] = [
  { id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal', read: false },
  { id: 'r1', title: 'Export ready', timestamp: 'Earlier', kind: 'result', read: false },
]

function names() {
  const named = [screen.getByRole('dialog'), ...screen.getAllByRole('button'), ...screen.getAllByRole('tab'), screen.getByRole('tablist')]
  for (const element of named) {
    const name = (element.getAttribute('aria-label') ?? element.textContent ?? '').trim()
    expect(name, element.outerHTML.slice(0, 80)).not.toBe('')
  }
}

describe('NotificationCenter labels', () => {
  it('names every control and region with the defaults', () => {
    render(<NotificationCenter items={items} onDismiss={() => undefined} onMarkAllRead={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Notifications, 2 unread' }))
    names()
  })

  it('keeps the default for blank caller labels, including a blank dismiss name', () => {
    render(<NotificationCenter items={items} labels={{ title: 'Avisos', markAllRead: '  ', confirm: '', dismiss: () => ' ' }} onDismiss={() => undefined} onMarkAllRead={() => undefined}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Avisos, 2 unread' }))
    expect(screen.getByRole('button', { name: 'Mark all read' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Confirm: Approve export' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Dismiss: Export ready' })).toBeInTheDocument()
    names()
  })

  it('carries Arabic and pseudo-localized caller text without truncation in an RTL container', () => {
    const view = render(<NotificationCenter direction="rtl" items={items} labels={{ title: 'الإشعارات', confirm: 'تأكيد' }}/>)
    fireEvent.click(screen.getByRole('button', { name: 'الإشعارات, 2 unread' }))
    expect(screen.getByRole('dialog', { name: 'الإشعارات' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('button', { name: 'تأكيد: Approve export' })).toBeInTheDocument()
    view.unmount()
    const pseudo = '⟦ Ñöţîƒîçåţîöñš ··· ⟧'
    render(<NotificationCenter items={[]} labels={{ title: pseudo }}/>)
    fireEvent.click(screen.getByRole('button', { name: pseudo }))
    expect(screen.getByRole('heading', { name: pseudo })).toBeInTheDocument()
  })
})
