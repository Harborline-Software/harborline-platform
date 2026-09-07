import * as React from 'react'

import { useMediaQuery } from '@harborline-platform/hlp.ui.use-media-query'

const DEFAULT_BREAKPOINT = 768

export interface UseNavCollapsedOptions {
  collapsed?: boolean
  onCollapsedChange?: (collapsed: boolean) => void
  defaultCollapsed?: boolean
  autoCollapseBelow?: number
  overlayBelow?: number
}

export interface UseNavCollapsedResult {
  collapsed: boolean
  toggle: () => void
  setCollapsed: (collapsed: boolean) => void
  isOverlay: boolean
}

function maximumWidthQuery(threshold: number): string {
  return `(max-width: ${Math.max(0, threshold - 1)}px)`
}

/**
 * Resolve controlled or uncontrolled navigation collapse state. Persistence,
 * shortcuts, and navigation rendering intentionally remain host-owned.
 */
export function useNavCollapsed(
  options: UseNavCollapsedOptions = {},
): UseNavCollapsedResult {
  const {
    collapsed: controlledCollapsed,
    onCollapsedChange,
    defaultCollapsed = false,
    autoCollapseBelow = DEFAULT_BREAKPOINT,
    overlayBelow = DEFAULT_BREAKPOINT,
  } = options

  const isControlled = controlledCollapsed !== undefined
  const [internalCollapsed, setInternalCollapsed] = React.useState(defaultCollapsed)
  const [userOverrode, setUserOverrode] = React.useState(false)

  const autoMatch = useMediaQuery(maximumWidthQuery(autoCollapseBelow))
  const overlayMatch = useMediaQuery(maximumWidthQuery(overlayBelow))
  const isNarrow = autoCollapseBelow > 0 && autoMatch
  const isOverlay = overlayBelow > 0 && overlayMatch
  const baseCollapsed = isControlled ? controlledCollapsed : internalCollapsed
  const effectiveCollapsed = !userOverrode && isNarrow ? true : baseCollapsed

  const previousNarrowRef = React.useRef(isNarrow)
  React.useEffect(() => {
    const crossedToNarrow = isNarrow && !previousNarrowRef.current
    previousNarrowRef.current = isNarrow
    if (
      isControlled
      && crossedToNarrow
      && !userOverrode
      && !controlledCollapsed
    ) {
      onCollapsedChange?.(true)
    }
  }, [controlledCollapsed, isControlled, isNarrow, onCollapsedChange, userOverrode])

  const setCollapsed = React.useCallback((next: boolean) => {
    setUserOverrode(true)
    if (!isControlled) setInternalCollapsed(next)
    onCollapsedChange?.(next)
  }, [isControlled, onCollapsedChange])

  const toggle = React.useCallback(() => {
    setCollapsed(!effectiveCollapsed)
  }, [effectiveCollapsed, setCollapsed])

  return { collapsed: effectiveCollapsed, toggle, setCollapsed, isOverlay }
}
