import type { HTMLAttributes, ReactElement } from 'react'

export type EmptyStateVariant = 'informational' | 'positive' | 'actionable'

export interface EmptyStateAction {
  label: string
  onClick: () => void
}

export interface EmptyStateProps
  extends Omit<HTMLAttributes<HTMLDivElement>, 'children' | 'title'> {
  variant: EmptyStateVariant
  title: string
  description?: string
  action?: EmptyStateAction
}

type EmptyStateIcon = 'info' | 'circle-check' | 'circle-plus'

interface IconDefinition {
  icon: EmptyStateIcon
  tone: 'muted' | 'success'
  path: ReactElement
}

const iconDefinitions: Record<EmptyStateVariant, IconDefinition> = {
  informational: {
    icon: 'info',
    tone: 'muted',
    path: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 10.75v5.5" />
        <path d="M12 7.75h.01" />
      </>
    ),
  },
  positive: {
    icon: 'circle-check',
    tone: 'success',
    path: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="m8.25 12.25 2.35 2.35 5.15-5.2" />
      </>
    ),
  },
  actionable: {
    icon: 'circle-plus',
    tone: 'muted',
    path: (
      <>
        <circle cx="12" cy="12" r="9" />
        <path d="M12 8v8M8 12h8" />
      </>
    ),
  },
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')
const hasText = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0

function VariantIcon({ variant }: { variant: EmptyStateVariant }) {
  const definition = iconDefinitions[variant]

  return (
    <svg
      aria-hidden="true"
      className="hl-empty-state__icon"
      data-hl-icon={definition.icon}
      data-hl-tone={definition.tone}
      focusable="false"
      viewBox="0 0 24 24"
    >
      {definition.path}
    </svg>
  )
}

export function EmptyState({
  variant,
  title,
  description,
  action,
  className,
  ...hostAttributes
}: EmptyStateProps) {
  if (!hasText(title)) throw new Error('title-required')

  const actionLabelPresent = hasText(action?.label)
  const actionCallbackPresent = typeof action?.onClick === 'function'
  if (actionLabelPresent !== actionCallbackPresent) throw new Error('action-incomplete')

  return (
    <div
      {...hostAttributes}
      className={classes('hl-empty-state', `hl-empty-state--${variant}`, className)}
      data-hl-variant={variant}
    >
      <VariantIcon variant={variant} />
      <p className="hl-empty-state__title" data-hl-tone="foreground">
        {title}
      </p>
      {hasText(description) ? (
        <p className="hl-empty-state__description" data-hl-tone="muted">
          {description}
        </p>
      ) : null}
      {actionLabelPresent && actionCallbackPresent ? (
        <button
          type="button"
          className="hl-empty-state__action"
          data-hl-tone="secondary"
          onClick={action.onClick}
        >
          {action.label}
        </button>
      ) : null}
    </div>
  )
}
