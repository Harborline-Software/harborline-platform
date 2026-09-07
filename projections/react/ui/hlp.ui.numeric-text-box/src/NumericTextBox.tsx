import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type {
  NumericTextBoxProps,
  NumericTextBoxSize,
  NumericTextBoxSizeAlias,
  NumericTextBoxValue,
} from './NumericTextBox.types'

const STRICT_NUMBER = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?$/

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function normalizeSize(size: NumericTextBoxSizeAlias): NumericTextBoxSize {
  if (size === 'small') return 'sm'
  if (size === 'medium') return 'md'
  if (size === 'large') return 'lg'
  return size
}

function validateConfiguration({
  controlled,
  value,
  min,
  max,
  step,
  decimals,
}: {
  controlled: boolean
  value: NumericTextBoxValue | undefined
  min: number | undefined
  max: number | undefined
  step: number
  decimals: number
}): void {
  if (min !== undefined && !Number.isFinite(min)) throw new Error('invalid-bound')
  if (max !== undefined && !Number.isFinite(max)) throw new Error('invalid-bound')
  if (min !== undefined && max !== undefined && min > max) throw new Error('invalid-min-max')
  if (!Number.isFinite(step) || step <= 0) throw new Error('invalid-step')
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > 20) throw new Error('invalid-decimals')
  if (controlled && value !== undefined && value !== null && !Number.isFinite(value)) {
    throw new Error('invalid-controlled-value')
  }
}

function clamp(value: number, min: number | undefined, max: number | undefined): number {
  return Math.min(max ?? Number.POSITIVE_INFINITY, Math.max(min ?? Number.NEGATIVE_INFINITY, value))
}

function parseStrict(value: string): NumericTextBoxValue {
  const trimmed = value.trim()
  if (trimmed.length === 0 || !STRICT_NUMBER.test(trimmed)) return null
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : null
}

function fractionDigits(format: string | undefined, fallback: number): {
  readonly kind: 'plain' | 'currency' | 'percent'
  readonly digits: number
} {
  const match = /^([cp])(\d*)$/.exec(format ?? '')
  if (!match) return { kind: 'plain', digits: fallback }
  const kind = match[1] === 'c' ? 'currency' : 'percent'
  const digitsText = match[2]
  const digits = digitsText === '' ? (kind === 'currency' ? 2 : 0) : Number(digitsText)
  return { kind, digits }
}

function formatValue(
  value: NumericTextBoxValue,
  decimals: number,
  format: string | undefined,
  locale: string,
  currency: string,
  formatter: (value: number, options?: Intl.NumberFormatOptions, localeOverride?: string) => string,
): string {
  if (value === null) return ''
  const intent = fractionDigits(format, decimals)
  if (!Number.isInteger(intent.digits) || intent.digits < 0 || intent.digits > 20) return String(value)
  const options: Intl.NumberFormatOptions = {
    minimumFractionDigits: intent.digits,
    maximumFractionDigits: intent.digits,
  }
  if (intent.kind === 'currency') {
    options.style = 'currency'
    options.currency = currency
  } else if (intent.kind === 'percent') {
    options.style = 'percent'
  }
  try {
    return formatter(value, options, locale)
  } catch {
    return String(value)
  }
}

export const NumericTextBox = React.forwardRef<HTMLInputElement, NumericTextBoxProps>(function NumericTextBox(
  props,
  forwardedRef,
) {
  const controlled = Object.prototype.hasOwnProperty.call(props, 'value')
  const {
    value,
    defaultValue,
    onChange,
    min,
    max,
    step = 1,
    decimals = 2,
    format,
    locale,
    currency = 'USD',
    spinners = true,
    placeholder,
    disabled = false,
    readOnly = false,
    size = 'md',
    fillMode = 'solid',
    rounded = 'medium',
    id,
    name,
    required = false,
    error = false,
    onFocus,
    onBlur,
    incrementLabel,
    decrementLabel,
    className,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledBy,
    'aria-describedby': ariaDescribedBy,
    ...hostAttributes
  } = props
  const { locale: contextLocale, direction, formatNumber, resolveString } = useHarborlineStrings()
  const effectiveLocale = locale ?? contextLocale
  const canonicalSize = normalizeSize(size)
  const [internalValue, setInternalValue] = React.useState<NumericTextBoxValue>(() => defaultValue ?? null)
  const [editing, setEditing] = React.useState(false)
  const [rawValue, setRawValue] = React.useState('')
  const suppressNextBlurCommit = React.useRef(false)
  const currentValue = controlled ? (value ?? null) : internalValue

  validateConfiguration({ controlled, value, min, max, step, decimals })

  const requestValue = React.useCallback((next: NumericTextBoxValue) => {
    if (disabled || readOnly) return
    if (!controlled) setInternalValue(next)
    onChange?.(next)
  }, [controlled, disabled, onChange, readOnly])

  const commit = React.useCallback((text: string) => {
    const parsed = parseStrict(text)
    requestValue(parsed === null ? null : clamp(parsed, min, max))
    setEditing(false)
  }, [max, min, requestValue])

  const spin = React.useCallback((directionValue: 1 | -1) => {
    if (disabled || readOnly) return
    const anchor = currentValue ?? 0
    requestValue(clamp(anchor + directionValue * step, min, max))
  }, [currentValue, disabled, max, min, readOnly, requestValue, step])

  const incrementDisabled = disabled || (max !== undefined && (currentValue ?? 0) >= max)
  const decrementDisabled = disabled || (min !== undefined && (currentValue ?? 0) <= min)
  const displayedValue = editing
    ? rawValue
    : formatValue(currentValue, decimals, format, effectiveLocale, currency, formatNumber)

  return (
    <div
      {...hostAttributes}
      className={classes('hl-numeric-text-box', className)}
      data-disabled={disabled || undefined}
      data-error={error || undefined}
      data-fill-mode={fillMode}
      data-readonly={readOnly || undefined}
      data-rounded={rounded}
      data-size={canonicalSize}
      dir={direction}
    >
      <input
        aria-describedby={ariaDescribedBy}
        aria-invalid={error || undefined}
        aria-label={ariaLabel}
        aria-labelledby={ariaLabelledBy}
        aria-required={required || undefined}
        className="hl-numeric-text-box__input"
        disabled={disabled}
        id={id}
        inputMode="decimal"
        name={name}
        onBlur={event => {
          if (suppressNextBlurCommit.current) {
            suppressNextBlurCommit.current = false
          } else if (!readOnly && !disabled) {
            commit(event.currentTarget.value)
          } else {
            setEditing(false)
          }
          onBlur?.(event)
        }}
        onChange={event => {
          suppressNextBlurCommit.current = false
          setEditing(true)
          setRawValue(event.currentTarget.value)
        }}
        onFocus={event => {
          suppressNextBlurCommit.current = false
          setEditing(true)
          setRawValue(currentValue === null ? '' : String(currentValue))
          onFocus?.(event)
        }}
        onKeyDown={event => {
          if (event.key === 'Enter' && !disabled && !readOnly) {
            event.preventDefault()
            suppressNextBlurCommit.current = true
            commit(rawValue)
          } else if (spinners && event.key === 'ArrowUp' && !incrementDisabled && !readOnly) {
            event.preventDefault()
            spin(1)
          } else if (spinners && event.key === 'ArrowDown' && !decrementDisabled && !readOnly) {
            event.preventDefault()
            spin(-1)
          }
        }}
        placeholder={placeholder}
        readOnly={readOnly}
        ref={forwardedRef}
        required={required}
        type="text"
        value={displayedValue}
      />
      {spinners && !readOnly ? (
        <div className="hl-numeric-text-box__spinners">
          <button
            aria-label={resolveString(incrementLabel, 'forms.numeric.increment')}
            className="hl-numeric-text-box__spinner"
            disabled={incrementDisabled}
            onClick={() => spin(1)}
            onMouseDown={event => event.preventDefault()}
            type="button"
          >
            <span aria-hidden="true">▲</span>
          </button>
          <button
            aria-label={resolveString(decrementLabel, 'forms.numeric.decrement')}
            className="hl-numeric-text-box__spinner"
            disabled={decrementDisabled}
            onClick={() => spin(-1)}
            onMouseDown={event => event.preventDefault()}
            type="button"
          >
            <span aria-hidden="true">▼</span>
          </button>
        </div>
      ) : null}
    </div>
  )
})
