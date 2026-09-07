import * as React from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cn } from './cn'
import { Loader } from './Loader'
import { useButtonGroupContext } from './ButtonGroup'
import { useHarborlineStrings } from './locale'

export type ButtonVariant = 'primary' | 'secondary' | 'tertiary' | 'destructive' | 'ghost'
export type ButtonIntent =
  | 'primary' | 'secondary' | 'danger' | 'warning' | 'info'
  | 'success' | 'light' | 'dark' | 'subtle' | 'transparent'
export type ButtonSize = 'sm' | 'md' | 'lg' | 'icon'
export type ButtonType = 'button' | 'submit' | 'reset'
export type ButtonFillMode = 'solid' | 'flat' | 'outline' | 'link' | 'clear'
export type ButtonThemeColor =
  | 'base' | 'primary' | 'secondary' | 'tertiary'
  | 'info' | 'success' | 'warning' | 'error'
export type ButtonRounded = 'small' | 'medium' | 'large' | 'full' | 'none'

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  /** Canonical cross-projection semantic intent. `variant` remains a compatibility alias. */
  intent?: ButtonIntent
  variant?: ButtonVariant
  size?: ButtonSize
  type?: ButtonType
  disabled?: boolean
  loading?: boolean
  asChild?: boolean
  leadingIcon?: React.ReactNode
  trailingIcon?: React.ReactNode
  children: React.ReactNode
  /** React 19 ref-as-prop; forwarded to the underlying button element. */
  ref?: React.Ref<HTMLButtonElement>
  /**
   * Kendo fillMode axis: controls the fill treatment independent of variant/themeColor.
   * When provided, overrides the fill portion of the variant class.
   */
  fillMode?: ButtonFillMode
  /**
   * Kendo themeColor axis: semantic color independent of fillMode.
   * When provided alongside fillMode, the two axes combine.
   * When provided without fillMode, applies solid fill with this theme color.
   */
  themeColor?: ButtonThemeColor
  /**
   * Border-radius scale. Overrides the default rounded-md from the base class.
   */
  rounded?: ButtonRounded
  /**
   * When inside a ButtonGroup with selection mode, identifies this button in the selection set.
   * The ButtonGroup will automatically apply active styling and aria-pressed when selected.
   */
  value?: string
}

const variantClasses = {
  primary: 'bg-primary text-primary-foreground hover:bg-primary/90 focus-visible:ring-ring',
  secondary:
    'bg-background text-foreground border border-border hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring',
  tertiary: 'bg-transparent text-primary hover:bg-accent focus-visible:ring-ring',
  destructive: 'bg-destructive text-destructive-foreground hover:bg-destructive/90 focus-visible:ring-destructive',
  ghost: 'bg-transparent text-foreground hover:bg-accent hover:text-accent-foreground focus-visible:ring-ring',
} satisfies Record<ButtonVariant, string>

const intentClasses = {
  primary: variantClasses.primary,
  secondary: variantClasses.secondary,
  danger: variantClasses.destructive,
  warning: 'bg-warning text-primary-foreground hover:bg-warning/90 focus-visible:ring-warning',
  info: 'bg-accent-brand text-primary-foreground hover:bg-accent-hover focus-visible:ring-accent-brand',
  success: 'bg-success text-primary-foreground hover:bg-success/90 focus-visible:ring-success',
  light: 'bg-background text-foreground border border-border hover:bg-accent focus-visible:ring-ring',
  dark: 'bg-foreground text-background hover:opacity-90 focus-visible:ring-ring',
  subtle: variantClasses.tertiary,
  transparent: variantClasses.ghost,
} satisfies Record<ButtonIntent, string>

const sizeClasses = {
  sm: 'h-8 px-3 text-xs gap-1.5',
  md: 'h-10 px-4 text-sm gap-2',
  lg: 'h-12 px-6 text-base gap-2',
  icon: 'h-10 w-10 p-0',
} satisfies Record<ButtonSize, string>

const roundedClasses: Record<ButtonRounded, string> = {
  small: 'rounded-sm',
  medium: 'rounded-md',
  large: 'rounded-lg',
  full: 'rounded-full',
  none: 'rounded-none',
}

// themeColor solid-fill classes
const themeColorSolidClasses: Record<ButtonThemeColor, string> = {
  base: 'bg-secondary text-secondary-foreground hover:bg-secondary/80',
  primary: 'bg-primary text-primary-foreground hover:bg-primary/90',
  secondary: 'bg-secondary text-secondary-foreground hover:bg-secondary/80',
  tertiary: 'bg-muted text-foreground hover:bg-muted/80',
  info: 'bg-accent-brand text-primary-foreground hover:bg-accent-hover',
  success: 'bg-success text-primary-foreground hover:bg-success/90',
  warning: 'bg-warning text-primary-foreground hover:bg-warning/90',
  error: 'bg-destructive text-destructive-foreground hover:bg-destructive/90',
}

// themeColor text color for non-solid fillModes
const themeColorTextClasses: Record<ButtonThemeColor, string> = {
  base: 'text-foreground',
  primary: 'text-primary',
  secondary: 'text-secondary-foreground',
  tertiary: 'text-foreground',
  info: 'text-accent-brand',
  success: 'text-success',
  warning: 'text-warning',
  error: 'text-destructive',
}

function buildFillModeClass(fillMode: ButtonFillMode, themeColor?: ButtonThemeColor): string {
  const colorText = themeColor ? themeColorTextClasses[themeColor] : 'text-foreground'
  switch (fillMode) {
    case 'solid':
      return themeColor ? themeColorSolidClasses[themeColor] : ''
    case 'flat':
      return cn('bg-transparent hover:bg-accent', colorText)
    case 'outline':
      return cn('bg-transparent border border-current hover:bg-accent', colorText)
    case 'link':
      return cn('bg-transparent underline-offset-4 hover:underline p-0 h-auto', colorText)
    case 'clear':
      return cn('bg-transparent hover:bg-accent border-0 shadow-none', colorText)
  }
}

const BASE_CLASSES_NO_ROUNDED =
  'inline-flex max-w-full items-center justify-center whitespace-normal text-center font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50'

const baseClasses = BASE_CLASSES_NO_ROUNDED + ' rounded-md'

const canonicalIntentForVariant: Record<ButtonVariant, ButtonIntent> = {
  primary: 'primary',
  secondary: 'secondary',
  tertiary: 'subtle',
  destructive: 'danger',
  ghost: 'transparent',
}

const canonicalIntentForThemeColor: Record<ButtonThemeColor, ButtonIntent> = {
  base: 'secondary',
  primary: 'primary',
  secondary: 'secondary',
  tertiary: 'subtle',
  info: 'info',
  success: 'success',
  warning: 'warning',
  error: 'danger',
}

export function Button({
  intent,
  variant = 'secondary',
  size = 'md',
  type = 'button',
  disabled = false,
  loading = false,
  asChild = false,
  leadingIcon,
  trailingIcon,
  children,
  className,
  fillMode,
  themeColor,
  rounded,
  value,
  onClick,
  ...props
}: ButtonProps) {
  const Comp = asChild ? Slot : 'button'
  const { locale, direction, t } = useHarborlineStrings()

  if (
    !asChild &&
    size === 'icon' &&
    !props['aria-label'] &&
    !props['aria-labelledby']
  ) {
    throw new Error('accessible-name-required: icon-only Button requires aria-label or aria-labelledby')
  }

  // When inside a ButtonGroup with selection mode, wire into the selection context
  const ctx = useButtonGroupContext()
  const isInSelectableGroup = ctx.selection !== 'none' && value !== undefined
  const isSelected = isInSelectableGroup && ctx.selected.includes(value)
  const isDisabled = disabled || (isInSelectableGroup ? ctx.disabled : false)
  const isInert = isDisabled || loading

  const handleClick = (e: React.MouseEvent<HTMLButtonElement>) => {
    if (isInert) {
      e.preventDefault()
      e.stopPropagation()
      return
    }
    if (isInSelectableGroup) ctx.onSelect(value)
    onClick?.(e)
  }

  const loaderSize = size === 'lg' ? 'sm' : 'xs'
  const effectiveLeading = loading ? (
    <Loader variant="spinner" size={loaderSize} />
  ) : (
    leadingIcon
  )

  // When fillMode or themeColor is set, they override the variant-based appearance
  const appearanceClass = fillMode
    ? buildFillModeClass(fillMode, themeColor)
    : themeColor
      ? themeColorSolidClasses[themeColor]
      : intent
        ? intentClasses[intent]
        : variantClasses[variant]

  // Explicit `rounded` overrides the default rounded-md in baseClasses
  const roundedClass = rounded ? roundedClasses[rounded] : undefined
  const effectiveIntent = themeColor
    ? canonicalIntentForThemeColor[themeColor]
    : intent ?? canonicalIntentForVariant[variant]

  const buttonProps = {
    ...props,
    lang: locale,
    dir: direction,
    'data-hl-intent': effectiveIntent,
    'data-hl-fill': fillMode ?? 'solid',
    'data-hl-rounded': rounded ?? 'medium',
    'data-hl-size': size,
    ...(!asChild ? { type } : {}),
    disabled: isDisabled,
    'aria-busy': loading || undefined,
    'aria-disabled': isInert || undefined,
    ...(isInSelectableGroup ? { 'aria-pressed': isSelected } : {}),
    onClick: handleClick,
    className: cn(
      rounded ? BASE_CLASSES_NO_ROUNDED : baseClasses,
      roundedClass,
      appearanceClass,
      sizeClasses[size],
      // Active/selected styling when participating in a ButtonGroup selection
      isSelected && 'bg-primary text-primary-foreground border-primary hover:bg-primary/90',
      className,
    ),
  }

  if (asChild) {
    // Slot merges props into its single child element; must provide exactly one child
    return (
      <>
        <Comp {...buttonProps}>{children}</Comp>
        {loading && (
          <span role="status" aria-label={t('common.loading')} className="hl-button__status">
            {t('common.loading')}
          </span>
        )}
      </>
    )
  }

  return (
    <>
      <Comp {...buttonProps}>
        {effectiveLeading}
        {children}
        {trailingIcon}
      </Comp>
      {loading && (
        <span role="status" aria-label={t('common.loading')} className="hl-button__status">
          {t('common.loading')}
        </span>
      )}
    </>
  )
}
