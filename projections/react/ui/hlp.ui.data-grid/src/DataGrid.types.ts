import type * as React from 'react'

export type DataGridValueKind = 'text' | 'number' | 'boolean' | 'date' | 'custom'

export type DataGridGroupingState = readonly string[]

export interface DataGridCellContext<TRow> {
  readonly row: TRow
  readonly rowId: string
  readonly column: DataGridColumnDef<TRow>
  readonly value: unknown
}

export interface DataGridColumnDef<TRow> {
  readonly id: string
  readonly field: keyof TRow | ((row: TRow) => unknown)
  readonly header: React.ReactNode
  /** Required integer; lower priorities are removed first as width narrows. */
  readonly removalPriority: number
  readonly accessibleName?: string
  readonly valueKind?: DataGridValueKind
  readonly renderCell?: (context: DataGridCellContext<TRow>) => React.ReactNode
  readonly formatGroupValue?: (value: unknown) => React.ReactNode
}

export interface DataGridChildren<TRow> {
  readonly count: number
  readonly state: 'unloaded' | 'loading' | 'loaded' | 'failed'
  readonly children: readonly TRow[]
}
export interface DataGridChildrenRequest { readonly rowId: string }

/**
 * The grid's one list-state seam (conformance/hlp.ui.data-grid/selection-v1.json). Selection,
 * scroll offset and measured column widths travel in and out as a single object so a host that
 * closes an inspector can hand the very same object back on remount (L1701). The grid never
 * publishes selection through a second channel.
 */
export interface DataGridListState {
  /** drilldown-model.md row 0: the peeked row, remembered by id and never by row object. */
  readonly selectedRowId: string | null
  readonly scrollTop: number
  readonly columnWidths: Readonly<Record<string, number>>
}

/** drilldown-model.md row 1: a commit is a declared host action; the grid never navigates. */
export interface DataGridRowActivation { readonly rowId: string }

export interface DataGridProps<TRow> {
  readonly lazyChildren?: Readonly<Record<string, DataGridChildren<TRow>>>
  readonly onChildrenRequest?: (request: DataGridChildrenRequest) => void
  readonly accessibleName: string
  readonly rows: readonly TRow[]
  readonly columns: readonly DataGridColumnDef<TRow>[]
  readonly getRowId: (row: TRow) => string
  readonly grouping?: DataGridGroupingState
  readonly zebra?: boolean
  readonly className?: string
  readonly empty?: React.ReactNode
  readonly listState?: DataGridListState
  readonly onListStateChange?: (state: DataGridListState) => void
  readonly onRowActivate?: (activation: DataGridRowActivation) => void
}
