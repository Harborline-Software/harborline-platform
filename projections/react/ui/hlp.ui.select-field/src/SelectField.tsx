import * as React from 'react'

import { useFormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { Input } from '@harborline-platform/hlp.ui.input'

export type SelectFieldSize = 'sm' | 'md' | 'lg'
export interface SelectOption { value: string; label: string; disabled?: boolean }

interface SelectFieldBaseProps {
  name: string
  options: readonly SelectOption[]
  placeholder?: string
  error?: boolean
  required?: boolean
  readOnly?: boolean
  maxVisibleOptions?: number
  size?: SelectFieldSize
  accessibleName?: string
  open?: boolean
  onOpenChange?: (open: boolean) => void
}
type ButtonAttributes = Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, keyof SelectFieldBaseProps | 'children' | 'defaultValue' | 'onChange' | 'value'>
type InputAttributes = Omit<React.InputHTMLAttributes<HTMLInputElement>, keyof SelectFieldBaseProps | 'children' | 'defaultValue' | 'onChange' | 'value' | 'multiple'>
type SearchableSingleProps = SelectFieldBaseProps & InputAttributes & {
  searchable: true; multiple?: false; value: string; onValueChange: (value: string) => void
}
type ButtonTriggerProps = SelectFieldBaseProps & ButtonAttributes & (
  | { searchable?: false; multiple?: false; value: string; onValueChange: (value: string) => void }
  | { searchable?: boolean; multiple: true; value: readonly string[]; onValueChange: (value: string[]) => void }
)
export type SelectFieldProps = SearchableSingleProps | ButtonTriggerProps
type SelectFieldWithRefProps =
  | (SearchableSingleProps & React.RefAttributes<HTMLInputElement>)
  | (ButtonTriggerProps & React.RefAttributes<HTMLButtonElement>)

function nativeAttributes<T extends SelectFieldProps>(props: T) {
  const {
    options: _options, value: _value, onValueChange: _onValueChange, multiple: _multiple,
    searchable: _searchable, maxVisibleOptions: _maxVisibleOptions, size: _size,
    accessibleName: _accessibleName, open: _open, onOpenChange: _onOpenChange,
    error: _error, required: _required, readOnly: _readOnly, ...attributes
  } = props
  return attributes
}

function joinIds(...values: Array<string | undefined>): string | undefined {
  return [...new Set(values.flatMap(value => value?.split(/\s+/).filter(Boolean) ?? []))].join(' ') || undefined
}

function sameSelection(left: string | readonly string[], right: string | readonly string[]): boolean {
  if (left === right) return true
  if (typeof left === 'string' || typeof right === 'string') return false
  return left.length === right.length && left.every((value, index) => value === right[index])
}

function sameOptions(left: readonly SelectOption[], right: readonly SelectOption[]): boolean {
  return left === right || left.length === right.length && left.every((option, index) => {
    const other = right[index]!
    return option.value === other.value && option.label === other.label && Boolean(option.disabled) === Boolean(other.disabled)
  })
}

export const SelectField: React.NamedExoticComponent<SelectFieldWithRefProps> = React.forwardRef<HTMLInputElement | HTMLButtonElement, SelectFieldProps>(function SelectField(props, forwardedRef) {
  const {
    name, options, value, multiple = false, searchable = false, maxVisibleOptions = 25,
    readOnly = false, placeholder, disabled, error = false, required, size = 'md',
    accessibleName, open, onOpenChange, className, id, tabIndex, 'aria-describedby': ariaDescribedBy,
    'aria-label': ariaLabel, 'aria-labelledby': ariaLabelledBy,
  } = props
  if (!name.trim()) throw new Error('select-field-name-required')
  if (!['sm', 'md', 'lg'].includes(size)) throw new Error('unsupported-select-field-size')
  if (!Number.isInteger(maxVisibleOptions) || maxVisibleOptions <= 0) throw new Error('invalid-select-option-limit')
  if (new Set(options.map(option => option.value)).size !== options.length) throw new Error('duplicate-select-option-value')
  if (props.multiple && new Set(props.value).size !== props.value.length) throw new Error('duplicate-selected-value')

  const field = useFormFieldContext()
  const { direction, t } = useHarborlineStrings()
  const effectiveDisabled = disabled ?? field.disabled ?? false
  const locked = effectiveDisabled || readOnly
  const selectedValues = props.multiple ? props.value : [props.value]
  const selectedLabel = selectedValues.map(selected => options.find(option => option.value === selected)?.label).filter(label => label !== undefined).join(', ')
  const [query, setQuery] = React.useState(multiple ? '' : selectedLabel)
  const [localOpen, setLocalOpen] = React.useState(false)
  const renderedOpen = open ?? localOpen
  const [active, setActive] = React.useState<string | null>(null)
  const host = React.useRef<HTMLSpanElement>(null)
  const trigger = React.useRef<HTMLButtonElement>(null)
  const input = React.useRef<HTMLInputElement>(null)
  const search = React.useRef<HTMLInputElement>(null)
  const list = React.useRef<HTMLDivElement>(null)
  const composing = React.useRef(false)
  const typeahead = React.useRef({ text: '', time: 0 })
  const listId = React.useId().replace(/:/g, '') + '-listbox'
  const label = accessibleName ?? ariaLabel
  const labelledBy = ariaLabelledBy ?? (label ? undefined : field.labelId)
  const description = joinIds(field.describedBy, ariaDescribedBy)
  const pendingToggle = React.useRef<readonly string[] | null>(null)
  const previous = React.useRef({ options, value, multiple })
  if (previous.current.options !== options || previous.current.value !== value || previous.current.multiple !== multiple) {
    const optionsChanged = !sameOptions(previous.current.options, options)
    const selectionChanged = !sameSelection(previous.current.value, value) || previous.current.multiple !== multiple
    const acknowledged = multiple && renderedOpen && !optionsChanged && pendingToggle.current !== null
      && sameSelection(pendingToggle.current, value)
    previous.current = { options, value, multiple }
    if (optionsChanged || selectionChanged) {
      // A controlled acknowledgment keeps navigation; unrelated replacement cancels stale state.
      if (!acknowledged) { setQuery(multiple ? '' : selectedLabel); setActive(null) }
      pendingToggle.current = null
    }
  }
  const visible = React.useMemo(() => {
    if (!searchable) return options
    const matches: SelectOption[] = []
    const needle = query.toLowerCase()
    for (const option of options) {
      if (option.label.toLowerCase().includes(needle)) matches.push(option)
      if (matches.length === maxVisibleOptions) break
    }
    return matches
  }, [options, searchable, query, maxVisibleOptions])
  const activeIndex = visible.findIndex(option => option.value === active && !option.disabled)
  const optionIndices = React.useMemo(() => new Map(options.map((option, index) => [option.value, index])), [options])
  const optionId = (option: SelectOption) => listId + '-option-' + optionIndices.get(option.value)
  const activeId = activeIndex >= 0 ? optionId(visible[activeIndex]!) : undefined
  React.useImperativeHandle(forwardedRef, () => (input.current ?? trigger.current)!)

  const requestOpen = React.useCallback((next: boolean) => {
    if (effectiveDisabled) return
    if (next && locked) return
    if (!next) { pendingToggle.current = null; setQuery(multiple ? '' : selectedLabel); setActive(null) }
    if (next === renderedOpen) return
    if (next) {
      setActive(searchable && !multiple ? null : options.find(option => (Array.isArray(value) ? value.includes(option.value) : option.value === value) && !option.disabled)?.value ?? options.find(option => !option.disabled)?.value ?? null)
    }
    onOpenChange?.(next)
    if (open === undefined) setLocalOpen(next)
  }, [effectiveDisabled, locked, multiple, selectedLabel, renderedOpen, searchable, options, value, onOpenChange, open])
  const focusTrigger = () => (input.current ?? trigger.current)?.focus()
  React.useEffect(() => {
    if (!renderedOpen) { pendingToggle.current = null; setQuery(multiple ? '' : selectedLabel); setActive(null) }
  }, [renderedOpen, multiple, selectedLabel])
  React.useEffect(() => {
    if (renderedOpen && multiple) (search.current ?? list.current)?.focus()
  }, [renderedOpen, multiple])
  React.useEffect(() => {
    if (!renderedOpen) return
    const outside = (event: PointerEvent) => {
      if (!host.current?.contains(event.target as Node)) requestOpen(false)
    }
    document.addEventListener('pointerdown', outside)
    return () => document.removeEventListener('pointerdown', outside)
  }, [renderedOpen, requestOpen])

  // Read current props even when a detached/stale option handler is invoked.
  const current = React.useRef(props)
  current.current = props
  function choose(identity: string) {
    const latest = current.current
    const member = latest.options.find(option => option.value === identity)
    if (locked || latest.disabled || latest.readOnly || !member || member.disabled) return
    if (latest.multiple) {
      const next = latest.value.includes(identity) ? latest.value.filter(item => item !== identity) : [...latest.value, identity]
      pendingToggle.current = [...next]
      setActive(identity)
      latest.onValueChange(next)
    } else {
      latest.onValueChange(identity)
      requestOpen(false)
      focusTrigger()
    }
  }
  function move(key: string, origin = active) {
    const enabled = visible.filter(option => !option.disabled)
    if (!enabled.length) { setActive(null); return }
    const index = enabled.findIndex(option => option.value === origin)
    const next = key === 'Home' ? 0 : key === 'End' ? enabled.length - 1
      : index < 0 ? (key === 'ArrowUp' ? enabled.length - 1 : 0)
      : (index + (key === 'ArrowUp' ? -1 : 1) + enabled.length) % enabled.length
    setActive(enabled[next]!.value)
  }
  function keyDown(event: React.KeyboardEvent<HTMLElement>, editing: boolean, filterOnly = false) {
    if (locked) return
    if (composing.current || event.nativeEvent.isComposing || event.keyCode === 229) {
      if (event.key === 'Enter') event.preventDefault()
      return
    }
    if (event.key === 'Escape') { event.preventDefault(); requestOpen(false); focusTrigger(); return }
    if (event.key === 'Tab') { window.setTimeout(() => requestOpen(false), 0); return }
    if (filterOnly) {
      if (event.key === 'Enter') event.preventDefault()
      if (event.key === 'ArrowDown') { event.preventDefault(); if (activeIndex < 0) move('Home'); list.current?.focus() }
      return
    }
    if (!renderedOpen) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ...(!editing ? [' '] : [])].includes(event.key)) {
        event.preventDefault()
        requestOpen(true)
        if (!multiple && event.key.startsWith('Arrow')) {
          const origin = !searchable
            ? options.find(option => option.value === value && !option.disabled)?.value ?? options.find(option => !option.disabled)?.value ?? null
            : active
          move(event.key, origin)
        }
      }
      return
    }
    if (['ArrowDown', 'ArrowUp', ...(!editing ? ['Home', 'End'] : [])].includes(event.key)) {
      event.preventDefault(); move(event.key)
    } else if (event.key === 'Enter' || (!editing && event.key === ' ')) {
      event.preventDefault()
      if (activeIndex >= 0) choose(visible[activeIndex]!.value)
    } else if (!editing && event.key.length === 1 && !event.altKey && !event.ctrlKey && !event.metaKey) {
      const now = Date.now()
      typeahead.current.text = (now - typeahead.current.time < 500 ? typeahead.current.text : '') + event.key.toLowerCase()
      typeahead.current.time = now
      const enabled = visible.filter(option => !option.disabled)
      const start = enabled.findIndex(option => option.value === active) + 1
      const match = [...enabled.slice(start), ...enabled.slice(0, start)].find(option => option.label.toLowerCase().startsWith(typeahead.current.text))
      if (match) setActive(match.value)
    }
  }
  const metadata = {
    'aria-controls': listId, 'aria-describedby': description, 'aria-expanded': renderedOpen,
    'aria-haspopup': 'listbox' as const, 'aria-invalid': error || undefined, 'aria-label': label,
    'aria-labelledby': labelledBy, 'aria-required': !multiple && (required ?? field.required) || undefined,
    'aria-readonly': !multiple && readOnly || undefined, disabled: effectiveDisabled, id: id ?? name, name, tabIndex,
  }
  return (
    <span className="hl-select-field" data-hl-direction={direction} data-hl-open={renderedOpen || undefined}
      data-hl-size={size} dir={direction} ref={host}
      onBlur={event => { if (!host.current?.contains(event.relatedTarget as Node | null)) requestOpen(false) }}>
      {props.searchable && !props.multiple ? (
        <Input {...nativeAttributes(props)} {...metadata}
          className={className} size={size} invalid={error} ref={input} readOnly={readOnly}
          role="combobox" aria-autocomplete="list" aria-activedescendant={renderedOpen ? activeId : undefined}
          autoComplete="off" placeholder={placeholder ?? t('forms.selectPlaceholder')} value={renderedOpen ? query : selectedLabel}
          onChange={event => { if (!locked) { requestOpen(true); setQuery(event.target.value); setActive(null) } }}
          onFocus={event => { props.onFocus?.(event); requestOpen(true) }}
          onBlur={props.onBlur}
          onCompositionStart={() => { composing.current = true }} onCompositionEnd={() => { composing.current = false }}
          onKeyDown={event => { props.onKeyDown?.(event); if (!event.defaultPrevented) keyDown(event, true) }} />
      ) : (
        <button {...nativeAttributes(props)} {...metadata} ref={trigger} type="button" role={multiple ? undefined : 'combobox'}
          aria-activedescendant={!multiple && renderedOpen ? activeId : undefined}
          className={['hl-select-field__trigger', error && 'hl-select-field__trigger--invalid', className].filter(Boolean).join(' ')}
          onClick={() => requestOpen(!renderedOpen)} onFocus={props.onFocus} onBlur={props.onBlur}
          onKeyDown={event => { props.onKeyDown?.(event); if (!event.defaultPrevented) keyDown(event, false) }}>
          <span className={'hl-select-field__value' + (!selectedLabel ? ' hl-select-field__placeholder' : '')}>{selectedLabel || placeholder || t('forms.selectPlaceholder')}</span>
          <span aria-hidden="true" className="hl-select-field__indicator"><svg focusable="false" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round"><path d="m4 6 4 4 4-4" /></svg></span>
        </button>
      )}
      {renderedOpen ? (
        <div className="hl-select-field__popup">
          {searchable && multiple ? <Input ref={search} role="searchbox" aria-label={label} aria-labelledby={labelledBy}
            aria-controls={listId} autoComplete="off" value={query} disabled={effectiveDisabled} readOnly={readOnly}
            onChange={event => { if (!locked) { setQuery(event.target.value); setActive(null) } }}
            onCompositionStart={() => { composing.current = true }} onCompositionEnd={() => { composing.current = false }}
            onKeyDown={event => keyDown(event, true, true)} /> : null}
          <div ref={list} id={listId} role="listbox" aria-label={label} aria-labelledby={labelledBy ?? (multiple && !label ? id ?? name : undefined)}
            aria-multiselectable={multiple || undefined} aria-activedescendant={multiple ? activeId : undefined}
            aria-required={multiple && (required ?? field.required) || undefined} aria-readonly={multiple && readOnly || undefined}
            className="hl-select-field__listbox" tabIndex={multiple ? -1 : undefined}
            onKeyDown={event => keyDown(event, false)}>
            {visible.map(option => (
              <div key={option.value} id={optionId(option)} role="option" aria-disabled={option.disabled || undefined}
                aria-selected={selectedValues.includes(option.value)} className="hl-select-field__option"
                data-hl-active={option.value === active || undefined} onClick={() => choose(option.value)}
                onMouseDown={event => event.preventDefault()}>
                <span className="hl-select-field__check" aria-hidden="true">{selectedValues.includes(option.value) ? '✓' : ''}</span><span>{option.label}</span>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </span>
  )
})
