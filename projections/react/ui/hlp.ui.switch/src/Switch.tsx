import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { useTouchSizing } from '@harborline-platform/hlp.ui.use-can-show-master-detail'

export type SwitchSize = 'sm' | 'md' | 'lg' | 'small' | 'medium' | 'large'

export interface SwitchProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'name' | 'onChange' | 'size'> {
  checked?: boolean
  defaultChecked?: boolean
  onCheckedChange?: (checked: boolean) => void
  onChange?: (checked: boolean) => void
  label?: string
  accessibleName?: string
  description?: string
  onText?: string
  offText?: string
  size?: SwitchSize
  name?: string
  error?: boolean
  required?: boolean
}

const normalizedSizes: Record<SwitchSize, 'sm' | 'md' | 'lg'> = {
  sm: 'sm', small: 'sm', md: 'md', medium: 'md', lg: 'lg', large: 'lg',
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function joinIds(...values: Array<string | undefined>): string | undefined {
  const ids = [...new Set(values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? []))]
  return ids.length ? ids.join(' ') : undefined
}

// Instance override, then catalog, then fallback. A catalog miss echoes the key back, and blank text
// at any step counts as unresolved, so the state copy is never empty.
function stateText(override: string | undefined, key: string, fallback: string, t: (key: string) => string) {
  if (override?.trim()) return override
  const value = t(key)
  return value === key || !value.trim() ? fallback : value
}

export const Switch = React.forwardRef<HTMLButtonElement, SwitchProps>(function Switch(
  {
    checked,
    defaultChecked = false,
    onCheckedChange,
    onChange,
    disabled,
    label,
    accessibleName,
    description,
    onText,
    offText,
    size = 'md',
    id,
    name,
    error = false,
    required,
    className,
    'aria-describedby': ariaDescribedBy,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledBy,
    ...buttonAttributes
  },
  forwardedRef,
) {
  if (!(size in normalizedSizes)) throw new Error('unsupported-switch-size')
  const formField = useFormFieldContext()
  const { direction, t } = useHarborlineStrings()
  const touchSized = useTouchSizing()
  const effectiveDisabled = disabled ?? formField.disabled ?? false
  const effectiveRequired = required ?? formField.required ?? false
  const controlled = checked !== undefined
  const [internalChecked, setInternalChecked] = React.useState(defaultChecked)
  const renderedChecked = controlled ? checked : internalChecked
  const generatedId = React.useId().replace(/:/g, '')
  const switchId = id ?? formField.id ?? `hl-switch-${generatedId}`
  const visibleLabel = label?.trim() ? label : undefined
  const labelId = visibleLabel ? `${switchId}-label` : undefined
  const descriptionId = description ? `${switchId}-description` : undefined
  // A blank name is no name: it falls through to the label, and fails if there is none.
  const effectiveName = [accessibleName, ariaLabel].find(value => value?.trim()) ?? undefined
  // A rendered visible label is the name (accname 1.2 computes aria-labelledby before aria-label; WCAG
  // 2.5.3 Label in Name); a caller name applies only when no visible label is rendered.
  const labelledBy = (ariaLabelledBy?.trim() ? ariaLabelledBy : undefined) ?? labelId ?? (effectiveName ? undefined : formField.labelId)
  if (!effectiveName && !labelledBy) throw new Error('accessible-switch-label-required')
  const describedBy = joinIds(formField.describedBy, ariaDescribedBy, descriptionId)
  const localizedOn = stateText(onText, 'forms.switch.on', 'On', t)
  const localizedOff = stateText(offText, 'forms.switch.off', 'Off', t)
  const requestChange = onCheckedChange ?? onChange

  return (
    <span
      className={classes('hl-switch', effectiveDisabled && 'hl-switch--disabled')}
      data-hl-direction={direction}
      data-hl-size={normalizedSizes[size]}
      data-hl-touch={touchSized || undefined}
      dir={direction}
    >
      <button
        {...buttonAttributes}
        aria-checked={renderedChecked}
        aria-describedby={describedBy}
        aria-invalid={error || undefined}
        aria-label={effectiveName}
        aria-labelledby={labelledBy}
        aria-required={effectiveRequired || undefined}
        className={classes('hl-switch__control', error && 'hl-switch__control--invalid', className)}
        data-hl-checked={renderedChecked || undefined}
        disabled={effectiveDisabled}
        id={switchId}
        onClick={() => {
          const next = !renderedChecked
          if (!controlled) setInternalChecked(next)
          requestChange?.(next)
        }}
        ref={forwardedRef}
        role="switch"
        type="button"
      >
        <span aria-hidden="true" className="hl-switch__thumb" />
      </button>
      {visibleLabel ? <label className="hl-switch__label" htmlFor={switchId} id={labelId}>{visibleLabel}</label> : null}
      <span aria-hidden="true" className="hl-switch__state">{renderedChecked ? localizedOn : localizedOff}</span>
      {description ? <span className="hl-switch__description" id={descriptionId}>{description}</span> : null}
      {error ? <span aria-hidden="true" className="hl-switch__error-indicator">!</span> : null}
      {name ? <input disabled={effectiveDisabled} name={name} type="hidden" value={renderedChecked ? 'on' : 'off'} /> : null}
    </span>
  )
})
