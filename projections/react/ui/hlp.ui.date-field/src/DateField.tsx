import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export interface DateFieldProps {
  name: string
  value: string
  onChange: (value: string) => void
  min?: string
  max?: string
  disabled?: boolean
  required?: boolean
  error?: boolean
  'aria-label'?: string
  'aria-labelledby'?: string
}

export function DateField({
  name,
  value,
  onChange,
  min,
  max,
  disabled,
  required,
  error,
  'aria-label': ariaLabel,
  'aria-labelledby': ariaLabelledBy,
}: DateFieldProps) {
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
        'hl-date-field',
        error && 'hl-date-field--invalid',
        effectiveDisabled && 'hl-date-field--disabled',
      )}
      disabled={effectiveDisabled}
      id={name}
      max={max}
      min={min}
      name={name}
      onChange={event => onChange(event.currentTarget.value)}
      required={effectiveRequired}
      type="date"
      value={value}
    />
  )
}
