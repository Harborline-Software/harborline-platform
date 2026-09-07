import { forwardRef, type ButtonHTMLAttributes } from 'react'

export type IconButtonVariant = 'default' | 'ghost' | 'outline' | 'destructive'
export type IconButtonSize = 'sm' | 'md' | 'lg' | 'touch'

export interface IconButtonProps
  extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'aria-busy' | 'aria-label'> {
  'aria-label': string
  variant?: IconButtonVariant
  size?: IconButtonSize
  loading?: boolean
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(
  function IconButton(
    {
      'aria-label': accessibleName,
      variant = 'default',
      size = 'md',
      loading = false,
      disabled = false,
      type = 'button',
      className,
      children,
      ...hostAttributes
    },
    ref,
  ) {
    if (typeof accessibleName !== 'string' || !accessibleName.trim()) {
      throw new Error('accessible-name-required')
    }

    return (
      <button
        {...hostAttributes}
        ref={ref}
        type={type}
        aria-label={accessibleName}
        aria-busy={loading || undefined}
        disabled={disabled || loading}
        className={classes(
          'hl-icon-button',
          `hl-icon-button--${variant}`,
          `hl-icon-button--${size}`,
          className,
        )}
        data-hl-loading={loading || undefined}
        data-hl-size={size}
        data-hl-variant={variant}
      >
        {loading ? (
          <svg
            aria-hidden="true"
            className="hl-icon-button__loading-indicator"
            data-hl-loading-indicator="true"
            focusable="false"
            viewBox="0 0 24 24"
          >
            <circle className="hl-icon-button__loading-track" cx="12" cy="12" r="9" />
            <path className="hl-icon-button__loading-segment" d="M12 3a9 9 0 0 1 9 9" />
          </svg>
        ) : (
          children
        )}
      </button>
    )
  },
)
