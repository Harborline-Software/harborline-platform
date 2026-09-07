import { useCallback, useMemo, useState } from 'react'

/** Immutable, projection-neutral snapshot of builder layer state. */
export interface LayersSnapshot {
  readonly activeId: string | null
  readonly enabledIds: ReadonlySet<string>
}

export type LayersCommand =
  | { readonly type: 'activate'; readonly id: string }
  | { readonly type: 'clear' }
  | { readonly type: 'toggle'; readonly id: string }

/** React-facing compatibility surface from the pinned Harborline App implementation. */
export interface LayersState extends LayersSnapshot {
  setActive: (id: string) => void
  clearActive: () => void
  toggleEnabled: (id: string) => void
  isEnabled: (id: string) => boolean
  isActive: (id: string) => boolean
}

export interface LayerIdentity {
  readonly id: string
}

/**
 * Creates the initial snapshot. Omission selects the first lens; an explicit
 * `null` deliberately selects the neutral base state.
 */
export function createLayersState(
  lensIds: readonly string[],
  initialActiveId?: string | null,
): LayersSnapshot {
  const activeId = initialActiveId === undefined ? (lensIds[0] ?? null) : initialActiveId
  return {
    activeId,
    enabledIds: new Set(activeId === null ? [] : [activeId]),
  }
}

/** Pure transition function shared by hooks and non-React projections. */
export function reduceLayersState(
  state: LayersSnapshot,
  command: LayersCommand,
): LayersSnapshot {
  switch (command.type) {
    case 'activate': {
      if (state.activeId === command.id && state.enabledIds.has(command.id)) return state
      const enabledIds = new Set(state.enabledIds)
      enabledIds.add(command.id)
      return { activeId: command.id, enabledIds }
    }
    case 'clear':
      return state.activeId === null ? state : { activeId: null, enabledIds: state.enabledIds }
    case 'toggle': {
      const enabledIds = new Set(state.enabledIds)
      if (enabledIds.has(command.id)) enabledIds.delete(command.id)
      else enabledIds.add(command.id)
      return {
        activeId: state.activeId === command.id ? null : state.activeId,
        enabledIds,
      }
    }
  }
}

/**
 * React projection over the pure reducer. The host owns the lens list and all
 * persistence, subscriptions, rendering, and builder-document state.
 */
export function useLayers(
  lenses: readonly LayerIdentity[],
  initialActiveId?: string | null,
): LayersState {
  const [snapshot, setSnapshot] = useState<LayersSnapshot>(() =>
    createLayersState(lenses.map(lens => lens.id), initialActiveId),
  )

  const setActive = useCallback((id: string) => {
    setSnapshot(current => reduceLayersState(current, { type: 'activate', id }))
  }, [])
  const clearActive = useCallback(() => {
    setSnapshot(current => reduceLayersState(current, { type: 'clear' }))
  }, [])
  const toggleEnabled = useCallback((id: string) => {
    setSnapshot(current => reduceLayersState(current, { type: 'toggle', id }))
  }, [])
  const isEnabled = useCallback((id: string) => snapshot.enabledIds.has(id), [snapshot.enabledIds])
  const isActive = useCallback((id: string) => snapshot.activeId === id, [snapshot.activeId])

  return useMemo(
    () => ({ ...snapshot, setActive, clearActive, toggleEnabled, isEnabled, isActive }),
    [snapshot, setActive, clearActive, toggleEnabled, isEnabled, isActive],
  )
}
