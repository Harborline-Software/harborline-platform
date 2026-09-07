import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export interface SearchInputProps {
  value: string
  onChange: (value: string) => void
  placeholder?: string
  accessibleLabel?: string
  clearLabel?: string
  debounceMs?: number
  className?: string
  direction?: 'ltr' | 'rtl'
}

export function SearchInput({
  value, onChange, placeholder, accessibleLabel, clearLabel, debounceMs = 200,
  className, direction,
}: SearchInputProps) {
  if (!Number.isFinite(debounceMs) || debounceMs < 0) throw new Error('invalid-debounce-delay')
  const { direction: localeDirection, resolveString } = useHarborlineStrings()
  const resolvedPlaceholder = resolveString(placeholder, 'structural.search.placeholder')
  const resolvedName = accessibleLabel ?? resolvedPlaceholder
  if (resolvedName.trim().length === 0) throw new Error('accessible-name-required')
  const [draft, setDraft] = React.useState(value)
  const [busy, setBusy] = React.useState(false)
  const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null)

  const cancel = React.useCallback(() => {
    if (timer.current !== null) clearTimeout(timer.current)
    timer.current = null
  }, [])

  React.useEffect(() => {
    cancel()
    setDraft(value)
    setBusy(false)
  }, [cancel, value])
  React.useEffect(() => cancel, [cancel])

  const edit = (next: string) => {
    cancel()
    setDraft(next)
    setBusy(true)
    timer.current = setTimeout(() => {
      timer.current = null
      setBusy(false)
      onChange(next)
    }, debounceMs)
  }
  const clear = () => {
    cancel()
    setDraft('')
    setBusy(false)
    onChange('')
  }

  return (
    <div dir={direction ?? localeDirection} aria-busy={busy} className={cn('hl-search-input', className)}>
      <svg aria-hidden="true" viewBox="0 0 20 20" className="hl-search-input__search"><circle cx="8" cy="8" r="5"/><path d="m12 12 5 5"/></svg>
      <input
        type="search"
        role="searchbox"
        aria-label={resolvedName}
        value={draft}
        placeholder={resolvedPlaceholder}
        onChange={event => edit(event.currentTarget.value)}
        className="hl-search-input__control"
      />
      {draft.length > 0 ? (
        <button type="button" onClick={clear} aria-label={resolveString(clearLabel, 'structural.search.clear')} className="hl-search-input__clear">
          <svg aria-hidden="true" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg>
        </button>
      ) : null}
    </div>
  )
}
