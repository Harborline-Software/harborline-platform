import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { defaultStrings, interpolate, type HarborlineStringKey } from '@harborline-platform/hlp.ui.default-strings'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { Popover, PopoverContent, PopoverTrigger } from '@harborline-platform/hlp.ui.popover'
import { useCanShowRail } from '@harborline-platform/hlp.ui.use-is-mobile'

export type NotificationKind = 'proposal' | 'result' | 'system' | (string & {})
export interface NotificationCenterItem {
  id: string
  title: string
  body?: string
  timestamp: string
  kind: NotificationKind
  read?: boolean
  preview?: string
  actor?: string
}
export interface NotificationGroup { kind: NotificationKind; label: string }
export interface NotificationCenterLabels {
  title: string
  all: string
  confirm: string
  deny: string
  markAllRead: string
  viewAll: string
  empty: string
  groupLabel: string
  unread: string
  dismiss: (title: string) => string
}
export interface NotificationCenterProps {
  items: readonly NotificationCenterItem[]
  groups?: readonly NotificationGroup[]
  onConfirm?: (item: NotificationCenterItem) => void
  onDeny?: (item: NotificationCenterItem) => void
  onAction?: (item: NotificationCenterItem) => void
  onMarkAllRead?: () => void
  onDismiss?: (item: NotificationCenterItem) => void
  onViewAll?: () => void
  maxVisible?: number
  badgeMode?: 'unread' | 'decisions'
  formatCount?: (count: number) => string
  getBellLabel?: (info: { pending: number; unread: number; mode: 'unread' | 'decisions' }) => string
  labels?: Partial<NotificationCenterLabels>
  direction?: 'ltr' | 'rtl'
  className?: string
}

const defaults: NotificationCenterLabels = {
  title: 'Notifications', all: 'All', confirm: 'Confirm', deny: 'Deny',
  markAllRead: 'Mark all read', viewAll: 'View all', empty: 'No notifications',
  groupLabel: 'Notification groups', unread: 'Unread', dismiss: title => `Dismiss: ${title}`,
}

type Translate = (key: string, vars?: Record<string, string | number>) => string
const catalogKeys: Record<Exclude<keyof NotificationCenterLabels, 'dismiss'>, string> = {
  title: 'navigation.notifications.title', all: 'navigation.notifications.all', confirm: 'navigation.notifications.confirm',
  deny: 'navigation.notifications.deny', markAllRead: 'navigation.notifications.markAllRead', viewAll: 'navigation.notifications.viewAll',
  empty: 'navigation.notifications.empty', groupLabel: 'navigation.notifications.groups', unread: 'navigation.notifications.unread',
}

// The default copy comes from the host catalog, else the default catalog (navigation.notifications.*),
// which is also the fallback for a blank host value; no English literal is used.
function catalogDefaults(t: Translate): NotificationCenterLabels {
  const read = (key: string, vars?: Record<string, string | number>) => {
    const fallback = interpolate(defaultStrings[key as HarborlineStringKey], vars)
    const value = t(key, vars)
    return value.trim() && value !== key ? value : fallback
  }
  const resolved = { ...defaults }
  for (const [label, key] of Object.entries(catalogKeys) as Array<[keyof typeof catalogKeys, string]>) resolved[label] = read(key)
  resolved.dismiss = title => read('navigation.notifications.dismiss', { title })
  return resolved
}

function resolveLabels(overrides: Partial<NotificationCenterLabels> | undefined, base: NotificationCenterLabels): NotificationCenterLabels {
  const resolved = { ...base }
  for (const [key, value] of Object.entries(overrides ?? {}) as Array<[keyof NotificationCenterLabels, unknown]>) {
    if (typeof value === 'string' && value.trim()) (resolved as Record<string, unknown>)[key] = value
  }
  const dismiss = overrides?.dismiss
  if (dismiss) resolved.dismiss = title => { const text = dismiss(title); return text?.trim() ? text : base.dismiss(title) }
  return resolved
}

function derivedGroups(items: readonly NotificationCenterItem[]): NotificationGroup[] {
  const seen = new Set<string>()
  const groups: NotificationGroup[] = []
  for (const item of items) if (!seen.has(item.kind)) {
    seen.add(item.kind)
    groups.push({ kind: item.kind, label: item.kind.charAt(0).toUpperCase() + item.kind.slice(1) })
  }
  return groups
}

export function NotificationCenter({
  items, groups: explicitGroups, onConfirm, onDeny, onAction, onMarkAllRead,
  onDismiss, onViewAll, maxVisible = 10, badgeMode = 'unread', formatCount = String,
  getBellLabel, labels: overrides, direction, className,
}: NotificationCenterProps) {
  if (!Number.isFinite(maxVisible) || maxVisible < 0) throw new Error('invalid-notification-limit')
  if (new Set(items.map(item => item.id)).size !== items.length) throw new Error('duplicate-notification-id')
  // Caller text overrides each label; blank caller text is no text and keeps the catalog default, so no
  // chrome or control name is ever empty.
  const { t } = useHarborlineStrings()
  const labels = React.useMemo(() => resolveLabels(overrides, catalogDefaults(t)), [overrides, t])
  const unread = items.filter(item => !item.read).length
  const pending = items.filter(item => item.kind === 'proposal').length
  const otherUnread = items.filter(item => item.kind !== 'proposal' && !item.read).length
  const bellLabel = getBellLabel?.({ pending, unread, mode: badgeMode }) ??
    (badgeMode === 'decisions'
      ? pending ? `${labels.title}, ${formatCount(pending)} approval pending` : otherUnread ? `${labels.title}, ${formatCount(otherUnread)} unread` : labels.title
      : unread ? `${labels.title}, ${formatCount(unread)} unread` : labels.title)
  if (!bellLabel.trim() || !labels.title.trim()) throw new Error('accessible-notification-label-required')
  const groups = React.useMemo(() => explicitGroups ?? derivedGroups(items), [explicitGroups, items])
  const [open, setOpen] = React.useState(false)
  const [theme, setTheme] = React.useState<string>()
  const [kind, setKind] = React.useState<NotificationKind | 'all'>('all')
  const actionLatch = React.useRef(new Set<string>())
  const rootRef = React.useRef<HTMLDivElement>(null)
  const contentRef = React.useRef<HTMLDivElement>(null)
  // An action that removes its own control (dismissing an item, marking all read) leaves focus nowhere
  // once the host applies the change, which may be in a later render. Focus then settles on the row now
  // at the same position (else the last row), else the first control in the dialog. The settle waits
  // only while focus is still on the control the action started from: focus anywhere else cancels it,
  // so focus the user moved is never taken back.
  const settle = React.useRef<{ row: number | null; origin: Element | null } | null>(null)
  const startSettle = (row: number | null) => { settle.current = { row, origin: document.activeElement } }
  React.useLayoutEffect(() => {
    const pending = settle.current
    const dialog = contentRef.current
    if (!pending || !dialog) return
    const active = document.activeElement
    if (active && active !== document.body && active.isConnected) {
      if (active !== pending.origin) settle.current = null
      return
    }
    settle.current = null
    const rows = dialog.querySelectorAll<HTMLElement>('.hl-notification-center__item')
    const row = pending.row === null || rows.length === 0 ? null : rows[Math.min(pending.row, rows.length - 1)]
    const target = row?.querySelector<HTMLElement>('button') ?? dialog.querySelector<HTMLElement>('button') ?? dialog
    target.focus({ preventScroll: true })
  })
  const canShowRail = useCanShowRail()
  const filtered = kind === 'all' ? items : items.filter(item => item.kind === kind)
  const visible = filtered.slice(0, maxVisible)
  const numeric = badgeMode === 'unread' ? unread : pending
  const dot = badgeMode === 'decisions' && pending === 0 && otherUnread > 0
  const badge = numeric > 99 ? `${formatCount(99)}+` : formatCount(numeric)

  const requestOpen = (next: boolean) => {
    settle.current = null
    if (next) {
      actionLatch.current.clear()
      setTheme(rootRef.current?.closest<HTMLElement>('[data-theme]')?.dataset.theme)
    }
    setOpen(next)
  }
  const decide = (verb: 'confirm' | 'deny', item: NotificationCenterItem) => {
    const token = `${verb}:${item.id}`
    if (actionLatch.current.has(token)) return
    actionLatch.current.add(token)
    if (verb === 'confirm') onConfirm?.(item)
    else onDeny?.(item)
    setOpen(false)
  }

  return (
    <div ref={rootRef} dir={direction} className={cn('hl-notification-center', className)}>
      <Popover open={open} onOpenChange={requestOpen}>
        <PopoverTrigger asChild>
          <button type="button" aria-label={bellLabel} className="hl-notification-center__trigger" data-hl-touch={!canShowRail || undefined}>
            <svg aria-hidden="true" viewBox="0 0 24 24"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M14 21h-4"/></svg>
            {numeric > 0 ? <span aria-hidden="true" className="hl-notification-center__badge">{badge}</span> : null}
            {dot ? <span aria-hidden="true" className="hl-notification-center__dot"/> : null}
          </button>
        </PopoverTrigger>
        <PopoverContent
          role="dialog"
          aria-label={labels.title}
          align="end"
          className="hl-notification-center__dialog"
          ref={contentRef}
          tabIndex={-1}
          data-theme={theme}
          dir={direction}
          onInteractOutside={event => {
            const target = event.detail.originalEvent.target
            if (target instanceof HTMLElement) queueMicrotask(() => target.focus())
          }}
        >
          <header className="hl-notification-center__header">
            <h2>{labels.title}</h2>
            {unread > 0 && onMarkAllRead ? <button type="button" onClick={() => { startSettle(null); onMarkAllRead() }}>{labels.markAllRead}</button> : null}
          </header>
          {groups.length > 1 ? (
            <div role="tablist" aria-label={labels.groupLabel} className="hl-notification-center__tabs">
              <button type="button" role="tab" aria-selected={kind === 'all'} onClick={() => setKind('all')}>{labels.all}</button>
              {groups.map(group => <button key={group.kind} type="button" role="tab" aria-selected={kind === group.kind} onClick={() => setKind(group.kind)}>{group.label}</button>)}
            </div>
          ) : null}
          <div className="hl-notification-center__scroll">
            {visible.length === 0 ? <p className="hl-notification-center__empty">{labels.empty}</p> : (
              <ul className="hl-notification-center__items">
                {visible.map((item, row) => <NotificationRow key={item.id} item={item} labels={labels} onAction={onAction} onDismiss={onDismiss ? dismissed => { startSettle(row); onDismiss(dismissed) } : undefined} onConfirm={() => decide('confirm', item)} onDeny={() => decide('deny', item)}/>) }
              </ul>
            )}
          </div>
          {onViewAll || filtered.length > maxVisible ? (
            <footer><button type="button" onClick={() => { onViewAll?.(); setOpen(false) }}>{labels.viewAll}</button></footer>
          ) : null}
        </PopoverContent>
      </Popover>
    </div>
  )
}

function NotificationRow({ item, labels, onAction, onDismiss, onConfirm, onDeny }: {
  item: NotificationCenterItem
  labels: NotificationCenterLabels
  onAction?: (item: NotificationCenterItem) => void
  onDismiss?: (item: NotificationCenterItem) => void
  onConfirm: () => void
  onDeny: () => void
}) {
  return (
    <li className="hl-notification-center__item" data-hl-kind={item.kind} data-hl-read={item.read || undefined}>
      <div className="hl-notification-center__item-copy">
        {!item.read ? <span aria-label={labels.unread} className="hl-notification-center__unread"/> : null}
        <strong>{item.title}</strong>{item.actor ? <span>{item.actor}</span> : null}
        {item.body ? <p>{item.body}</p> : null}{item.preview ? <code>{item.preview}</code> : null}<time>{item.timestamp}</time>
      </div>
      {item.kind === 'proposal' ? <div className="hl-notification-center__actions"><button type="button" aria-label={`${labels.confirm}: ${item.title}`} onClick={onConfirm}>{labels.confirm}</button><button type="button" aria-label={`${labels.deny}: ${item.title}`} onClick={onDeny}>{labels.deny}</button></div> : (
        <div className="hl-notification-center__actions">
          {onAction ? <button type="button" onClick={() => onAction(item)}>{item.title}</button> : null}
          {onDismiss ? <button type="button" className="hl-notification-center__dismiss" aria-label={labels.dismiss(item.title)} onClick={() => onDismiss(item)}><svg aria-hidden="true" fill="none" focusable="false" viewBox="0 0 16 16"><path d="M3 3l10 10M13 3 3 13" /></svg></button> : null}
        </div>
      )}
    </li>
  )
}
