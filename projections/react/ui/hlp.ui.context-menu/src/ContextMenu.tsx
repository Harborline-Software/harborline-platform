import * as React from 'react'

export interface ContextMenuItem {
  id: string
  label: React.ReactNode
  icon?: React.ReactNode
  disabled?: boolean
  danger?: boolean
  onSelect: () => void
}

export interface ContextMenuGroup { items: readonly ContextMenuItem[] }

export interface ContextMenuProps {
  groups: readonly ContextMenuGroup[]
  children: React.ReactNode
  className?: string
  accessibleLabel?: string
  locale?: string
  direction?: 'ltr' | 'rtl'
}

interface Position { x: number; y: number }

export function clampContextMenuPosition(position: Position, menu: {width: number; height: number}, viewport: {width: number; height: number}, inset = 8): Position {
  return {
    x: Math.max(inset, Math.min(position.x, viewport.width - menu.width - inset)),
    y: Math.max(inset, Math.min(position.y, viewport.height - menu.height - inset)),
  }
}

export function ContextMenu({groups, children, className = '', accessibleLabel = 'Context menu', locale, direction}: ContextMenuProps) {
  const items = React.useMemo(() => groups.flatMap(group => group.items), [groups])
  const enabled = React.useMemo(() => items.map((item, index) => item.disabled ? -1 : index).filter(index => index >= 0), [items])
  const [position, setPosition] = React.useState<Position | null>(null)
  const [activeIndex, setActiveIndex] = React.useState(-1)
  const triggerRef = React.useRef<HTMLDivElement>(null)
  const menuRef = React.useRef<HTMLDivElement>(null)
  // The element that had focus when the menu opened, by keyboard or by pointer: every close returns
  // focus there (APG menu pattern). <body> is no place to return to, so it is not recorded.
  const opener = React.useRef<HTMLElement | null>(null)

  if (!accessibleLabel.trim()) throw new Error('accessible-name-required')
  const duplicate = items.find((item, index) => items.findIndex(candidate => candidate.id === item.id) !== index)
  if (duplicate) throw new Error(`duplicate-item-id: ${duplicate.id}`)

  const open = React.useCallback((next: Position) => {
    const active = document.activeElement
    opener.current = active instanceof HTMLElement && active !== document.body ? active : null
    setPosition(next)
    setActiveIndex(enabled[0] ?? -1)
  }, [enabled])

  const close = React.useCallback((restoreFocus = true) => {
    setPosition(null)
    setActiveIndex(-1)
    if (restoreFocus) queueMicrotask(() => opener.current?.focus())
  }, [])

  const move = React.useCallback((kind: 'next' | 'previous' | 'first' | 'last') => {
    if (!enabled.length) return
    const slot = enabled.indexOf(activeIndex)
    const next = kind === 'first' ? enabled[0]
      : kind === 'last' ? enabled.at(-1)!
      : kind === 'next' ? enabled[(slot + 1 + enabled.length) % enabled.length]
      : enabled[(slot - 1 + enabled.length) % enabled.length]
    setActiveIndex(next)
  }, [activeIndex, enabled])

  React.useLayoutEffect(() => {
    if (!position || !menuRef.current) return
    const bounds = menuRef.current.getBoundingClientRect()
    const clamped = clampContextMenuPosition(position, bounds, {width: window.innerWidth, height: window.innerHeight})
    if (clamped.x !== position.x || clamped.y !== position.y) setPosition(clamped)
  }, [position])

  React.useEffect(() => {
    if (!position) return
    const item = menuRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]')[activeIndex]
    ;(item ?? menuRef.current)?.focus()
    // An outside press closes the menu and lets focus go where the press puts it. The press's default
    // action moves focus only after this listener returns, so a restore queued now would be undone:
    // the opener gets focus back afterwards, and only when the press landed on nothing focusable.
    const outside = (event: MouseEvent) => {
      if (menuRef.current?.contains(event.target as Node)) return
      const returnTo = opener.current
      close(false)
      setTimeout(() => {
        if (!document.activeElement || document.activeElement === document.body) returnTo?.focus()
      }, 0)
    }
    document.addEventListener('mousedown', outside)
    return () => document.removeEventListener('mousedown', outside)
  }, [activeIndex, close, position])

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (!position && ((event.shiftKey && event.key === 'F10') || event.key === 'ContextMenu')) {
      event.preventDefault()
      const bounds = triggerRef.current?.getBoundingClientRect()
      open({x: bounds?.left ?? 8, y: bounds?.bottom ?? 8})
      return
    }
    if (!position) return
    if (event.key === 'Escape') { event.preventDefault(); close(); return }
    if (event.key === 'ArrowDown') { event.preventDefault(); move('next'); return }
    if (event.key === 'ArrowUp') { event.preventDefault(); move('previous'); return }
    if (event.key === 'Home') { event.preventDefault(); move('first'); return }
    if (event.key === 'End') { event.preventDefault(); move('last'); return }
    if ((event.key === 'Enter' || event.key === ' ') && activeIndex >= 0) {
      event.preventDefault()
      const item = items[activeIndex]
      if (!item.disabled) { item.onSelect(); close() }
    }
  }

  return <>
    <div ref={triggerRef} className={className} onKeyDown={onKeyDown} onContextMenu={event => {
      event.preventDefault()
      event.stopPropagation()
      open({x: event.clientX, y: event.clientY})
    }}>{children}</div>
    {position && <div ref={menuRef} role="menu" tabIndex={-1} aria-label={accessibleLabel} lang={locale} dir={direction}
      data-active-id={activeIndex >= 0 ? items[activeIndex]?.id : undefined}
      style={{position: 'fixed', left: position.x, top: position.y, zIndex: 9999}}
      className="hl-context-menu" onKeyDown={onKeyDown}>
      {groups.map((group, groupIndex) => <React.Fragment key={groupIndex}>
        {groupIndex > 0 && <div role="separator" className="hl-context-menu__separator" />}
        {group.items.map(item => {
          const index = items.indexOf(item)
          return <button key={item.id} type="button" role="menuitem" tabIndex={index === activeIndex ? 0 : -1}
            disabled={item.disabled} data-danger={item.danger || undefined} data-active={index === activeIndex || undefined}
            className="hl-context-menu__item" onClick={() => { if (!item.disabled) { item.onSelect(); close() } }}>
            {item.icon && <span aria-hidden="true" className="hl-context-menu__icon">{item.icon}</span>}
            <span>{item.label}</span>
          </button>
        })}
      </React.Fragment>)}
    </div>}
  </>
}
