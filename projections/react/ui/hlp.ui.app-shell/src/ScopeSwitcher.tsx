import * as React from 'react'
import { persistenceKey, useShellAxis, type ShellStorage } from './shell-state'
import type { ShellScopeDirectory, ShellScopeOption } from './types'

export function orderScopeOptions(options: readonly ShellScopeOption[], activeId: string | undefined, pinnedIds: readonly string[]): ShellScopeOption[] {
  const active = options.filter(o => o.id === activeId)
  const pinned = pinnedIds.map(id => options.find(o => o.id === id)).filter((o): o is ShellScopeOption => o !== undefined && o.id !== activeId)
  const rest = options.filter(o => o.id !== activeId && !pinnedIds.includes(o.id))
  return [...active, ...pinned, ...rest]
}

export interface ScopeSwitcherProps {
  switcherId: string; shellId: string; storage?: ShellStorage
  options: readonly ShellScopeOption[]
  activeId?: string; defaultActiveId?: string; onActiveChange?: (id: string) => void
  pinnedIds?: readonly string[]; defaultPinnedIds?: readonly string[]; onPinnedChange?: (ids: readonly string[]) => void
  pinCap?: number; scopeLabel: string; directory?: ShellScopeDirectory
  presentation: 'rail' | 'header'
  pinLabel?: string; unpinLabel?: string; filterLabel?: string
}

const isString = (v: unknown): v is string => typeof v === 'string'
const isStringArray = (v: unknown): v is string[] => Array.isArray(v) && v.every(isString)

export function ScopeSwitcher({ switcherId, shellId, storage, options, activeId, defaultActiveId, onActiveChange, pinnedIds, defaultPinnedIds, onPinnedChange, pinCap = 4, scopeLabel, directory, presentation, pinLabel = 'Pin', unpinLabel = 'Unpin', filterLabel = 'Filter' }: ScopeSwitcherProps) {
  const [open, setOpen] = React.useState(false)
  const [query, setQuery] = React.useState('')
  const [active, setActive] = useShellAxis<string>(activeId, defaultActiveId ?? options[0]?.id ?? '', persistenceKey(shellId, 'switcher', switcherId, 'active'), storage, isString)
  const [pins, setPins] = useShellAxis<readonly string[]>(pinnedIds, defaultPinnedIds ?? [], persistenceKey(shellId, 'switcher', switcherId, 'pins'), storage, isStringArray)
  const triggerRef = React.useRef<HTMLButtonElement>(null)
  const rootRef = React.useRef<HTMLDivElement>(null)
  const close = React.useCallback((restoreFocus: boolean) => { setOpen(false); setQuery(''); if (restoreFocus) queueMicrotask(() => triggerRef.current?.focus()) }, [])
  React.useEffect(() => {
    if (!open) return
    const onPointer = (event: PointerEvent) => { if (rootRef.current && !rootRef.current.contains(event.target as Node)) close(false) }
    document.addEventListener('pointerdown', onPointer)
    return () => document.removeEventListener('pointerdown', onPointer)
  }, [open, close])

  if (options.length === 0) return null
  const current = options.find(o => o.id === active) ?? options[0]
  if (options.length === 1) {
    if (presentation === 'rail') return null
    return <div className="hl-app-shell__switcher-static" data-switcher={switcherId}>{monogram(current)}<span className="hl-app-shell__switcher-label">{current.label}</span></div>
  }
  const ordered = orderScopeOptions(options, current.id, pins)
  const visible = query ? ordered.filter(o => o.label.toLowerCase().includes(query.toLowerCase())) : ordered
  const togglePin = (option: ShellScopeOption) => {
    const isPinned = pins.includes(option.id)
    if (!isPinned && pins.length >= pinCap && pinnedIds === undefined) return
    const next = isPinned ? pins.filter(id => id !== option.id) : [...pins, option.id]
    if (!isPinned && next.length > pinCap && pinnedIds === undefined) return
    setPins(next); onPinnedChange?.(next)
  }
  return (
    <div ref={rootRef} className="hl-app-shell__switcher" data-switcher={switcherId}>
      <button ref={triggerRef} type="button" className="hl-app-shell__switcher-trigger" title={current.label} aria-haspopup="menu" aria-expanded={open} onClick={() => (open ? close(false) : setOpen(true))}>
        {monogram(current)}<span className="hl-app-shell__switcher-label">{current.label}</span>
        <span aria-hidden="true">⌄</span><span className="hl-visually-hidden">{scopeLabel}</span>
      </button>
      {open ? (
        <div className="hl-app-shell__switcher-menu" data-shell-scroll-region onKeyDown={event => { if (event.key === 'Escape') { event.preventDefault(); close(true) } }}>
          {options.length > 7 ? <input className="hl-app-shell__switcher-filter" aria-label={filterLabel} value={query} onChange={event => setQuery(event.target.value)} /> : null}
          {/* the filter input sits beside the menu, not inside it: role="menu" only allows menu-item children */}
          <div role="menu" aria-label={scopeLabel}>
          {visible.map(option => {
            const isPinned = pins.includes(option.id)
            return (
              <div key={option.id} role="menuitemradio" aria-checked={option.id === current.id} tabIndex={0} className="hl-app-shell__switcher-option"
                   onClick={() => { if (option.id !== current.id) { setActive(option.id); onActiveChange?.(option.id) } close(false) }}
                   onKeyDown={event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); if (option.id !== current.id) { setActive(option.id); onActiveChange?.(option.id) } close(true) } }}>
                {monogram(option)}<span className="hl-app-shell__switcher-label">{option.label}</span>
                {option.pinnable === false ? null : (
                  // Pointer-only, presentational pin affordance: a menu-item row allows no nested
                  // interactive or AT-visible content, so this is a span, not a button. Keyboard and
                  // AT users pin from the rail's sibling pin controls.
                  <span aria-hidden="true" className="hl-app-shell__pin-toggle" title={`${isPinned ? unpinLabel : pinLabel} ${option.label}`}
                        onClick={event => { event.stopPropagation(); togglePin(option) }}>
                    <svg aria-hidden="true" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d={isPinned ? 'M12 17v5M15 9.34V6h1a2 2 0 0 0 0-4H7.89M2 2l20 20M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11' : 'M12 17v5M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V6h1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4h1z'} /></svg>
                  </span>
                )}
              </div>
            )
          })}
          {directory ? <div role="menuitem" tabIndex={0} className="hl-app-shell__switcher-option" onClick={() => { directory.invoke(); close(false) }} onKeyDown={event => { if (event.key === 'Enter') { directory.invoke(); close(true) } }}>{directory.label}</div> : null}
          </div>
        </div>
      ) : null}
    </div>
  )
}

function monogram(option: ShellScopeOption) {
  if (option.icon) return <span className="hl-app-shell__monogram" aria-hidden="true">{option.icon}</span>
  return <span className="hl-app-shell__monogram" aria-hidden="true">{(option.monogram ?? option.label.split(/\s+/).map(word => word[0] ?? '').join('').slice(0, 2)).toUpperCase()}</span>
}
