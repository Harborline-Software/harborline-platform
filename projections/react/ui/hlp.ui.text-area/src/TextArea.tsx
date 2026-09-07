import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'

export type TextAreaResize = 'none' | 'vertical' | 'horizontal' | 'both'
export type TextAreaSize = 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large'
export type TextAreaFillMode = 'solid' | 'outline' | 'flat'
export type TextAreaRounded = 'small' | 'medium' | 'large' | 'full'

export interface TextAreaProps
  extends Omit<React.TextareaHTMLAttributes<HTMLTextAreaElement>,
    'defaultValue' | 'disabled' | 'onChange' | 'required' | 'rows' | 'size' | 'value'> {
  value?: string
  defaultValue?: string
  onChange?: (value: string) => void
  rows?: number
  resize?: TextAreaResize
  autoResize?: boolean
  showCounter?: boolean
  maxLength?: number
  size?: TextAreaSize
  fillMode?: TextAreaFillMode
  rounded?: TextAreaRounded
  error?: boolean
  /** @deprecated Use error. Retained as a React compatibility input. */
  invalid?: boolean
  required?: boolean
  disabled?: boolean
}

const canonicalSize = {
  sm: 'sm',
  small: 'sm',
  md: 'md',
  medium: 'md',
  lg: 'lg',
  large: 'lg',
} satisfies Record<TextAreaSize, 'sm' | 'md' | 'lg'>

const joinIds = (...values: Array<string | undefined>) => {
  const ids = values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? [])
  return [...new Set(ids)].join(' ') || undefined
}

export const TextArea = React.forwardRef<HTMLTextAreaElement, TextAreaProps>(function TextArea(
  {
    value,
    defaultValue,
    onChange,
    rows = 3,
    resize = 'vertical',
    autoResize = false,
    showCounter = false,
    maxLength,
    size = 'md',
    fillMode = 'solid',
    rounded = 'medium',
    error = false,
    invalid,
    required,
    disabled,
    id,
    className,
    'aria-describedby': ariaDescribedBy,
    'aria-invalid': ariaInvalid,
    'aria-labelledby': ariaLabelledBy,
    'aria-required': ariaRequired,
    ...textareaAttributes
  },
  forwardedRef,
) {
  const context = useFormFieldContext()
  const controlled = value !== undefined
  const [uncontrolledValue, setUncontrolledValue] = React.useState(defaultValue ?? '')
  const renderedValue = controlled ? value : uncontrolledValue
  const effectiveDisabled = disabled ?? context.disabled ?? false
  const effectiveRequired = required ?? context.required ?? false
  const effectiveError = error || (invalid ?? false)
  const effectiveResize = autoResize ? 'none' : resize
  const effectiveId = id ?? context.id
  const describedBy = joinIds(context.describedBy, ariaDescribedBy)

  if (invalid !== undefined) {
    console.warn('TextArea: the `invalid` prop is deprecated. Use `error` instead.')
  }

  return (
    <div
      className="hl-text-area__root"
      data-hl-auto-resize={autoResize || undefined}
      data-hl-counter={showCounter || undefined}
    >
      <textarea
        {...textareaAttributes}
        aria-describedby={describedBy}
        aria-invalid={effectiveError ? true : ariaInvalid}
        aria-labelledby={ariaLabelledBy ?? context.labelId}
        aria-required={effectiveRequired ? true : ariaRequired}
        className={cn(
          'hl-text-area',
          `hl-text-area--size-${canonicalSize[size]}`,
          `hl-text-area--fill-${fillMode}`,
          `hl-text-area--rounded-${rounded}`,
          `hl-text-area--resize-${effectiveResize}`,
          effectiveError && 'hl-text-area--error',
          effectiveDisabled && 'hl-text-area--disabled',
          className,
        )}
        data-hl-fill-mode={fillMode}
        data-hl-invalid={effectiveError || undefined}
        data-hl-resize={effectiveResize}
        data-hl-rounded={rounded}
        data-hl-size={canonicalSize[size]}
        defaultValue={controlled ? undefined : defaultValue}
        disabled={effectiveDisabled}
        id={effectiveId}
        maxLength={maxLength}
        onChange={event => {
          const nextValue = event.currentTarget.value
          if (!controlled) setUncontrolledValue(nextValue)
          onChange?.(nextValue)
        }}
        ref={forwardedRef}
        required={effectiveRequired}
        rows={rows}
        value={controlled ? value : undefined}
      />
      {showCounter ? (
        <span aria-hidden="true" className="hl-text-area__counter">
          {renderedValue.length}{maxLength === undefined ? '' : `/${maxLength}`}
        </span>
      ) : null}
    </div>
  )
})
