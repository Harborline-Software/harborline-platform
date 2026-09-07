import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export interface DateTimeFieldProps {
  name: string
  value: string
  onChange: (value: string) => void
  min?: string
  max?: string
  step?: number
  disabled?: boolean
  required?: boolean
  error?: boolean
  'aria-label'?: string
  'aria-labelledby'?: string
}

export function DateTimeField({
  name,
  value,
  onChange,
  min,
  max,
  step,
  disabled,
  required,
  error,
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledBy,
}: DateTimeFieldProps) {
  const context = useFormFieldContext()
  const effectiveDisabled = Boolean(disabled || context.disabled)
  const effectiveRequired = Boolean(required || context.required)

  return (
    <input
      aria-describedby={context.describedBy || undefined}
      aria-invalid={error ? true : undefined}
      aria-label={ariaLabel}
      aria-labelledby={ariaLabelledBy ?? context.labelId}
      aria-required={effectiveRequired || undefined}
      className={classes(
        'hl-date-time-field',
        error && 'hl-date-time-field--invalid',
        effectiveDisabled && 'hl-date-time-field--disabled',
      )}
      disabled={effectiveDisabled}
      id={name}
      max={max}
      min={min}
      name={name}
      onChange={event => onChange(event.currentTarget.value)}
      required={effectiveRequired}
      step={step}
      type="datetime-local"
      value={value}
    />
  )
}
