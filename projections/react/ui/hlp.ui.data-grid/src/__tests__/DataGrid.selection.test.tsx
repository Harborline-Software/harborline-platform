import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DataGrid, DATA_GRID_ROW_HEIGHT } from '../DataGrid'
import type { DataGridChildren, DataGridColumnDef, DataGridListState } from '../DataGrid.types'

const read = (name: string) => JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-grid', name), 'utf8'))
const selection = read('selection-v1.json')
const base = read(selection.staticFixture)

type Row = { id: string; [key: string]: unknown }
const columns: DataGridColumnDef<Row>[] = base.columns.map((column: DataGridColumnDef<Row>) => ({ ...column, field: column.id }))

// Fixture authority: a name the fixture does not declare fails loudly rather than silently
// replaying an empty step.
function fixtureRow(id: string): Row {
  const row = base.rows.find((candidate: Row) => candidate.id === id)
  if (!row) throw new Error(`missing-fixture-row: ${id}`)
  return row
}

function cell(container: HTMLElement, rowId: string): HTMLElement {
  const element = container.querySelector<HTMLElement>(`[data-row-id='${rowId}'] .hl-data-grid__cell`)
  if (!element) throw new Error(`missing-visible-row: ${rowId}`)
  return element
}

const selectedIds = (container: HTMLElement) =>
  [...container.querySelectorAll('[data-row-id][aria-selected="true"]')].map(row => row.getAttribute('data-row-id'))

describe('fixture-owned selection model', () => {
  it('replays every declared transition: click and Arrow keys peek, Enter and double click commit', () => {
    const activations: string[] = []
    let state: DataGridListState = selection.initial
    let grouping: string[] = []
    const props = () => ({
      accessibleName: 'Selection',
      rows: base.rows as Row[],
      columns,
      getRowId: (row: Row) => row.id,
      grouping,
      listState: state,
      onListStateChange: (next: DataGridListState) => { state = next },
      onRowActivate: (activation: { rowId: string }) => activations.push(activation.rowId),
    })
    const view = render(<DataGrid {...props()} />)
    const resize = (capacity: number) => {
      Object.defineProperty(view.container.querySelector('.hl-data-grid'), 'clientWidth', { configurable: true, value: capacity * selection.minimumColumnWidth })
      fireEvent(window, new Event('resize'))
    }

    for (const transition of selection.transitions) {
      const action = transition.action
      switch (action.kind) {
        case 'click':
          fireEvent.click(cell(view.container, fixtureRow(action.rowId).id))
          break
        case 'doubleClick':
          fireEvent.click(cell(view.container, fixtureRow(action.rowId).id))
          view.rerender(<DataGrid {...props()} />)
          fireEvent.doubleClick(cell(view.container, action.rowId))
          break
        case 'key':
          fireEvent.keyDown(cell(view.container, fixtureRow(action.rowId).id), { key: action.key })
          break
        case 'grouping':
          grouping = action.grouping
          break
        case 'capacity':
          resize(action.capacity)
          break
        default:
          throw new Error(`undeclared-action-kind: ${action.kind}`)
      }
      view.rerender(<DataGrid {...props()} />)
      expect(state.selectedRowId, transition.id).toEqual(transition.expect.selectedRowId)
      expect(activations, transition.id).toEqual(transition.expect.activations)
      expect(selectedIds(view.container), transition.id).toEqual(transition.expect.ariaSelected)
    }

    // The commit callback is optional: a grid with no host action must not throw on Enter.
    const bare = render(<DataGrid accessibleName="Bare" rows={base.rows as Row[]} columns={columns} getRowId={(row: Row) => row.id} />)
    fireEvent.keyDown(cell(bare.container, selection.validationCases[0].rowId), { key: 'Enter' })
    expect(activations).toEqual(selection.transitions.at(-1).expect.activations)
  })

  it('keeps the selection across lazy collapse and re-expansion and across an equal re-materialised snapshot', () => {
    const lazy = selection.lazyExpansion
    const branch: Row = { id: lazy.branchId, asset: lazy.branchAsset }
    let state: DataGridListState = selection.initial
    const children = () => lazy.childIds.map((id: string) => fixtureRow(id))
    let snapshot: DataGridChildren<Row> = { count: lazy.count, state: 'loaded', children: children() }
    const props = () => ({
      accessibleName: 'Lazy selection',
      rows: [branch],
      columns,
      getRowId: (row: Row) => row.id,
      lazyChildren: { [lazy.branchId]: snapshot },
      onChildrenRequest: () => {},
      listState: state,
      onListStateChange: (next: DataGridListState) => { state = next },
    })
    const view = render(<DataGrid {...props()} />)
    const disclosure = () => view.container.querySelector('[aria-expanded]')!

    fireEvent.keyDown(disclosure(), { key: lazy.expandKey })
    view.rerender(<DataGrid {...props()} />)
    fireEvent.click(cell(view.container, lazy.selectRowId))
    view.rerender(<DataGrid {...props()} />)
    expect(state.selectedRowId).toEqual(lazy.selectRowId)
    expect(selectedIds(view.container)).toEqual([lazy.selectRowId])

    fireEvent.keyDown(disclosure(), { key: lazy.collapseKey })
    view.rerender(<DataGrid {...props()} />)
    expect(state.selectedRowId).toEqual(lazy.selectRowId)
    expect(selectedIds(view.container)).toEqual([])

    // A re-materialised but equal snapshot is a new object with equal values: selection is
    // remembered by row id, so it survives.
    snapshot = { count: lazy.count, state: 'loaded', children: children().map(row => ({ ...row })) }
    fireEvent.keyDown(disclosure(), { key: lazy.expandKey })
    view.rerender(<DataGrid {...props()} />)
    expect(state.selectedRowId).toEqual(lazy.selectRowId)
    expect(selectedIds(view.container)).toEqual([lazy.selectRowId])
  })

  it('restores scroll, selection and the retained column set on the first render after a remount', () => {
    const preserved = selection.preservedListState
    const rows: Row[] = Array.from({ length: preserved.rowCount }, (_, index) => ({ ...fixtureRow(base.rows[0].id), id: `${preserved.idPrefix}${index}` }))
    let state: DataGridListState = selection.initial
    const props = () => ({
      accessibleName: 'Preserved',
      rows,
      columns,
      getRowId: (row: Row) => row.id,
      listState: state,
      onListStateChange: (next: DataGridListState) => { state = next },
    })
    const view = render(<DataGrid {...props()} />)
    Object.defineProperty(view.container.querySelector('.hl-data-grid'), 'clientWidth', { configurable: true, value: preserved.capacity * selection.minimumColumnWidth })
    fireEvent(window, new Event('resize'))
    view.rerender(<DataGrid {...props()} />)
    expect(state.columnWidths).toEqual(preserved.expectedColumnWidths)

    const viewport = view.container.querySelector<HTMLElement>('.hl-data-grid__viewport')!
    viewport.scrollTop = preserved.scrollTop
    fireEvent.scroll(viewport)
    view.rerender(<DataGrid {...props()} />)
    fireEvent.click(cell(view.container, preserved.selectRowId))
    view.rerender(<DataGrid {...props()} />)
    const closed = state
    expect(closed.scrollTop).toEqual(preserved.scrollTop)
    expect(closed.selectedRowId).toEqual(preserved.selectRowId)

    // Closing the inspector unmounts the grid; reopening the page remounts it with the state the
    // host kept. Nothing is measured yet, so the first render is the whole assertion (L1701).
    view.unmount()
    const reopened = render(<DataGrid {...props()} />)
    expect([...reopened.container.querySelectorAll('[role="columnheader"]')].map(header => header.textContent))
      .toEqual(preserved.expectedColumnIds.map((id: string) => base.columns.find((column: { id: string; header: string }) => column.id === id).header))
    expect(reopened.container.querySelector<HTMLElement>('.hl-data-grid__viewport')!.scrollTop).toEqual(preserved.scrollTop)
    expect(reopened.container.querySelector('[data-row-id]')).toHaveAttribute('data-row-id', preserved.expectedFirstRenderedId)
    expect(selectedIds(reopened.container)).toEqual([preserved.selectRowId])
    expect(preserved.scrollTop / DATA_GRID_ROW_HEIGHT).toEqual(Number(preserved.expectedFirstRenderedId.slice(preserved.idPrefix.length)))
  })

  it('republishes measured widths only when their VALUE changes, even when the host re-materialises its columns', () => {
    const preserved = selection.preservedListState
    let state: DataGridListState = selection.initial
    let publishes = 0
    // A host that inlines its column array hands the grid an equal-but-new array on every render.
    // Comparing the measured widths by identity would republish forever.
    const props = () => ({
      accessibleName: 'Re-materialised columns',
      rows: base.rows as Row[],
      columns: base.columns.map((column: DataGridColumnDef<Row>) => ({ ...column, field: column.id })),
      getRowId: (row: Row) => row.id,
      listState: state,
      onListStateChange: (next: DataGridListState) => { publishes += 1; state = next },
    })
    const view = render(<DataGrid {...props()} />)
    Object.defineProperty(view.container.querySelector('.hl-data-grid'), 'clientWidth', { configurable: true, value: preserved.capacity * selection.minimumColumnWidth })
    fireEvent(window, new Event('resize'))
    for (let round = 0; round < 3; round += 1) view.rerender(<DataGrid {...props()} />)
    expect(publishes).toEqual(1)
    expect(state.columnWidths).toEqual(preserved.expectedColumnWidths)
  })
  // Fixture-owned fence: selection changes ONLY on a declared peek gesture or a host-supplied
  // selectedRowId. Every other route that moves focus — the corner keys, an unhandled key, a
  // wheel scroll, a responsive column removal — leaves selectedRowId exactly where it was. One
  // case per declared row so a regression names the route it broke.
  const nonPeek = selection.nonPeekGestures
  const nonPeekRows: Row[] = Array.from({ length: nonPeek.rowCount }, (_, index) => ({ ...fixtureRow(base.rows[0].id), id: `${nonPeek.idPrefix}${index}` }))

  it.each(nonPeek.gestures.map((gesture: Record<string, unknown>) => [gesture.id as string, gesture] as const))(
    'non-peek gesture never changes the selection: %s',
    (_id, gesture: Record<string, unknown>) => {
      const visibleColumnIds = (container: HTMLElement) =>
        [...container.querySelectorAll('.hl-data-grid__row--leaf')[0].querySelectorAll('[data-column-id]')].map(cell => cell.getAttribute('data-column-id'))
      const tabStop = (container: HTMLElement) => {
        const stops = [...container.querySelectorAll<HTMLElement>('.hl-data-grid__cell[tabindex="0"]')]
        if (stops.length > 1) throw new Error(`multiple-tab-stops: ${stops.length}`)
        if (stops.length === 0) return null
        return { rowId: stops[0].closest('[data-row-id]')?.getAttribute('data-row-id') ?? null, columnId: stops[0].getAttribute('data-column-id') }
      }
      const expectedRow = (name: string) => {
        switch (name) {
          case 'same': return nonPeek.startFocusRowId
          case 'first': return `${nonPeek.idPrefix}0`
          case 'last': return `${nonPeek.idPrefix}${nonPeek.rowCount - 1}`
          case 'windowStart': return `${nonPeek.idPrefix}${nonPeek.scrollRowIndex}`
          case 'none': return null
          default: throw new Error(`undeclared-focus-row: ${name}`)
        }
      }
      const expectedColumn = (name: string, ids: (string | null)[]) => {
        switch (name) {
          case 'same': return nonPeek.startFocusColumnId
          case 'first': return ids[0]
          case 'last': return ids[ids.length - 1]
          case 'none': return null
          default: return name
        }
      }

      // The temporal axis of the invariant: no starting selection, a selection on the focused row,
      // and a selection the gesture leaves off screen.
      for (const start of nonPeek.startSelections as (string | null)[]) {
        let state: DataGridListState = { ...selection.initial, selectedRowId: start }
        const props = () => ({
          accessibleName: String(gesture.id),
          rows: nonPeekRows,
          columns,
          getRowId: (row: Row) => row.id,
          listState: state,
          onListStateChange: (next: DataGridListState) => { state = next },
        })
        const view = render(<DataGrid {...props()} />)
        const focused = view.container.querySelector<HTMLElement>(`[data-row-id='${nonPeek.startFocusRowId}'] [data-column-id='${nonPeek.startFocusColumnId}']`)
        if (!focused) throw new Error(`missing-fixture-focus: ${nonPeek.startFocusRowId}/${nonPeek.startFocusColumnId}`)
        fireEvent.focus(focused)
        view.rerender(<DataGrid {...props()} />)

        switch (gesture.kind) {
          case 'key':
            fireEvent.keyDown(focused, { key: gesture.key as string, ctrlKey: gesture.ctrlKey === true })
            break
          case 'scroll': {
            const viewport = view.container.querySelector<HTMLElement>('.hl-data-grid__viewport')!
            if (gesture.focusInside !== true) fireEvent.blur(focused)
            viewport.scrollTop = nonPeek.scrollRowIndex * DATA_GRID_ROW_HEIGHT
            fireEvent.scroll(viewport)
            break
          }
          case 'capacity':
            Object.defineProperty(view.container.querySelector('.hl-data-grid'), 'clientWidth', { configurable: true, value: (gesture.capacity as number) * selection.minimumColumnWidth })
            fireEvent(window, new Event('resize'))
            break
          default:
            throw new Error(`undeclared-gesture-kind: ${gesture.kind}`)
        }
        view.rerender(<DataGrid {...props()} />)

        const label = `${gesture.id} / selection ${String(start)}`
        expect(state.selectedRowId, label).toEqual(start)
        expect(selectedIds(view.container).filter(id => id !== start), label).toEqual([])
        const stop = tabStop(view.container)
        expect(stop?.rowId ?? null, label).toEqual(expectedRow(gesture.expectFocusRow as string))
        if (gesture.expectFocusColumn !== 'none') {
          expect(stop?.columnId ?? null, label).toEqual(expectedColumn(gesture.expectFocusColumn as string, visibleColumnIds(view.container)))
        }
        view.unmount()
      }
    },
  )
})
