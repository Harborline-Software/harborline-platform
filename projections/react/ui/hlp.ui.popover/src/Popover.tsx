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
    'a[href]',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
  ].join(','))
}

// Sequential focus order (T-1128). The content is portaled to the end of <body>, so native Tab order
// put it after the whole page. As in the HTML popover focus navigation model (WHATWG HTML 6.6.2: a
// showing popover's focus navigation scope is inserted after its invoker, inside the invoker's own
// scope), the content sits right after its trigger. Tab leaves the content only when it has no later
// Tab stop, and goes to the next stop after the trigger; Shift+Tab leaves only when it has no earlier
// stop, and goes to the trigger. When nothing follows a trigger inside an outer popover's content, the
// walk continues after the outer trigger, at any depth. Shared with the Blazor lane's popover.js.
const contentSelector = '.hl-popover__content'
const tabbableCandidates = [
  'a[href]', 'area[href]', 'button', 'input', 'select', 'textarea', 'iframe', 'summary',
  '[tabindex]', '[contenteditable]:not([contenteditable="false"])',
].join(',')
// Each open content's trigger, read when a key arrives.
const contentOwners = new WeakMap<Element, () => HTMLElement | null>()
// The open contents, to find the content an open trigger owns (the way in, nested).
const openContents = new Set<HTMLElement>()

function tabbable(element: HTMLElement): boolean {
  if (element.tabIndex < 0 || element.matches(':disabled')) return false
  if (element instanceof HTMLInputElement && element.type === 'hidden') return false
  for (let node: HTMLElement | null = element; node; node = node.parentElement) {
    if (node.hidden || (node as HTMLElement & { inert?: boolean }).inert === true) return false
  }
  const visible = (element as HTMLElement & { checkVisibility?: (options?: object) => boolean }).checkVisibility
  return typeof visible !== 'function' || visible.call(element, { visibilityProperty: true, checkVisibilityCSS: true })
}

function radioGroup(element: Element): HTMLInputElement | null {
  return element instanceof HTMLInputElement && element.type === 'radio' && element.name ? element : null
}

function sameGroup(a: Element, b: Element): boolean {
  const left = radioGroup(a)
  const right = radioGroup(b)
  return !!left && !!right && left.name === right.name && left.form === right.form
}

// The nearest popover content or modal layer around an element: the focus navigation scope it is in.
function scopeOf(element: HTMLElement): HTMLElement | null {
  return element.parentElement?.closest<HTMLElement>(`${contentSelector},[aria-modal="true"]`) ?? null
}

// The scope's Tab stops in document order, as the browser has them: tabbable, directly in this scope
// (not in another popover's content), and a radio group as one stop, its checked radio or else its first.
function stopsIn(scope: HTMLElement | null): HTMLElement[] {
  const own = scope?.matches(contentSelector) ? scope : null
  const all = Array.from((scope ?? document).querySelectorAll<HTMLElement>(tabbableCandidates))
    .filter(tabbable)
    .filter(element => element.closest(contentSelector) === own)
  return all.filter(element => {
    if (!radioGroup(element)) return true
    const group = all.filter(other => sameGroup(other, element))
    return element === (group.find(radio => (radio as HTMLInputElement).checked) ?? group[0])
  })
}

function follows(from: Element, other: Element): boolean {
  return !!(from.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_FOLLOWING) && !other.contains(from)
}

function precedes(from: Element, other: Element): boolean {
  return !!(from.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_PRECEDING) && !other.contains(from)
}

function ownerOf(content: HTMLElement): HTMLElement | null {
  const trigger = contentOwners.get(content)?.()
  return trigger?.isConnected ? trigger : null
}

function contentOf(trigger: Element): HTMLElement | null {
  for (const content of openContents) if (content.isConnected && ownerOf(content) === trigger) return content
  return null
}

// The next Tab stop after an element in its scope; past the end of a popover content, the next stop
// after that content's trigger. Null at the end of the page or of a modal layer: the key is then left to
// the browser, or to the layer's trap, which is where native Tab from the trigger goes.
function stopAfter(element: HTMLElement, depth = 0): HTMLElement | null {
  const scope = scopeOf(element)
  const next = stopsIn(scope).find(stop => !element.contains(stop) && follows(element, stop))
  if (next) return next
  const owner = scope?.matches(contentSelector) && depth < 32 ? ownerOf(scope) : null
  return owner ? stopAfter(owner, depth + 1) : null
}

// Where Shift+Tab out of a trigger's content lands: the trigger, or, when it cannot take focus (disabled
// or hidden while open), the Tab stop before it, walking out of popover contents the same way. Null when
// nothing precedes it: the key is then left to the browser.
function stopBack(trigger: HTMLElement, depth = 0): HTMLElement | null {
  if (tabbable(trigger)) return trigger
  const scope = scopeOf(trigger)
  const previous = stopsIn(scope).filter(stop => precedes(trigger, stop)).at(-1)
  if (previous) return previous
  const owner = scope?.matches(contentSelector) && depth < 32 ? ownerOf(scope) : null
  return owner ? stopBack(owner, depth + 1) : null
}

// Handles (prevents) the key only when it moves focus itself; Tab within the content stays native.
function routeTab(event: { key: string, shiftKey: boolean, target: EventTarget | null, defaultPrevented: boolean, preventDefault(): void }, trigger: HTMLElement | null, content: HTMLElement | null): void {
  if (event.key !== 'Tab' || event.defaultPrevented || !trigger?.isConnected || !content) return
  const target = event.target
  if (!(target instanceof HTMLElement) || !content.contains(target)) return
  // Tab on an open inner popover's trigger is that popover's way in, not a way out of this content.
  if (!event.shiftKey && contentOf(target)) return
  const others = stopsIn(content).filter(stop => stop !== target && !sameGroup(stop, target))
  if (others.some(stop => event.shiftKey ? precedes(target, stop) : follows(target, stop))) return
  const destination = event.shiftKey ? stopBack(trigger) : stopAfter(trigger)
  if (!destination) return
  event.preventDefault()
  destination.focus()
}

// The way in mirrors the way out (b31 re-review): while open, Tab on the trigger goes to the content's
// first Tab stop, or the content element when it has none (as the opening focus does), and Shift+Tab from
// the next stop after the trigger in the trigger's own scope (modal layer, outer content or page) goes to
// the content's last stop. A key already handled is left alone; nothing is handled once closed.
type TabEvent = { key: string, shiftKey: boolean, target: EventTarget | null, defaultPrevented: boolean, preventDefault(): void }

function enterContent(event: TabEvent, trigger: HTMLElement | null, content: HTMLElement | null): void {
  if (event.key !== 'Tab' || event.defaultPrevented || !trigger?.isConnected || !content?.isConnected) return
  let destination: HTMLElement
  if (!event.shiftKey) {
    if (event.target !== trigger) return
    destination = stopsIn(content)[0] ?? content
  } else {
    const next = stopsIn(scopeOf(trigger)).find(stop => !trigger.contains(stop) && follows(trigger, stop))
    if (!next || event.target !== next) return
    destination = stopsIn(content).at(-1) ?? content
    // The last stop may be an open inner trigger, whose content follows it: walk in (WHATWG 6.6.2).
    for (let inner = contentOf(destination), depth = 0; inner && depth < 32; inner = contentOf(destination), depth++) {
      destination = stopsIn(inner).at(-1) ?? inner
    }
  }
  event.preventDefault()
  destination.focus()
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
  // Set by an outside pointer dismissal, cleared by any other request and by every open or close.
  const outsidePointer = React.useRef(false)

  const requestOpen = React.useCallback((next: boolean) => {
    outsidePointer.current = false
    if (!controlled) setUncontrolledOpen(next)
    onOpenChange?.(next)
  }, [controlled, onOpenChange])

  // Focus after a close (G5, R-0125; WHATWG HTML hide popover algorithm), shared with the Blazor lane's
  // popover.js. An outside pointer dismissal never moves focus: light dismiss passes focusPreviousElement
  // false, and focus stays where the browser's pointer default put it (the pressed field, else the body).
  // Every other close (Escape, the close control, trigger activation, a host-driven close) restores the
  // trigger, without scrolling, only when focus was inside the content or was lost with it: the content
  // is already unmounted here, so focus that was inside it is now on the body. If the user has put focus
  // elsewhere, it stays there. Previously every close focused the trigger, and a pressed field kept focus
  // only because the browser's mousedown default ran after this effect.
  React.useLayoutEffect(() => {
    if (previouslyOpen.current && !renderedOpen) {
      const active = document.activeElement
      if (!outsidePointer.current && (active === null || active === document.body)) triggerRef.current?.focus({ preventScroll: true })
    }
    outsidePointer.current = false
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
      if (prevented) return
      requestOpen(false)
      outsidePointer.current = true
    }
    // The open popover is the topmost layer. An Escape from its trigger button is taken in the capture
    // phase, before an outer layer (a dialog or sheet) listening on the document; the trigger has no
    // Escape behaviour of its own. An Escape from inside the content is handled on the content itself
    // (PopoverContent), after any widget in it. Any other Escape, including one from an anchor that
    // may use Escape itself, goes through the bubble listener and still closes it, as before. Inside
    // a Dialog or Sheet the gain needs their bubble-phase, portal-scoped listeners (separate batches).
    const fromTrigger = (target: EventTarget | null) => target instanceof Node && triggerRef.current?.contains(target) === true
    const onKeyDownCapture = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented || !fromTrigger(event.target)) return
      event.preventDefault()
      requestOpen(false)
    }
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented) return
      if (event.target instanceof Node && contentRef.current?.contains(event.target)) return
      event.preventDefault()
      requestOpen(false)
    }

    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDownCapture, true)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDownCapture, true)
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
  { asChild = false, children, className, onClick, onKeyDown, type = 'button', ...attributes },
  forwardedRef,
) {
  const context = usePopover('popover-trigger')
  const activate = (event: React.MouseEvent<HTMLElement>) => {
    onClick?.(event as React.MouseEvent<HTMLButtonElement>)
    if (!event.defaultPrevented) context.requestOpen(!context.open)
  }
  // Tab on the open trigger enters the content (T-1128), composed with the trigger's props: the caller's
  // onKeyDown runs first and a key it handled is left alone (b31 final check), as on the content side.
  // React delegates from its root, ahead of a modal layer's trap on the document.
  const enter = (event: React.KeyboardEvent<HTMLElement>) => {
    // The synthetic event, so a later handler in the same React dispatch (an outer content) sees it handled.
    if (context.open) enterContent(event, context.triggerRef.current, context.contentRef.current)
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
      onKeyDown: (event: React.KeyboardEvent<HTMLElement>) => {
        childProps.onKeyDown?.(event)
        onKeyDown?.(event as React.KeyboardEvent<HTMLButtonElement>)
        enter(event)
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
      onKeyDown={event => { onKeyDown?.(event); enter(event) }}
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
    // T-713: placing only here and on window resize or scroll left the popover behind an anchor
    // that moved while the page settled. While open, re-place whenever the anchor's box, the
    // content's size or the viewport changes, as the Blazor lane's connect() does.
    let frame = 0
    let measured = ''
    const follow = () => {
      const bounds = anchor.getBoundingClientRect()
      const key = [bounds.left, bounds.top, bounds.width, bounds.height, content.offsetWidth, content.offsetHeight,
        document.documentElement.clientWidth || window.innerWidth, document.documentElement.clientHeight || window.innerHeight].join()
      if (key !== measured) { measured = key; update() }
      frame = requestAnimationFrame(follow)
    }
    follow()
    window.addEventListener('resize', update)
    window.addEventListener('scroll', update, true)
    return () => {
      cancelAnimationFrame(frame)
      window.removeEventListener('resize', update)
      window.removeEventListener('scroll', update, true)
    }
  }, [align, context.anchorRef, context.contentRef, context.open, context.triggerRef, side, sideOffset])

  // Registers this content's trigger for the focus order walk out of nested popovers (T-1128).
  React.useLayoutEffect(() => {
    const content = context.contentRef.current
    if (!context.open || !content) return
    contentOwners.set(content, () => context.triggerRef.current)
    openContents.add(content)
    // Shift+Tab from the stop after the trigger, on the document after the stop's own handlers. (Tab on
    // the trigger is PopoverTrigger's onKeyDown, after the caller's.)
    const onMirror = (event: KeyboardEvent) => { if (event.shiftKey) enterContent(event, context.triggerRef.current, content) }
    document.addEventListener('keydown', onMirror)
    return () => {
      contentOwners.delete(content)
      openContents.delete(content)
      document.removeEventListener('keydown', onMirror)
    }
  }, [context.contentRef, context.open, context.triggerRef])

  // Opening moves focus into the content: its first focusable control, else the content element itself
  // (tabindex -1), as the Blazor lane's popover.js does. Without the fallback a pointer open that does not
  // focus the trigger (WebKit) left focus outside, and Escape went to an enclosing layer instead.
  React.useLayoutEffect(() => {
    const content = context.contentRef.current
    if (!context.open || !content) return
    // The first match may refuse focus (inside a disabled fieldset, for example); focus still ends inside.
    focusableInside(content)?.focus()
    if (!content.contains(document.activeElement)) content.focus()
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
      tabIndex={hostAttributes.tabIndex ?? -1}
      style={{ position: 'fixed', left: placement.left, top: placement.top, ...style }}
      onKeyDown={event => {
        hostAttributes.onKeyDown?.(event)
        // After any widget inside the content, before an outer layer listening on the document.
        if (event.key === 'Tab') return routeTab(event, context.triggerRef.current, context.contentRef.current)
        if (event.key !== 'Escape' || event.defaultPrevented) return
        event.preventDefault()
        context.requestOpen(false)
      }}
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
