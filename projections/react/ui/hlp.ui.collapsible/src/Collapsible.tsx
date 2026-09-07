import * as React from 'react'

export interface CollapsibleProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'title'> {
  title: string
  subtitle?: string
  headerActions?: React.ReactNode
  open?: boolean
  defaultOpen?: boolean
  onOpenChange?: (open: boolean) => void
  disabled?: boolean
  children?: React.ReactNode
}

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export function Collapsible({
  title,
  subtitle,
  headerActions,
  open,
  defaultOpen = false,
  onOpenChange,
  disabled = false,
  children,
  className,
  ...hostAttributes
}: CollapsibleProps) {
  if (!title.trim()) throw new Error('title-required')

  const controlled = open !== undefined
  const [uncontrolledOpen, setUncontrolledOpen] = React.useState(defaultOpen)
  const expanded = controlled ? open : uncontrolledOpen
  const instanceId = React.useId().replace(/:/g, '')
  const titleId = `hl-collapsible-${instanceId}-title`
  const subtitleId = `hl-collapsible-${instanceId}-subtitle`
  const contentId = `hl-collapsible-${instanceId}-content`

  const toggle = () => {
    if (disabled) return
    const next = !expanded
    if (!controlled) setUncontrolledOpen(next)
    onOpenChange?.(next)
  }

  return (
    <div
      {...hostAttributes}
      className={classes('hl-collapsible', disabled && 'hl-collapsible--disabled', className)}
      data-hl-open={expanded ? 'true' : 'false'}
    >
      <div className="hl-collapsible__header">
        <button
          aria-controls={contentId}
          aria-describedby={subtitle ? subtitleId : undefined}
          aria-expanded={expanded}
          aria-labelledby={titleId}
          className="hl-collapsible__trigger"
          disabled={disabled}
          onClick={toggle}
          type="button"
        >
          <span className="hl-collapsible__labels">
            <span className="hl-collapsible__title" id={titleId}>{title}</span>
            {subtitle ? (
              <span className="hl-collapsible__subtitle" id={subtitleId}>{subtitle}</span>
            ) : null}
          </span>
          <svg
            aria-hidden="true"
            className="hl-collapsible__chevron"
            fill="none"
            focusable="false"
            viewBox="0 0 16 16"
          >
            <path d="m4 6 4 4 4-4" />
          </svg>
        </button>
        {headerActions ? <div className="hl-collapsible__actions">{headerActions}</div> : null}
      </div>
      <div
        aria-labelledby={titleId}
        className="hl-collapsible__content"
        hidden={!expanded}
        id={contentId}
        role="region"
      >
        {children}
      </div>
    </div>
  )
}
