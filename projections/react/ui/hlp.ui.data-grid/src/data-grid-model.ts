import type { DataGridChildren, DataGridColumnDef, DataGridGroupingState, DataGridValueKind } from './DataGrid.types'

const VALUE_KINDS = new Set<DataGridValueKind>(['text', 'number', 'boolean', 'date', 'custom'])
export const DATA_GRID_MIN_COLUMN_WIDTH = 160

export interface PreparedLeaf<TRow> {
  readonly kind: 'leaf'
  readonly key: string
  readonly row: TRow
  readonly rowId: string
  readonly sourceIndex: number
}

export interface PreparedGroup<TRow> {
  readonly lazy?: DataGridChildren<TRow> & { readonly rowId: string }
  readonly kind: 'group'
  readonly key: string
  readonly columnId: string
  readonly value: unknown
  readonly depth: number
  readonly leaves: readonly PreparedLeaf<TRow>[]
  readonly children: readonly PreparedEntry<TRow>[]
}

export type PreparedEntry<TRow> = PreparedLeaf<TRow> | PreparedGroup<TRow>

export interface PreparedDataGrid<TRow> {
  readonly columns: readonly DataGridColumnDef<TRow>[]
  readonly roots: readonly PreparedEntry<TRow>[]
  readonly rowCount: number
  readonly normalizationOperations: number
}

export interface VisibleLeaf<TRow> extends PreparedLeaf<TRow> {
  readonly leafIndex: number
}

export type VisibleEntry<TRow> = PreparedGroup<TRow> | VisibleLeaf<TRow>

export function columnValue<TRow>(column: DataGridColumnDef<TRow>, row: TRow): unknown {
  return typeof column.field === 'function' ? column.field(row) : row[column.field]
}

export function columnsForWidth<TRow>(columns: readonly DataGridColumnDef<TRow>[], width: number | null): readonly DataGridColumnDef<TRow>[] {
  if (width === null || width <= 0) return columns
  const capacity = Math.max(1, Math.floor(width / DATA_GRID_MIN_COLUMN_WIDTH))
  if (capacity >= columns.length) return columns
  const retained = new Set([...columns]
    .sort((left, right) => right.removalPriority - left.removalPriority || left.id.localeCompare(right.id))
    .slice(0, capacity)
    .map(column => column.id))
  return columns.filter(column => retained.has(column.id))
}

export function branchCellValue<TRow>(group: PreparedGroup<TRow>, column: DataGridColumnDef<TRow>): unknown {
  const distinct = new Map<string, unknown>()
  for (const leaf of group.leaves) {
    const value = columnValue(column, leaf.row)
    const key = valueKey(value, column.valueKind)
    if (!distinct.has(key)) distinct.set(key, value)
  }
  if (distinct.size > 1) return `mixed — ${distinct.size} ${column.valueKind ?? 'text'}`
  return distinct.values().next().value
}

function valueKey(value: unknown, valueKind?: DataGridValueKind): string {
  if (valueKind === 'date') {
    const instant = value instanceof Date ? value.getTime() : typeof value === 'string' ? Date.parse(value) : Number.NaN
    if (Number.isFinite(instant)) return `date:${instant}`
  }
  if (value instanceof Date) return `date:${value.toISOString()}`
  if (value === null) return 'null:'
  if (value === undefined) return 'undefined:'
  const kind = typeof value
  if (kind === 'object') {
    try {
      return `object:${JSON.stringify(value)}`
    } catch {
      return `object:${String(value)}`
    }
  }
  return `${kind}:${String(value)}`
}

function groupEntries<TRow>(
  leaves: readonly PreparedLeaf<TRow>[],
  grouping: DataGridGroupingState,
  columnsById: ReadonlyMap<string, DataGridColumnDef<TRow>>,
  depth: number,
  parentKey: string,
): readonly PreparedEntry<TRow>[] {
  if (depth >= grouping.length) return leaves
  const columnId = grouping[depth]
  const column = columnsById.get(columnId)
  if (!column) throw new Error('unknown-grouping-column')

  const buckets = new Map<string, { value: unknown; leaves: PreparedLeaf<TRow>[] }>()
  for (const leaf of leaves) {
    const value = columnValue(column, leaf.row)
    const key = valueKey(value, column.valueKind)
    const bucket = buckets.get(key)
    if (bucket) bucket.leaves.push(leaf)
    else buckets.set(key, { value, leaves: [leaf] })
  }

  return [...buckets.entries()].map(([bucketKey, bucket]) => {
    const key = `${parentKey}/group:${depth}:${columnId.length}:${columnId}:${bucketKey.length}:${bucketKey}`
    return {
      kind: 'group' as const,
      key,
      columnId,
      value: bucket.value,
      depth,
      leaves: bucket.leaves,
      children: groupEntries(bucket.leaves, grouping, columnsById, depth + 1, key),
    }
  })
}

export function prepareDataGrid<TRow>(
  rows: readonly TRow[],
  columns: readonly DataGridColumnDef<TRow>[],
  getRowId: (row: TRow) => string,
  grouping: DataGridGroupingState = [],
  lazyChildren: Readonly<Record<string, DataGridChildren<TRow>>> = {},
): PreparedDataGrid<TRow> {
  const columnsById = new Map<string, DataGridColumnDef<TRow>>()
  let normalizationOperations = 0
  for (const column of columns) {
    normalizationOperations += 1
    if (column.id.trim().length === 0) throw new Error('column-id-required')
    if (columnsById.has(column.id)) throw new Error('duplicate-column-id')
    if (!Number.isInteger(column.removalPriority)) throw new Error('column-removal-priority-required')
    if (column.valueKind !== undefined && !VALUE_KINDS.has(column.valueKind)) throw new Error('unsupported-value-kind')
    columnsById.set(column.id, column)
  }
  for (const columnId of grouping) {
    normalizationOperations += 1
    if (!columnsById.has(columnId)) throw new Error('unknown-grouping-column')
  }

  const rowIds = new Set<string>()
  const loaded = new Map<string, readonly PreparedLeaf<TRow>[]>()
  const leaf = (row: TRow, sourceIndex: number): PreparedLeaf<TRow> => {
    normalizationOperations += 1
    const rowId = getRowId(row)
    if (rowId.trim().length === 0) throw new Error('row-id-required')
    if (rowIds.has(rowId)) throw new Error('duplicate-row-id')
    rowIds.add(rowId)
    return { kind: 'leaf', key: `row:${rowId}`, row, rowId, sourceIndex }
  }
  const expand = (entries: readonly PreparedEntry<TRow>[], depth: number): readonly PreparedEntry<TRow>[] => entries.map(entry => {
    if (entry.kind === 'group') {
      const children = expand(entry.children, depth + 1)
      return { ...entry, children, leaves: entry.leaves.flatMap(leaf => loaded.get(leaf.rowId) ?? [leaf]) }
    }
    const lazy = Object.hasOwn(lazyChildren, entry.rowId) ? lazyChildren[entry.rowId] : undefined
    if (!lazy) return entry
    if (!Number.isSafeInteger(lazy.count) || lazy.count < 0 || lazy.count > 2147483647 || !['unloaded', 'loading', 'loaded', 'failed'].includes(lazy.state)) throw new Error('invalid-lazy-children')
    const children = expand(lazy.children.map(leaf), depth + 1)
    const leaves = children.flatMap(child => child.kind === 'leaf' ? [child] : child.leaves)
    loaded.set(entry.rowId, leaves)
    return { kind: 'group', key: entry.key, columnId: columns[0]?.id ?? '', value: columns[0] ? columnValue(columns[0], entry.row) : '', depth,
      lazy: { ...lazy, rowId: entry.rowId }, children, leaves }
  })
  const leaves = rows.map(leaf)

  return {
    columns,
    roots: expand(groupEntries(leaves, grouping, columnsById, 0, 'root'), 0),
    rowCount: rows.length,
    normalizationOperations,
  }
}

export function flattenVisibleEntries<TRow>(
  roots: readonly PreparedEntry<TRow>[],
  collapsedGroups: ReadonlySet<string>,
): readonly VisibleEntry<TRow>[] {
  const visible: VisibleEntry<TRow>[] = []
  let leafIndex = 0
  const visit = (entries: readonly PreparedEntry<TRow>[]) => {
    for (const entry of entries) {
      if (entry.kind === 'leaf') {
        visible.push({ ...entry, leafIndex })
        leafIndex += 1
        continue
      }
      visible.push(entry)
      if (!collapsedGroups.has(entry.key)) visit(entry.children)
    }
  }
  visit(roots)
  return visible
}

export function collectGroupKeys<TRow>(roots: readonly PreparedEntry<TRow>[]): ReadonlySet<string> {
  const keys = new Set<string>()
  const visit = (entries: readonly PreparedEntry<TRow>[]) => {
    for (const entry of entries) {
      if (entry.kind !== 'group') continue
      keys.add(entry.key)
      visit(entry.children)
    }
  }
  visit(roots)
  return keys
}

// A lazy branch's outstanding request completes only on a distinguishable host response: the
// observable snapshot VALUE — state plus the ordered canonical child keys — must differ from the
// snapshot observed when the request started. Object identity, reference equality and
// re-materialised-but-equal snapshots are never distinguishable, and count is deliberately excluded
// (a count refresh is not a response). Structural, not deep equality: getRowId is the component's
// canonical content key for a row.
export function childrenSnapshotKey<TRow>(
  snapshot: DataGridChildren<TRow>,
  getRowId: (row: TRow) => string,
): string {
  return JSON.stringify([snapshot.state, snapshot.children.map(getRowId)])
}

export function completesChildrenRequest<TRow>(
  snapshot: DataGridChildren<TRow>,
  observed: DataGridChildren<TRow>,
  getRowId: (row: TRow) => string,
): boolean {
  return completesChildrenRequestKey(snapshot, childrenSnapshotKey(observed, getRowId), getRowId)
}

// The component remembers the KEY observed when the request started, never the snapshot object: a
// host that mutates the children array it already handed over would otherwise alias the observed
// snapshot and the request could never complete (review 3 of ticket 230 slice 2).
export function completesChildrenRequestKey<TRow>(
  snapshot: DataGridChildren<TRow>,
  observedKey: string,
  getRowId: (row: TRow) => string,
): boolean {
  return (snapshot.state === 'loaded' || snapshot.state === 'failed') &&
    childrenSnapshotKey(snapshot, getRowId) !== observedKey
}
