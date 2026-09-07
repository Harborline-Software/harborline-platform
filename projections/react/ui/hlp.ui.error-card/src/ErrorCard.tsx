import type { HTMLAttributes } from 'react'

export type ErrorCardVariant = 'page' | 'default' | 'compact'

export interface ErrorCardProps
  extends Omit<HTMLAttributes<HTMLDivElement>, 'children' | 'title'> {
  title: string
  message?: string
  onRetry?: () => void
  retryLabel?: string
  variant?: ErrorCardVariant
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

export function ErrorCard({
  title,
  message,
  onRetry,
  retryLabel,
  variant = 'default',
  className,
  ...hostAttributes
}: ErrorCardProps) {
  if (!title.trim()) throw new Error('title-required')

  const effectiveRetryLabel = retryLabel?.trim() ? retryLabel : 'Retry'
  const TitleElement = variant === 'page' ? 'h2' : 'p'

  return (
    <div
      {...hostAttributes}
      role="alert"
      className={classes('hl-error-card', `hl-error-card--${variant}`, className)}
      data-hl-variant={variant}
      data-hl-tone="destructive-soft"
    >
      <TitleElement className="hl-error-card__title" data-hl-tone="destructive">
        {title}
      </TitleElement>
      {message ? (
        <p className="hl-error-card__message" data-hl-tone="muted">
          {message}
        </p>
      ) : null}
      {onRetry ? (
        <button
          type="button"
          className="hl-error-card__retry"
          data-hl-tone="destructive"
          onClick={onRetry}
        >
          {effectiveRetryLabel}
        </button>
      ) : null}
    </div>
  )
}
