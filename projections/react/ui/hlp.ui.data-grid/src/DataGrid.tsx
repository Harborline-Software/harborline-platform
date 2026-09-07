import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type { DataGridChildren, DataGridColumnDef, DataGridListState, DataGridProps } from './DataGrid.types'
import {
  collectGroupKeys,
  branchCellValue,
  columnValue,
  columnsForWidth,
  childrenSnapshotKey,
  completesChildrenRequestKey,
  flattenVisibleEntries,
  prepareDataGrid,
  type PreparedGroup,
  type VisibleEntry,
} from './data-grid-model'

export const DATA_GRID_MAX_RENDERED_ROWS = 32
export const DATA_GRID_ROW_HEIGHT = 44
const EMPTY_GROUPING: readonly string[] = []
export const DATA_GRID_EMPTY_LIST_STATE: DataGridListState = { selectedRowId: null, scrollTop: 0, columnWidths: {} }

// The remembered list state is compared BY VALUE: a host that re-materialises an equal state object
// between renders must not look like a change, and an in-place mutation of the widths map must.
function sameWidths(left: Readonly<Record<string, number>>, right: Readonly<Record<string, number>>): boolean {
  const keys = Object.keys(left)
  return keys.length === Object.keys(right).length && keys.every(key => left[key] === right[key])
}

// A remount is handed the widths measured before the unmount, so the retained column set is right on
// the FIRST render instead of after a measurement pass (L1701).
function restoredWidth(columnWidths: Readonly<Record<string, number>>): number | null {
  const widths = Object.values(columnWidths)
  return widths.length === 0 ? null : widths.reduce((total, width) => total + width, 0)
}

interface FocusLocation {
  readonly rowKey: string
  readonly columnId: string
}

function classes(base: string, extra?: string): string {
  return extra ? `${base} ${extra}` : base
}

function groupText(value: unknown): string {
  if (value === null || value === undefined) return ''
  return value instanceof Date ? value.toISOString() : String(value)
}

function defaultCell(value: unknown): React.ReactNode {
  if (value === null || value === undefined) return ''
  if (typeof value === 'boolean') return String(value)
  if (value instanceof Date) return value.toISOString()
  if (typeof value !== 'object') return String(value)
  try {
    return JSON.stringify(value)
  } catch {
    return String(value)
  }
}

function gridColumns<TRow>(columns: readonly DataGridColumnDef<TRow>[]): React.CSSProperties {
  return { '--hl-data-grid-columns': `repeat(${Math.max(columns.length, 1)}, minmax(10rem, 1fr))` } as React.CSSProperties
}

function isModifiedCornerKey(event: React.KeyboardEvent): boolean {
  return event.ctrlKey || event.metaKey
}

function cellRefKey(rowKey: string, columnId: string): string {
  return `${rowKey.length}:${rowKey}${columnId}`
}

function recoverFocusColumn<TRow>(
  previousColumns: readonly DataGridColumnDef<TRow>[],
  visibleColumns: readonly DataGridColumnDef<TRow>[],
  focusedColumnId?: string,
): string {
  const previousIndex = focusedColumnId === undefined ? 0 : Math.max(0, previousColumns.findIndex(column => column.id === focusedColumnId))
  return visibleColumns[Math.min(previousIndex, visibleColumns.length - 1)].id
}

export function DataGrid<TRow>({
  accessibleName,
  rows,
  columns,
  getRowId,
  grouping,
  zebra = false,
  className,
  empty,
  lazyChildren,
  onChildrenRequest,
  listState,
  onListStateChange,
  onRowActivate,
}: DataGridProps<TRow>) {
  const { direction, t } = useHarborlineStrings()
  if (Object.keys(lazyChildren ?? {}).length > 0 && !onChildrenRequest) throw new Error('children-request-required')
  const gridRef = React.useRef<HTMLDivElement>(null)
  const viewportRef = React.useRef<HTMLDivElement>(null)
  const [internalListState, setInternalListState] = React.useState<DataGridListState>(listState ?? DATA_GRID_EMPTY_LIST_STATE)
  const list = listState ?? internalListState
  const publishList = (next: DataGridListState) => { setInternalListState(next); onListStateChange?.(next) }
  const [availableWidth, setAvailableWidth] = React.useState<number | null>(() => restoredWidth(list.columnWidths))
  const publishedWidths = React.useRef<Record<string, number> | null>(null)
  const effectiveGrouping = grouping ?? EMPTY_GROUPING
  const prepared = React.useMemo(
    () => prepareDataGrid(rows, columns, getRowId, effectiveGrouping, lazyChildren),
    [rows, columns, getRowId, effectiveGrouping, lazyChildren],
  )
  const visibleColumns = React.useMemo(() => columnsForWidth(columns, availableWidth), [availableWidth, columns])
  const [collapsedGroups, setCollapsedGroups] = React.useState<ReadonlySet<string>>(() => new Set())
  const [expandedLazy, setExpandedLazy] = React.useState<ReadonlySet<string>>(() => new Set())
  const pending = React.useRef(new Map<string, string>())
  const snapshotKey = (state: DataGridChildren<TRow>) => childrenSnapshotKey(state, getRowId)
  const completesRequest = (state: DataGridChildren<TRow>, previousKey: string) =>
    completesChildrenRequestKey(state, previousKey, getRowId)
  const effectiveCollapsed = new Set(collapsedGroups)
  for (const rowId of Object.keys(lazyChildren ?? {})) if (!expandedLazy.has(`row:${rowId}`)) effectiveCollapsed.add(`row:${rowId}`)
  const visibleEntries = React.useMemo(
    () => flattenVisibleEntries(prepared.roots, effectiveCollapsed),
    [prepared.roots, collapsedGroups, expandedLazy],
  )
  const validGroupKeys = React.useMemo(() => collectGroupKeys(prepared.roots), [prepared.roots])
  const [focus, setFocus] = React.useState<FocusLocation | null>(null)
  const cellRefs = React.useRef(new Map<string, HTMLElement>())
  const requestedFocus = React.useRef<FocusLocation | null>(null)
  const focusWithin = React.useRef(false)
  const previousVisibleColumns = React.useRef(visibleColumns)
  const instanceId = React.useId()

  React.useEffect(() => {
    setExpandedLazy(current => {
      const retained = new Set([...current].filter(key => validGroupKeys.has(key)))
      return retained.size === current.size ? current : retained
    })
    for (const [rowId, previous] of pending.current) {
      const state = lazyChildren?.[rowId]
      if (!validGroupKeys.has(`row:${rowId}`) || !state || completesRequest(state, previous)) pending.current.delete(rowId)
      else pending.current.set(rowId, snapshotKey(state))
    }
    setCollapsedGroups(current => {
      const retained = new Set([...current].filter(key => validGroupKeys.has(key)))
      return retained.size === current.size ? current : retained
    })
  }, [validGroupKeys, lazyChildren])

  const entryIndex = focus === null ? -1 : visibleEntries.findIndex(entry => entry.key === focus.rowKey)
  const effectiveRowIndex = entryIndex >= 0 ? entryIndex : 0
  const effectiveEntry = visibleEntries[effectiveRowIndex]
  const effectiveColumnIndex = effectiveEntry?.kind === 'group'
    ? 0
    : visibleColumns.findIndex(column => column.id === focus?.columnId)
  const boundedStart = Math.min(
    Math.max(0, Math.floor(list.scrollTop / DATA_GRID_ROW_HEIGHT)),
    Math.max(0, visibleEntries.length - DATA_GRID_MAX_RENDERED_ROWS),
  )
  const windowEntries = visibleEntries.slice(boundedStart, boundedStart + DATA_GRID_MAX_RENDERED_ROWS)

  React.useEffect(() => {
    const priorColumns = previousVisibleColumns.current
    previousVisibleColumns.current = visibleColumns
    if (visibleEntries.length === 0 || visibleColumns.length === 0) {
      if (focus !== null) setFocus(null)
      return
    }
    if (entryIndex < 0 || effectiveColumnIndex < 0) {
      const recovered = {
        rowKey: entryIndex < 0 ? visibleEntries[0].key : focus!.rowKey,
        columnId: focus === null || effectiveColumnIndex < 0 ? recoverFocusColumn(priorColumns, visibleColumns, focus?.columnId) : focus.columnId,
      }
      if (focusWithin.current) requestedFocus.current = recovered
      setFocus(recovered)
    }
  }, [effectiveColumnIndex, entryIndex, focus, visibleColumns, visibleEntries])

  React.useLayoutEffect(() => {
    const measure = () => {
      const width = gridRef.current?.clientWidth ?? 0
      if (width > 0) setAvailableWidth(width)
    }
    measure()
    window.addEventListener('resize', measure)
    const observer = typeof ResizeObserver === 'undefined' || gridRef.current === null ? undefined : new ResizeObserver(measure)
    if (gridRef.current) observer?.observe(gridRef.current)
    return () => { window.removeEventListener('resize', measure); observer?.disconnect() }
  }, [])

  React.useEffect(() => {
    if (availableWidth === null || visibleColumns.length === 0) return
    const width = availableWidth / visibleColumns.length
    const columnWidths = Object.fromEntries(visibleColumns.map(column => [column.id, width]))
    // Compared against what THIS grid last published, not against the incoming prop: a controlled
    // host that ignores the callback would otherwise be re-published on every render.
    if (publishedWidths.current !== null && sameWidths(publishedWidths.current, columnWidths)) return
    publishedWidths.current = columnWidths
    publishList({ ...list, columnWidths })
  }, [availableWidth, visibleColumns])

  React.useLayoutEffect(() => {
    if (viewportRef.current && list.scrollTop > 0) viewportRef.current.scrollTop = list.scrollTop
  }, [])

  React.useEffect(() => {
    const requested = requestedFocus.current
    if (focus === null || requested === null || requested.rowKey !== focus.rowKey || requested.columnId !== focus.columnId) return
    requestedFocus.current = null
    cellRefs.current.get(cellRefKey(focus.rowKey, focus.columnId))?.focus()
  }, [focus, boundedStart])

  function focusCell(entry: VisibleEntry<TRow>, columnIndex: number): void {
    const nextColumn = entry.kind === 'group' ? 0 : Math.max(0, Math.min(columnIndex, visibleColumns.length - 1))
    const next = { rowKey: entry.key, columnId: visibleColumns[nextColumn].id }
    requestedFocus.current = next
    setFocus(next)
  }

  // Moving focus is NOT selecting. Only a declared peek gesture (the fixture's peekKeys and a
  // single click) publishes selectedRowId; every other route through here — the corner keys, an
  // unhandled key, a scroll, focus recovery after a column change — moves focus and the window
  // only, so the inspector stays on the row the user actually peeked.
  function moveFocus(rowIndex: number, columnIndex: number, peek = false): void {
    if (visibleEntries.length === 0 || visibleColumns.length === 0) return
    const nextRow = Math.max(0, Math.min(rowIndex, visibleEntries.length - 1))
    const entry = visibleEntries[nextRow]
    // One publish, never two: the scroll move and the peek are decided together and written once.
    let nextList = list
    if (nextRow < boundedStart) nextList = { ...nextList, scrollTop: nextRow * DATA_GRID_ROW_HEIGHT }
    else if (nextRow >= boundedStart + DATA_GRID_MAX_RENDERED_ROWS) {
      nextList = { ...nextList, scrollTop: (nextRow - DATA_GRID_MAX_RENDERED_ROWS + 1) * DATA_GRID_ROW_HEIGHT }
    }
    if (peek && entry.kind !== 'group' && entry.rowId !== nextList.selectedRowId) nextList = { ...nextList, selectedRowId: entry.rowId }
    if (nextList !== list) publishList(nextList)
    focusCell(entry, columnIndex)
  }

  function toggleGroup(group: PreparedGroup<TRow>, force?: 'expand' | 'collapse'): void {
    if (group.lazy) {
      const collapse = force === 'collapse' || (force === undefined && expandedLazy.has(group.key))
      setExpandedLazy(current => { const next = new Set(current); if (collapse) next.delete(group.key); else next.add(group.key); return next })
      if (!collapse && ['unloaded', 'failed'].includes(group.lazy.state) && !pending.current.has(group.lazy.rowId)) {
        pending.current.set(group.lazy.rowId, snapshotKey(lazyChildren![group.lazy.rowId]))
        onChildrenRequest?.({ rowId: group.lazy.rowId })
      }
      return
    }
    setCollapsedGroups(current => {
      const next = new Set(current)
      const shouldCollapse = force === 'collapse' || (force === undefined && !current.has(group.key))
      if (shouldCollapse) next.add(group.key)
      else next.delete(group.key)
      return next
    })
  }

  function handleKeyDown(event: React.KeyboardEvent, rowIndex: number, columnIndex: number, entry: VisibleEntry<TRow>): void {
    let nextRow = rowIndex
    let nextColumn = columnIndex
    let handled = true
    // drilldown-model.md row 0: only a row-changing Arrow key peeks. Home/End/Ctrl+Home/Ctrl+End
    // are focus, not selection.
    let peek = false
    switch (event.key) {
      case 'ArrowDown': nextRow += 1; peek = true; break
      case 'ArrowUp': nextRow -= 1; peek = true; break
      case 'ArrowRight':
        if (entry.kind === 'group') toggleGroup(entry, 'expand')
        else nextColumn += 1
        break
      case 'ArrowLeft':
        if (entry.kind === 'group') toggleGroup(entry, 'collapse')
        else nextColumn -= 1
        break
      case 'Home':
        if (isModifiedCornerKey(event)) nextRow = 0
        nextColumn = 0
        break
      case 'End':
        if (isModifiedCornerKey(event)) nextRow = visibleEntries.length - 1
        nextColumn = visibleColumns.length - 1
        break
      case 'Enter':
        // drilldown-model.md row 1: commit, never peek. Selection is untouched and the grid does
        // not navigate — it emits the declared host action with the row id.
        if (entry.kind === 'group') toggleGroup(entry)
        else { event.preventDefault(); onRowActivate?.({ rowId: entry.rowId }); return }
        break
      case ' ':
        if (entry.kind === 'group') toggleGroup(entry)
        else handled = false
        break
      default: handled = false
    }
    if (!handled) return
    event.preventDefault()
    moveFocus(nextRow, nextColumn, peek)
  }

  function bindCellRef(key: string, element: HTMLElement | null): void {
    if (element) cellRefs.current.set(key, element)
    else cellRefs.current.delete(key)
  }

  function handleScroll(event: React.UIEvent<HTMLDivElement>): void {
    const nextStart = Math.floor(event.currentTarget.scrollTop / DATA_GRID_ROW_HEIGHT)
    const limited = Math.min(nextStart, Math.max(0, visibleEntries.length - DATA_GRID_MAX_RENDERED_ROWS))
    publishList({ ...list, scrollTop: limited * DATA_GRID_ROW_HEIGHT })
    // A scroll is not a peek. The single tab stop follows the window so the grid stays keyboard
    // reachable, but the published offset is the one the user scrolled to and the selection is
    // left exactly where they put it.
    if (!focusWithin.current) return
    const entry = visibleEntries[limited]
    if (entry) focusCell(entry, effectiveColumnIndex)
  }

  const columnStyle = gridColumns(visibleColumns)
  const resolvedEmpty = empty ?? t('dataGrid.noResults')
  const canvasHeight = Math.max(windowEntries.length > 0 ? visibleEntries.length * DATA_GRID_ROW_HEIGHT : DATA_GRID_ROW_HEIGHT, DATA_GRID_ROW_HEIGHT)

  return (
    <div
      aria-colcount={visibleColumns.length}
      aria-label={accessibleName}
      aria-rowcount={visibleEntries.length}
      className={classes('hl-data-grid', className)}
      data-zebra={zebra ? 'true' : 'false'}
      dir={direction}
      role={effectiveGrouping.length > 0 || Object.keys(lazyChildren ?? {}).length > 0 ? 'treegrid' : 'grid'}
      ref={gridRef}
      style={columnStyle}
    >
      {Object.keys(lazyChildren ?? {}).length > 0 && <div className="hl-data-grid__aggregate-note">Aggregates: loaded children only</div>}
      <div className="hl-data-grid__scroller">
      <div className="hl-data-grid__header" role="rowgroup">
        <div className="hl-data-grid__row hl-data-grid__row--header" role="row">
          {visibleColumns.map((column, columnIndex) => (
            <div
              aria-colindex={columnIndex + 1}
              aria-label={column.accessibleName}
              className="hl-data-grid__header-cell"
              id={`${instanceId}-column-${columnIndex}`}
              key={column.id}
              role="columnheader"
            >
              {column.header}
            </div>
          ))}
        </div>
      </div>
      <div
        className="hl-data-grid__viewport"
        data-rendered-row-count={windowEntries.length}
        onBlurCapture={event => {
          if (!event.currentTarget.contains(event.relatedTarget)) focusWithin.current = false
        }}
        onFocusCapture={() => { focusWithin.current = true }}
        onScroll={handleScroll}
        ref={viewportRef}
        role="rowgroup"
      >
        <div className="hl-data-grid__canvas" style={{ blockSize: canvasHeight }}>
          {windowEntries.length === 0 ? (
            <div className="hl-data-grid__row hl-data-grid__row--empty" role="row">
              <div className="hl-data-grid__cell hl-data-grid__empty" role="gridcell">{resolvedEmpty}</div>
            </div>
          ) : windowEntries.map((entry, offset) => {
            const logicalIndex = boundedStart + offset
            const rowStyle = { insetBlockStart: logicalIndex * DATA_GRID_ROW_HEIGHT, ...columnStyle } as React.CSSProperties
            if (entry.kind === 'group') {
              const collapsed = effectiveCollapsed.has(entry.key)
              const previous = entry.lazy && pending.current.get(entry.lazy.rowId)
              const childState = entry.lazy && (previous && !completesRequest(entry.lazy, previous) ? 'loading' : entry.lazy.state)
              const groupColumn = columns.find(candidate => candidate.id === entry.columnId)
              const cellKey = cellRefKey(entry.key, visibleColumns[0]?.id ?? '')
              return (
                <div
                  aria-level={entry.depth + 1}
                  aria-rowindex={logicalIndex + 1}
                  className="hl-data-grid__row hl-data-grid__row--group"
                  data-group-id={entry.key}
                  data-children-state={childState}
                  aria-busy={childState === 'loading' || undefined}
                  key={entry.key}
                  role="row"
                  style={rowStyle}
                >
                  {visibleColumns.map((column, columnIndex) => columnIndex === 0 ? (
                    <div aria-colindex={1} aria-expanded={!collapsed} className="hl-data-grid__cell hl-data-grid__group-cell" data-group-depth={entry.depth}
                      key={column.id} onClick={() => toggleGroup(entry)} onFocus={() => setFocus({ rowKey: entry.key, columnId: column.id })}
                      onKeyDown={event => handleKeyDown(event, logicalIndex, 0, entry)} ref={element => bindCellRef(cellKey, element)} role="gridcell"
                      tabIndex={focus?.rowKey === entry.key || (focus === null && logicalIndex === 0) ? 0 : -1}>
                      <span aria-hidden="true" className="hl-data-grid__disclosure" data-collapsed={collapsed ? 'true' : 'false'}><svg fill="none" focusable="false" viewBox="0 0 16 16"><path d="m4 6 4 4 4-4" /></svg></span>
                      <span className="hl-data-grid__group-cell-content">{groupColumn?.formatGroupValue ? groupColumn.formatGroupValue(entry.value) : groupText(entry.value)} ({entry.lazy?.count ?? entry.leaves.length})</span>
                      {childState === 'loading' && <span role="status">Loading children…</span>}
                      {childState === 'failed' && <span role="alert">Children failed to load. Collapse and expand to retry.</span>}
                      <span className="hl-data-grid__visually-hidden">{collapsed ? t('dataGrid.expandGroup') : t('dataGrid.collapseGroup')}</span>
                    </div>
                  ) : (
                    <div aria-colindex={columnIndex + 1} className="hl-data-grid__cell hl-data-grid__group-cell" data-column-id={column.id} data-aggregate-scope={entry.lazy ? 'loaded' : undefined} key={column.id} role="gridcell">
                      <span className="hl-data-grid__group-cell-content">{defaultCell(branchCellValue(entry, column))}</span>
                    </div>
                  ))}
                </div>
              )
            }
            return (
              <div
                aria-rowindex={logicalIndex + 1}
                aria-selected={list.selectedRowId === entry.rowId}
                className="hl-data-grid__row hl-data-grid__row--leaf"
                onClick={() => { if (list.selectedRowId !== entry.rowId) publishList({ ...list, selectedRowId: entry.rowId }) }}
                onDoubleClick={() => onRowActivate?.({ rowId: entry.rowId })}
                data-leaf-index={entry.leafIndex}
                data-row-id={entry.rowId}
                data-zebra-stripe={zebra && entry.leafIndex % 2 === 1 ? 'alternate' : 'base'}
                key={entry.key}
                role="row"
                style={rowStyle}
              >
                {visibleColumns.map((column, columnIndex) => {
                  const value = columnValue(column, entry.row)
                  const cellKey = cellRefKey(entry.key, column.id)
                  return (
                    <div
                      aria-colindex={columnIndex + 1}
                      aria-describedby={`${instanceId}-column-${columnIndex}`}
                      className="hl-data-grid__cell"
                      data-column-id={column.id}
                      key={column.id}
                      onFocus={() => setFocus({ rowKey: entry.key, columnId: column.id })}
                      onKeyDown={event => handleKeyDown(event, logicalIndex, columnIndex, entry)}
                      ref={element => bindCellRef(cellKey, element)}
                      role="gridcell"
                      tabIndex={(focus === null && logicalIndex === 0 && columnIndex === 0) || (focus?.rowKey === entry.key && focus.columnId === column.id) ? 0 : -1}
                    >
                      {column.renderCell
                        ? column.renderCell({ row: entry.row, rowId: entry.rowId, column, value })
                        : defaultCell(value)}
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>
      </div>
      </div>
    </div>
  )
}
