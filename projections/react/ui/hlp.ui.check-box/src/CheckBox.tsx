import * as React from 'react'

export type CheckBoxState = boolean | 'mixed'
export type CheckBoxLabelPlacement = 'before' | 'after'
export type CheckBoxSize = 'sm' | 'md' | 'lg'

export interface CheckBoxProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>,
    'checked' | 'children' | 'className' | 'defaultChecked' | 'onChange' | 'size' | 'type'> {
  checked?: CheckBoxState
  defaultChecked?: boolean
  onChange?: (checked: boolean) => void
  label?: string
  labelPlacement?: CheckBoxLabelPlacement
  size?: CheckBoxSize
  error?: boolean
  describedBy?: string
  className?: string
}

const classes = (...values: Array<string | undefined | false>) => values.filter(Boolean).join(' ')

export const CheckBox = React.forwardRef<HTMLInputElement, CheckBoxProps>(function CheckBox(
  {
    checked,
    defaultChecked = false,
    onChange,
    label,
    labelPlacement = 'after',
    size = 'md',
    error = false,
    describedBy,
    disabled = false,
    required = false,
    className,
    'aria-describedby': ariaDescribedBy,
    'aria-invalid': ariaInvalid,
    'aria-required': ariaRequired,
    ...inputAttributes
  },
  forwardedRef,
) {
  const controlled = checked !== undefined
  const [uncontrolledChecked, setUncontrolledChecked] = React.useState(defaultChecked)
  const renderedState: CheckBoxState = controlled ? checked : uncontrolledChecked
  const mixed = renderedState === 'mixed'
  const inputRef = React.useRef<HTMLInputElement | null>(null)

  const assignRef = React.useCallback((element: HTMLInputElement | null) => {
    inputRef.current = element
    if (typeof forwardedRef === 'function') forwardedRef(element)
    else if (forwardedRef) forwardedRef.current = element
  }, [forwardedRef])

  React.useLayoutEffect(() => {
    if (inputRef.current) inputRef.current.indeterminate = mixed
  }, [mixed, renderedState])

  const handleChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const next = mixed ? true : event.currentTarget.checked
    if (!controlled) setUncontrolledChecked(next)
    onChange?.(next)
    if (mixed && inputRef.current) inputRef.current.indeterminate = true
  }

  const control = (
    <input
      {...inputAttributes}
      aria-checked={mixed ? 'mixed' : undefined}
      aria-describedby={describedBy ?? ariaDescribedBy}
      aria-invalid={error ? true : ariaInvalid}
      aria-required={required ? true : ariaRequired}
      checked={mixed ? false : renderedState}
      className="hl-check-box__control"
      disabled={disabled}
      onChange={handleChange}
      ref={assignRef}
      required={required}
      type="checkbox"
    />
  )

  return (
    <label
      className={classes(
        'hl-check-box',
        `hl-check-box--${size}`,
        disabled && 'hl-check-box--disabled',
        error && 'hl-check-box--error',
        className,
      )}
      data-hl-error={error || undefined}
      data-hl-label-placement={labelPlacement}
      data-hl-size={size}
    >
      {label && labelPlacement === 'before' ? <span className="hl-check-box__label">{label}</span> : null}
      {control}
      {label && labelPlacement === 'after' ? <span className="hl-check-box__label">{label}</span> : null}
    </label>
  )
})
