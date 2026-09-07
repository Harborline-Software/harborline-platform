import * as React from 'react'

import { FormFieldContext, type FormFieldContextValue } from './FormFieldContext'

export interface FormFieldProps
  extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children'> {
  label: string
  name: string
  required?: boolean
  disabled?: boolean
  hint?: string
  error?: string
  children: React.ReactNode
}

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export function FormField({
  label,
  name,
  required = false,
  disabled = false,
  hint,
  error,
  children,
  className,
  ...hostAttributes
}: FormFieldProps) {
  const labelId = `${name}-label`
  const hintId = hint ? `${name}-hint` : undefined
  const renderedError = disabled ? undefined : error
  const errorId = renderedError ? `${name}-error` : undefined
  const describedBy = [hintId, errorId].filter(Boolean).join(' ') || undefined
  const context = React.useMemo<FormFieldContextValue>(() => ({
    id: name,
    labelId,
    describedBy,
    required,
    disabled,
  }), [name, labelId, describedBy, required, disabled])

  return (
    <div
      {...hostAttributes}
      className={classes(
        'hl-form-field',
        disabled && 'hl-form-field--disabled',
        renderedError && 'hl-form-field--error',
        className,
      )}
      data-hl-disabled={disabled || undefined}
      data-hl-error={Boolean(renderedError) || undefined}
      data-hl-required={required || undefined}
    >
      <label className="hl-form-field__label" htmlFor={name} id={labelId}>
        <span className="hl-form-field__label-text">{label}</span>
        {required ? (
          <span aria-hidden="true" className="hl-form-field__required">*</span>
        ) : null}
      </label>

      <FormFieldContext.Provider value={context}>
        <div className="hl-form-field__control">{children}</div>
      </FormFieldContext.Provider>

      {hint ? (
        <div className="hl-form-field__hint" id={hintId}>{hint}</div>
      ) : null}
      {renderedError ? (
        <div className="hl-form-field__error" id={errorId} role="alert">{renderedError}</div>
      ) : null}
    </div>
  )
}
