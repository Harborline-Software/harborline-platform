import * as React from 'react'

export interface ShellStorage { get(key: string): Promise<string | null>; set(key: string, value: string): Promise<void> }

export function persistenceKey(shellId: string, ...parts: string[]) { return ['hlp-app-shell:v1', shellId, ...parts].join(':') }

export async function readRecord<T>(storage: ShellStorage | undefined, key: string, valid: (v: unknown) => v is T): Promise<T | null> {
  if (!storage) return null
  try {
    const raw = await storage.get(key)
    if (raw === null || raw === undefined) return null
    const parsed = JSON.parse(raw) as { v?: number; value?: unknown }
    return parsed?.v === 1 && valid(parsed.value) ? parsed.value : null
  } catch { return null }
}

export function writeRecord(storage: ShellStorage | undefined, key: string, value: unknown) {
  if (!storage) return
  try { void storage.set(key, JSON.stringify({ v: 1, value })).catch(() => undefined) } catch { /* fire-and-forget */ }
}

export function useShellAxis<T>(controlled: T | undefined, fallback: T, storageKey: string | null, storage: ShellStorage | undefined, valid: (v: unknown) => v is T): [T, (next: T) => void] {
  const [local, setLocal] = React.useState(fallback)
  const touched = React.useRef(false)
  React.useEffect(() => {
    if (controlled !== undefined || !storageKey) return
    let live = true
    void readRecord(storage, storageKey, valid).then(restored => { if (live && restored !== null && !touched.current) setLocal(restored) })
    return () => { live = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- restore runs once per key
  }, [storageKey])
  const set = React.useCallback((next: T) => {
    touched.current = true
    if (controlled === undefined) setLocal(next)
    if (storageKey) writeRecord(storage, storageKey, next)
  }, [controlled, storage, storageKey])
  return [controlled === undefined ? local : controlled, set]
}

const EDITABLE = new Set(['INPUT', 'TEXTAREA', 'SELECT'])
export function normalizeShortcut(event: KeyboardEvent): string | null {
  if (!event.ctrlKey && !event.metaKey) return null
  const key = event.key.length === 1 ? event.key.toUpperCase() : event.key
  return `Mod+${event.shiftKey ? 'Shift+' : ''}${key}`
}

export interface ShellShortcutHandlers {
  commandSurface?: string | null; create?: string | null; toggleRail?: string | null; inspector?: string | null
  onCommandSurface?: () => void; onCreate?: () => void; onToggleRail?: () => void; onInspector?: () => void
  onWorkspace?: (index: number) => void
  /** @deprecated Compatibility aliases. */ toggleNavigation?: string | null; openSearch?: string | null; onToggleNavigation?: () => void; onOpenSearch?: () => void
}

export function useShellShortcuts({ commandSurface, create = 'Mod+N', toggleRail, inspector = 'Mod+Shift+I', onCommandSurface, onCreate = () => undefined, onToggleRail, onInspector = () => undefined, onWorkspace = () => undefined, toggleNavigation, openSearch, onToggleNavigation, onOpenSearch }: ShellShortcutHandlers) {
  if (commandSurface === undefined) commandSurface = openSearch === undefined ? 'Mod+K' : openSearch
  if (toggleRail === undefined) toggleRail = toggleNavigation === undefined ? (onToggleNavigation ? 'Mod+B' : 'Mod+\\') : toggleNavigation
  onCommandSurface ??= onOpenSearch ?? (() => undefined); onToggleRail ??= onToggleNavigation ?? (() => undefined)
  const handlers = React.useRef({ commandSurface, create, toggleRail, inspector, onCommandSurface, onCreate, onToggleRail, onInspector, onWorkspace })
  handlers.current = { commandSurface, create, toggleRail, inspector, onCommandSurface, onCreate, onToggleRail, onInspector, onWorkspace }
  React.useEffect(() => {
    const listener = (event: KeyboardEvent) => {
      if (event.defaultPrevented) return
      const target = event.target as HTMLElement | null
      if (target && (EDITABLE.has(target.tagName) || target.isContentEditable)) return
      const token = normalizeShortcut(event)
      if (!token) return
      const current = handlers.current
      let invoke: (() => void) | undefined
      if (current.toggleRail !== null && token === current.toggleRail) invoke = current.onToggleRail
      else if (current.commandSurface !== null && token === current.commandSurface) invoke = current.onCommandSurface
      else if (current.create !== null && token === current.create) invoke = current.onCreate
      else if (current.inspector !== null && token === current.inspector) invoke = current.onInspector
      else if (/^Mod\+[1-9]$/.test(token)) invoke = () => current.onWorkspace(Number(token.at(-1)) - 1)
      if (invoke) { event.preventDefault(); invoke() }
    }
    document.addEventListener('keydown', listener)
    return () => document.removeEventListener('keydown', listener)
  }, [])
}
