import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { defaultStrings } from '@harborline-platform/hlp.ui.default-strings'
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
  const { direction: localeDirection, t } = useHarborlineStrings()
  // Caller text, then the host catalog, then the default catalog. Blank text at any step is no text, and a
  // catalog miss echoes the key, so neither the searchbox nor the clear button is ever unnamed.
  const copy = (caller: string | undefined, key: 'structural.search.placeholder' | 'structural.search.clear') => {
    if (caller?.trim()) return caller
    const catalog = t(key)
    return catalog.trim() && catalog !== key ? catalog : defaultStrings[key]
  }
  const resolvedPlaceholder = copy(placeholder, 'structural.search.placeholder')
  // Blank caller text is not a name; fall back to the resolved placeholder, as in Blazor.
  const resolvedName = accessibleLabel?.trim() ? accessibleLabel : resolvedPlaceholder
  if (resolvedName.trim().length === 0) throw new Error('accessible-name-required')
  const [draft, setDraft] = React.useState(value)
  const [busy, setBusy] = React.useState(false)
  const timer = React.useRef<ReturnType<typeof setTimeout> | null>(null)
  const inputRef = React.useRef<HTMLInputElement>(null)

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
    // The clear control disappears with the draft; keep keyboard focus on the searchbox.
    inputRef.current?.focus()
  }

  return (
    <div dir={direction ?? localeDirection} aria-busy={busy} className={cn('hl-search-input', className)}>
      <svg aria-hidden="true" viewBox="0 0 20 20" className="hl-search-input__search"><circle cx="8" cy="8" r="5"/><path d="m12 12 5 5"/></svg>
      <input
        ref={inputRef}
        type="search"
        role="searchbox"
        aria-label={resolvedName}
        value={draft}
        placeholder={resolvedPlaceholder}
        onChange={event => edit(event.currentTarget.value)}
        onKeyDown={event => {
          // Escape on a nonempty search is Clear: the browser's own search clearing would publish only
          // after the debounce, and an unhandled Escape would also close an enclosing overlay. An empty
          // search leaves Escape to the overlay. During IME composition Escape cancels the composition only.
          if (event.key !== 'Escape' || event.defaultPrevented || draft.length === 0) return
          if (event.nativeEvent.isComposing || event.keyCode === 229) return
          event.preventDefault()
          clear()
        }}
        className="hl-search-input__control"
      />
      {draft.length > 0 ? (
        <button type="button" onClick={clear} aria-label={copy(clearLabel, 'structural.search.clear')} className="hl-search-input__clear">
          <svg aria-hidden="true" viewBox="0 0 20 20"><path d="m5 5 10 10M15 5 5 15" /></svg>
        </button>
      ) : null}
    </div>
  )
}
