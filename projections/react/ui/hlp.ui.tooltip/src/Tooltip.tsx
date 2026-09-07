import * as React from 'react'

export type TooltipSide = 'top' | 'right' | 'bottom' | 'left'

export interface TooltipProps {
  content: string
  side?: TooltipSide
  delayDuration?: number
  skipDelayDuration?: number
  open?: boolean
  onOpenChange?: (open: boolean) => void
  defaultOpen?: boolean
  children: React.ReactNode
  className?: string
  triggerClassName?: string
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function mergeDescriptionIds(existing: string | undefined, tooltipId: string): string {
  return [...new Set(`${existing ?? ''} ${tooltipId}`.trim().split(/\s+/).filter(Boolean))].join(' ')
}

export function Tooltip({
  children,
  className,
  content,
  defaultOpen = false,
  delayDuration = 700,
  onOpenChange,
  open,
  side = 'top',
  skipDelayDuration,
  triggerClassName,
}: TooltipProps) {
  void skipDelayDuration
  const controlled = open !== undefined
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen)
  const visible = controlled ? open : uncontrolledOpen
  const hovered = React.useRef(false)
  const focused = React.useRef(false)
  const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null)
  const reactId = React.useId().replace(/:/g, '')
  const tooltipId = `hl-tooltip-${reactId}`

  const clearPending = React.useCallback(() => {
    if (timer.current !== null) {
      clearTimeout(timer.current)
      timer.current = null
    }
  }, [])

  const requestOpen = React.useCallback((next: boolean) => {
    if (!controlled) setUncontrolledOpen(next)
    onOpenChange?.(next)
  }, [controlled, onOpenChange])

  const requestDelayedOpen = React.useCallback(() => {
    clearPending()
    timer.current = setTimeout(() => {
      timer.current = null
      if (hovered.current || focused.current) requestOpen(true)
    }, Math.max(0, delayDuration))
  }, [clearPending, delayDuration, requestOpen])

  const release = React.useCallback(() => {
    if (hovered.current || focused.current) return
    clearPending()
    if (visible) requestOpen(false)
  }, [clearPending, requestOpen, visible])

  React.useEffect(() => clearPending, [clearPending])

  const childCount = React.Children.count(children)
  const canonicalTrigger = childCount === 1 && React.isValidElement<Record<string, unknown>>(children)
    ? children
    : null
  const trigger = canonicalTrigger
    ? React.cloneElement(canonicalTrigger, {
        'aria-describedby': visible
          ? mergeDescriptionIds(canonicalTrigger.props['aria-describedby'] as string | undefined, tooltipId)
          : canonicalTrigger.props['aria-describedby'],
      })
    : children

  return (
    <span
      className={classes('hl-tooltip__trigger', triggerClassName)}
      onBlur={event => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
        focused.current = false
        release()
      }}
      onFocus={() => {
        focused.current = true
        requestDelayedOpen()
      }}
      onMouseEnter={() => {
        hovered.current = true
        requestDelayedOpen()
      }}
      onMouseLeave={() => {
        hovered.current = false
        release()
      }}
      onKeyDown={event => {
        if (event.key !== 'Escape') return
        clearPending()
        if (!visible) return
        event.preventDefault()
        event.stopPropagation()
        requestOpen(false)
      }}
    >
      {trigger}
      {visible ? (
        <span
          className={classes('hl-tooltip__content', className)}
          data-side={side}
          id={tooltipId}
          role="tooltip"
        >
          {content}
        </span>
      ) : null}
    </span>
  )
}
