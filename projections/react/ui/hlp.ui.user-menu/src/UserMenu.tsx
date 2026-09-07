import * as React from 'react'

import { useCanShowRail } from '@harborline-platform/hlp.ui.use-is-mobile'

export interface UserIdentity {
  name: string
  email?: string
  role?: string
  avatarUri?: string
}

interface UserMenuItemBase {
  id: string
  disabled?: boolean
  closeOnSelect?: boolean
}

export interface UserMenuActionItem extends UserMenuItemBase {
  kind: 'action'
  label: string
  subtitle?: string
  icon?: React.ReactNode
  onActivate: () => void
}

export interface UserMenuLinkItem extends UserMenuItemBase {
  kind: 'link'
  label: string
  subtitle?: string
  icon?: React.ReactNode
  destination: string
}

export interface UserMenuCustomItem extends UserMenuItemBase {
  kind: 'custom'
  content: React.ReactNode
}

export interface UserMenuSeparatorItem {
  kind: 'separator'
  id?: string
}

export type UserMenuItem = UserMenuActionItem | UserMenuLinkItem | UserMenuCustomItem | UserMenuSeparatorItem

export interface UserMenuLabels {
  trigger: (name: string) => string
  panel: string
  signOut: string
}

export interface UserMenuProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'onChange'> {
  identity: UserIdentity
  items?: readonly UserMenuItem[]
  onSignOut?: () => void
  placement?: 'top' | 'bottom'
  alignment?: 'inline-start' | 'center' | 'inline-end'
  open?: boolean
  defaultOpen?: boolean
  onOpenChange?: (open: boolean) => void
  labels?: Partial<UserMenuLabels>
  direction?: 'ltr' | 'rtl'
  canShowRail?: boolean
}

const defaultLabels: UserMenuLabels = {
  trigger: name => `Account menu for ${name}`,
  panel: 'Account menu',
  signOut: 'Sign out',
}

function graphemes(value: string) {
  if (typeof Intl.Segmenter === 'function') return [...new Intl.Segmenter(undefined, { granularity: 'grapheme' }).segment(value)].map(part => part.segment)
  return Array.from(value)
}

export function userInitials(name: string) {
  const parts = name.trim().split(/\s+/u).filter(Boolean)
  if (parts.length === 0) return ''
  if (parts.length === 1) return graphemes(parts[0]).slice(0, 2).join('').toLocaleUpperCase()
  return `${graphemes(parts[0])[0] ?? ''}${graphemes(parts.at(-1) ?? '')[0] ?? ''}`.toLocaleUpperCase()
}

function validate(identity: UserIdentity, items: readonly UserMenuItem[]) {
  if (!identity.name.trim()) throw new Error('user-menu-name-required')
  const ids = new Set<string>()
  for (const item of items) {
    if (item.kind === 'separator') continue
    if (ids.has(item.id)) throw new Error('duplicate-user-menu-item-id')
    ids.add(item.id)
    if ((item.kind === 'action' && (!item.label.trim() || typeof item.onActivate !== 'function')) ||
      (item.kind === 'link' && (!item.label.trim() || !item.destination.trim())) ||
      (item.kind === 'custom' && item.content === null)) throw new Error('invalid-user-menu-entry')
  }
}

export function UserMenu({
  identity, items = [], onSignOut, placement = 'bottom', alignment = 'inline-end',
  open: controlledOpen, defaultOpen = false, onOpenChange, labels: labelOverrides,
  direction, canShowRail: canShowRailOverride, className, ...attributes
}: UserMenuProps) {
  validate(identity, items)
  const labels = React.useMemo(() => ({ ...defaultLabels, ...labelOverrides }), [labelOverrides])
  const responsiveCanShowRail = useCanShowRail()
  const canShowRail = canShowRailOverride ?? responsiveCanShowRail
  const [localOpen, setLocalOpen] = React.useState(defaultOpen)
  const open = controlledOpen ?? localOpen
  const triggerRef = React.useRef<HTMLButtonElement>(null)
  const panelRef = React.useRef<HTMLDivElement>(null)
  const itemRefs = React.useRef(new Map<string, HTMLElement>())
  const [avatarFailed, setAvatarFailed] = React.useState(false)
  const [focusId, setFocusId] = React.useState<string | null>(null)
  const customPattern = items.some(item => item.kind === 'custom')
  const surfaceId = React.useId()
  const simpleItems = items.filter((item): item is UserMenuActionItem | UserMenuLinkItem => item.kind === 'action' || item.kind === 'link')
  const enabledIds = [...simpleItems.filter(item => !item.disabled).map(item => item.id), ...(onSignOut ? ['__sign-out'] : [])]

  const requestOpen = React.useCallback((next: boolean, restoreFocus = false) => {
    if (controlledOpen === undefined) setLocalOpen(next)
    onOpenChange?.(next)
    if (!next && restoreFocus) queueMicrotask(() => triggerRef.current?.focus())
  }, [controlledOpen, onOpenChange])

  React.useEffect(() => {
    if (!open) return
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node | null
      if (!panelRef.current?.contains(target) && !triggerRef.current?.contains(target)) requestOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [open, requestOpen])

  React.useEffect(() => {
    if (!open) return
    queueMicrotask(() => {
      if (customPattern) panelRef.current?.querySelector<HTMLElement>('button:not([disabled]),a[href],input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')?.focus()
      else {
        const first = enabledIds[0]
        setFocusId(first ?? null)
        if (first) itemRefs.current.get(first)?.focus()
      }
    })
  }, [customPattern, open])

  const closeAfterSelection = (closeOnSelect = true) => { if (closeOnSelect) requestOpen(false, true) }
  const activateAction = (item: UserMenuActionItem) => {
    if (item.disabled) return
    item.onActivate()
    closeAfterSelection(item.closeOnSelect)
  }
  const focusItem = (id: string) => {
    setFocusId(id)
    itemRefs.current.get(id)?.focus()
  }
  const onMenuKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      requestOpen(false, true)
      return
    }
    if (customPattern || enabledIds.length === 0) return
    const current = Math.max(0, enabledIds.indexOf(focusId ?? ''))
    let target: string | undefined
    if (event.key === 'ArrowDown') target = enabledIds[(current + 1) % enabledIds.length]
    else if (event.key === 'ArrowUp') target = enabledIds[(current - 1 + enabledIds.length) % enabledIds.length]
    else if (event.key === 'Home') target = enabledIds[0]
    else if (event.key === 'End') target = enabledIds.at(-1)
    else if (event.key.length === 1 && !event.ctrlKey && !event.metaKey && !event.altKey) {
      const sought = event.key.toLocaleLowerCase()
      const ordered = [...enabledIds.slice(current + 1), ...enabledIds.slice(0, current + 1)]
      target = ordered.find(id => {
        const item = simpleItems.find(candidate => candidate.id === id)
        return (item?.label ?? labels.signOut).toLocaleLowerCase().startsWith(sought)
      })
    }
    if (target) {
      event.preventDefault()
      focusItem(target)
    }
  }
  const onPanelKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      requestOpen(false, true)
      return
    }
    onMenuKeyDown(event)
  }

  return (
    <div {...attributes} dir={direction} className={`hl-user-menu${className ? ` ${className}` : ''}`} data-placement={placement} data-alignment={alignment}>
      <button ref={triggerRef} type="button" className="hl-user-menu__trigger" data-hl-touch={!canShowRail || undefined} aria-label={labels.trigger(identity.name)} aria-expanded={open} aria-controls={open ? surfaceId : undefined} onClick={() => requestOpen(!open, open)}>
        <span className="hl-user-menu__avatar" aria-hidden="true">
          {identity.avatarUri && !avatarFailed ? <img src={identity.avatarUri} alt="" onError={() => setAvatarFailed(true)}/> : userInitials(identity.name)}
        </span>
        <span className="hl-user-menu__trigger-name">{identity.name}</span>
      </button>
      {open ? (
        <div ref={panelRef} id={surfaceId} role={customPattern ? 'dialog' : 'menu'} aria-label={labels.panel} className="hl-user-menu__surface" data-placement={placement} data-alignment={alignment} onKeyDown={onPanelKeyDown}>
          <header className="hl-user-menu__identity">
            <strong>{identity.name}</strong>{identity.email ? <span>{identity.email}</span> : null}{identity.role ? <span>{identity.role}</span> : null}
          </header>
          <div className="hl-user-menu__items">
            {items.map((item, index) => {
              if (item.kind === 'separator') return <hr key={item.id ?? `separator-${index}`} role={customPattern ? undefined : 'separator'}/>
              if (item.kind === 'custom') return <div key={item.id} className="hl-user-menu__custom" data-item-id={item.id} onClick={() => closeAfterSelection(item.closeOnSelect)}>{item.content}</div>
              const content = <>{item.icon ? <span aria-hidden="true">{item.icon}</span> : null}<span><span>{item.label}</span>{item.subtitle ? <small>{item.subtitle}</small> : null}</span></>
              const common = {
                ref: (element: HTMLElement | null) => { if (element) itemRefs.current.set(item.id, element); else itemRefs.current.delete(item.id) },
                role: customPattern ? undefined : 'menuitem',
                tabIndex: customPattern ? undefined : focusId === item.id ? 0 : -1,
                'aria-disabled': item.disabled || undefined,
                onFocus: () => setFocusId(item.id),
                className: 'hl-user-menu__item',
              }
              if (item.kind === 'link') return <a {...common} key={item.id} href={item.disabled ? undefined : item.destination} onClick={event => { if (item.disabled) event.preventDefault(); else closeAfterSelection(item.closeOnSelect) }}>{content}</a>
              return <button {...common} key={item.id} type="button" disabled={item.disabled} onClick={() => activateAction(item)}>{content}</button>
            })}
            {onSignOut ? <button ref={element => { if (element) itemRefs.current.set('__sign-out', element); else itemRefs.current.delete('__sign-out') }} type="button" role={customPattern ? undefined : 'menuitem'} tabIndex={customPattern ? undefined : focusId === '__sign-out' ? 0 : -1} className="hl-user-menu__item hl-user-menu__sign-out" onFocus={() => setFocusId('__sign-out')} onClick={() => { onSignOut(); requestOpen(false, true) }}>{labels.signOut}</button> : null}
          </div>
        </div>
      ) : null}
    </div>
  )
}
