import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fireEvent, render } from '@testing-library/react'
import { it, expect } from 'vitest'
import { DataGrid } from '../DataGrid'
import { childrenSnapshotKey, completesChildrenRequest, completesChildrenRequestKey } from '../data-grid-model'
import type { DataGridChildren } from '../DataGrid.types'
const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-grid/lazy-children-v1.json'), 'utf8'))
const base = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-grid', fixture.staticFixture), 'utf8'))
const columns = base.columns.map((c: any) => ({ ...c, field: c.id }))
// Re-materialise a snapshot as a fresh object graph — new snapshot object, new array, new row
// objects with equal values — the shape a host produces on any JSON round-trip or immutable
// rebuild. Nothing in the result is reference-equal to its input.
const rematerialise = (state: DataGridChildren<any>): DataGridChildren<any> =>
  ({ count: state.count, state: state.state, children: state.children.map((child: any) => structuredClone({ ...child })) })
for (const initial of ['unloaded', 'failed'] as const)
for (const activation of fixture.activation)
for (const replacement of ['count-only', 'same-state', 'collapse/re-expand', 'independent-branch'])
it(`request lifetime completes only on a distinguishable snapshot VALUE, never object identity: ${initial}, ${JSON.stringify(activation)}, ${replacement}`, () => {
  const events: string[] = []
  let state: DataGridChildren<any> = { count: fixture.count, state: initial, children: [] }
  let other: DataGridChildren<any> = { count: 1, state: 'unloaded', children: [] }
  const props = () => ({ accessibleName: 'Lifetime', rows: [fixture.branch, fixture.otherBranch], columns,
    getRowId: (r: any) => r.id, lazyChildren: { rig: state, deck: other }, onChildrenRequest: (r: {rowId: string}) => events.push(r.rowId) })
  const view = render(<DataGrid {...props()} />)
  const cell = (id = 'rig') => view.container.querySelector(`[data-group-id='row:${id}'] [aria-expanded]`)!
  const activate = (id = 'rig') => activation === 'click' ? fireEvent.click(cell(id)) : fireEvent.keyDown(cell(id), { key: activation })
  const again = (id = 'rig') => { fireEvent.keyDown(cell(id), { key: 'ArrowLeft' }); activate(id) }
  activate()
  for (let refresh = 0; refresh < 3; refresh++) {
    if (replacement === 'count-only') state = rematerialise({ ...state, count: state.count + 1 })
    if (replacement === 'same-state') state = rematerialise(state)
    if (replacement === 'independent-branch') {
      if (refresh === 0) activate('deck')
      other = rematerialise({ ...other, count: other.count + 1 })
      state = rematerialise(state)
    }
    view.rerender(<DataGrid {...props()} />)
    again()
    expect(events.filter(id => id === 'rig')).toHaveLength(1)
    if (replacement === 'independent-branch') { again('deck'); expect(events.filter(id => id === 'deck')).toHaveLength(1) }
    expect(cell().closest('[data-group-id]')).toHaveAttribute('data-children-state', 'loading')
  }
  // New retained content distinguishes a failed response from a count refresh.
  state = { ...state, state: 'failed', children: [base.rows[0]] }
  view.rerender(<DataGrid {...props()} />)
  again()
  expect(events.filter(id => id === 'rig')).toHaveLength(2)
  // A re-materialised but EQUAL failed snapshot cannot finish the pending retry.
  state = rematerialise(state)
  view.rerender(<DataGrid {...props()} />); again()
  expect(events.filter(id => id === 'rig')).toHaveLength(2)
  expect(cell().closest('[data-group-id]')).toHaveAttribute('data-children-state', 'loading')
  // A count change carrying re-materialised equal children is still not a response.
  state = rematerialise({ ...state, count: state.count + 1 })
  view.rerender(<DataGrid {...props()} />); again()
  expect(events.filter(id => id === 'rig')).toHaveLength(2)
  // An equal snapshot arriving during loading neither completes nor cancels the request.
  state = rematerialise({ ...state, state: 'loading' })
  view.rerender(<DataGrid {...props()} />)
  state = rematerialise(state)
  view.rerender(<DataGrid {...props()} />); again()
  expect(events.filter(id => id === 'rig')).toHaveLength(2)
  expect(cell().closest('[data-group-id]')).toHaveAttribute('data-children-state', 'loading')
  // failed -> loading -> failed with re-materialised EQUAL children: the move away from the
  // observed loading snapshot is itself distinguishable, so the retry completes exactly once.
  state = rematerialise({ ...state, state: 'failed' })
  view.rerender(<DataGrid {...props()} />); again()
  expect(events.filter(id => id === 'rig')).toHaveLength(3)
  state = { ...state, state: 'loaded', children: [base.rows[1]] }
  view.rerender(<DataGrid {...props()} />)
  again()
  expect(events.filter(id => id === 'rig')).toHaveLength(3)
  state = { ...state, state: initial, children: [] }
  view.rerender(<DataGrid {...props()} />)
  again()
  expect(events.filter(id => id === 'rig')).toHaveLength(4)
  // Removal completes the request even though the branch comes back with EQUAL children.
  view.rerender(<DataGrid {...props()} rows={[fixture.otherBranch]} lazyChildren={{ deck: other }} />)
  state = rematerialise(state)
  view.rerender(<DataGrid {...props()} />)
  activate()
  expect(events.filter(id => id === 'rig')).toHaveLength(5)
  view.unmount()
})
it('count-only refresh cannot finish a pending retry', () => {
  const events: unknown[] = []
  let state = { count: fixture.count, state: 'failed' as const, children: [] }
  const props = () => ({ accessibleName: 'Invariant', rows: [fixture.branch], columns, getRowId: (r: any) => r.id, lazyChildren: { [fixture.branch.id]: state }, onChildrenRequest: (r: unknown) => events.push(r) })
  const view = render(<DataGrid {...props()} />)
  fireEvent.keyDown(view.container.querySelector('[aria-expanded]')!, { key: 'ArrowRight' })
  state = { ...state, count: fixture.countReplacement }
  view.rerender(<DataGrid {...props()} />)
  fireEvent.keyDown(view.container.querySelector('[aria-expanded]')!, { key: 'ArrowRight' })
  expect(events).toEqual([JSON.parse(fixture.eventJson)])
})
it('static ancestor keeps first LOADED representative in source order', () => {
  for (const example of fixture.sourceOrderCases) {
    const view = render(<DataGrid accessibleName="Source order" {...example} columns={columns} getRowId={(r: any) => r.id} onChildrenRequest={() => {}} />)
    expect([...view.container.querySelectorAll('[data-group-id^="root/"] [data-column-id="due"]')].map(cell => cell.textContent)).toEqual(example.expectedAncestorDue)
    view.unmount()
  }
})

// One row per source permutation (27 substitutions each) with its own budget, rather than one
// giant test raised above the default: a per-row timeout keeps a slow machine from failing the
// whole property.
it.each([[[0, 1, 2]], [[0, 2, 1]], [[1, 0, 2]], [[1, 2, 0]], [[2, 0, 1]], [[2, 1, 0]]])(
  'every static ancestor aggregate follows independent source traversal with loaded leaf substitution: order %j',
  (order: number[]) => {
  for (const example of fixture.sourceOrderCases) for (let mask = 0; mask < 27; mask++) {
    const rows = order.map(index => example.rows[index])
    const states: Record<string, DataGridChildren<any>> = {}
    rows.forEach((row, index) => {
      const mode = Math.floor(mask / 3 ** index) % 3
      if (mode === 0) return
      const nested = { ...row, id: `${row.id}-nested` }
      states[row.id] = { count: 612, state: mode === 1 ? 'unloaded' : 'loaded', children: mode === 1 ? [] : [nested, { ...row, id: `${row.id}-last` }] }
      if (mode === 2) states[nested.id] = { count: 100, state: 'loaded', children: [{ ...row, id: `${row.id}-first` }] }
    })
    // This oracle never reads prepared groups or presentation order.
    const leaves = (source: any[]): any[] => source.flatMap(row => states[row.id] ? leaves([...states[row.id].children]) : [row])
    const expected: string[][] = []
    const visit = (source: any[], depth: number) => {
      if (depth === example.grouping.length) return
      for (const value of new Set(source.map(row => row[example.grouping[depth]]))) {
        const members = source.filter(row => row[example.grouping[depth]] === value)
        const loaded = leaves(members)
        expected.push(columns.slice(1).map((column: any) => {
          const values = loaded.map(row => row[column.id])
          const identities = new Set(values.map(value => column.valueKind === 'date' ? Date.parse(value) : value))
          return identities.size > 1 ? `mixed — ${identities.size} ${column.valueKind}` : String(values[0] ?? '')
        }))
        visit(members, depth + 1)
      }
    }
    visit(rows, 0)
    const view = render(<DataGrid accessibleName="Aggregate property" rows={rows} columns={columns} getRowId={(r: any) => r.id}
      grouping={example.grouping} lazyChildren={states} onChildrenRequest={() => {}} />)
    expect([...view.container.querySelectorAll('[data-group-id^="root/"]')].map(group => [...group.querySelectorAll('[data-column-id]')].map(cell => cell.textContent)), `order=${order}, mask=${mask}`).toEqual(expected)
    view.unmount()
  }
}, 60000)


// Unit test for the model-layer structural comparison that decides completion.
const rowId = (row: { id: string }) => row.id
const snapshot = (state: DataGridChildren<any>['state'], ids: string[], count = 3): DataGridChildren<any> =>
  ({ count, state, children: ids.map(id => ({ id, label: `label-${id}` })) })
it('completesChildrenRequest compares VALUE, never object identity', () => {
  const observed = snapshot('failed', ['a', 'b'])
  // Re-materialised equal snapshot: every object is new, the value is the same.
  expect(completesChildrenRequest(rematerialise(observed), observed, rowId)).toBe(false)
  // Reference-identical snapshot.
  expect(completesChildrenRequest(observed, observed, rowId)).toBe(false)
  // Count alone is not a response.
  expect(completesChildrenRequest(rematerialise({ ...observed, count: observed.count + 9 }), observed, rowId)).toBe(false)
  // Loading is never a completion, however different its children.
  expect(completesChildrenRequest(snapshot('loading', ['z']), observed, rowId)).toBe(false)
  // Distinguishable VALUES: state, membership, order, length.
  expect(completesChildrenRequest(snapshot('loaded', ['a', 'b']), observed, rowId)).toBe(true)
  expect(completesChildrenRequest(snapshot('failed', ['a', 'c']), observed, rowId)).toBe(true)
  expect(completesChildrenRequest(snapshot('failed', ['b', 'a']), observed, rowId)).toBe(true)
  expect(completesChildrenRequest(snapshot('failed', ['a']), observed, rowId)).toBe(true)
  expect(completesChildrenRequest(snapshot('failed', ['a', 'b', 'c']), observed, rowId)).toBe(true)
})

it('a request completes when the host mutates the handed-over children array in place (key, not object, is remembered)', () => {
  const rowId = (row: { id: string }) => row.id
  const live = [{ id: 'a' }]
  const observedKey = childrenSnapshotKey({ count: 1, state: 'failed', children: live }, rowId)
  live.push({ id: 'b' })
  const aliased = { count: 1, state: 'failed' as const, children: live }
  expect(completesChildrenRequest(aliased, aliased, rowId)).toBe(false)
  expect(completesChildrenRequestKey(aliased, observedKey, rowId)).toBe(true)
})
