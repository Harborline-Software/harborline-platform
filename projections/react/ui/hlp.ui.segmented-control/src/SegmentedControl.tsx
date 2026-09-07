import * as React from 'react'

import { useHarborlineLocale } from '@harborline-platform/hlp.ui.locale-provider'
import { useTouchSizing } from '@harborline-platform/hlp.ui.use-can-show-master-detail'

export type SegmentedControlSize = 'sm' | 'md' | 'lg' | 'touch'

export interface SegmentedOption {
  value: string
  label: React.ReactNode
  accessibleLabel?: string
  disabled?: boolean
  automationId?: string
}

export interface SegmentedControlProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'onChange'> {
  accessibleName: string
  options: readonly SegmentedOption[]
  value: string
  onValueChange: (value: string) => void
  size?: SegmentedControlSize
  fullWidth?: boolean
  disabled?: boolean
}

const sizes = new Set<SegmentedControlSize>(['sm', 'md', 'lg', 'touch'])

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function assertProps(accessibleName: string, options: readonly SegmentedOption[], size: SegmentedControlSize): void {
  if (accessibleName.trim().length === 0) throw new Error('accessible-segmented-control-label-required')
  if (!sizes.has(size)) throw new Error('unsupported-segmented-control-size')
  if (new Set(options.map(option => option.value)).size !== options.length) {
    throw new Error('duplicate-segmented-option-value')
  }
}

export const SegmentedControl = React.forwardRef<HTMLDivElement, SegmentedControlProps>(function SegmentedControl(
  {
    accessibleName,
    options,
    value,
    onValueChange,
    size = 'md',
    fullWidth = false,
    disabled = false,
    className,
    ...hostAttributes
  },
  forwardedRef,
) {
  assertProps(accessibleName, options, size)
  const { direction } = useHarborlineLocale()
  const touchSized = useTouchSizing()
  const optionRefs = React.useRef<Array<HTMLButtonElement | null>>([])
  const enabledIndexes = options.flatMap((option, index) => disabled || option.disabled ? [] : [index])
  const selectedIndex = options.findIndex((option, index) => option.value === value && enabledIndexes.includes(index))
  const tabStopIndex = selectedIndex >= 0 ? selectedIndex : (enabledIndexes[0] ?? -1)

  const moveTo = React.useCallback((fromIndex: number, movement: 'next' | 'previous' | 'first' | 'last') => {
    if (enabledIndexes.length === 0) return
    let targetIndex: number
    if (movement === 'first') targetIndex = enabledIndexes[0]!
    else if (movement === 'last') targetIndex = enabledIndexes.at(-1)!
    else {
      const currentPosition = enabledIndexes.indexOf(fromIndex)
      const origin = currentPosition >= 0 ? currentPosition : 0
      const offset = movement === 'next' ? 1 : -1
      targetIndex = enabledIndexes[(origin + offset + enabledIndexes.length) % enabledIndexes.length]!
    }
    optionRefs.current[targetIndex]?.focus()
    onValueChange(options[targetIndex]!.value)
  }, [enabledIndexes, onValueChange, options])

  return (
    <div
      {...hostAttributes}
      aria-label={accessibleName}
      className={classes('hl-segmented-control', fullWidth && 'hl-segmented-control--full-width', className)}
      data-hl-direction={direction}
      data-hl-full-width={fullWidth || undefined}
      data-hl-size={size}
      data-hl-touch={touchSized || size === 'touch' || undefined}
      dir={direction}
      ref={forwardedRef}
      role="radiogroup"
    >
      {options.map((option, index) => {
        const optionDisabled = disabled || Boolean(option.disabled)
        return (
          <button
            aria-checked={value === option.value}
            aria-label={option.accessibleLabel}
            className="hl-segmented-control__option"
            data-automation-id={option.automationId}
            data-hl-selected={value === option.value || undefined}
            disabled={optionDisabled}
            key={option.value}
            onClick={() => onValueChange(option.value)}
            onKeyDown={event => {
              let movement: 'next' | 'previous' | 'first' | 'last' | undefined
              if (event.key === 'Home') movement = 'first'
              else if (event.key === 'End') movement = 'last'
              else if (event.key === 'ArrowDown') movement = 'next'
              else if (event.key === 'ArrowUp') movement = 'previous'
              else if (event.key === 'ArrowRight') movement = direction === 'rtl' ? 'previous' : 'next'
              else if (event.key === 'ArrowLeft') movement = direction === 'rtl' ? 'next' : 'previous'
              if (movement === undefined) return
              event.preventDefault()
              moveTo(index, movement)
            }}
            ref={element => { optionRefs.current[index] = element }}
            role="radio"
            tabIndex={index === tabStopIndex ? 0 : -1}
            type="button"
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
})
