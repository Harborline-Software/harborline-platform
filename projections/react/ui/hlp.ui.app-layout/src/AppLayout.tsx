import * as React from 'react'

export const APP_LAYOUT_RAIL_QUERY = '(min-width: 840px)'
export type SideNavMode = 'rail' | 'overlay' | 'hidden' | 'auto'
export type ContentScroll = 'main' | 'page'

export interface AppLayoutProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  body: React.ReactNode
  header?: React.ReactNode
  sideNav?: React.ReactNode
  sideNavMode?: SideNavMode
  sideNavOpen?: boolean
  mobileNavOpen?: boolean
  defaultMobileNavOpen?: boolean
  onMobileNavOpenChange?: (open: boolean) => void
  headerFixed?: boolean
  contentScroll?: ContentScroll
  mobileNavLabel?: string
  railCapable?: boolean
}

function useRailCapability(override: boolean | undefined) {
  const [matches, setMatches] = React.useState(() => override ?? (typeof window !== 'undefined' && window.matchMedia(APP_LAYOUT_RAIL_QUERY).matches))
  React.useEffect(() => {
    if (override !== undefined) {
      setMatches(override)
      return
    }
    const query = window.matchMedia(APP_LAYOUT_RAIL_QUERY)
    const update = (event: MediaQueryListEvent) => setMatches(event.matches)
    setMatches(query.matches)
    query.addEventListener('change', update)
    return () => query.removeEventListener('change', update)
  }, [override])
  return matches
}

function focusableElements(root: HTMLElement) {
  return [...root.querySelectorAll<HTMLElement>('a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')]
    .filter(element => !element.hasAttribute('hidden') && element.getAttribute('aria-hidden') !== 'true')
}

export function AppLayout({
  body, header, sideNav, sideNavMode = 'auto', sideNavOpen = true,
  mobileNavOpen: controlledMobileOpen, defaultMobileNavOpen = false,
  onMobileNavOpenChange, headerFixed = false, contentScroll = 'main',
  mobileNavLabel = 'Navigation', railCapable: railCapableOverride,
  className, ...attributes
}: AppLayoutProps) {
  if (body === null || body === undefined) throw new Error('app-layout-body-required')
  if (!(['rail', 'overlay', 'hidden', 'auto'] as const).includes(sideNavMode)) throw new Error('unsupported-side-nav-mode')
  if (!(['main', 'page'] as const).includes(contentScroll)) throw new Error('unsupported-content-scroll')
  const railCapable = useRailCapability(railCapableOverride)
  const [localMobileOpen, setLocalMobileOpen] = React.useState(defaultMobileNavOpen)
  const mobileOpen = controlledMobileOpen ?? localMobileOpen
  const triggerRef = React.useRef<HTMLButtonElement>(null)
  const drawerRef = React.useRef<HTMLDivElement>(null)
  const wasRailCapable = React.useRef(railCapable)
  const drawerId = React.useId()
  const navigationAvailable = sideNavMode !== 'hidden' && sideNav !== null && sideNav !== undefined
  const useRail = navigationAvailable && sideNavMode !== 'overlay' && railCapable && sideNavOpen
  const useDrawer = navigationAvailable && (sideNavMode === 'overlay' || !railCapable)

  const requestMobileOpen = React.useCallback((next: boolean, restoreFocus = false) => {
    if (controlledMobileOpen === undefined) setLocalMobileOpen(next)
    onMobileNavOpenChange?.(next)
    if (!next && restoreFocus) queueMicrotask(() => triggerRef.current?.focus())
  }, [controlledMobileOpen, onMobileNavOpenChange])

  React.useEffect(() => {
    const crossedIntoRail = !wasRailCapable.current && railCapable
    wasRailCapable.current = railCapable
    if (crossedIntoRail && mobileOpen && sideNavMode !== 'overlay') requestMobileOpen(false)
  }, [mobileOpen, railCapable, requestMobileOpen, sideNavMode])

  React.useEffect(() => {
    if (!useDrawer || !mobileOpen) return
    queueMicrotask(() => {
      const drawer = drawerRef.current
      if (!drawer) return
      const target = focusableElements(drawer).find(element => !element.hasAttribute('data-hl-drawer-close')) ?? drawer.querySelector<HTMLElement>('[data-hl-drawer-close]')
      target?.focus()
    })
  }, [mobileOpen, useDrawer])

  const onDrawerKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      requestMobileOpen(false, true)
      return
    }
    if (event.key !== 'Tab' || !drawerRef.current) return
    const focusable = focusableElements(drawerRef.current)
    if (focusable.length === 0) {
      event.preventDefault()
      return
    }
    const current = focusable.indexOf(document.activeElement as HTMLElement)
    if (event.shiftKey && current <= 0) {
      event.preventDefault()
      focusable.at(-1)?.focus()
    } else if (!event.shiftKey && current === focusable.length - 1) {
      event.preventDefault()
      focusable[0].focus()
    }
  }

  return (
    <div {...attributes} className={`hl-app-layout${headerFixed ? ' hl-app-layout--fixed-header' : ''} hl-app-layout--scroll-${contentScroll}${className ? ` ${className}` : ''}`} data-side-nav-mode={sideNavMode} data-content-scroll={contentScroll}>
      {useDrawer ? <button ref={triggerRef} type="button" className="hl-app-layout__nav-trigger" aria-label={mobileNavLabel} aria-expanded={mobileOpen} aria-controls={drawerId} onClick={() => requestMobileOpen(!mobileOpen, mobileOpen)}><svg aria-hidden="true" width="18" height="18" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="1.75" viewBox="0 0 20 20"><path d="M3 5.5h14M3 10h14M3 14.5h14" /></svg></button> : null}
      <div className="hl-app-layout__frame">
        {header !== null && header !== undefined ? <header className="hl-app-layout__header">{header}</header> : null}
        {useRail ? <nav aria-label={mobileNavLabel} className="hl-app-layout__rail" data-open="true">{sideNav}</nav> : null}
        <main id="main" className="hl-app-layout__main">{body}</main>
      </div>
      {useDrawer && mobileOpen ? (
        <div className="hl-app-layout__overlay" onPointerDown={event => { if (event.target === event.currentTarget) requestMobileOpen(false, true) }}>
          <div ref={drawerRef} id={drawerId} role="dialog" aria-modal="true" aria-label={mobileNavLabel} className="hl-app-layout__drawer" onKeyDown={onDrawerKeyDown}>
            <button type="button" data-hl-drawer-close aria-label={`Close ${mobileNavLabel}`} className="hl-app-layout__drawer-close" onClick={() => requestMobileOpen(false, true)}><svg aria-hidden="true" width="18" height="18" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="1.75" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg></button>
            <nav aria-label={mobileNavLabel}>{sideNav}</nav>
          </div>
        </div>
      ) : null}
    </div>
  )
}
