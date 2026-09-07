import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { useOutsideClick } from '@harborline-platform/hlp.ui.use-outside-click'

export interface ActionMenuItem {
  label: string
  onClick: () => void
  /** disclosure-model.md — the id the composer knows the action by; also what the facet fence reads. */
  id?: string
  icon?: React.ReactNode
  /** disclosure-model.md §1 — declared as data; the shared token renders it right-aligned and monospace.
   *  It is decorative (aria-hidden): assistive technology reads `keyShortcuts`, not this string. */
  shortcutHint?: string
  /** WAI-ARIA 1.2 §6.6.7 aria-keyshortcuts — the same chord in UI Events KeyboardEvent.key names
   *  ("Control+Shift+C"). Declared alongside the display hint, never parsed out of it. */
  keyShortcuts?: string
  /** disclosure-model.md §2 — a toggle lives in the menu with a check mark. Declaring it makes the
   *  entry a checkbox item; the host owns the value and changes it through `onClick`, the one
   *  callback every entry reports through. */
  checked?: boolean
  variant?: 'default' | 'destructive'
  disabled?: boolean
  separator?: false
}

interface ActionMenuSeparator {
  separator: true
}

export type ActionMenuEntry = ActionMenuItem | ActionMenuSeparator

export interface ActionMenuProps {
  items: readonly ActionMenuEntry[]
  trigger?: React.ReactNode
  align?: 'left' | 'right'
  className?: string
  /** disclosure-model.md §5 — the scope this menu is the home of: a row id, a page id. Two menus
   *  mounted for one scope id in one shell instance is refused. Omitting it does not opt out: the
   *  shell derives the scope from the nearest scope host, so every composed menu carries one. */
  scopeId?: string
  /** disclosure-model.md — the facet ids of the object the composer is showing. A facet is a place,
   *  so it can never be an entry here. */
  facetIds?: readonly string[]
  /** disclosure-model.md — "honest empty and unavailable states, phrased as what happened and
   *  what to do". A menu holding nothing to draw says so rather than opening a blank box; the
   *  trigger stays where it is because §5 makes it the one home for this scope. Overrides the
   *  `buttons.noActions` catalog key. */
  empty?: string
}

type FocusBoundary = 'first' | 'last'

function isSeparator(entry: ActionMenuEntry): entry is ActionMenuSeparator {
  return entry.separator === true
}

/** disclosure-model.md §5. One shell instance is one loaded module, so the claim table is module
 *  level: mounting a second menu for a scope that already has a home is the refusal. */
const scopeHomes = new Map<string, number>()

/** disclosure-model.md §5 — the scope of the surface a menu sits on, published by the nearest scope
 *  host (a window, a panel, a detail-panel section) with this context's Provider. The default is the
 *  shell's own root surface, so the chain always terminates in a scope: a composer can name a
 *  narrower one, but it cannot decline to have one. Requiring the prop instead would be a public
 *  contract break for every packed consumer, and — worse — a scope a composer forgets to pass is
 *  exactly the omission the fence has to catch, so omission must resolve, not disable. */
export const ActionMenuScopeContext = React.createContext<string>('window')

function useOneHomePerScope(scopeId: string) {
  React.useLayoutEffect(() => {
    const claimed = (scopeHomes.get(scopeId) ?? 0) + 1
    if (claimed > 1) throw new Error('action-menu-scope-taken')
    scopeHomes.set(scopeId, claimed)
    return () => {
      const remaining = (scopeHomes.get(scopeId) ?? 1) - 1
      if (remaining > 0) scopeHomes.set(scopeId, remaining)
      else scopeHomes.delete(scopeId)
    }
  }, [scopeId])
}

export function ActionMenu({ items, trigger, align = 'right', className, scopeId, facetIds, empty }: ActionMenuProps) {
  const hostScopeId = React.useContext(ActionMenuScopeContext)
  const resolvedScopeId = scopeId ?? hostScopeId
  const { direction, t } = useHarborlineStrings()
  const [open, setOpen] = React.useState(false)
  const rootRef = React.useRef<HTMLDivElement>(null)
  const triggerRef = React.useRef<HTMLElement>(null)
  const menuRef = React.useRef<HTMLDivElement>(null)
  const itemRefs = React.useRef<Array<HTMLButtonElement | null>>([])
  const pendingFocus = React.useRef<FocusBoundary | null>(null)
  const typeahead = React.useRef('')
  const typeaheadTimer = React.useRef<ReturnType<typeof setTimeout> | null>(null)
  const menuId = React.useId()
  const defaultLabel = t('buttons.moreActions')

  if (trigger === undefined && defaultLabel.trim().length === 0) {
    throw new Error('accessible-name-required')
  }

  useOneHomePerScope(resolvedScopeId)

  if (facetIds !== undefined) {
    const facets = new Set(facetIds)
    for (const entry of items) {
      if (!isSeparator(entry) && entry.id !== undefined && facets.has(entry.id)) {
        throw new Error('action-menu-facet-not-an-item')
      }
    }
  }

  /** Nothing to draw: no entries at all, or separators only. An all-disabled menu still draws
   *  items a user can read, so it is a different state and keeps its items. */
  const isEmpty = React.useMemo(() => items.every(isSeparator), [items])
  const catalogEmpty = t('buttons.noActions')
  const emptyText = empty?.trim() ? empty : (catalogEmpty === 'buttons.noActions' ? 'No actions available' : catalogEmpty)

  const enabledIndices = React.useMemo(
    () => items.flatMap((entry, index) => !isSeparator(entry) && !entry.disabled ? [index] : []),
    [items],
  )

  const focusIndex = React.useCallback((index: number) => {
    itemRefs.current[index]?.focus()
  }, [])

  const focusBoundary = React.useCallback((boundary: FocusBoundary) => {
    const index = boundary === 'first' ? enabledIndices[0] : enabledIndices.at(-1)
    if (index === undefined) triggerRef.current?.focus()
    else focusIndex(index)
  }, [enabledIndices, focusIndex])

  const openMenu = React.useCallback((boundary: FocusBoundary = 'first') => {
    pendingFocus.current = boundary
    setOpen(true)
  }, [])

  const closeMenu = React.useCallback((restoreFocus: boolean) => {
    pendingFocus.current = null
    setOpen(false)
    if (restoreFocus) queueMicrotask(() => triggerRef.current?.focus())
  }, [])

  React.useLayoutEffect(() => {
    if (!open || !pendingFocus.current) return
    const boundary = pendingFocus.current
    pendingFocus.current = null
    focusBoundary(boundary)
  }, [focusBoundary, open])

  React.useEffect(() => () => {
    if (typeaheadTimer.current) clearTimeout(typeaheadTimer.current)
  }, [])

  useOutsideClick(rootRef, () => closeMenu(false), { enabled: open, eventType: 'pointerdown' })

  const moveFocus = React.useCallback((currentIndex: number, delta: number) => {
    if (enabledIndices.length === 0) return
    const currentPosition = enabledIndices.indexOf(currentIndex)
    const start = currentPosition < 0 ? (delta > 0 ? -1 : 0) : currentPosition
    const nextPosition = (start + delta + enabledIndices.length) % enabledIndices.length
    const nextIndex = enabledIndices[nextPosition]
    if (nextIndex !== undefined) focusIndex(nextIndex)
  }, [enabledIndices, focusIndex])

  const focusByTypeahead = React.useCallback((event: React.KeyboardEvent<HTMLButtonElement>, currentIndex: number) => {
    if (event.key.length !== 1 || event.altKey || event.ctrlKey || event.metaKey) return false
    const key = event.key.toLocaleLowerCase()
    typeahead.current = key
    if (typeaheadTimer.current) clearTimeout(typeaheadTimer.current)
    typeaheadTimer.current = setTimeout(() => { typeahead.current = '' }, 500)

    const currentPosition = enabledIndices.indexOf(currentIndex)
    const ordered = [
      ...enabledIndices.slice(currentPosition + 1),
      ...enabledIndices.slice(0, currentPosition + 1),
    ]
    const match = ordered.find(index => {
      const entry = items[index]
      return entry !== undefined
        && !isSeparator(entry)
        && entry.label.trim().toLocaleLowerCase().startsWith(typeahead.current)
    })
    if (match !== undefined) focusIndex(match)
    return true
  }, [enabledIndices, focusIndex, items])

  const invoke = React.useCallback((item: ActionMenuItem) => {
    if (item.disabled) return
    item.onClick()
    closeMenu(true)
  }, [closeMenu])

  const onTriggerKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'ArrowDown' || event.key === 'Home') {
      event.preventDefault()
      openMenu('first')
    } else if (event.key === 'ArrowUp' || event.key === 'End') {
      event.preventDefault()
      openMenu('last')
    } else if (event.key === 'Escape' && open) {
      event.preventDefault()
      closeMenu(true)
    }
  }

  const triggerElement = trigger ?? (
    <button type="button" aria-label={defaultLabel} className="hl-action-menu__trigger">
      <svg aria-hidden="true" viewBox="0 0 20 20" className="hl-action-menu__trigger-icon">
        <circle cx="4" cy="10" r="1.6" />
        <circle cx="10" cy="10" r="1.6" />
        <circle cx="16" cy="10" r="1.6" />
      </svg>
    </button>
  )

  const renderedTrigger = React.isValidElement<Record<string, unknown>>(triggerElement)
    ? React.cloneElement(triggerElement, {
        className: cn('hl-action-menu__trigger', 'hl-action-menu__trigger--custom', triggerElement.props.className as string | undefined),
        'aria-controls': menuId,
        'aria-expanded': open,
        'aria-haspopup': 'menu',
        onClick: (event: React.MouseEvent<HTMLElement>) => {
          triggerRef.current = event.currentTarget
          setOpen(value => {
            pendingFocus.current = value ? null : 'first'
            return !value
          })
          const original = triggerElement.props.onClick as React.MouseEventHandler<HTMLElement> | undefined
          original?.(event)
        },
        onKeyDown: (event: React.KeyboardEvent<HTMLElement>) => {
          triggerRef.current = event.currentTarget
          onTriggerKeyDown(event)
          const original = triggerElement.props.onKeyDown as React.KeyboardEventHandler<HTMLElement> | undefined
          original?.(event)
        },
      })
    : (
        <button
          ref={triggerRef as React.RefObject<HTMLButtonElement>}
          type="button"
          aria-controls={menuId}
          aria-expanded={open}
          aria-haspopup="menu"
          className="hl-action-menu__trigger"
          onClick={() => setOpen(value => {
            pendingFocus.current = value ? null : 'first'
            return !value
          })}
          onKeyDown={onTriggerKeyDown}
        >
          {triggerElement}
        </button>
      )

  return (
    <div ref={rootRef} dir={direction} className={cn('hl-action-menu', className)} data-hl-align={align}>
      {renderedTrigger}
      {open ? (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          aria-orientation="vertical"
          className="hl-action-menu__menu"
          data-hl-empty={enabledIndices.length === 0 || undefined}
          data-hl-state={isEmpty ? 'empty' : 'populated'}
          onKeyDown={event => {
            if (event.key === 'Escape') {
              event.preventDefault()
              closeMenu(true)
            } else if (event.key === 'Tab') {
              closeMenu(false)
            }
          }}
        >
          {isEmpty ? (
            <p role="menuitem" aria-disabled="true" tabIndex={-1} className="hl-action-menu__empty" data-hl-empty-message="true">
              {emptyText}
            </p>
          ) : items.map((entry, index) => {
            if (isSeparator(entry)) {
              return <div key={`separator-${index}`} role="separator" className="hl-action-menu__separator" />
            }
            return (
              <button
                key={`${entry.label}-${index}`}
                ref={element => { itemRefs.current[index] = element }}
                type="button"
                tabIndex={-1}
                role={entry.checked === undefined ? 'menuitem' : 'menuitemcheckbox'}
                aria-checked={entry.checked}
                aria-disabled={entry.disabled || undefined}
                aria-keyshortcuts={entry.keyShortcuts}
                className="hl-action-menu__item"
                data-hl-item-id={entry.id}
                data-hl-variant={entry.variant ?? 'default'}
                disabled={entry.disabled}
                onClick={() => invoke(entry)}
                onKeyDown={event => {
                  if (event.key === 'ArrowDown') {
                    event.preventDefault()
                    moveFocus(index, 1)
                  } else if (event.key === 'ArrowUp') {
                    event.preventDefault()
                    moveFocus(index, -1)
                  } else if (event.key === 'Home') {
                    event.preventDefault()
                    focusBoundary('first')
                  } else if (event.key === 'End') {
                    event.preventDefault()
                    focusBoundary('last')
                  } else if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault()
                    invoke(entry)
                  } else {
                    focusByTypeahead(event, index)
                  }
                }}
              >
                {entry.checked === undefined
                  ? null
                  : <span aria-hidden="true" className="hl-action-menu__item-check" data-hl-checked={entry.checked}>{entry.checked ? '✓' : null}</span>}
                {entry.icon ? <span aria-hidden="true" className="hl-action-menu__item-icon">{entry.icon}</span> : null}
                <span className="hl-action-menu__item-label">{entry.label}</span>
                {entry.shortcutHint === undefined
                  ? null
                  : <span aria-hidden="true" className="hl-action-menu__item-hint">{entry.shortcutHint}</span>}
              </button>
            )
          })}
        </div>
      ) : null}
    </div>
  )
}
