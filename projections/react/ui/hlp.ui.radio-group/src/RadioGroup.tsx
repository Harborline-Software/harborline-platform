import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'

export interface RadioOption {
  value: string
  label: string
  description?: string
  disabled?: boolean
}

export type RadioGroupOrientation = 'vertical' | 'horizontal'

export interface RadioGroupProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'onChange'> {
  name: string
  value: string
  onChange: (value: string) => void
  options: readonly RadioOption[]
  orientation?: RadioGroupOrientation
  disabled?: boolean
  error?: boolean
  required?: boolean
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function joinIds(...values: Array<string | undefined>): string | undefined {
  const ids = [...new Set(values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? []))]
  return ids.length ? ids.join(' ') : undefined
}

export const RadioGroup = React.forwardRef<HTMLDivElement, RadioGroupProps>(function RadioGroup(
  {
    name,
    value,
    onChange,
    options,
    orientation = 'vertical',
    disabled = false,
    error = false,
    required = false,
    className,
    'aria-describedby': ariaDescribedBy,
    'aria-disabled': ariaDisabled,
    'aria-invalid': ariaInvalid,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledBy,
    'aria-required': ariaRequired,
    ...hostAttributes
  },
  forwardedRef,
) {
  const formField = useFormFieldContext()
  const effectiveDisabled = disabled || Boolean(formField.disabled)
  const effectiveRequired = required || Boolean(formField.required)
  const describedBy = joinIds(formField.describedBy, ariaDescribedBy)
  const instanceId = React.useId().replace(/:/g, '')

  return (
    <div
      {...hostAttributes}
      aria-describedby={describedBy}
      aria-disabled={effectiveDisabled ? true : ariaDisabled}
      aria-invalid={error ? true : ariaInvalid}
      aria-label={ariaLabel}
      aria-labelledby={ariaLabelledBy ?? formField.labelId}
      aria-required={effectiveRequired ? true : ariaRequired}
      className={classes(
        'hl-radio-group',
        `hl-radio-group--${orientation}`,
        error && 'hl-radio-group--error',
        className,
      )}
      data-hl-disabled={effectiveDisabled || undefined}
      data-hl-error={error || undefined}
      data-hl-orientation={orientation}
      data-hl-required={effectiveRequired || undefined}
      ref={forwardedRef}
      role="radiogroup"
    >
      {options.map((option, index) => {
        const optionDisabled = effectiveDisabled || Boolean(option.disabled)
        const optionId = index === 0 && formField.id
          ? formField.id
          : `hl-radio-${instanceId}-${index}`
        const descriptionId = option.description ? `${optionId}-description` : undefined
        return (
          <div className="hl-radio-group__option" key={`${option.value}-${index}`}>
            <label className={classes('hl-radio-group__label', optionDisabled && 'hl-radio-group__label--disabled')} htmlFor={optionId}>
              <input
                aria-describedby={descriptionId}
                checked={value === option.value}
                className="hl-radio-group__control"
                disabled={optionDisabled}
                id={optionId}
                name={name}
                onChange={() => {
                  if (!optionDisabled) onChange(option.value)
                }}
                required={effectiveRequired}
                type="radio"
                value={option.value}
              />
              <span className="hl-radio-group__label-text">{option.label}</span>
            </label>
            {option.description ? (
              <span className="hl-radio-group__description" id={descriptionId}>{option.description}</span>
            ) : null}
          </div>
        )
      })}
    </div>
  )
})
