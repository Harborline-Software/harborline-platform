import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { DataGrid } from '../DataGrid'
import type { DataGridColumnDef, DataGridValueKind } from '../DataGrid.types'
import { fixture, sharedCases } from './fixtures'

interface Row { readonly id: string; readonly a: string; readonly b: string; readonly c: string }
const getRowId = (row: Row) => row.id
const columns: readonly DataGridColumnDef<Row>[] = [
  { id: 'a', field: 'a', header: 'a', removalPriority: 30 },
  { id: 'b', field: 'b', header: 'b', removalPriority: 20 },
  { id: 'c', field: 'c', header: 'c', removalPriority: 10 },
]

interface SliceRow { readonly id: string; readonly [key: string]: unknown }
interface SliceFixture {
  readonly columns: ReadonlyArray<{ readonly id: string; readonly header: string; readonly removalPriority: number; readonly valueKind: DataGridValueKind }>
  readonly rows: readonly SliceRow[]
  readonly grouping: readonly string[]
  readonly visibleColumnsByCapacity: ReadonlyArray<{ readonly capacity: number; readonly ids: readonly string[] }>
  readonly branchCells: readonly string[]
  readonly dateDistinctnessCases: ReadonlyArray<{ readonly id: string; readonly values: readonly string[]; readonly expected: string }>
  readonly canonicalIdentityCases: ReadonlyArray<{
    readonly id: string
    readonly valueKind: DataGridValueKind
    readonly runtimeType: 'string' | 'number' | 'numberSpecial' | 'bigInteger' | 'dateTimeOffset' | 'dateTime' | 'null'
    readonly value: string | number | null
    readonly expectedIdentity: string
  }>
  readonly aggregateRepresentative: {
    readonly rule: 'first-by-source-order'
    readonly valueKind: DataGridValueKind
    readonly values: readonly string[]
    readonly expectedLabel: string
  }
  readonly focusRecovery: {
    readonly rule: 'nearest-survivor-by-column-order'
    readonly focusedColumnId: string
    readonly afterCapacity: number
    readonly expectedColumnId: string
  }
  readonly groupingIdentityCases: ReadonlyArray<{
    readonly id: string
    readonly valueKind: DataGridValueKind
    readonly runtimeType: 'string' | 'number' | 'dateTimeOffset' | 'dateTime'
    readonly rows: ReadonlyArray<{ readonly id: string; readonly value: string | number }>
    readonly expectedGroupKeys: readonly string[]
  }>
}
const sliceFixture = JSON.parse(readFileSync(resolve(import.meta.dirname, '../../../../../../conformance/hlp.ui.data-grid/slice-1-grid.json'), 'utf8')) as SliceFixture

describe('DataGrid revision-1 shared fixtures', () => {
  it('consumes every frozen case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'data-grid.app-grid-dump',
      'data-grid.app-grouped-report',
      'data-grid.column-order',
      'data-grid.row-replacement',
      'data-grid.group-replacement',
      'data-grid.custom-cell',
      'data-grid.zebra-grouping',
      'data-grid.identity-errors',
      'data-grid.removal-priority-required',
      'data-grid.keyboard-small',
      'data-grid.large-data',
      'data-grid.repeated-update',
      'data-grid.localized-context',
      'data-grid.provider-isolation',
      'data-grid.projection-equivalence',
      'data-grid.lazy-children',
      'data-grid.selection',
    ])
  })

  it('preserves column order and completely replaces stale headers and cells', () => {
    fixture(sharedCases, 'data-grid.column-order')
    fixture(sharedCases, 'data-grid.row-replacement')
    const before = [{ id: 'r1', a: 'old-a', b: 'old-b', c: 'old-c' }, { id: 'r2', a: 'keep-a', b: 'keep-b', c: 'keep-c' }]
    const rendered = render(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} rows={before} />)
    rendered.rerender(<DataGrid accessibleName="Grid" columns={[columns[2], columns[0]]} getRowId={getRowId} rows={[
      before[1],
      { id: 'r3', a: 'new-a', b: 'new-b', c: 'new-c' },
    ]} />)
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['c', 'a'])
    expect(screen.getAllByRole('gridcell').map(cell => cell.textContent)).toEqual(['keep-c', 'keep-a', 'new-c', 'new-a'])
    expect(screen.queryByText('old-a')).toBeNull()
    expect(document.querySelector('[data-row-id="r2"]')).toBeInTheDocument()
  })

  it('replaces grouping and removes stale group and expansion state', () => {
    fixture(sharedCases, 'data-grid.group-replacement')
    const groupedRows = [
      { id: 'r1', a: 'North', b: 'Open', c: '1' },
      { id: 'r2', a: 'South', b: 'Open', c: '2' },
    ]
    const rendered = render(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} grouping={['a', 'b']} rows={groupedRows} />)
    expect(document.querySelectorAll('[data-group-depth="1"]')).toHaveLength(2)
    rendered.rerender(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} grouping={['b']} rows={groupedRows} />)
    expect(document.querySelectorAll('[data-group-id]')).toHaveLength(1)
    expect(document.querySelectorAll('[data-group-depth="1"]')).toHaveLength(0)
    expect(screen.getByRole('gridcell', { name: /Open.*Collapse group/ })).toHaveAttribute('aria-expanded', 'true')
  })

  it('retains a still-valid collapsed group by semantic identity across row replacement', () => {
    const groupedRows = [
      { id: 'r1', a: 'North', b: 'Open', c: '1' },
      { id: 'r2', a: 'South', b: 'Open', c: '2' },
    ]
    const rendered = render(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} grouping={['a']} rows={groupedRows} />)
    fireEvent.click(screen.getByRole('gridcell', { name: /South.*Collapse group/ }))
    rendered.rerender(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} grouping={['a']} rows={[groupedRows[1]]} />)
    expect(screen.getByRole('gridcell', { name: /South.*Expand group/ })).toHaveAttribute('aria-expanded', 'false')
    expect(document.querySelector('[data-row-id="r2"]')).toBeNull()
  })

  it('emits every frozen stable identity and vocabulary error', () => {
    fixture(sharedCases, 'data-grid.identity-errors')
    const row = { id: 'r1', a: 'a', b: 'b', c: 'c' }
    expect(() => render(<DataGrid accessibleName="Grid" columns={columns} getRowId={() => ' '} rows={[row]} />)).toThrow('row-id-required')
    expect(() => render(<DataGrid accessibleName="Grid" columns={columns} getRowId={() => 'same'} rows={[row, row]} />)).toThrow('duplicate-row-id')
    expect(() => render(<DataGrid accessibleName="Grid" columns={[{ ...columns[0], id: ' ' }]} getRowId={getRowId} rows={[row]} />)).toThrow('column-id-required')
    expect(() => render(<DataGrid accessibleName="Grid" columns={[columns[0], columns[0]]} getRowId={getRowId} rows={[row]} />)).toThrow('duplicate-column-id')
    expect(() => render(<DataGrid accessibleName="Grid" columns={[{ ...columns[0], removalPriority: 1.5 }]} getRowId={getRowId} rows={[row]} />)).toThrow('column-removal-priority-required')
    expect(() => render(<DataGrid accessibleName="Grid" columns={columns} getRowId={getRowId} grouping={['missing']} rows={[row]} />)).toThrow('unknown-grouping-column')
    const invalidKind = 'provider-special' as DataGridValueKind
    expect(() => render(<DataGrid accessibleName="Grid" columns={[{ ...columns[0], valueKind: invalidKind }]} getRowId={getRowId} rows={[row]} />)).toThrow('unsupported-value-kind')
  })

  it('rejects a mixed removal-priority declaration from the shared fixture', () => {
    const priorityCase = fixture(sharedCases, 'data-grid.removal-priority-required')
    const mixedColumns = (priorityCase.input.columns as ReadonlyArray<{ readonly id: string; readonly removalPriority?: number }>).map(column => ({
      id: column.id,
      field: 'a' as const,
      header: column.id,
      ...(column.removalPriority === undefined ? {} : { removalPriority: column.removalPriority }),
    })) as readonly DataGridColumnDef<Row>[]

    expect(() => render(<DataGrid accessibleName="Grid" columns={mixedColumns} getRowId={getRowId} rows={[{ id: 'r1', a: 'a', b: 'b', c: 'c' }]} />))
      .toThrow(priorityCase.expected.error as string)
  })

  it('keeps the public surface provider-neutral', () => {
    fixture(sharedCases, 'data-grid.provider-isolation')
    const publicFiles = ['DataGrid.types.ts', 'index.ts'].map(name => readFileSync(resolve(import.meta.dirname, `../${name}`), 'utf8')).join('\n')
    expect(publicFiles).not.toMatch(/tanstack|telerik|virtualizer|providerInstance/i)
    expect(Object.keys(columns[0])).toEqual(['id', 'field', 'header', 'removalPriority'])
  })

  it('applies the shared slice-1 priority and mixed aggregate fixture', () => {
    const sliceColumns: readonly DataGridColumnDef<SliceRow>[] = sliceFixture.columns.map(column => ({
      ...column,
      field: column.id,
    }))
    const rendered = render(<DataGrid accessibleName="Slice 1" columns={sliceColumns} getRowId={row => row.id} grouping={sliceFixture.grouping} rows={sliceFixture.rows} />)
    const grid = rendered.container.querySelector('.hl-data-grid') as HTMLElement
    for (const expectation of sliceFixture.visibleColumnsByCapacity) {
      Object.defineProperty(grid, 'clientWidth', { configurable: true, value: expectation.capacity * 160 })
      fireEvent(window, new Event('resize'))
      expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(
        expectation.ids.map(id => sliceFixture.columns.find(column => column.id === id)?.header),
      )
    }

    Object.defineProperty(grid, 'clientWidth', { configurable: true, value: sliceFixture.visibleColumnsByCapacity[0].capacity * 160 })
    fireEvent(window, new Event('resize'))
    expect([...rendered.container.querySelectorAll('[data-group-id] .hl-data-grid__group-cell-content')].map(cell => cell.textContent?.trim())).toEqual(sliceFixture.branchCells)
  })

  it('compares declared date values by instant from the shared fixture', () => {
    const dateColumn = sliceFixture.columns.find(column => column.valueKind === 'date')!
    for (const dateCase of sliceFixture.dateDistinctnessCases) {
      const rows = dateCase.values.map((value, index) => ({ id: `${dateCase.id}-${index}`, asset: dateCase.id, [dateColumn.id]: value }))
      const columnsForCase: readonly DataGridColumnDef<SliceRow>[] = [
        { ...sliceFixture.columns[0], field: sliceFixture.columns[0].id },
        { ...dateColumn, field: dateColumn.id },
      ]
      const rendered = render(<DataGrid accessibleName={dateCase.id} columns={columnsForCase} getRowId={row => row.id} grouping={[sliceFixture.columns[0].id]} rows={rows} />)
      const text = rendered.container.querySelector(`[data-group-id] [data-column-id="${dateColumn.id}"]`)?.textContent?.trim()
      expect(text?.startsWith('mixed —') ? text : 'single-valued').toBe(dateCase.expected)
      rendered.unmount()
    }
  })

  it('matches every fixture-owned projection grouping identity', () => {
    for (const identityCase of sliceFixture.groupingIdentityCases) {
      const rows = identityCase.rows.map(row => ({
        id: row.id,
        value: identityCase.runtimeType === 'dateTime' || identityCase.runtimeType === 'dateTimeOffset'
          ? new Date(row.value)
          : row.value,
      }))
      const identityColumn: DataGridColumnDef<(typeof rows)[number]> = {
        id: 'value',
        field: 'value',
        header: 'Value',
        removalPriority: 1,
        valueKind: identityCase.valueKind,
      }
      const rendered = render(<DataGrid accessibleName={identityCase.id} columns={[identityColumn]} getRowId={row => row.id} grouping={[identityColumn.id]} rows={rows} />)
      const actual = [...rendered.container.querySelectorAll('[data-group-id]')].map(group => group.getAttribute('data-group-id'))

      expect(actual).toHaveLength(identityCase.expectedGroupKeys.length)
      expect(actual).toEqual(identityCase.expectedGroupKeys)
      rendered.unmount()
    }
  })

  it('maps every fixture scalar to its canonical identity', () => {
    for (const identityCase of sliceFixture.canonicalIdentityCases) {
      const value = identityCase.runtimeType === 'numberSpecial'
        ? Number.NaN
        : identityCase.runtimeType === 'bigInteger'
          ? Number(identityCase.value)
        : identityCase.runtimeType === 'dateTime' || identityCase.runtimeType === 'dateTimeOffset'
          ? new Date(identityCase.value as string)
          : identityCase.value
      const identityColumn: DataGridColumnDef<{ readonly id: string; readonly value: unknown }> = {
        id: 'value',
        field: 'value',
        header: 'Value',
        removalPriority: 1,
        valueKind: identityCase.valueKind,
      }
      const rendered = render(<DataGrid accessibleName={identityCase.id} columns={[identityColumn]} getRowId={row => row.id} grouping={[identityColumn.id]} rows={[{ id: identityCase.id, value }]} />)
      const expected = `root/group:0:5:value:${identityCase.expectedIdentity.length}:${identityCase.expectedIdentity}`

      expect(rendered.container.querySelector('[data-group-id]')).toHaveAttribute('data-group-id', expected)
      rendered.unmount()
    }
  })

  it('uses the fixture-owned first source value as the aggregate representative', () => {
    expect(sliceFixture.aggregateRepresentative.rule).toBe('first-by-source-order')
    const rows = sliceFixture.aggregateRepresentative.values.map((value, index) => ({ id: `representative-${index}`, group: 'same', value }))
    const representativeColumns: readonly DataGridColumnDef<(typeof rows)[number]>[] = [
      { id: 'group', field: 'group', header: 'Group', removalPriority: 2, valueKind: 'text' },
      { id: 'value', field: 'value', header: 'Value', removalPriority: 1, valueKind: sliceFixture.aggregateRepresentative.valueKind },
    ]
    const rendered = render(<DataGrid accessibleName="Representative" columns={representativeColumns} getRowId={row => row.id} grouping={['group']} rows={rows} />)

    expect(rendered.container.querySelector('[data-group-id] [data-column-id="value"]')?.textContent?.trim()).toBe(sliceFixture.aggregateRepresentative.expectedLabel)
  })

  it('recovers focus to the fixture-owned nearest surviving column', async () => {
    expect(sliceFixture.focusRecovery.rule).toBe('nearest-survivor-by-column-order')
    const sliceColumns: readonly DataGridColumnDef<SliceRow>[] = sliceFixture.columns.map(column => ({ ...column, field: column.id }))
    const rendered = render(<DataGrid accessibleName="Focus recovery" columns={sliceColumns} getRowId={row => row.id} rows={sliceFixture.rows} />)
    const grid = rendered.container.querySelector('.hl-data-grid') as HTMLElement
    Object.defineProperty(grid, 'clientWidth', { configurable: true, value: sliceFixture.visibleColumnsByCapacity[0].capacity * 160 })
    fireEvent(window, new Event('resize'))
    const focused = rendered.container.querySelector(`[data-row-id="a1"] [data-column-id="${sliceFixture.focusRecovery.focusedColumnId}"]`) as HTMLElement
    focused.focus()

    Object.defineProperty(grid, 'clientWidth', { configurable: true, value: sliceFixture.focusRecovery.afterCapacity * 160 })
    fireEvent(window, new Event('resize'))

    await waitFor(() => expect(rendered.container.querySelector(`[data-row-id="a1"] [data-column-id="${sliceFixture.focusRecovery.expectedColumnId}"]`)).toHaveFocus())
  })

  it('recovers every focused column for every survivor set through resize and replacement', async () => {
    expect(sliceFixture.focusRecovery.rule).toBe('nearest-survivor-by-column-order')
    const allColumns: readonly DataGridColumnDef<SliceRow>[] = sliceFixture.columns.map(column => ({ ...column, field: column.id }))
    const focusOrder = [3, 0, 1, 2]
    const survivorMasks = [0b0101, ...Array.from({ length: 15 }, (_, index) => index + 1).filter(mask => mask !== 0b0101)]

    for (const focusedIndex of focusOrder) {
      for (const survivorMask of survivorMasks) {
        const survivors = allColumns.filter((_, index) => (survivorMask & (1 << index)) !== 0)
        const focusedId = allColumns[focusedIndex].id
        const expectedId = survivors.some(column => column.id === focusedId)
          ? focusedId
          : survivors[Math.min(focusedIndex, survivors.length - 1)].id

        for (const entryPoint of ['replacement', 'resize'] as const) {
          const resizeColumns = allColumns.map((column, index) => ({
            ...column,
            removalPriority: (survivorMask & (1 << index)) !== 0 ? 1000 - index : -index,
          }))
          const rendered = render(<DataGrid accessibleName="Focus invariant" columns={entryPoint === 'resize' ? resizeColumns : allColumns} getRowId={row => row.id} rows={sliceFixture.rows} />)
          const focused = rendered.container.querySelector(`[data-row-id="a1"] [data-column-id="${focusedId}"]`) as HTMLElement
          focused.focus()

          if (entryPoint === 'replacement') {
            rendered.rerender(<DataGrid accessibleName="Focus invariant" columns={survivors} getRowId={row => row.id} rows={sliceFixture.rows} />)
          } else {
            const grid = rendered.container.querySelector('.hl-data-grid') as HTMLElement
            Object.defineProperty(grid, 'clientWidth', { configurable: true, value: survivors.length * 160 })
            fireEvent(window, new Event('resize'))
          }

          const expected = rendered.container.querySelector(`[data-row-id="a1"] [data-column-id="${expectedId}"]`)
          await waitFor(() => {
            expect(expected).toHaveAttribute('tabindex', '0')
            expect(expected).toHaveFocus()
          })
          rendered.unmount()
        }
      }
    }
  })

})
