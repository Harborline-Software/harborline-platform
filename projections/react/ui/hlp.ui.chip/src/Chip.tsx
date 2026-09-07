import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type ChipFillMode = 'solid' | 'flat' | 'outline'
export type ChipSize = 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large'
export type ChipThemeColor =
  | 'base'
  | 'primary'
  | 'secondary'
  | 'tertiary'
  | 'info'
  | 'success'
  | 'warning'
  | 'error'
export type ChipRounded = 'small' | 'medium' | 'large' | 'full'

export interface ChipProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>,
  'children' | 'className' | 'disabled' | 'onClick' | 'onSelect'> {
  readonly text?: string
  readonly accessibleLabel?: string
  readonly icon?: React.ReactNode
  readonly avatar?: string
  readonly selectable?: boolean
  readonly removable?: boolean
  readonly selected?: boolean
  readonly defaultSelected?: boolean
  readonly onSelectedChange?: (selected: boolean) => void
  readonly onRemove?: () => void
  readonly removeLabel?: string
  readonly disabled?: boolean
  readonly fillMode?: ChipFillMode
  readonly themeColor?: ChipThemeColor
  readonly size?: ChipSize
  readonly rounded?: ChipRounded
  readonly className?: string
}

function normalizeSize(size: ChipSize): 'sm' | 'md' | 'lg' {
  if (size === 'small') return 'sm'
  if (size === 'medium') return 'md'
  if (size === 'large') return 'lg'
  return size
}

interface ChipContentProps {
  readonly avatar?: string
  readonly icon?: React.ReactNode
  readonly text?: string
}

function ChipContent({ avatar, icon, text }: ChipContentProps) {
  return <>
    {avatar ? <img alt="" className="hl-chip__avatar" src={avatar} /> : null}
    {icon ? <span aria-hidden="true" className="hl-chip__icon">{icon}</span> : null}
    {text ? <span className="hl-chip__text">{text}</span> : null}
  </>
}

export function Chip({
  text,
  accessibleLabel,
  icon,
  avatar,
  selectable = true,
  removable = false,
  selected: controlledSelected,
  defaultSelected = false,
  onSelectedChange,
  onRemove,
  removeLabel,
  disabled = false,
  fillMode = 'solid',
  themeColor = 'base',
  size = 'md',
  rounded = 'full',
  className,
  'aria-label': ariaLabel,
  ...buttonAttributes
}: ChipProps) {
  const { direction, t } = useHarborlineStrings()
  const [uncontrolledSelected, setUncontrolledSelected] = React.useState(defaultSelected)
  const controlled = controlledSelected !== undefined
  const selected = controlled ? controlledSelected : uncontrolledSelected
  const effectiveLabel = ariaLabel ?? accessibleLabel
  const canonicalSize = normalizeSize(size)

  if (!(text?.trim() || effectiveLabel?.trim())) throw new Error('accessible-content-required')

  const styleAttributes = {
    'data-hl-fill-mode': fillMode,
    'data-hl-theme-color': themeColor,
    'data-hl-size': canonicalSize,
    'data-hl-rounded': rounded,
    'data-hl-selected': selected || undefined,
  } as const

  const toggle = () => {
    if (disabled || !selectable) return
    const next = !selected
    if (!controlled) setUncontrolledSelected(next)
    onSelectedChange?.(next)
  }

  const body = selectable ? (
    <button
      {...buttonAttributes}
      {...styleAttributes}
      aria-label={effectiveLabel}
      aria-pressed={selected}
      className={cn('hl-chip__body', !removable && 'hl-chip', !removable && className)}
      dir={!removable ? direction : undefined}
      disabled={disabled}
      onClick={toggle}
      type="button"
    >
      <ChipContent avatar={avatar} icon={icon} text={text} />
    </button>
  ) : (
    <span
      {...styleAttributes}
      aria-label={effectiveLabel}
      className={cn('hl-chip__body', !removable && 'hl-chip', !removable && className)}
      dir={!removable ? direction : undefined}
    >
      <ChipContent avatar={avatar} icon={icon} text={text} />
    </span>
  )

  if (!removable) return body

  const effectiveRemoveLabel = removeLabel
    ?? t('feedback.removeItem', { label: text?.trim() || effectiveLabel?.trim() || '' })

  const remove = (event: React.SyntheticEvent<HTMLButtonElement>) => {
    event.stopPropagation()
    if (!disabled) onRemove?.()
  }

  return (
    <span {...styleAttributes} className={cn('hl-chip', className)} dir={direction}>
      {body}
      <button
        aria-label={effectiveRemoveLabel}
        className="hl-chip__remove"
        disabled={disabled}
        onClick={remove}
        onKeyDown={event => {
          if (event.key !== 'Backspace' && event.key !== 'Delete') return
          event.preventDefault()
          remove(event)
        }}
        type="button"
      >
        <svg aria-hidden="true" className="hl-chip__remove-icon" fill="none" viewBox="0 0 24 24">
          <path d="M6 6l12 12M18 6L6 18" stroke="currentColor" strokeLinecap="round" strokeWidth="2" />
        </svg>
      </button>
    </span>
  )
}
