import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { Input, type InputProps } from '@harborline-platform/hlp.ui.input'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export interface TextBoxProps
  extends Omit<InputProps, 'defaultValue' | 'disabled' | 'invalid' | 'onChange' | 'required' | 'suffix' | 'value'> {
  readonly value?: string
  readonly defaultValue?: string
  readonly onChange?: (value: string) => void
  readonly clearButton?: boolean
  readonly showReveal?: boolean
  readonly required?: boolean
  readonly disabled?: boolean
  readonly error?: boolean
  readonly invalid?: boolean
  readonly suffix?: React.ReactNode
  readonly clearLabel?: string
  readonly showPasswordLabel?: string
  readonly hidePasswordLabel?: string
}

function resolvedLabel(
  override: string | undefined,
  key: string,
  fallback: string,
  resolveString: (override: string | undefined, key: string) => string,
): string {
  const value = resolveString(override, key)
  return value === key ? fallback : value
}

export const TextBox = React.forwardRef<HTMLInputElement, TextBoxProps>(function TextBox(
  props,
  forwardedRef,
) {
  const controlled = Object.prototype.hasOwnProperty.call(props, 'value')
  const {
    value,
    defaultValue,
    onChange,
    clearButton = false,
    showReveal = false,
    required,
    disabled,
    error = false,
    invalid = false,
    prefix,
    suffix,
    type = 'text',
    id,
    dir,
    clearLabel,
    showPasswordLabel,
    hidePasswordLabel,
    'aria-describedby': ariaDescribedBy,
    'aria-invalid': ariaInvalid,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledBy,
    ...inputAttributes
  } = props
  const formField = useFormFieldContext()
  const { direction, resolveString } = useHarborlineStrings()
  const inputRef = React.useRef<HTMLInputElement | null>(null)
  React.useImperativeHandle(forwardedRef, () => inputRef.current as HTMLInputElement)

  const [internalValue, setInternalValue] = React.useState(() => defaultValue ?? '')
  const [revealed, setRevealed] = React.useState(false)
  const effectiveValue = controlled ? (value ?? '') : internalValue
  const effectiveDisabled = disabled ?? formField.disabled ?? false
  const effectiveRequired = required ?? formField.required ?? false
  const effectiveInvalid = error || invalid || ariaInvalid === true || ariaInvalid === 'true'
  const effectiveDirection = dir ?? direction
  const effectiveId = id ?? formField.id
  const describedBy = ariaDescribedBy ?? formField.describedBy
  const labelledBy = ariaLabelledBy ?? (ariaLabel ? undefined : formField.labelId)
  const password = type === 'password'
  const passwordVisible = password && revealed && !effectiveDisabled
  const showClear = clearButton && effectiveValue.length > 0 && !effectiveDisabled
  const showRevealAction = password && showReveal && !effectiveDisabled

  React.useEffect(() => {
    if (!password || effectiveDisabled) setRevealed(false)
  }, [effectiveDisabled, password])

  const requestValue = React.useCallback((next: string) => {
    if (effectiveDisabled) return
    if (!controlled) setInternalValue(next)
    onChange?.(next)
  }, [controlled, effectiveDisabled, onChange])

  const restoreInputFocus = React.useCallback((selection?: readonly [number | null, number | null]) => {
    const input = inputRef.current
    if (!input) return
    input.focus({ preventScroll: true })
    if (!selection || selection[0] === null || selection[1] === null) return
    try {
      input.setSelectionRange(selection[0], selection[1])
    } catch {
      // Some native input types do not expose a text selection range.
    }
  }, [])

  const clearName = resolvedLabel(clearLabel, 'common.clear', 'Clear', resolveString)
  const showName = resolvedLabel(showPasswordLabel, 'forms.textBox.showPassword', 'Show password', resolveString)
  const hideName = resolvedLabel(hidePasswordLabel, 'forms.textBox.hidePassword', 'Hide password', resolveString)

  const actions = showClear || showRevealAction ? (
    <span className="hl-text-box__actions" dir={effectiveDirection}>
      {showClear ? (
        <button
          aria-label={clearName}
          className="hl-text-box__action"
          data-hl-action="clear"
          onClick={() => {
            requestValue('')
            restoreInputFocus()
          }}
          onMouseDown={event => event.preventDefault()}
          type="button"
        >
          <svg aria-hidden="true" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg>
        </button>
      ) : null}
      {showRevealAction ? (
        <button
          aria-label={passwordVisible ? hideName : showName}
          aria-pressed={passwordVisible}
          className="hl-text-box__action"
          data-hl-action="reveal"
          onClick={() => {
            const selection = [inputRef.current?.selectionStart ?? null, inputRef.current?.selectionEnd ?? null] as const
            setRevealed(current => !current)
            queueMicrotask(() => restoreInputFocus(selection))
          }}
          onMouseDown={event => event.preventDefault()}
          type="button"
        >
          <svg aria-hidden="true" viewBox="0 0 20 20">
            <path d="M2.5 10s2.75-5 7.5-5 7.5 5 7.5 5-2.75 5-7.5 5-7.5-5-7.5-5Z" />
            <circle cx="10" cy="10" r="2.25" />
            {passwordVisible ? null : <path d="m3 3 14 14" />}
          </svg>
        </button>
      ) : null}
    </span>
  ) : null

  const composedSuffix = suffix !== undefined || actions !== null ? (
    <span className="hl-text-box__suffix" dir={effectiveDirection}>
      {suffix === undefined ? null : <span className="hl-text-box__caller-suffix">{suffix}</span>}
      {actions}
    </span>
  ) : undefined

  return (
    <Input
      {...inputAttributes}
      aria-describedby={describedBy}
      aria-invalid={effectiveInvalid || undefined}
      aria-label={ariaLabel}
      aria-labelledby={labelledBy}
      aria-required={effectiveRequired || undefined}
      data-hl-revealed={passwordVisible || undefined}
      dir={effectiveDirection}
      disabled={effectiveDisabled}
      id={effectiveId}
      invalid={effectiveInvalid}
      onChange={event => requestValue(event.currentTarget.value)}
      prefix={prefix}
      ref={inputRef}
      required={effectiveRequired}
      suffix={composedSuffix}
      type={passwordVisible ? 'text' : type}
      value={effectiveValue}
    />
  )
})
