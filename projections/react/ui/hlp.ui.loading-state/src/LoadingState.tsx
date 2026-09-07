import type { HTMLAttributes } from 'react'

export type LoadingStateVariant = 'page' | 'inline'

export interface LoadingStateProps
  extends Omit<HTMLAttributes<HTMLElement>, 'children'> {
  label: string
  variant?: LoadingStateVariant
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

export function LoadingState({
  label,
  variant = 'page',
  className,
  ...hostAttributes
}: LoadingStateProps) {
  if (!label.trim()) throw new Error('label-required')

  const sharedAttributes = {
    ...hostAttributes,
    role: 'status',
    'aria-live': 'polite' as const,
    'aria-label': label,
    className: classes('hl-loading-state', `hl-loading-state--${variant}`, className),
    'data-hl-variant': variant,
    'data-hl-tone': 'muted-foreground',
  }

  return variant === 'inline'
    ? <p {...sharedAttributes}><span className="hl-loading-state__label">{label}</span></p>
    : <div {...sharedAttributes}><span className="hl-loading-state__label">{label}</span></div>
}
