import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'

export interface NumberFieldProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>,
    'children' | 'defaultValue' | 'disabled' | 'max' | 'min' | 'name' | 'onChange' | 'required' | 'step' | 'type' | 'value'> {
  name: string
  value: number | string
  onChange: (value: string) => void
  step?: number
  min?: number
  max?: number
  disabled?: boolean
  required?: boolean
  error?: boolean
}

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

const joinIds = (...values: Array<string | undefined>) => {
  const ids = values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? [])
  return [...new Set(ids)].join(' ') || undefined
}

export const NumberField = React.forwardRef<HTMLInputElement, NumberFieldProps>(function NumberField(
  {
    name,
    value,
    onChange,
    step,
    min,
    max,
    disabled = false,
    required = false,
    error = false,
    className,
    'aria-describedby': ariaDescribedBy,
    'aria-invalid': ariaInvalid,
    'aria-required': ariaRequired,
    ...inputAttributes
  },
  forwardedRef,
) {
  const context = useFormFieldContext()
  const effectiveDisabled = disabled || context.disabled === true
  const effectiveRequired = required || context.required === true
  const describedBy = joinIds(context.describedBy, ariaDescribedBy)

  return (
    <input
      {...inputAttributes}
      aria-describedby={describedBy}
      aria-invalid={error ? true : ariaInvalid}
      aria-required={effectiveRequired ? true : ariaRequired}
      className={classes(
        'hl-number-field',
        effectiveDisabled && 'hl-number-field--disabled',
        error && 'hl-number-field--error',
        className,
      )}
      data-hl-disabled={effectiveDisabled || undefined}
      data-hl-error={error || undefined}
      disabled={effectiveDisabled}
      id={name}
      max={max}
      min={min}
      name={name}
      onChange={event => onChange(event.currentTarget.value)}
      ref={forwardedRef}
      required={effectiveRequired}
      step={step}
      type="number"
      value={value}
    />
  )
})
