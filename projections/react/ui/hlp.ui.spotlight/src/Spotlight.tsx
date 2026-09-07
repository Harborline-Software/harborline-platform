import * as React from 'react'
import { createPortal } from 'react-dom'

import { cn } from '@harborline-platform/hlp.ui.cn'

export interface SpotlightItem {
  id: string
  label: string
  description?: string
  icon?: React.ReactNode
  shortcut?: string
  badge?: string
  onSelect: () => void
  keepOpen?: boolean
}

export interface SpotlightSection {
  id: string
  label: string
  items: SpotlightItem[]
  loading?: boolean
  loadingLabel?: string
}

export interface SpotlightProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  query: string
  onQueryChange: (query: string) => void
  sections: SpotlightSection[]
  pinnedAction?: SpotlightItem
  ariaLabel: string
  placeholder?: string
  escLabel?: string
  empty?: string
  resultsCountLabel?: (count: number) => string
  footer?: React.ReactNode
  className?: string
}

const focusableSelector = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function assertValid(sections: readonly SpotlightSection[], pinnedAction?: SpotlightItem): void {
  const ids = new Set<string>()
  const items = pinnedAction ? [pinnedAction, ...sections.flatMap(section => section.items)] : sections.flatMap(section => section.items)
  for (const item of items) {
    if (ids.has(item.id)) throw new Error('duplicate-item-id')
    ids.add(item.id)
  }
  for (const section of sections) {
    if ((section.items.length > 0 || section.loading) && !section.label.trim()) {
      throw new Error('section-label-required')
    }
  }
}

function trapTab(event: React.KeyboardEvent<HTMLElement>, container: HTMLElement): void {
  if (event.key !== 'Tab') return
  const focusable = Array.from(container.querySelectorAll<HTMLElement>(focusableSelector))
    .filter(element => !element.hasAttribute('disabled') && element.tabIndex >= 0)
  if (focusable.length === 0) {
    event.preventDefault()
    container.focus()
    return
  }

  const first = focusable[0]
  const last = focusable[focusable.length - 1]
  if (event.shiftKey && (document.activeElement === first || document.activeElement === container)) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === container)) {
    event.preventDefault()
    first.focus()
  }
}

export function Spotlight({
  open,
  onOpenChange,
  query,
  onQueryChange,
  sections,
  pinnedAction,
  ariaLabel,
  placeholder,
  escLabel = 'Esc',
  empty = 'No results',
  resultsCountLabel,
  footer,
  className,
}: SpotlightProps): React.ReactPortal | null {
  assertValid(sections, pinnedAction)

  const [activeIndex, setActiveIndex] = React.useState(0)
  const inputRef = React.useRef<HTMLInputElement>(null)
  const dialogRef = React.useRef<HTMLDivElement>(null)
  const listRef = React.useRef<HTMLDivElement>(null)
  const instanceId = React.useId().replace(/:/g, '')
  const listboxId = `hl-spotlight-${instanceId}-listbox`
  const flatItems = React.useMemo(
    () => pinnedAction ? [pinnedAction, ...sections.flatMap(section => section.items)] : sections.flatMap(section => section.items),
    [pinnedAction, sections],
  )
  const renderedSections = sections.filter(section => section.items.length > 0 || section.loading)

  React.useEffect(() => {
    if (open) setActiveIndex(0)
  }, [flatItems.length, open])

  React.useLayoutEffect(() => {
    if (!open) return
    const priorFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const containFocus = (event: FocusEvent) => {
      const target = event.target
      if (target instanceof Node && dialogRef.current && !dialogRef.current.contains(target)) inputRef.current?.focus()
    }
    inputRef.current?.focus()
    document.addEventListener('focusin', containFocus)
    return () => {
      document.removeEventListener('focusin', containFocus)
      priorFocus?.focus()
    }
  }, [open])

  React.useEffect(() => {
    if (!open) return
    const dismissOnEscape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape' || event.defaultPrevented) return
      event.preventDefault()
      onOpenChange(false)
    }
    document.addEventListener('keydown', dismissOnEscape)
    return () => document.removeEventListener('keydown', dismissOnEscape)
  }, [onOpenChange, open])

  React.useEffect(() => {
    const active = listRef.current?.querySelector<HTMLElement>('[data-active="true"]')
    active?.scrollIntoView?.({ block: 'nearest' })
  }, [activeIndex])

  if (!open) return null

  const optionId = (index: number) => `hl-spotlight-${instanceId}-option-${index}`
  const activeItem = flatItems[activeIndex]
  const activeId = activeItem ? optionId(activeIndex) : undefined

  const select = (item: SpotlightItem | undefined) => {
    if (!item) return
    if (!item.keepOpen) onOpenChange(false)
    item.onSelect()
  }

  const handleInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      setActiveIndex(index => Math.min(index + 1, Math.max(0, flatItems.length - 1)))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      setActiveIndex(index => Math.max(index - 1, 0))
    } else if (event.key === 'Enter') {
      event.preventDefault()
      select(flatItems[activeIndex])
    }
  }

  const renderOption = (item: SpotlightItem, index: number) => {
    const active = index === activeIndex
    return (
      <div
        aria-selected={active}
        className={cn('hl-spotlight__option', active && 'hl-spotlight__option--active')}
        data-active={active ? 'true' : 'false'}
        data-item-id={item.id}
        id={optionId(index)}
        key={item.id}
        onClick={() => select(item)}
        onMouseEnter={() => setActiveIndex(index)}
        role="option"
      >
        {item.icon ? <span aria-hidden="true" className="hl-spotlight__icon">{item.icon}</span> : null}
        <span className="hl-spotlight__label">{item.label}</span>
        {item.description ? <span className="hl-spotlight__description">{item.description}</span> : null}
        {item.badge ? <span className="hl-spotlight__badge">{item.badge}</span> : null}
        {item.shortcut ? <kbd className="hl-spotlight__shortcut">{item.shortcut}</kbd> : null}
      </div>
    )
  }

  let sectionCursor = pinnedAction ? 1 : 0
  return createPortal(
    <div className="hl-spotlight__portal" data-hl-module="hlp.ui.spotlight">
      <div
        aria-hidden="true"
        className="hl-spotlight__scrim"
        data-testid="spotlight-scrim"
        onMouseDown={() => onOpenChange(false)}
      />
      <div
        aria-label={ariaLabel}
        aria-modal="true"
        className={cn('hl-spotlight', className)}
        onKeyDown={event => {
          if (dialogRef.current) trapTab(event, dialogRef.current)
        }}
        ref={dialogRef}
        role="dialog"
        tabIndex={-1}
      >
        <div className="hl-spotlight__query-row">
          <svg aria-hidden="true" className="hl-spotlight__search-icon" fill="none" focusable="false" viewBox="0 0 24 24">
            <path d="m21 21-4.35-4.35m1.35-5.65a7 7 0 1 1-14 0 7 7 0 0 1 14 0Z" />
          </svg>
          <input
            aria-activedescendant={activeId}
            aria-autocomplete="list"
            aria-controls={listboxId}
            aria-expanded="true"
            aria-label={placeholder ?? ariaLabel}
            autoComplete="off"
            className="hl-spotlight__query"
            onChange={event => onQueryChange(event.target.value)}
            onKeyDown={handleInputKeyDown}
            placeholder={placeholder}
            ref={inputRef}
            role="combobox"
            value={query}
          />
          <kbd className="hl-spotlight__escape">{escLabel}</kbd>
        </div>

        <div aria-live="polite" className="hl-spotlight__sr-only" role="status">
          {resultsCountLabel?.(flatItems.length) ?? ''}
        </div>

        <div aria-label={ariaLabel} className="hl-spotlight__results" id={listboxId} ref={listRef} role="listbox">
          {flatItems.length === 0 && renderedSections.length === 0 ? (
            <div className="hl-spotlight__empty">{empty}</div>
          ) : (
            <>
              {pinnedAction ? renderOption(pinnedAction, 0) : null}
              {renderedSections.map(section => {
                const start = sectionCursor
                sectionCursor += section.items.length
                return (
                  <div aria-label={section.label} key={section.id} role="group">
                    <div aria-hidden="true" className="hl-spotlight__group-label">{section.label}</div>
                    {section.items.map((item, offset) => renderOption(item, start + offset))}
                    {section.loading && section.items.length === 0 ? (
                      <div className="hl-spotlight__loading">{section.loadingLabel ?? '…'}</div>
                    ) : null}
                  </div>
                )
              })}
            </>
          )}
        </div>

        {footer ? <div className="hl-spotlight__footer">{footer}</div> : null}
      </div>
    </div>,
    document.body,
  )
}
