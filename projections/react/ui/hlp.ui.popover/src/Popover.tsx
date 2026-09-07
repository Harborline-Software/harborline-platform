import * as React from 'react'
import { createPortal } from 'react-dom'

export type PopoverAlign = 'start' | 'center' | 'end'
export type PopoverSide = 'top' | 'right' | 'bottom' | 'left'

export interface PopoverProps {
  children?: React.ReactNode
  defaultOpen?: boolean
  open?: boolean
  onOpenChange?: (open: boolean) => void
}

export interface PopoverTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean
  children?: React.ReactNode
}

export interface PopoverAnchorProps extends React.HTMLAttributes<HTMLDivElement> {
  asChild?: boolean
  children?: React.ReactNode
}

export interface PopoverInteractOutsideEvent {
  readonly detail: { readonly originalEvent: PointerEvent }
  readonly defaultPrevented: boolean
  preventDefault(): void
}

export interface PopoverContentProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'align' | 'role'> {
  align?: PopoverAlign
  side?: PopoverSide
  sideOffset?: number
  role?: 'dialog' | 'menu'
  onInteractOutside?: (event: PopoverInteractOutsideEvent) => void
}

export interface PopoverCloseProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean
  children?: React.ReactNode
}

interface PopoverContextValue {
  anchorRef: React.RefObject<HTMLElement | null>
  contentId: string
  contentRef: React.RefObject<HTMLDivElement | null>
  contentRole: 'dialog' | 'menu'
  interactOutsideRef: React.RefObject<PopoverContentProps['onInteractOutside']>
  open: boolean
  requestOpen: (open: boolean) => void
  setContentRole: React.Dispatch<React.SetStateAction<'dialog' | 'menu'>>
  triggerRef: React.RefObject<HTMLElement | null>
}

const PopoverContext = React.createContext<PopoverContextValue | null>(null)

function usePopover(part: string): PopoverContextValue {
  const value = React.useContext(PopoverContext)
  if (!value) throw new Error(`${part}-requires-popover`)
  return value
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function composeRefs<T>(...refs: Array<React.Ref<T> | undefined>): React.RefCallback<T> {
  return value => {
    for (const ref of refs) {
      if (typeof ref === 'function') ref(value)
      else if (ref) ref.current = value
    }
  }
}

function onlyElement(children: React.ReactNode, part: string): React.ReactElement<Record<string, unknown>> {
  if (!React.isValidElement<Record<string, unknown>>(children)) {
    throw new Error(`${part}-as-child-requires-element`)
  }
  return children
}

function focusableInside(element: HTMLElement): HTMLElement | null {
  return element.querySelector<HTMLElement>([
    'button:not([disabled])',
    '[href]',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(','))
}

function directionOf(element: HTMLElement): 'ltr' | 'rtl' {
  const explicit = element.closest<HTMLElement>('[dir]')?.dir
  if (explicit === 'rtl') return 'rtl'
  if (explicit === 'ltr') return 'ltr'
  return getComputedStyle(element).direction === 'rtl' ? 'rtl' : 'ltr'
}

export interface PlacementRect {
  bottom: number
  height: number
  left: number
  right: number
  top: number
  width: number
}

export interface Placement {
  left: number
  side: PopoverSide
  top: number
}

// The placement contract, shared with the Blazor lane's popover.js and replayed by both lanes from
// conformance/hlp.ui.popover/placement-v1.json: flip to the opposite side when the requested side
// lacks room and the opposite has more, THEN clamp the resolved coordinate into the viewport.
// Clamping alone leaves the surface on top of its anchor at an edge (ticket 298).
export function place(
  anchor: PlacementRect,
  content: { height: number; width: number },
  requestedSide: PopoverSide,
  align: PopoverAlign,
  offset: number,
  direction: 'ltr' | 'rtl',
  viewport: { height: number; width: number },
): Placement {
  const margin = 8
  const viewportWidth = viewport.width
  const viewportHeight = viewport.height
  let side = requestedSide

  const available = {
    top: anchor.top - margin,
    right: viewportWidth - anchor.right - margin,
    bottom: viewportHeight - anchor.bottom - margin,
    left: anchor.left - margin,
  }
  const required = side === 'top' || side === 'bottom' ? content.height + offset : content.width + offset
  const opposite: Record<PopoverSide, PopoverSide> = {
    top: 'bottom', right: 'left', bottom: 'top', left: 'right',
  }
  if (available[side] < required && available[opposite[side]] > available[side]) side = opposite[side]

  let left = anchor.left
  let top = anchor.bottom + offset
  if (side === 'top') top = anchor.top - content.height - offset
  if (side === 'right') left = anchor.right + offset
  if (side === 'left') left = anchor.left - content.width - offset

  if (side === 'top' || side === 'bottom') {
    if (align === 'center') left = anchor.left + (anchor.width - content.width) / 2
    if (align === 'start') left = direction === 'rtl' ? anchor.right - content.width : anchor.left
    if (align === 'end') left = direction === 'rtl' ? anchor.left : anchor.right - content.width
  } else {
    if (align === 'center') top = anchor.top + (anchor.height - content.height) / 2
    if (align === 'start') top = anchor.top
    if (align === 'end') top = anchor.bottom - content.height
  }

  const maximumLeft = Math.max(margin, viewportWidth - content.width - margin)
  const maximumTop = Math.max(margin, viewportHeight - content.height - margin)
  return {
    side,
    left: Math.min(Math.max(left, margin), maximumLeft),
    top: Math.min(Math.max(top, margin), maximumTop),
  }
}

export function Popover({ children, defaultOpen = false, open, onOpenChange }: PopoverProps) {
  const controlled = open !== undefined
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen)
  const renderedOpen = controlled ? open : uncontrolledOpen
  const triggerRef = React.useRef<HTMLElement | null>(null)
  const anchorRef = React.useRef<HTMLElement | null>(null)
  const contentRef = React.useRef<HTMLDivElement | null>(null)
  const interactOutsideRef = React.useRef<PopoverContentProps['onInteractOutside']>(undefined)
  const [contentRole, setContentRole] = React.useState<'dialog' | 'menu'>('dialog')
  const reactId = React.useId().replace(/:/g, '')
  const contentId = `hl-popover-${reactId}-content`
  const previouslyOpen = React.useRef(renderedOpen)

  const requestOpen = React.useCallback((next: boolean) => {
    if (!controlled) setUncontrolledOpen(next)
    onOpenChange?.(next)
  }, [controlled, onOpenChange])

  React.useLayoutEffect(() => {
    if (previouslyOpen.current && !renderedOpen) triggerRef.current?.focus()
    previouslyOpen.current = renderedOpen
  }, [renderedOpen])

  React.useEffect(() => {
    if (!renderedOpen) return

    const onPointerDown = (originalEvent: PointerEvent) => {
      const target = originalEvent.target
      if (!(target instanceof Node)) return
      if (contentRef.current?.contains(target) || triggerRef.current?.contains(target) || anchorRef.current?.contains(target)) return

      let prevented = false
      const outsideEvent: PopoverInteractOutsideEvent = {
        detail: { originalEvent },
        get defaultPrevented() { return prevented },
        preventDefault() { prevented = true },
      }
      interactOutsideRef.current?.(outsideEvent)
      if (!prevented) requestOpen(false)
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        requestOpen(false)
      }
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [renderedOpen, requestOpen])

  const context = React.useMemo<PopoverContextValue>(() => ({
    anchorRef,
    contentId,
    contentRef,
    contentRole,
    interactOutsideRef,
    open: renderedOpen,
    requestOpen,
    setContentRole,
    triggerRef,
  }), [contentId, contentRole, renderedOpen, requestOpen])

  return (
    <PopoverContext.Provider value={context}>
      <span className="hl-popover-root">{children}</span>
    </PopoverContext.Provider>
  )
}

export const PopoverTrigger = React.forwardRef<HTMLElement, PopoverTriggerProps>(function PopoverTrigger(
  { asChild = false, children, className, onClick, type = 'button', ...attributes },
  forwardedRef,
) {
  const context = usePopover('popover-trigger')
  const activate = (event: React.MouseEvent<HTMLElement>) => {
    onClick?.(event as React.MouseEvent<HTMLButtonElement>)
    if (!event.defaultPrevented) context.requestOpen(!context.open)
  }

  if (asChild) {
    const child = onlyElement(children, 'popover-trigger')
    const childProps = child.props as React.HTMLAttributes<HTMLElement> & { ref?: React.Ref<HTMLElement> }
    const childClick = childProps.onClick
    return React.cloneElement(child, {
      ...attributes,
      ...childProps,
      'aria-controls': context.open ? context.contentId : undefined,
      'aria-expanded': context.open,
      'aria-haspopup': childProps['aria-haspopup'] ?? context.contentRole,
      className: classes('hl-popover-trigger', className, childProps.className as string | undefined),
      onClick: (event: React.MouseEvent<HTMLElement>) => {
        childClick?.(event)
        if (!event.defaultPrevented) activate(event)
      },
      ref: composeRefs(childProps.ref, context.triggerRef, forwardedRef),
    })
  }

  return (
    <button
      {...attributes}
      aria-controls={context.open ? context.contentId : undefined}
      aria-expanded={context.open}
      aria-haspopup={context.contentRole}
      className={classes('hl-popover-trigger', className)}
      onClick={activate}
      ref={composeRefs(context.triggerRef as React.Ref<HTMLElement>, forwardedRef) as React.Ref<HTMLButtonElement>}
      type={type}
    >
      {children}
    </button>
  )
})

export const PopoverAnchor = React.forwardRef<HTMLElement, PopoverAnchorProps>(function PopoverAnchor(
  { asChild = false, children, className, ...attributes },
  forwardedRef,
) {
  const context = usePopover('popover-anchor')
  if (asChild) {
    const child = onlyElement(children, 'popover-anchor')
    const childProps = child.props as React.HTMLAttributes<HTMLElement> & { ref?: React.Ref<HTMLElement> }
    return React.cloneElement(child, {
      ...attributes,
      ...childProps,
      className: classes('hl-popover-anchor', className, childProps.className as string | undefined),
      ref: composeRefs(childProps.ref, context.anchorRef, forwardedRef),
    })
  }
  return (
    <div
      {...attributes}
      className={classes('hl-popover-anchor', className)}
      ref={composeRefs(context.anchorRef as React.Ref<HTMLElement>, forwardedRef) as React.Ref<HTMLDivElement>}
    >
      {children}
    </div>
  )
})

export const PopoverContent = React.forwardRef<HTMLDivElement, PopoverContentProps>(function PopoverContent(
  {
    align = 'center',
    children,
    className,
    onInteractOutside,
    role = 'dialog',
    side = 'bottom',
    sideOffset = 6,
    style,
    ...hostAttributes
  },
  forwardedRef,
) {
  const context = usePopover('popover-content')
  const [placement, setPlacement] = React.useState<Placement>({ left: 8, side, top: 8 })

  React.useLayoutEffect(() => {
    context.setContentRole(role)
    return () => context.setContentRole('dialog')
  }, [context.setContentRole, role])

  React.useLayoutEffect(() => {
    context.interactOutsideRef.current = onInteractOutside
    return () => { context.interactOutsideRef.current = undefined }
  }, [context.interactOutsideRef, onInteractOutside])

  React.useLayoutEffect(() => {
    if (!context.open) return
    const content = context.contentRef.current
    const anchor = context.anchorRef.current ?? context.triggerRef.current
    if (!content || !anchor) return

    const update = () => {
      const bounds = anchor.getBoundingClientRect()
      setPlacement(place(
        bounds,
        { height: content.offsetHeight, width: content.offsetWidth },
        side,
        align,
        sideOffset,
        directionOf(anchor),
        {
          height: document.documentElement.clientHeight || window.innerHeight,
          width: document.documentElement.clientWidth || window.innerWidth,
        },
      ))
    }
    update()
    window.addEventListener('resize', update)
    window.addEventListener('scroll', update, true)
    return () => {
      window.removeEventListener('resize', update)
      window.removeEventListener('scroll', update, true)
    }
  }, [align, context.anchorRef, context.contentRef, context.open, context.triggerRef, side, sideOffset])

  React.useLayoutEffect(() => {
    if (!context.open || !context.contentRef.current) return
    focusableInside(context.contentRef.current)?.focus()
  }, [context.contentRef, context.open])

  if (!context.open || typeof document === 'undefined') return null

  return createPortal(
    <div
      {...hostAttributes}
      className={classes('hl-popover__content', className)}
      data-align={align}
      data-side={placement.side}
      id={context.contentId}
      ref={composeRefs(context.contentRef, forwardedRef)}
      role={role}
      style={{ position: 'fixed', left: placement.left, top: placement.top, ...style }}
    >
      {children}
    </div>,
    document.body,
  )
})

export const PopoverClose = React.forwardRef<HTMLElement, PopoverCloseProps>(function PopoverClose(
  { asChild = false, children, className, onClick, type = 'button', ...attributes },
  forwardedRef,
) {
  const context = usePopover('popover-close')
  const close = (event: React.MouseEvent<HTMLElement>) => {
    onClick?.(event as React.MouseEvent<HTMLButtonElement>)
    if (!event.defaultPrevented) context.requestOpen(false)
  }

  if (asChild) {
    const child = onlyElement(children, 'popover-close')
    const childProps = child.props as React.HTMLAttributes<HTMLElement> & { ref?: React.Ref<HTMLElement> }
    const childClick = childProps.onClick
    return React.cloneElement(child, {
      ...attributes,
      ...childProps,
      className: classes('hl-popover-close', className, childProps.className as string | undefined),
      onClick: (event: React.MouseEvent<HTMLElement>) => {
        childClick?.(event)
        if (!event.defaultPrevented) close(event)
      },
      ref: composeRefs(childProps.ref, forwardedRef),
    })
  }

  return (
    <button {...attributes} className={classes('hl-popover-close', className)} onClick={close} ref={forwardedRef as React.Ref<HTMLButtonElement>} type={type}>
      {children}
    </button>
  )
})
