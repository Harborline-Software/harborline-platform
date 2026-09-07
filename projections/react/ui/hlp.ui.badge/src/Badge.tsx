import type { HTMLAttributes, ReactNode } from 'react'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type BadgeVariant = 'default' | 'secondary' | 'info' | 'success' | 'warning' | 'danger'
export type BadgeSize = 'sm' | 'md'
export type BadgeAppearance = 'solid' | 'subtle' | 'outline'
export type BadgeShape = 'rounded' | 'pill' | 'square'
export type BadgeFillMode = 'solid' | 'outline' | 'flat'
export type BadgeRounded = 'none' | 'sm' | 'md' | 'lg' | 'full'
export type BadgeThemeColor = 'primary' | 'secondary' | 'tertiary' | 'info' | 'success' | 'warning' | 'error' | 'inverse'
export type BadgePosition = 'edge' | 'outside' | 'inside'
export interface BadgeAlign { horizontal: 'start' | 'end'; vertical: 'top' | 'bottom' }
export interface BadgeProps extends Omit<HTMLAttributes<HTMLSpanElement>, 'children'> { variant?: BadgeVariant; size?: BadgeSize; appearance?: BadgeAppearance; shape?: BadgeShape; leadingIcon?: ReactNode; trailingIcon?: ReactNode; children?: ReactNode; fillMode?: BadgeFillMode; rounded?: BadgeRounded; themeColor?: BadgeThemeColor; align?: BadgeAlign; position?: BadgePosition; cutoutBorder?: boolean; announceChanges?: boolean }
const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export function Badge({ variant = 'default', size = 'md', appearance, shape, leadingIcon, trailingIcon, children, fillMode, rounded, themeColor, align, position, cutoutBorder = false, announceChanges = false, className, ...host }: BadgeProps) {
  const { direction } = useHarborlineStrings()
  const effectiveFill = fillMode ?? (appearance === 'subtle' ? 'flat' : appearance) ?? 'flat'
  const effectiveRounded = rounded ?? (shape === 'pill' ? 'full' : shape === 'square' ? 'none' : shape === 'rounded' ? 'sm' : 'sm')
  const overlay = align && position ? `hl-badge--${align.vertical}-${align.horizontal}-${position}` : undefined
  return (
    <span {...host} dir={direction} className={classes('hl-badge', `hl-badge--${size}`, `hl-badge--${effectiveFill}`, `hl-badge--rounded-${effectiveRounded}`, `hl-badge--${themeColor ?? variant}`, overlay, cutoutBorder && 'hl-badge--cutout', className)} data-hl-fill={effectiveFill} data-hl-position={position}>
      {leadingIcon && <span className="hl-badge__icon" aria-hidden="true">{leadingIcon}</span>}
      {announceChanges ? <span aria-live="polite" aria-atomic="true">{children}</span> : children}
      {trailingIcon && <span className="hl-badge__icon" aria-hidden="true">{trailingIcon}</span>}
    </span>
  )
}
