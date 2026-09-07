import * as React from 'react'

import { useCanShowMasterDetail } from '@harborline-platform/hlp.ui.use-can-show-master-detail'

/** drilldown-model.md:18 — a facet is the same object seen differently; it is a place, so it is
 *  addressable by id and carries the count from the same query (`:44`). */
export interface DetailPanelFacet {
  readonly id: string
  readonly label: string
  readonly count: number
}

/** drilldown-model.md:22 — following one crosses to a *different* object. */
export interface DetailPanelRelation {
  readonly id: string
  readonly label: string
  readonly targetId: string
}

export interface DetailPanelSubject {
  readonly id: string
  readonly title: string
  /** drilldown-model.md:23 — the route `Open as page` promotes to; the host owns navigation. */
  readonly route: string
  readonly facets: readonly DetailPanelFacet[]
  readonly relations?: readonly DetailPanelRelation[]
}

export interface DetailPanelProps extends Omit<React.HTMLAttributes<HTMLElement>, 'children'> {
  label: string
  children?: React.ReactNode
  open?: boolean
  defaultOpen?: boolean
  onOpenChange?: (open: boolean) => void
  width?: number
  dismissible?: boolean
  closeLabel?: string
  railCapable?: boolean
  subject?: DetailPanelSubject
  /** Level 2. drilldown-model.md:22 — exactly one stack level; the host owns the stack. */
  pushed?: DetailPanelSubject | null
  activeFacetId?: string
  onFacetChange?: (facetId: string) => void
  onFollowRelation?: (relation: DetailPanelRelation) => void
  onBack?: () => void
  onOpenAsPage?: (route: string) => void
  backLabelPrefix?: string
  openAsPageLabel?: string
  facetsLabel?: string
}

const TAB_KEYS = ['ArrowRight', 'ArrowLeft', 'Home', 'End'] as const

function validateSubject(subject: DetailPanelSubject) {
  if (subject.facets.length === 0) throw new Error('detail-panel-facets-required')
  if (new Set(subject.facets.map(facet => facet.id)).size !== subject.facets.length) {
    throw new Error('detail-panel-duplicate-facet-id')
  }
}

/** The remembered facet is keyed by the subject's declared id, never by the object reference:
 *  a re-materialised equal subject keeps its facet, a different id starts at the first one. */
function useRememberedFacet(subject: DetailPanelSubject | null | undefined) {
  const key = subject?.id ?? null
  const [remembered, setRemembered] = React.useState<{ key: string | null; facetId: string | null }>({ key, facetId: null })
  const current = remembered.key === key ? remembered : { key, facetId: null }
  if (current !== remembered) setRemembered(current)
  return [current.facetId, (facetId: string) => setRemembered({ key, facetId })] as const
}

function focusableElements(root: HTMLElement) {
  return [...root.querySelectorAll<HTMLElement>('a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])')]
    .filter(element => !element.hasAttribute('hidden') && element.getAttribute('aria-hidden') !== 'true')
}

export function DetailPanel({
  label,
  children,
  open: controlledOpen,
  defaultOpen = false,
  onOpenChange,
  width = 360,
  dismissible = true,
  closeLabel = 'Close',
  railCapable: railCapableOverride,
  subject,
  pushed = null,
  activeFacetId: controlledFacetId,
  onFacetChange,
  onFollowRelation,
  onBack,
  onOpenAsPage,
  backLabelPrefix = 'Back to ',
  openAsPageLabel = 'Open as page',
  facetsLabel = 'Facets',
  className,
  style,
  ...attributes
}: DetailPanelProps): React.JSX.Element | null {
  const tabBaseId = React.useId()
  const [rememberedRootFacet, rememberRootFacet] = useRememberedFacet(subject)
  const [rememberedPushedFacet, rememberPushedFacet] = useRememberedFacet(pushed)
  const tabRefs = React.useRef<Array<HTMLButtonElement | null>>([])
  const pendingTabFocus = React.useRef<string | null>(null)
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen)
  const renderedOpen = controlledOpen ?? uncontrolledOpen
  // One decider (ticket 154): the default docked-versus-modal gate IS the capability policy —
  // the panel CALLS useCanShowMasterDetail rather than re-evaluating its rail query. The
  // explicit railCapable prop remains the test/override seam.
  const policyRailCapable = useCanShowMasterDetail()
  const railCapable = railCapableOverride ?? policyRailCapable
  const dialogRef = React.useRef<HTMLDivElement>(null)
  const restoreFocusRef = React.useRef<HTMLElement | null>(null)
  const wasModalOpen = React.useRef(false)

  if (renderedOpen && subject === undefined && (children === null || children === undefined)) {
    throw new Error('detail-panel-content-required')
  }
  if (renderedOpen) {
    if (pushed !== null && subject === undefined) throw new Error('detail-panel-push-requires-object')
    if (subject) validateSubject(subject)
    if (pushed) validateSubject(pushed)
  }

  // drilldown-model.md:36 — one component: the frame is the same whichever level it holds.
  const current = pushed ?? subject
  const remembered = pushed ? rememberedPushedFacet : rememberedRootFacet
  const rememberFacet = pushed ? rememberPushedFacet : rememberRootFacet
  const facets = current?.facets ?? []
  const activeFacetId = current
    ? controlledFacetId ?? (facets.some(facet => facet.id === remembered) ? remembered! : facets[0].id)
    : undefined
  if (current && !facets.some(facet => facet.id === activeFacetId)) throw new Error('detail-panel-facet-unknown')

  const selectFacet = (facetId: string) => {
    if (facetId === activeFacetId) return
    if (controlledFacetId === undefined) rememberFacet(facetId)
    onFacetChange?.(facetId)
  }

  const onTabListKeyDown = (event: React.KeyboardEvent) => {
    // Only the keys this tab bar handles are consumed; Escape, Tab and every printable key
    // pass through to the surrounding shell untouched, and nothing stops propagation.
    if (!(TAB_KEYS as readonly string[]).includes(event.key)) return
    event.preventDefault()
    const index = facets.findIndex(facet => facet.id === activeFacetId)
    const next = event.key === 'Home' ? 0
      : event.key === 'End' ? facets.length - 1
        : (index + (event.key === 'ArrowRight' ? 1 : -1) + facets.length) % facets.length
    pendingTabFocus.current = facets[next].id
    selectFacet(facets[next].id)
  }

  // Roving tabindex: keyboard selection moves DOM focus with it, once, after the render that
  // moved the selection.
  React.useLayoutEffect(() => {
    const pending = pendingTabFocus.current
    if (pending === null) return
    pendingTabFocus.current = null
    tabRefs.current[facets.findIndex(facet => facet.id === pending)]?.focus()
  })

  const drilldown = current ? (
    <>
      <header className="hl-detail-panel__header">
        {pushed ? (
          <button type="button" className="hl-detail-panel__back" data-detail-panel-back onClick={() => onBack?.()}>
            {`${backLabelPrefix}${subject!.title}`}
          </button>
        ) : null}
        <h2 className="hl-detail-panel__title">{current.title}</h2>
      </header>
      <div role="tablist" aria-label={facetsLabel} className="hl-detail-panel__tabs" data-handled-keys={TAB_KEYS.join(' ')}>
        {facets.map((facet, index) => (
          <button
            key={facet.id}
            ref={element => { tabRefs.current[index] = element }}
            type="button"
            role="tab"
            id={`${tabBaseId}-${facet.id}`}
            aria-selected={facet.id === activeFacetId}
            aria-controls={`${tabBaseId}-panel`}
            tabIndex={facet.id === activeFacetId ? 0 : -1}
            className="hl-detail-panel__tab"
            data-facet-id={facet.id}
            onClick={() => selectFacet(facet.id)}
            onKeyDown={onTabListKeyDown}
          >
            <span data-facet-label>{facet.label}</span>
            <span className="hl-detail-panel__tab-count" data-facet-count>{facet.count}</span>
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`${tabBaseId}-panel`} aria-labelledby={`${tabBaseId}-${activeFacetId}`} className="hl-detail-panel__facet">
        {children}
      </div>
      <footer className="hl-detail-panel__actions">
        {pushed
          // drilldown-model.md:23 with chrome-spec §9: at level 2 the relation controls are
          // ABSENT, not disabled, and the one way onward is stated at the point of work.
          ? (
              <button type="button" className="hl-detail-panel__promote" data-detail-panel-promote onClick={() => onOpenAsPage?.(current.route)}>
                {openAsPageLabel}
              </button>
            )
          : (current.relations ?? []).map(relation => (
              <button key={relation.id} type="button" className="hl-detail-panel__relation" data-relation-id={relation.id} onClick={() => onFollowRelation?.(relation)}>
                {relation.label}
              </button>
            ))}
      </footer>
    </>
  ) : children

  const requestOpen = React.useCallback((next: boolean) => {
    if (controlledOpen === undefined) setUncontrolledOpen(next)
    onOpenChange?.(next)
    if (!next) queueMicrotask(() => restoreFocusRef.current?.focus())
  }, [controlledOpen, onOpenChange])

  const modalOpen = renderedOpen && !railCapable
  React.useLayoutEffect(() => {
    if (modalOpen && !wasModalOpen.current) {
      restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
      queueMicrotask(() => {
        const dialog = dialogRef.current
        if (!dialog) return
        ;(focusableElements(dialog)[0] ?? dialog).focus()
      })
    }
    wasModalOpen.current = modalOpen
  }, [modalOpen])

  const onDialogKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      requestOpen(false)
      return
    }
    if (event.key !== 'Tab' || !dialogRef.current) return
    const focusable = focusableElements(dialogRef.current)
    if (focusable.length === 0) {
      event.preventDefault()
      dialogRef.current.focus()
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

  if (!renderedOpen) return null

  const clampedWidth = Math.min(720, Math.max(220, width))
  const panelStyle = { ...style, '--hl-detail-panel-size': `${clampedWidth}px` } as React.CSSProperties
  const panelClassName = `hl-detail-panel${className ? ` ${className}` : ''}`
  const closeControl = dismissible ? (
    <div className="hl-detail-panel__bar">
      <button type="button" aria-label={closeLabel} className="hl-detail-panel__close" onClick={() => requestOpen(false)}>
        <svg aria-hidden="true" width="18" height="18" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="1.75" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg>
      </button>
    </div>
  ) : null

  if (railCapable) {
    return (
      <aside {...attributes} aria-label={label} className={panelClassName} data-detail-panel style={panelStyle}>
        {closeControl}
        <div className="hl-detail-panel__body">{drilldown}</div>
      </aside>
    )
  }

  return (
    <div className="hl-detail-panel__overlay" onPointerDown={event => { if (event.target === event.currentTarget) requestOpen(false) }}>
      <div {...attributes} aria-label={label} aria-modal="true" className={panelClassName} data-detail-panel ref={dialogRef} role="dialog" style={panelStyle} tabIndex={-1} onKeyDown={onDialogKeyDown}>
        {closeControl}
        <div className="hl-detail-panel__body">{drilldown}</div>
      </div>
    </div>
  )
}
