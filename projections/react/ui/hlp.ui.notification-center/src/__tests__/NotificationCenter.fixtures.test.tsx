import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// rtl-touch asserts the logical direction and touch marker; the 44px bell is stylesheet behaviour left
// to the gallery's touch check. projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['notification-center.projection-equivalence']
const item = (id: string, kind: string, read = false): NotificationCenterItem => ({ id, title: `Title ${id}`, timestamp: 'Now', kind, read })
const bell = () => document.querySelector<HTMLButtonElement>('.hl-notification-center__trigger')!
const open = () => fireEvent.click(bell())
const dialogOpen = () => screen.queryByRole('dialog') !== null
// The dialog, group tab list and dismiss names, read with the dialog open, then closed again. Two kinds
// make the tab list exist; the result row carries the dismiss control.
const namedItems = (title: string): NotificationCenterItem[] => [{ id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal' }, { id: 'r1', title, timestamp: 'Now', kind: 'result' }]
function chromeNames() {
  open()
  const names = { dialog: screen.getByRole('dialog').getAttribute('aria-label'), tablist: screen.getByRole('tablist').getAttribute('aria-label'), dismiss: document.querySelector('.hl-notification-center__dismiss')!.getAttribute('aria-label') }
  open()
  return names
}

describe('NotificationCenter shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    expect(cases.length).toBeGreaterThan(0)
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, async () => { await assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void | Promise<void>> = {
  'notification-center.empty': ({ expected }) => {
    render(<NotificationCenter items={[]}/>)
    expect(bell().getAttribute('aria-label')).toBe(expected.bellLabel)
    expect(document.querySelector('.hl-notification-center__badge')?.textContent ?? null).toBe(expected.badge)
    open()
    expect(screen.getByText(expected.empty)).toBeInTheDocument()
  },
  'notification-center.badge-unread': ({ input, expected }) => {
    render(<NotificationCenter badgeMode={input.mode} items={input.items.map((entry: any, index: number) => item(`n${index}`, entry.kind, entry.read))}/>)
    expect(document.querySelector('.hl-notification-center__badge')?.textContent).toBe(expected.numericBadge)
    expect(bell().getAttribute('aria-label')).toBe(expected.bellLabel)
  },
  'notification-center.badge-decisions': ({ input, expected }) => {
    const items = [...Array.from({ length: input.proposalUnread }, (_, index) => item(`p${index}`, 'proposal')), ...Array.from({ length: input.otherUnread }, (_, index) => item(`o${index}`, 'result'))]
    render(<NotificationCenter badgeMode={input.mode} items={items}/>)
    expect(document.querySelector('.hl-notification-center__badge')?.textContent).toBe(expected.numericBadge)
    expect(bell().getAttribute('aria-label')).toBe(expected.bellLabel)
  },
  'notification-center.overflow-format': ({ input, expected }) => {
    render(<NotificationCenter formatCount={count => `${input.formatPrefix}${count}`} items={Array.from({ length: input.unread }, (_, index) => item(`n${index}`, 'result'))}/>)
    const badge = document.querySelector('.hl-notification-center__badge')!.textContent
    expect(badge).toBe(expected.badge)
    expect(badge!.length <= `${input.formatPrefix}99+`.length).toBe(expected.bounded)
  },
  'notification-center.open-close': ({ input, expected }) => {
    render(<NotificationCenter items={[item('r1', 'result')]}/>)
    const states = (input.commands as string[]).map(() => { open(); return dialogOpen() })
    expect(states).toEqual(expected.states)
    open()
    expect(screen.getByRole('dialog').getAttribute('role')).toBe(expected.role)
  },
  'notification-center.dismissal-focus': async ({ input, expected }) => {
    const user = userEvent.setup()
    const outcomes = []
    for (const method of input.methods as string[]) {
      const view = render(<><NotificationCenter items={[item('p1', 'proposal'), item('r1', 'result')]} onConfirm={() => undefined} onViewAll={() => undefined}/><button type="button">Outside</button></>)
      await user.click(bell())
      if (method === 'Escape') await user.keyboard('[Escape]')
      else if (method === 'outside-pointer') await user.click(screen.getByRole('button', { name: 'Outside' }))
      else if (method === 'proposal') await user.click(screen.getByRole('button', { name: 'Confirm: Title p1' }))
      else await user.click(screen.getByRole('button', { name: 'View all' }))
      const expectedFocus = method === 'outside-pointer' ? screen.getByRole('button', { name: 'Outside' }) : bell()
      outcomes.push({ closed: !dialogOpen(), focus: document.activeElement === expectedFocus })
      view.unmount()
    }
    expect(outcomes.every(outcome => outcome.closed)).toBe(expected.closed)
    expect(outcomes.every(outcome => outcome.focus)).toBe(expected.triggerFocusRestoredExceptOutsideTarget)
  },
  'notification-center.groups': ({ input, expected }) => {
    render(<NotificationCenter items={(input.kinds as string[]).map((kind, index) => item(`n${index}`, kind))}/>)
    open()
    const tabs = screen.getAllByRole('tab')
    expect(tabs).toHaveLength(expected.tabs)
    const labels = tabs.slice(1).map(tab => tab.textContent!.toLowerCase())
    expect(labels).toEqual(expected.groupKinds)
    expect(JSON.stringify(labels) === JSON.stringify([...new Set(input.kinds as string[])])).toBe(expected.firstAppearanceOrder)
  },
  'notification-center.filter': ({ input, expected }) => {
    const items = (input.items as string[]).map(entry => { const [kind, id] = entry.split(':'); return item(id!, kind!) })
    render(<NotificationCenter items={items}/>)
    open()
    fireEvent.click(screen.getByRole('tab', { name: input.activeKind.charAt(0).toUpperCase() + input.activeKind.slice(1) }))
    const visible = Array.from(document.querySelectorAll('.hl-notification-center__item strong')).map(node => node.textContent!.replace('Title ', ''))
    expect(visible).toEqual(expected.visibleIds)
    expect(JSON.stringify(visible) === JSON.stringify(items.filter(entry => entry.kind === input.activeKind).map(entry => entry.id))).toBe(expected.callerOrderPreserved)
  },
  'notification-center.proposal': ({ input, expected }) => {
    const confirm = vi.fn(); const deny = vi.fn()
    const items = [item(input.id, 'proposal')]
    render(<NotificationCenter items={items} onConfirm={confirm} onDeny={deny}/>)
    for (const action of input.actions as string[]) {
      open()
      const button = screen.getByRole('button', { name: `${action === 'confirm' ? 'Confirm' : 'Deny'}: Title ${input.id}` })
      // Two activations before React re-renders: only the per-open latch can stop the second.
      act(() => { button.click(); button.click() })
      expect(dialogOpen()).toBe(!expected.closes)
    }
    expect(confirm).toHaveBeenCalledTimes(expected.confirmCountPerOpen)
    expect(deny).toHaveBeenCalledTimes(expected.denyCountPerOpen)
    expect(confirm.mock.calls[0]![0] === items[0]).toBe(expected.itemPreserved)
  },
  'notification-center.informational': ({ input, expected }) => {
    const action = vi.fn()
    render(<NotificationCenter items={[item(input.id, 'result')]} onAction={action}/>)
    open()
    fireEvent.click(screen.getByRole('button', { name: `Title ${input.id}` }))
    expect(action.mock.calls.map(call => call[0].id)).toEqual(expected.actionIds)
    expect(screen.queryByRole('button', { name: /^Confirm/ }) !== null).toBe(expected.confirmPresent)
    expect(screen.queryByRole('button', { name: /^Deny/ }) !== null).toBe(expected.denyPresent)
    expect(dialogOpen()).toBe(expected.remainsOpen)
  },
  'notification-center.mark-read': ({ input, expected }) => {
    const marked = vi.fn()
    render(<NotificationCenter items={Array.from({ length: input.unread }, (_, index) => item(`n${index}`, 'result'))} onMarkAllRead={input.hasCallback ? marked : undefined}/>)
    open()
    const button = screen.queryByRole('button', { name: 'Mark all read' })
    expect(button !== null).toBe(expected.present)
    if (input.activate) fireEvent.click(button!)
    expect(marked).toHaveBeenCalledTimes(expected.callbackCount)
  },
  'notification-center.dismiss-item': ({ input, expected }) => {
    const dismissed = vi.fn()
    const items = Object.freeze([item('n1', input.kind)])
    render(<NotificationCenter items={items} onDismiss={input.hasCallback ? dismissed : undefined}/>)
    open()
    if (input.activate) fireEvent.click(screen.getByRole('button', { name: 'Dismiss: Title n1' }))
    expect(dismissed).toHaveBeenCalledTimes(expected.callbackCount)
    expect(items.length !== 1 || screen.queryByText('Title n1') === null).toBe(expected.itemMutated)
  },
  'notification-center.limit-view-all': ({ input, expected }) => {
    const viewAll = vi.fn()
    render(<NotificationCenter items={Array.from({ length: input.items }, (_, index) => item(`n${index}`, 'result'))} maxVisible={input.maxVisible} onViewAll={input.hasCallback ? viewAll : undefined}/>)
    open()
    expect(document.querySelectorAll('.hl-notification-center__item')).toHaveLength(expected.visible)
    const button = screen.queryByRole('button', { name: 'View all' })
    expect(button !== null).toBe(expected.viewAll)
    fireEvent.click(button!)
    expect(viewAll).toHaveBeenCalledTimes(expected.callbackCount)
    expect(!dialogOpen()).toBe(expected.closes)
  },
  'notification-center.catalog-defaults': ({ input, expected }) => {
    const center = <NotificationCenter items={namedItems(input.dismissedTitle)} onDismiss={() => undefined}/>
    const view = render(center)
    expect(chromeNames()).toEqual(expected.defaultCatalog)
    view.rerender(<HarborlineLocaleProvider locale={input.hostCatalog.locale} catalog={input.hostCatalog.entries}>{center}</HarborlineLocaleProvider>)
    expect(chromeNames()).toEqual(expected.hostCatalog)
  },
  'notification-center.caller-text-exact': ({ input, expected }) => {
    render(<HarborlineLocaleProvider locale={input.hostCatalog.locale} catalog={input.hostCatalog.entries}><NotificationCenter items={namedItems(input.dismissedTitle)} labels={input.labels} onDismiss={() => undefined}/></HarborlineLocaleProvider>)
    const { dialog, tablist } = chromeNames()
    expect({ dialog, tablist }).toEqual(expected)
  },
  'notification-center.bell-catalog': ({ input, expected }) => {
    // Bell names for N pending proposals (decisions mode) or N unread results (unread mode).
    const bells = (counts: { pending?: number[]; unread?: number[] }, host?: { locale: string; entries: Record<string, string> }, formatCount?: (count: number) => string) => {
      const name = (mode: 'decisions' | 'unread', count: number) => {
        const items = Array.from({ length: count }, (_, index) => item(`n${index}`, mode === 'decisions' ? 'proposal' : 'result'))
        const center = <NotificationCenter badgeMode={mode} formatCount={formatCount} items={items}/>
        const view = render(host ? <HarborlineLocaleProvider locale={host.locale} catalog={host.entries}>{center}</HarborlineLocaleProvider> : center)
        const label = bell().getAttribute('aria-label')
        view.unmount()
        return label
      }
      return { ...(counts.pending ? { pending: counts.pending.map(count => name('decisions', count)) } : {}), ...(counts.unread ? { unread: counts.unread.map(count => name('unread', count)) } : {}) }
    }
    expect(bells(input.defaultCatalog)).toEqual(expected.defaultCatalog)
    expect(bells(input.hostCatalog, input.hostCatalog)).toEqual(expected.hostCatalog)
    const template = input.formatCount as string
    expect(bells({ unread: [2] }, undefined, count => template.replace('{count}', String(count))).unread![0]).toBe(expected.formatted)
  },
  'notification-center.localization': ({ input, expected }) => {
    const labels = input.labels
    render(<NotificationCenter items={[item('p1', 'proposal', false)]} labels={labels} onMarkAllRead={() => undefined} onViewAll={() => undefined}/>)
    const bellOverridden = bell().getAttribute('aria-label')!.startsWith(labels.title)
    open()
    const dialog = screen.getByRole('dialog')
    const chrome = [
      dialog.getAttribute('aria-label') === labels.title,
      within(dialog).getByRole('heading').textContent === labels.title,
      within(dialog).getByRole('button', { name: `${labels.confirm}: Title p1` }).textContent === labels.confirm,
      within(dialog).getByRole('button', { name: `${labels.deny}: Title p1` }).textContent === labels.deny,
      within(dialog).queryByRole('button', { name: labels.markAllRead }) !== null,
      within(dialog).queryByRole('button', { name: labels.viewAll }) !== null,
    ]
    const view = render(<NotificationCenter items={[]} labels={labels}/>)
    fireEvent.click(view.container.querySelector('.hl-notification-center__trigger')!)
    const empty = screen.queryByText(labels.empty) !== null
    expect(bellOverridden && chrome.every(Boolean) && empty).toBe(expected.allChromeOverridden)
  },
  'notification-center.rtl-touch': ({ input, expected }) => {
    // The test setup's matchMedia reports no match, so canShowRail is false, as the fixture requires.
    // The 44px bell is the gallery's touch check, not asserted here.
    render(<NotificationCenter direction={input.direction} items={[item('r1', 'result')]}/>)
    open()
    const logical = document.querySelector('.hl-notification-center')!.getAttribute('dir') === input.direction && screen.getByRole('dialog').getAttribute('dir') === input.direction
    expect(logical).toBe(expected.logicalEdges)
    expect(bell().dataset.hlTouch === 'true').toBe(!input.canShowRail)
  },
  'notification-center.invalid-input': ({ input, expected }) => {
    const errors: string[] = []
    for (const props of [{ items: (input.ids as string[]).map(id => item(id, 'result')) }, { items: [], maxVisible: input.maxVisible }]) {
      try { render(<NotificationCenter {...props}/>) } catch (error) { errors.push((error as Error).message) }
    }
    expect(errors).toEqual(expected.errors)
  },
}
