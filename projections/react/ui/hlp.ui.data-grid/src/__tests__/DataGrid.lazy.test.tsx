import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { act, fireEvent, render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DataGrid } from '../DataGrid'
import type { DataGridChildren, DataGridColumnDef } from '../DataGrid.types'

const read = (name: string) => JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-grid', name), 'utf8'))
const lazy = read('lazy-children-v1.json')
const base = read(lazy.staticFixture)
type Row = { id: string; [key: string]: unknown }
const columns: DataGridColumnDef<Row>[] = base.columns.map((column: DataGridColumnDef<Row>) => ({ ...column, field: column.id }))

describe('fixture-owned lazy children transitions', () => {
  it('replays interleaved requests, collapsed responses, nested rows and retained failure children', () => {
    const events: string[] = []
    const allRows: Row[] = [lazy.branch, lazy.otherBranch, ...base.rows]
    const row = (id: string) => allRows.find(row => row.id === id)!
    let host = { rows: [] as Row[], lazyChildren: {} as Record<string, DataGridChildren<Row>> }
    const props = () => ({ ...host, columns, accessibleName: 'Interleaving', getRowId: (row: Row) => row.id, onChildrenRequest: (event: {rowId: string}) => events.push(event.rowId) })
    const view = render(<DataGrid {...props()} />)
    for (const step of lazy.interleaving) {
      if (step.host) {
        host = { rows: step.host.rows.map(row), lazyChildren: Object.fromEntries(Object.entries(step.host.lazyChildren).map(([id, value]) => {
          const state = value as DataGridChildren<string>
          return [id, { ...state, children: state.children.map(row) }]
        })) }
        view.rerender(<DataGrid {...props()} />)
      } else fireEvent.keyDown(view.container.querySelector(`[data-group-id='row:${step.rowId}'] [aria-expanded]`)!, { key: step.key })
      expect([...view.container.querySelectorAll('[data-row-id]')].map(row => row.getAttribute('data-row-id'))).toEqual(step.visibleIds)
      expect(events).toEqual(step.events)
      if (step.alert) expect(view.container.querySelector('[role="alert"]')).toHaveTextContent(lazy.failedLabel)
    }
    expect([...view.container.querySelector('[data-group-id="row:rig"]')!.querySelectorAll('[data-aggregate-scope]')].map(cell => cell.textContent)).toEqual(base.branchCells.slice(1))
    host = {
      rows: [lazy.branch],
      lazyChildren: { [lazy.branch.id]: { count: lazy.count, state: 'loaded',
        children: Array.from({ length: lazy.window.childCount }, (_, index) => ({ ...base.rows[0], id: `${lazy.window.idPrefix}${index}` })),
      } },
    }
    view.rerender(<DataGrid {...props()} />)
    fireEvent.keyDown(view.container.querySelector('[aria-expanded]')!, { key: 'End', ctrlKey: true })
    expect(view.container.querySelector('[role="treegrid"]')).toHaveAttribute('aria-rowcount', String(lazy.window.rowCount))
    expect(Number(view.container.querySelector('[data-rendered-row-count]')!.getAttribute('data-rendered-row-count'))).toBeLessThanOrEqual(lazy.window.maxRendered)
    expect(document.activeElement?.closest('[data-row-id]')).toHaveAttribute('data-row-id', lazy.window.lastId)
  })

  it('rejects fixture-owned invalid states and missing callbacks', () => {
    for (const id of lazy.ordinaryRowIds) {
      const view = render(<DataGrid accessibleName="Ordinary IDs" rows={[{ ...lazy.branch, id }]} columns={columns} getRowId={row => row.id} />)
      expect(view.container.querySelector('[data-row-id]')).toHaveAttribute('data-row-id', id)
      view.unmount()
    }
    for (const invalid of lazy.validationCases) expect(() => render(<DataGrid accessibleName="Invalid" rows={[lazy.branch]} columns={columns} getRowId={row => row.id}
      lazyChildren={{ [lazy.branch.id]: { ...invalid, children: [] } }} onChildrenRequest={() => {}} />)).toThrow(invalid.expected)
    expect(() => render(<DataGrid accessibleName="Invalid" rows={[lazy.branch]} columns={columns} getRowId={row => row.id}
      lazyChildren={{ [lazy.branch.id]: { count: lazy.count, state: 'unloaded', children: [] } }} />)).toThrow('children-request-required')
  })

  for (const entryPoint of lazy.focusEntryPoints) for (const activation of lazy.activation) it(`replays every state and ${entryPoint} focus via ${JSON.stringify(activation)}`, async () => {
    const events: string[] = []
    const snapshot = (transition: DataGridChildren<string>): DataGridChildren<Row> => ({ count: lazy.count, state: transition.state,
      children: transition.children.map(id => { const row = base.rows.find((row: Row) => row.id === id); if (!row) throw new Error(`Missing fixture row: ${id}`); return row }) })
    let state = snapshot(lazy.transitions[0])
    const props = () => ({ accessibleName: 'Lazy', rows: [lazy.branch], columns, getRowId: (row: Row) => row.id,
      lazyChildren: { [lazy.branch.id]: state }, onChildrenRequest: (event: unknown) => events.push(JSON.stringify(event)) })
    const view = render(<DataGrid {...props()} />)
    const resize = (capacity: number) => {
      Object.defineProperty(view.container.querySelector('.hl-data-grid'), 'clientWidth', { configurable: true, value: capacity * 160 })
      fireEvent(window, new Event('resize'))
    }
    const branch = () => view.container.querySelector('[data-group-id]')!
    const disclosure = () => branch().querySelector('[aria-expanded]')!
    const activate = () => activation === 'click' ? fireEvent.click(disclosure()) : fireEvent.keyDown(disclosure(), { key: activation })
    const validateTransition = () => {
      expect(branch()).toHaveAttribute('data-children-state', state.state)
      expect([...view.container.querySelectorAll('[data-row-id]')].map(row => row.getAttribute('data-row-id')))
        .toEqual(disclosure().getAttribute('aria-expanded') === 'true' ? state.children.map(row => row.id) : [])
      if (state.state === 'loading') expect(branch().textContent).toContain(lazy.loadingLabel)
      if (state.state === 'failed') expect(branch().textContent).toContain(lazy.failedLabel)
    }
    validateTransition()
    expect(disclosure()).toHaveAttribute('aria-expanded', 'false')
    expect(disclosure().textContent).toContain(`(${lazy.count})`)
    state = { ...state, count: lazy.countReplacement }; view.rerender(<DataGrid {...props()} />)
    expect(disclosure().textContent).toContain(`(${lazy.countReplacement})`)
    expect(events).toEqual([])
    activate()
    expect(events).toEqual([lazy.eventJson])
    expect(branch().textContent).toContain(lazy.loadingLabel)
    fireEvent.keyDown(disclosure(), { key: 'ArrowRight' })
    expect(events).toHaveLength(1)
    for (const transition of lazy.transitions.slice(1)) {
      state = { ...snapshot(transition), count: state.count }
      view.rerender(<DataGrid {...props()} />)
      validateTransition()
      if (transition.state !== 'loaded') continue
      expect([...view.container.querySelectorAll('[data-row-id]')].map(row => row.getAttribute('data-row-id'))).toEqual(lazy.expectedLoadedIds)
      for (const expected of base.visibleColumnsByCapacity) {
        const retained = columns.filter(column => expected.ids.includes(column.id))
        if (entryPoint === 'resize') resize(expected.capacity)
        else view.rerender(<DataGrid {...props()} columns={retained} />)
        expect([...view.container.querySelectorAll('[role="columnheader"]')].map(cell => cell.textContent)).toEqual(retained.map(column => column.header))
        expect([...branch().querySelectorAll('[data-aggregate-scope]')].map(cell => cell.textContent)).toEqual(expected.ids.slice(1).map((id: string) => base.branchCells[columns.findIndex(column => column.id === id)]))
        expect(view.container.textContent).toContain(lazy.aggregateLabel)
      }
      view.rerender(<DataGrid {...props()} />); resize(columns.length)
      const cell = view.container.querySelector(`[data-row-id='${lazy.expectedLoadedIds[0]}'] [data-column-id='${base.focusRecovery.focusedColumnId}']`) as HTMLElement
      act(() => cell.focus())
      const retained = base.visibleColumnsByCapacity.find((item: {capacity: number}) => item.capacity === base.focusRecovery.afterCapacity).ids
      if (entryPoint === 'resize') resize(base.focusRecovery.afterCapacity)
      else view.rerender(<DataGrid {...props()} columns={columns.filter(column => retained.includes(column.id))} />)
      expect(document.activeElement).toHaveAttribute('data-column-id', base.focusRecovery.expectedColumnId)
    }
    fireEvent.keyDown(disclosure(), { key: 'ArrowLeft' }); activate()
    expect(events).toEqual([lazy.eventJson, lazy.eventJson])
    view.rerender(<DataGrid {...props()} rows={[]} lazyChildren={{}} />)
    expect(view.container.querySelector('[data-group-id]')).toBeNull()
    view.rerender(<DataGrid {...props()} />)
    expect(disclosure()).toHaveAttribute('aria-expanded', 'false')
  })
})
