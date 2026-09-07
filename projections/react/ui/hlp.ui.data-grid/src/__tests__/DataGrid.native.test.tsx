import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { DataGrid } from '../DataGrid'
import type { DataGridColumnDef } from '../DataGrid.types'
import { qualityCases } from './fixtures'

interface Row {
  readonly id: string
  readonly property: string
  readonly status: string
  readonly amount: number
}

const rows: readonly Row[] = [
  { id: 'r1', property: 'Palm', status: 'active', amount: 10 },
  { id: 'r2', property: 'Palm', status: 'paused', amount: 20 },
  { id: 'r3', property: 'Bay', status: 'active', amount: 30 },
]

const columns: readonly DataGridColumnDef<Row>[] = [
  { id: 'property', field: 'property', header: 'Property', removalPriority: 30 },
  { id: 'status', field: 'status', header: 'Status', removalPriority: 20 },
  { id: 'amount', field: 'amount', header: 'Amount', removalPriority: 10, valueKind: 'number' },
]

const getRowId = (row: Row) => row.id

describe('DataGrid React projection', () => {
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'data-grid.quality.relationships',
      'data-grid.quality.row-metadata',
      'data-grid.quality.reflow',
      'data-grid.quality.labels',
      'data-grid.quality.pseudo',
      'data-grid.quality.light-dark',
      'data-grid.quality.tokens',
      'data-grid.quality.forced-colors',
      'data-grid.quality.visual-parity',
      'data-grid.quality.navigation',
      'data-grid.quality.group-activation',
      'data-grid.quality.roving-focus',
      'data-grid.quality.window-focus',
      'data-grid.quality.rtl',
      'data-grid.quality.reduced-motion',
      'data-grid.quality.identity-state',
    ])
  })

  it('renders a named accessible grid with ordered headers and row metadata', () => {
    render(<DataGrid accessibleName="Assets" columns={columns} getRowId={getRowId} rows={rows} />)
    const grid = screen.getByRole('grid', { name: 'Assets' })
    expect(grid).toHaveAttribute('aria-rowcount', '3')
    expect(grid).toHaveAttribute('aria-colcount', '3')
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Property', 'Status', 'Amount'])
    expect(screen.getAllByRole('row').slice(1).map(row => row.getAttribute('aria-rowindex'))).toEqual(['1', '2', '3'])
    expect(screen.getAllByRole('gridcell', { name: 'Palm' })[0].getAttribute('aria-describedby')).toMatch(/-column-0$/)
  })

  it('keeps custom cell rendering projection-native and zebra counts leaf rows only', () => {
    const customColumns: readonly DataGridColumnDef<Row>[] = columns.map(column => column.id === 'status'
      ? { ...column, renderCell: ({ value }) => <span data-tone="success">{value === 'active' ? 'Active' : 'Paused'}</span> }
      : column)
    render(<DataGrid accessibleName="Assets" columns={customColumns} getRowId={getRowId} grouping={['property']} rows={rows} zebra />)
    expect(screen.getByRole('treegrid', { name: 'Assets' })).toBeInTheDocument()
    expect(screen.getAllByRole('gridcell', { name: 'Active' })).toHaveLength(2)
    expect(document.querySelectorAll('[data-group-id]')).toHaveLength(2)
    expect(document.querySelectorAll('.hl-data-grid__disclosure svg')).toHaveLength(2)
    expect(document.querySelector('.hl-data-grid__disclosure')).toHaveAttribute('data-collapsed', 'false')
    expect(document.querySelector('.hl-data-grid__disclosure')).not.toHaveTextContent(/[▸▾]/)
    expect(document.querySelectorAll('[data-zebra-stripe="alternate"]')).toHaveLength(1)
    expect(document.querySelector('[data-group-id]')).not.toHaveAttribute('data-zebra-stripe')
  })

  it('expands and collapses groups with Enter and removes hidden descendants', async () => {
    const user = userEvent.setup()
    render(<DataGrid accessibleName="Assets" columns={columns} getRowId={getRowId} grouping={['property']} rows={rows} />)
    const groupCell = screen.getByRole('gridcell', { name: /Palm.*Collapse group/ })
    act(() => groupCell.focus())
    await user.keyboard('{Enter}')
    expect(groupCell).toHaveAttribute('aria-expanded', 'false')
    expect(groupCell.querySelector('.hl-data-grid__disclosure')).toHaveAttribute('data-collapsed', 'true')
    expect(document.querySelectorAll('[data-row-id="r1"], [data-row-id="r2"]')).toHaveLength(0)
    await user.keyboard('{Enter}')
    expect(groupCell).toHaveAttribute('aria-expanded', 'true')
    expect(document.querySelectorAll('[data-row-id="r1"], [data-row-id="r2"]')).toHaveLength(2)
  })

  it('supports roving arrows, row Home/End, and Ctrl corner navigation', async () => {
    const user = userEvent.setup()
    render(<DataGrid accessibleName="Assets" columns={columns} getRowId={getRowId} rows={rows} />)
    const first = screen.getAllByRole('gridcell')[0]
    await user.tab()
    expect(first).toHaveFocus()
    await user.keyboard('{ArrowRight}{ArrowDown}')
    expect(screen.getByRole('gridcell', { name: 'paused' })).toHaveFocus()
    await user.keyboard('{End}')
    expect(screen.getByRole('gridcell', { name: '20' })).toHaveFocus()
    await user.keyboard('{Home}')
    expect(screen.getAllByRole('gridcell', { name: 'Palm' })[1]).toHaveFocus()
    await user.keyboard('{Control>}{End}{/Control}')
    expect(screen.getByRole('gridcell', { name: '30' })).toHaveFocus()
    await user.keyboard('{Control>}{Home}{/Control}')
    expect(first).toHaveFocus()
    expect(document.querySelectorAll('[role="gridcell"][tabindex="0"]')).toHaveLength(1)
  })

  it('uses locale direction and localized empty content without reordering labels', () => {
    render(
      <HarborlineLocaleProvider catalog={{ 'dataGrid.noResults': 'لا توجد نتائج.' }} locale="ar-SA">
        <DataGrid accessibleName="الأصول" columns={columns} getRowId={getRowId} rows={[]} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('grid', { name: 'الأصول' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Property', 'Status', 'Amount'])
    expect(screen.getByRole('gridcell', { name: 'لا توجد نتائج.' })).toBeInTheDocument()
  })

  it('recovers the tab stop when the visible scroll window changes', () => {
    const manyRows = Array.from({ length: 200 }, (_, index) => ({ ...rows[0], id: `r${index}`, property: `P${index}` }))
    render(<DataGrid accessibleName="Assets" columns={columns} getRowId={getRowId} rows={manyRows} />)
    const first = screen.getAllByRole('gridcell')[0]
    fireEvent.focus(first)
    const viewport = document.querySelector('.hl-data-grid__viewport') as HTMLDivElement
    fireEvent.scroll(viewport, { target: { scrollTop: 44 * 100 } })
    expect(document.querySelector('[data-row-id="r100"] [tabindex="0"]')).toHaveFocus()
    expect(document.querySelectorAll('[role="gridcell"][tabindex="0"]')).toHaveLength(1)
  })

  it('uses semantic tokens, logical properties, dark mode, forced colors, and reduced motion', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-data-grid-background')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('inset-inline')
    expect(css).toContain('padding-inline')
    expect(css).toContain("[data-theme='dark'] .hl-data-grid")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion:[\s\S]*transition: none/)
  })
})
