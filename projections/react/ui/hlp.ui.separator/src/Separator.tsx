import type { HTMLAttributes } from 'react'

export type SeparatorOrientation = 'horizontal' | 'vertical'

export interface SeparatorProps
  extends Omit<HTMLAttributes<HTMLDivElement>, 'aria-orientation' | 'role'> {
  orientation?: SeparatorOrientation
  decorative?: boolean
  label?: string
}

const classes = (...values: Array<string | undefined>) => values.filter(Boolean).join(' ')

export function Separator({
  orientation = 'horizontal',
  decorative = true,
  label,
  className,
  ...hostAttributes
}: SeparatorProps) {
  const hasLabel = typeof label === 'string' && label.trim().length > 0
  const role = decorative ? 'none' : 'separator'
  const ariaOrientation = decorative ? undefined : orientation

  if (hasLabel) {
    return (
      <div
        {...hostAttributes}
        role={role}
        aria-orientation={ariaOrientation}
        className={classes(
          'hl-separator',
          'hl-separator--labeled',
          `hl-separator--${orientation}`,
          className,
        )}
        data-hl-orientation={orientation}
      >
        <span aria-hidden="true" className="hl-separator__rule" data-hl-rule="true" />
        <span aria-hidden="true" className="hl-separator__label">{label}</span>
        <span aria-hidden="true" className="hl-separator__rule" data-hl-rule="true" />
      </div>
    )
  }

  return (
    <div
      {...hostAttributes}
      role={role}
      aria-orientation={ariaOrientation}
      className={classes('hl-separator', `hl-separator--${orientation}`, className)}
      data-hl-orientation={orientation}
      data-hl-rule="true"
    />
  )
}
