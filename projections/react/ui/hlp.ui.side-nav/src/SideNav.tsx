import * as React from 'react'

import type {
  SideNavGroup as NeutralSideNavGroup,
  SideNavItemModel,
} from '@harborline-platform/hlp.ui.side-nav-group'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'
import { Tooltip } from '@harborline-platform/hlp.ui.tooltip'
import { useTouchSizing } from '@harborline-platform/hlp.ui.use-can-show-master-detail'

export interface SideNavItem extends Omit<SideNavItemModel, 'children'> {
  readonly icon?: React.ReactNode
  readonly badge?: React.ReactNode
  readonly accessory?: React.ReactNode
  readonly children?: readonly SideNavItem[]
}

export interface SideNavGroup extends Omit<NeutralSideNavGroup, 'items'> {
  readonly items: readonly SideNavItem[]
}

export type SideNavStructure = readonly SideNavItem[] | readonly SideNavGroup[]

export interface SideNavProps {
  readonly items: SideNavStructure
  readonly activeItemId?: string
  readonly collapsed?: boolean
  readonly onItemActivate?: (item: SideNavItem) => void
  readonly className?: string
  readonly navigationLabel?: string
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function isGroupStructure(items: SideNavStructure): items is readonly SideNavGroup[] {
  return items.length > 0 && 'items' in items[0]!
}

function itemLists(items: SideNavStructure): readonly (readonly SideNavItem[])[] {
  return isGroupStructure(items) ? items.map(group => group.items) : [items]
}

function validateItems(items: SideNavStructure): void {
  const identities = new Set<string>()

  if (isGroupStructure(items)) {
    for (const group of items) {
      if (!group.id.trim() || identities.has(group.id)) throw new Error('duplicate-id')
      identities.add(group.id)
    }
  }

  const visit = (item: SideNavItem): void => {
    if (!item.id.trim() || identities.has(item.id)) throw new Error('duplicate-id')
    if (!item.label.trim()) throw new Error('missing-label')
    if (item.href !== undefined && item.children !== undefined && item.children.length > 0) {
      throw new Error('invalid-action')
    }
    identities.add(item.id)
    item.children?.forEach(visit)
  }

  itemLists(items).forEach(list => list.forEach(visit))
}

function collectExpandableIds(items: SideNavStructure): Set<string> {
  const expandable = new Set<string>()
  const visit = (item: SideNavItem): void => {
    if (item.children?.length) {
      expandable.add(item.id)
      item.children.forEach(visit)
    }
  }
  itemLists(items).forEach(list => list.forEach(visit))
  return expandable
}

function collectActiveAncestors(items: SideNavStructure, activeItemId: string | undefined): Set<string> {
  const ancestors = new Set<string>()
  if (activeItemId === undefined) return ancestors

  const visit = (item: SideNavItem): boolean => {
    if (item.id === activeItemId) return true
    if (!item.children?.some(visit)) return false
    ancestors.add(item.id)
    return true
  }
  itemLists(items).forEach(list => list.some(visit))
  return ancestors
}

interface SideNavRowProps {
  readonly item: SideNavItem
  readonly activeItemId?: string
  readonly collapsed: boolean
  readonly direction: 'ltr' | 'rtl'
  readonly expandedIds: ReadonlySet<string>
  readonly onItemActivate?: (item: SideNavItem) => void
  readonly onToggle: (id: string) => void
}

function SideNavRow({
  item,
  activeItemId,
  collapsed,
  direction,
  expandedIds,
  onItemActivate,
  onToggle,
}: SideNavRowProps) {
  const hasChildren = Boolean(item.children?.length)
  const expanded = hasChildren && expandedIds.has(item.id)
  const active = !hasChildren && item.id === activeItemId
  const branchId = `hl-side-nav-branch-${React.useId().replace(/:/g, '')}`
  const disabled = item.disabled ?? false

  const content = (
    <>
      {item.icon === undefined ? null : (
        <span aria-hidden="true" className="hl-side-nav__icon">{item.icon}</span>
      )}
      {collapsed ? null : <span className="hl-side-nav__label">{item.label}</span>}
      {!collapsed && hasChildren ? (
        <span aria-hidden="true" className="hl-side-nav__disclosure">›</span>
      ) : null}
    </>
  )

  const controlClassName = classes('hl-side-nav__control', active && 'hl-side-nav__control--active')
  let control: React.ReactElement

  if (hasChildren) {
    control = (
      <button
        aria-controls={!collapsed && expanded ? branchId : undefined}
        aria-expanded={collapsed ? undefined : expanded}
        aria-label={collapsed ? item.label : undefined}
        className={controlClassName}
        disabled={disabled}
        onClick={() => onToggle(item.id)}
        type="button"
      >
        {content}
      </button>
    )
  } else if (item.href !== undefined) {
    control = (
      <a
        aria-current={active ? 'page' : undefined}
        aria-disabled={disabled || undefined}
        aria-label={collapsed ? item.label : undefined}
        className={controlClassName}
        href={disabled ? undefined : item.href}
        onClick={event => {
          if (disabled) {
            event.preventDefault()
            return
          }
          onItemActivate?.(item)
        }}
        onKeyDown={event => {
          if (!disabled) return
          if (event.key === 'Enter' || event.key === ' ') event.preventDefault()
        }}
        tabIndex={disabled ? -1 : undefined}
      >
        {content}
      </a>
    )
  } else {
    control = (
      <button
        aria-current={active ? 'page' : undefined}
        aria-label={collapsed ? item.label : undefined}
        className={controlClassName}
        disabled={disabled}
        onClick={() => onItemActivate?.(item)}
        type="button"
      >
        {content}
      </button>
    )
  }

  const namedControl = collapsed ? (
    <Tooltip
      content={item.label}
      delayDuration={400}
      side={direction === 'rtl' ? 'left' : 'right'}
      triggerClassName="hl-side-nav__tooltip-trigger"
    >
      {control}
    </Tooltip>
  ) : control

  const trailing = item.accessory ?? item.badge

  return (
    <li className="hl-side-nav__item">
      <div className="hl-side-nav__row">
        {namedControl}
        {trailing === undefined ? null : (
          <span className="hl-side-nav__accessory">{trailing}</span>
        )}
      </div>
      {!collapsed && expanded ? (
        <ul className="hl-side-nav__list hl-side-nav__list--nested" id={branchId}>
          {item.children!.map(child => (
            <SideNavRow
              activeItemId={activeItemId}
              collapsed={collapsed}
              direction={direction}
              expandedIds={expandedIds}
              item={child}
              key={child.id}
              onItemActivate={onItemActivate}
              onToggle={onToggle}
            />
          ))}
        </ul>
      ) : null}
    </li>
  )
}

export function SideNav({
  items,
  activeItemId,
  collapsed = false,
  onItemActivate,
  className,
  navigationLabel,
}: SideNavProps) {
  validateItems(items)
  const { direction, t } = useHarborlineStrings()
  const touchSized = useTouchSizing()
  const translatedNavigation = t('navigation.label')
  const accessibleName = navigationLabel
    ?? (translatedNavigation === 'navigation.label' ? 'Navigation' : translatedNavigation)
  if (!accessibleName.trim()) throw new Error('accessible-name-required')

  const activeAncestors = React.useMemo(
    () => collectActiveAncestors(items, activeItemId),
    [activeItemId, items],
  )
  const expandableIds = React.useMemo(() => collectExpandableIds(items), [items])
  const [expandedIds, setExpandedIds] = React.useState<Set<string>>(() => activeAncestors)

  React.useEffect(() => {
    setExpandedIds(current => {
      const next = new Set([...current].filter(id => expandableIds.has(id)))
      activeAncestors.forEach(id => next.add(id))
      if (next.size === current.size && [...next].every(id => current.has(id))) return current
      return next
    })
  }, [activeAncestors, expandableIds])

  const toggle = React.useCallback((id: string) => {
    setExpandedIds(current => {
      const next = new Set(current)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }, [])

  const renderItems = (list: readonly SideNavItem[]) => list.map(item => (
    <SideNavRow
      activeItemId={activeItemId}
      collapsed={collapsed}
      direction={direction}
      expandedIds={expandedIds}
      item={item}
      key={item.id}
      onItemActivate={onItemActivate}
      onToggle={toggle}
    />
  ))

  return (
    <nav
      aria-label={accessibleName}
      className={classes('hl-side-nav', className)}
      data-hl-collapsed={collapsed || undefined}
      data-hl-touch={touchSized || undefined}
      dir={direction}
    >
      {isGroupStructure(items) ? items.map((group, index) => {
        const labelId = `hl-side-nav-group-${index}-${group.id.replace(/[^a-zA-Z0-9_-]/g, '-')}`
        return (
          <div className="hl-side-nav__group" key={group.id}>
            {!collapsed && group.label ? (
              <p className="hl-side-nav__group-label" id={labelId}>{group.label}</p>
            ) : null}
            <ul
              aria-labelledby={!collapsed && group.label ? labelId : undefined}
              className="hl-side-nav__list"
            >
              {renderItems(group.items)}
            </ul>
          </div>
        )
      }) : (
        <ul className="hl-side-nav__list">{renderItems(items)}</ul>
      )}
    </nav>
  )
}
