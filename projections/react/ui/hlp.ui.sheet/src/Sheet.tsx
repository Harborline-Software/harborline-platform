import * as React from 'react'
import { createPortal } from 'react-dom'

export type SheetSide = 'top' | 'right' | 'bottom' | 'left'

export interface SheetProps {
  open?: boolean
  defaultOpen?: boolean
  onOpenChange?: (open: boolean) => void
  modal?: boolean
  children?: React.ReactNode
}

export interface SheetTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean
  children?: React.ReactNode
}

export interface SheetContentProps extends React.HTMLAttributes<HTMLDivElement> {
  closeLabel: string
  side?: SheetSide
}

export type SheetHeaderProps = React.HTMLAttributes<HTMLDivElement>
export type SheetFooterProps = React.HTMLAttributes<HTMLDivElement>
export type SheetTitleProps = React.HTMLAttributes<HTMLHeadingElement>
export type SheetDescriptionProps = React.HTMLAttributes<HTMLParagraphElement>

export interface SheetCloseProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean
  children?: React.ReactNode
}

interface SheetContextValue {
  contentId: string
  contentRef: React.RefObject<HTMLDivElement | null>
  defaultContentId: string
  defaultDescriptionId: string
  defaultTitleId: string
  describedBy: string | undefined
  labelledBy: string
  modal: boolean
  open: boolean
  requestOpen(open: boolean): void
  setContentId(id: string): void
  setDescribedBy(id: string | undefined): void
  setLabelledBy(id: string): void
  triggerRef: React.RefObject<HTMLElement | null>
}

const SheetContext = React.createContext<SheetContextValue | null>(null)

function useSheet(part: string): SheetContextValue {
  const value = React.useContext(SheetContext)
  if (!value) throw new Error(`${part}-requires-sheet`)
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

function focusableElements(root: HTMLElement): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>([
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])',
    '[contenteditable="true"]',
  ].join(',')))
}

export function Sheet({ children, defaultOpen = false, modal = true, onOpenChange, open }: SheetProps) {
  const controlled = open !== undefined
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen)
  const renderedOpen = controlled ? open : uncontrolledOpen
  const reactId = React.useId().replace(/:/g, '')
  const defaultContentId = `hl-sheet-${reactId}-content`
  const defaultTitleId = `hl-sheet-${reactId}-title`
  const defaultDescriptionId = `hl-sheet-${reactId}-description`
  const [contentId, setContentId] = React.useState(defaultContentId)
  const [labelledBy, setLabelledBy] = React.useState(defaultTitleId)
  const [describedBy, setDescribedBy] = React.useState<string | undefined>()
  const triggerRef = React.useRef<HTMLElement | null>(null)
  const contentRef = React.useRef<HTMLDivElement | null>(null)
  const previouslyOpen = React.useRef(renderedOpen)

  const requestOpen = React.useCallback((next: boolean) => {
    if (!controlled) setUncontrolledOpen(next)
    onOpenChange?.(next)
  }, [controlled, onOpenChange])

  React.useLayoutEffect(() => {
    if (previouslyOpen.current && !renderedOpen) triggerRef.current?.focus()
    previouslyOpen.current = renderedOpen
  }, [renderedOpen])

  const context = React.useMemo<SheetContextValue>(() => ({
    contentId,
    contentRef,
    defaultContentId,
    defaultDescriptionId,
    defaultTitleId,
    describedBy,
    labelledBy,
    modal,
    open: renderedOpen,
    requestOpen,
    setContentId,
    setDescribedBy,
    setLabelledBy,
    triggerRef,
  }), [contentId, defaultContentId, defaultDescriptionId, defaultTitleId, describedBy, labelledBy, modal, renderedOpen, requestOpen])

  return <SheetContext.Provider value={context}>{children}</SheetContext.Provider>
}

export const SheetTrigger = React.forwardRef<HTMLButtonElement, SheetTriggerProps>(function SheetTrigger(
  { asChild = false, children, className, onClick, type = 'button', ...attributes },
  forwardedRef,
) {
  const context = useSheet('sheet-trigger')
  const activate = (event: React.MouseEvent<HTMLElement>) => {
    onClick?.(event as React.MouseEvent<HTMLButtonElement>)
    if (!event.defaultPrevented) context.requestOpen(!context.open)
  }

  if (asChild) {
    const child = onlyElement(children, 'sheet-trigger')
    const childProps = child.props as React.HTMLAttributes<HTMLElement> & { ref?: React.Ref<HTMLElement> }
    const childClick = childProps.onClick
    return React.cloneElement(child, {
      ...attributes,
      ...childProps,
      className: classes('hl-sheet__trigger', className, childProps.className as string | undefined),
      'aria-controls': context.open ? context.contentId : undefined,
      'aria-expanded': context.open,
      'aria-haspopup': 'dialog',
      onClick: (event: React.MouseEvent<HTMLElement>) => {
        childClick?.(event)
        if (!event.defaultPrevented) activate(event)
      },
      ref: composeRefs(
        childProps.ref,
        context.triggerRef,
        forwardedRef as React.Ref<HTMLElement>,
      ),
    })
  }

  return (
    <button
      {...attributes}
      aria-controls={context.open ? context.contentId : undefined}
      aria-expanded={context.open}
      aria-haspopup="dialog"
      className={classes('hl-sheet__trigger', className)}
      onClick={activate}
      ref={composeRefs(context.triggerRef as React.Ref<HTMLElement>, forwardedRef) as React.Ref<HTMLButtonElement>}
      type={type}
    >
      {children}
    </button>
  )
})

export const SheetContent = React.forwardRef<HTMLDivElement, SheetContentProps>(function SheetContent(
  { children, className, closeLabel, id, side = 'right', ...hostAttributes },
  forwardedRef,
) {
  const context = useSheet('sheet-content')
  const portalRef = React.useRef<HTMLDivElement | null>(null)
  const actualId = id ?? context.defaultContentId
  if (!closeLabel.trim()) throw new Error('close-label-required')

  React.useLayoutEffect(() => {
    context.setContentId(actualId)
    return () => context.setContentId(context.defaultContentId)
  }, [actualId, context.defaultContentId, context.setContentId])

  React.useLayoutEffect(() => {
    if (!context.open) return
    const content = context.contentRef.current
    const portal = portalRef.current
    if (!content || !portal) return

    const first = focusableElements(content)[0]
    ;(first ?? content).focus()

    if (!context.modal) return
    const siblings = Array.from(document.body.children).filter(element => element !== portal)
    const previousInert = siblings.map(element => (element as HTMLElement & { inert?: boolean }).inert ?? false)
    const previousOverflow = document.body.style.overflow
    siblings.forEach(element => { (element as HTMLElement & { inert?: boolean }).inert = true })
    document.body.style.overflow = 'hidden'

    return () => {
      siblings.forEach((element, index) => {
        ;(element as HTMLElement & { inert?: boolean }).inert = previousInert[index]
      })
      document.body.style.overflow = previousOverflow
    }
  }, [context.contentRef, context.modal, context.open])

  React.useEffect(() => {
    if (!context.open) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape' && !event.defaultPrevented) {
        event.preventDefault()
        event.stopPropagation()
        context.requestOpen(false)
        return
      }
      if (event.key !== 'Tab' || !context.modal || !context.contentRef.current) return
      const focusables = focusableElements(context.contentRef.current)
      if (focusables.length === 0) {
        event.preventDefault()
        context.contentRef.current.focus()
        return
      }
      const first = focusables[0]
      const last = focusables.at(-1)!
      const active = document.activeElement
      if (event.shiftKey && (active === first || !context.contentRef.current.contains(active))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && (active === last || !context.contentRef.current.contains(active))) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown, true)
    return () => document.removeEventListener('keydown', onKeyDown, true)
  }, [context.contentRef, context.modal, context.open, context.requestOpen])

  if (!context.open || typeof document === 'undefined') return null

  return createPortal(
    <div className="hl-sheet__portal" data-hl-modal={context.modal ? 'true' : 'false'} ref={portalRef}>
      {context.modal ? (
        <div
          aria-hidden="true"
          className="hl-sheet__overlay"
          data-testid="sheet-overlay"
          onPointerDown={event => {
            if (event.currentTarget === event.target) context.requestOpen(false)
          }}
        />
      ) : null}
      <div
        {...hostAttributes}
        aria-describedby={context.describedBy}
        aria-labelledby={context.labelledBy}
        aria-modal={context.modal ? 'true' : 'false'}
        className={classes('hl-sheet__content', `hl-sheet__content--${side}`, className)}
        data-side={side}
        id={actualId}
        ref={composeRefs(context.contentRef, forwardedRef)}
        role="dialog"
        tabIndex={-1}
      >
        {children}
        <button
          aria-label={closeLabel}
          className="hl-sheet__built-in-close"
          onClick={() => context.requestOpen(false)}
          type="button"
        >
          <svg aria-hidden="true" focusable="false" viewBox="0 0 16 16">
            <path d="M3 3l10 10M13 3L3 13" />
          </svg>
        </button>
      </div>
    </div>,
    document.body,
  )
})

export function SheetHeader({ className, ...hostAttributes }: SheetHeaderProps) {
  return <div {...hostAttributes} className={classes('hl-sheet__header', className)} />
}

export function SheetFooter({ className, ...hostAttributes }: SheetFooterProps) {
  return <div {...hostAttributes} className={classes('hl-sheet__footer', className)} />
}

export const SheetTitle = React.forwardRef<HTMLHeadingElement, SheetTitleProps>(function SheetTitle(
  { className, id, ...hostAttributes },
  forwardedRef,
) {
  const context = useSheet('sheet-title')
  const actualId = id ?? context.defaultTitleId
  React.useLayoutEffect(() => {
    context.setLabelledBy(actualId)
    return () => context.setLabelledBy(context.defaultTitleId)
  }, [actualId, context.defaultTitleId, context.setLabelledBy])
  return (
    <h2 {...hostAttributes} className={classes('hl-sheet__title', className)} id={actualId} ref={forwardedRef} />
  )
})

export const SheetDescription = React.forwardRef<HTMLParagraphElement, SheetDescriptionProps>(
  function SheetDescription({ className, id, ...hostAttributes }, forwardedRef) {
    const context = useSheet('sheet-description')
    const actualId = id ?? context.defaultDescriptionId
    React.useLayoutEffect(() => {
      context.setDescribedBy(actualId)
      return () => context.setDescribedBy(undefined)
    }, [actualId, context.setDescribedBy])
    return (
      <p {...hostAttributes} className={classes('hl-sheet__description', className)} id={actualId} ref={forwardedRef} />
    )
  },
)

export const SheetClose = React.forwardRef<HTMLButtonElement, SheetCloseProps>(function SheetClose(
  { asChild = false, children, className, onClick, type = 'button', ...attributes },
  forwardedRef,
) {
  const context = useSheet('sheet-close')
  const close = (event: React.MouseEvent<HTMLElement>) => {
    onClick?.(event as React.MouseEvent<HTMLButtonElement>)
    if (!event.defaultPrevented) context.requestOpen(false)
  }

  if (asChild) {
    const child = onlyElement(children, 'sheet-close')
    const childProps = child.props as React.HTMLAttributes<HTMLElement> & { ref?: React.Ref<HTMLElement> }
    const childClick = childProps.onClick
    return React.cloneElement(child, {
      ...attributes,
      ...childProps,
      className: classes('hl-sheet__close', className, childProps.className as string | undefined),
      onClick: (event: React.MouseEvent<HTMLElement>) => {
        childClick?.(event)
        if (!event.defaultPrevented) close(event)
      },
      ref: composeRefs(childProps.ref, forwardedRef as React.Ref<HTMLElement>),
    })
  }

  return (
    <button {...attributes} className={classes('hl-sheet__close', className)} onClick={close} ref={forwardedRef} type={type}>
      {children}
    </button>
  )
})
