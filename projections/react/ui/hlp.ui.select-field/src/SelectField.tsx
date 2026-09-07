import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type SelectFieldSize = 'sm' | 'md' | 'lg'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

export interface SelectFieldProps
  extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'defaultValue' | 'name' | 'onChange' | 'size' | 'value'> {
  name: string
  value: string
  onValueChange: (value: string) => void
  options: readonly SelectOption[]
  placeholder?: string
  error?: boolean
  required?: boolean
  size?: SelectFieldSize
  accessibleName?: string
  open?: boolean
  onOpenChange?: (open: boolean) => void
}

const sizes = new Set<SelectFieldSize>(['sm', 'md', 'lg'])

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function joinIds(...values: Array<string | undefined>): string | undefined {
  const ids = [...new Set(values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? []))]
  return ids.length ? ids.join(' ') : undefined
}

function assertProps(name: string, options: readonly SelectOption[], size: SelectFieldSize): void {
  if (name.trim().length === 0) throw new Error('select-field-name-required')
  if (!sizes.has(size)) throw new Error('unsupported-select-field-size')
  if (new Set(options.map(option => option.value)).size !== options.length) {
    throw new Error('duplicate-select-option-value')
  }
}

export const SelectField = React.forwardRef<HTMLButtonElement, SelectFieldProps>(function SelectField(
  {
    name,
    value,
    onValueChange,
    options,
    placeholder,
    disabled,
    error = false,
    required,
    size = 'md',
    accessibleName,
    open,
    onOpenChange,
    className,
    id,
    tabIndex,
    onFocus,
    onBlur,
    'aria-describedby': ariaDescribedBy,
    'aria-label': ariaLabel,
    'aria-labelledby': ariaLabelledBy,
    ...triggerAttributes
  },
  forwardedRef,
) {
  assertProps(name, options, size)
  const formField = useFormFieldContext()
  const { direction, t } = useHarborlineStrings()
  const effectiveDisabled = disabled ?? formField.disabled ?? false
  const effectiveRequired = required ?? formField.required ?? false
  const [internalOpen, setInternalOpen] = React.useState(false)
  const isControlledOpen = open !== undefined
  const renderedOpen = isControlledOpen ? open : internalOpen
  const selectedIndex = options.findIndex(option => option.value === value)
  const firstEnabled = options.findIndex(option => !option.disabled)
  const [activeIndex, setActiveIndex] = React.useState(selectedIndex >= 0 && !options[selectedIndex]?.disabled ? selectedIndex : firstEnabled)
  const triggerRef = React.useRef<HTMLButtonElement | null>(null)
  const hostRef = React.useRef<HTMLSpanElement | null>(null)
  const listboxId = `${React.useId().replace(/:/g, '')}-listbox`
  const typeahead = React.useRef({ value: '', timer: 0 })

  React.useImperativeHandle(forwardedRef, () => triggerRef.current as HTMLButtonElement)

  const requestOpen = React.useCallback((next: boolean) => {
    if (effectiveDisabled || next === renderedOpen) return
    onOpenChange?.(next)
    if (!isControlledOpen) setInternalOpen(next)
    if (next) setActiveIndex(selectedIndex >= 0 && !options[selectedIndex]?.disabled ? selectedIndex : firstEnabled)
  }, [effectiveDisabled, firstEnabled, isControlledOpen, onOpenChange, options, renderedOpen, selectedIndex])

  const selectIndex = React.useCallback((index: number) => {
    const option = options[index]
    if (effectiveDisabled || option === undefined || option.disabled) return
    onValueChange(option.value)
    if (renderedOpen) {
      onOpenChange?.(false)
      if (!isControlledOpen) setInternalOpen(false)
    }
  }, [effectiveDisabled, isControlledOpen, onOpenChange, onValueChange, options, renderedOpen])

  const moveActive = React.useCallback((movement: 'next' | 'previous' | 'first' | 'last') => {
    const enabled = options.flatMap((option, index) => option.disabled ? [] : [index])
    if (enabled.length === 0) return
    if (movement === 'first') return setActiveIndex(enabled[0]!)
    if (movement === 'last') return setActiveIndex(enabled.at(-1)!)
    const current = enabled.indexOf(activeIndex)
    const origin = current >= 0 ? current : 0
    const offset = movement === 'next' ? 1 : -1
    setActiveIndex(enabled[(origin + offset + enabled.length) % enabled.length]!)
  }, [activeIndex, options])

  React.useEffect(() => {
    if (!renderedOpen) return
    const onPointerDown = (event: PointerEvent) => {
      if (!hostRef.current?.contains(event.target as Node)) requestOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [renderedOpen, requestOpen])

  React.useEffect(() => () => window.clearTimeout(typeahead.current.timer), [])

  const selected = selectedIndex >= 0 ? options[selectedIndex] : undefined
  const display = selected?.label ?? placeholder ?? t('forms.selectPlaceholder')
  const triggerId = id ?? name
  const describedBy = joinIds(formField.describedBy, ariaDescribedBy)
  const effectiveLabel = accessibleName ?? ariaLabel

  return (
    <span
      className="hl-select-field"
      data-hl-direction={direction}
      data-hl-open={renderedOpen || undefined}
      data-hl-size={size}
      dir={direction}
      ref={hostRef}
    >
      <button
        {...triggerAttributes}
        aria-activedescendant={renderedOpen && activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
        aria-controls={listboxId}
        aria-describedby={describedBy}
        aria-expanded={renderedOpen}
        aria-haspopup="listbox"
        aria-invalid={error || undefined}
        aria-label={effectiveLabel}
        aria-labelledby={ariaLabelledBy ?? (effectiveLabel ? undefined : formField.labelId)}
        aria-required={effectiveRequired || undefined}
        className={classes('hl-select-field__trigger', error && 'hl-select-field__trigger--invalid', className)}
        disabled={effectiveDisabled}
        id={triggerId}
        name={name}
        onBlur={event => {
          onBlur?.(event)
          if (!hostRef.current?.contains(event.relatedTarget as Node | null)) requestOpen(false)
        }}
        onClick={() => requestOpen(!renderedOpen)}
        onFocus={onFocus}
        onKeyDown={event => {
          if (effectiveDisabled) return
          if (!renderedOpen && ['Enter', ' ', 'ArrowDown', 'ArrowUp'].includes(event.key)) {
            event.preventDefault()
            requestOpen(true)
            if (event.key === 'ArrowDown') moveActive('next')
            if (event.key === 'ArrowUp') moveActive('previous')
            return
          }
          if (!renderedOpen) return
          if (event.key === 'Escape') {
            event.preventDefault()
            requestOpen(false)
            triggerRef.current?.focus()
          } else if (event.key === 'Tab') {
            requestOpen(false)
          } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Home' || event.key === 'End') {
            event.preventDefault()
            moveActive(event.key === 'ArrowDown' ? 'next' : event.key === 'ArrowUp' ? 'previous' : event.key === 'Home' ? 'first' : 'last')
          } else if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            selectIndex(activeIndex)
          } else if (event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey) {
            typeahead.current.value += event.key.toLocaleLowerCase()
            window.clearTimeout(typeahead.current.timer)
            typeahead.current.timer = window.setTimeout(() => { typeahead.current.value = '' }, 500)
            const enabled = options.flatMap((option, index) => option.disabled ? [] : [{ option, index }])
            const start = Math.max(0, enabled.findIndex(item => item.index === activeIndex) + 1)
            const ordered = [...enabled.slice(start), ...enabled.slice(0, start)]
            const match = ordered.find(item => item.option.label.toLocaleLowerCase().startsWith(typeahead.current.value))
            if (match) setActiveIndex(match.index)
          }
        }}
        ref={triggerRef}
        role="combobox"
        tabIndex={tabIndex}
        type="button"
      >
        <span className={classes('hl-select-field__value', !selected && 'hl-select-field__placeholder')}>{display}</span>
        <span aria-hidden="true" className="hl-select-field__indicator"><svg focusable="false" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="m4 6 4 4 4-4" /></svg></span>
      </button>
      {renderedOpen ? (
        <div aria-label={effectiveLabel} className="hl-select-field__listbox" id={listboxId} role="listbox">
          {options.map((option, index) => (
            <div
              aria-disabled={option.disabled || undefined}
              aria-selected={option.value === value}
              className="hl-select-field__option"
              data-hl-active={index === activeIndex || undefined}
              id={`${listboxId}-option-${index}`}
              key={option.value}
              onClick={() => selectIndex(index)}
              onMouseDown={event => event.preventDefault()}
              role="option"
            >
              <span className="hl-select-field__check" aria-hidden="true">{option.value === value ? '✓' : ''}</span>
              <span>{option.label}</span>
            </div>
          ))}
        </div>
      ) : null}
    </span>
  )
})
