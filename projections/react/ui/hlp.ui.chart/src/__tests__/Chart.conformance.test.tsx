import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chart } from '../Chart'
import type { ChartDefinition, LineChartDefinition } from '../Chart.types'
import { fixture, sharedCases } from './fixtures'

const line: LineChartDefinition = {
  kind: 'line',
  categories: ['Jan', 'Feb'],
  series: [{ name: 'Occupancy', values: [91, 92] }],
}

describe('Chart revision-1 shared fixtures', () => {
  it('consumes every frozen case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'chart.line-single',
      'chart.line-multiple',
      'chart.line-count-mismatch',
      'chart.line-null-gap',
      'chart.donut',
      'chart.donut-null',
      'chart.labels-and-values',
      'chart.visibility-defaults',
      'chart.replace-not-merge',
      'chart.accessible-name',
      'chart.localized-context',
      'chart.theme-motion',
      'chart.accessible-data',
      'chart.provider-isolation',
      'chart.empty-series',
    ])
  })

  it('chart.line-single and chart.line-multiple preserve order and values', () => {
    fixture(sharedCases, 'chart.line-single')
    fixture(sharedCases, 'chart.line-multiple')
    render(<Chart definition={{
      kind: 'line',
      categories: ['Jan', 'Feb'],
      series: [{ name: 'A', values: [1, 2] }, { name: 'B', values: [3, 4] }],
    }} />)
    expect([...document.querySelectorAll('[data-series-name]')].map(node => node.getAttribute('data-series-name'))).toEqual(['A', 'B'])
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Series', 'Jan', 'Feb'])
    expect(within(table).getAllByRole('rowheader').map(cell => cell.textContent)).toEqual(['A', 'B'])
    expect(within(table).getAllByRole('cell').map(cell => cell.textContent)).toEqual(['1', '2', '3', '4'])
  })

  it('chart.line-count-mismatch rejects mismatched values', () => {
    fixture(sharedCases, 'chart.line-count-mismatch')
    const invalid = { categories: ['Jan', 'Feb'], series: [{ name: 'A', values: [1] }] } as LineChartDefinition
    expect(() => render(<Chart definition={invalid} />)).toThrow('category-value-count-mismatch')
  })

  it('chart.line-null-gap keeps null missing without substituting zero', () => {
    fixture(sharedCases, 'chart.line-null-gap')
    render(<Chart definition={{ categories: ['Jan', 'Feb', 'Mar'], series: [{ name: 'A', values: [1, null, 3] }] }} />)
    expect(document.querySelector('[data-category="Feb"]')).toHaveAttribute('data-point-state', 'missing')
    expect(screen.getByRole('cell', { name: 'null' })).toHaveAttribute('data-value-state', 'missing')
    expect(document.querySelector('.hl-chart__line')).toHaveAttribute('d', expect.stringMatching(/^M .* M /))
  })

  it('chart.donut and chart.donut-null omit only null graphical slices', () => {
    fixture(sharedCases, 'chart.donut')
    fixture(sharedCases, 'chart.donut-null')
    render(<Chart definition={{ kind: 'donut', slices: [{ label: 'Palm', value: null }, { label: 'Bay', value: 20 }] }} />)
    expect([...document.querySelectorAll('[data-slice]')].map(node => node.getAttribute('data-slice'))).toEqual(['Bay'])
    expect(screen.getByRole('rowheader', { name: 'Palm' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: 'null' })).toHaveAttribute('data-value-state', 'missing')
  })

  it('chart.labels-and-values emits every frozen error code', () => {
    fixture(sharedCases, 'chart.labels-and-values')
    expect(() => render(<Chart definition={{ categories: [], series: [{ name: ' ', values: [] }] }} />)).toThrow('label-required')
    expect(() => render(<Chart definition={{ categories: [], series: [{ name: 'A', values: [] }, { name: 'A', values: [] }] }} />)).toThrow('duplicate-series-name')
    expect(() => render(<Chart definition={{ categories: ['A'], series: [{ name: 'A', values: [Number.NaN] }] }} />)).toThrow('non-finite-value')
    expect(() => render(<Chart definition={{ kind: 'donut', slices: [{ label: 'A', value: Number.POSITIVE_INFINITY }] }} />)).toThrow('non-finite-value')
  })

  it('chart.visibility-defaults uses visible, enabled, and host intents', () => {
    fixture(sharedCases, 'chart.visibility-defaults')
    render(<Chart definition={line} />)
    const chart = screen.getByRole('figure')
    expect(chart).toHaveAttribute('data-legend', 'visible')
    expect(chart).toHaveAttribute('data-tooltip', 'enabled')
    expect(chart).toHaveAttribute('data-motion', 'host')
    expect(document.querySelector('[data-chart-legend="visible"]')).toBeInTheDocument()
    expect(document.querySelector('svg title')).toBeInTheDocument()
  })

  it('chart.replace-not-merge removes stale categories and series', () => {
    fixture(sharedCases, 'chart.replace-not-merge')
    const rendered = render(<Chart definition={{ categories: ['Jan', 'Feb'], series: [{ name: 'Old', values: [1, 2] }] }} />)
    rendered.rerender(<Chart definition={{ categories: ['Mar'], series: [{ name: 'New', values: [3] }] }} />)
    expect(screen.getByRole('columnheader', { name: 'Mar' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Jan' })).toBeNull()
    expect(screen.queryByText('Old')).toBeNull()
    expect(document.querySelectorAll('[data-series-name]')).toHaveLength(1)
  })

  it('chart.accessible-name prefers explicit name, then title, then the localized default', () => {
    fixture(sharedCases, 'chart.accessible-name')
    const rendered = render(<Chart definition={{ ...line, accessibleName: 'Revenue trend', title: 'Revenue' }} />)
    expect(screen.getByRole('figure')).toHaveAccessibleName('Revenue trend')
    rendered.rerender(<Chart definition={{ ...line, accessibleName: undefined, title: 'Revenue' }} />)
    expect(screen.getByRole('figure')).toHaveAccessibleName('Revenue')
    rendered.rerender(<HarborlineLocaleProvider catalog={{ 'charts.chart': 'Graphique' }} locale="fr"><Chart definition={line} /></HarborlineLocaleProvider>)
    expect(screen.getByRole('figure')).toHaveAccessibleName('Graphique')
  })

  it('chart.localized-context preserves RTL direction, labels, order, and locale number formatting', () => {
    fixture(sharedCases, 'chart.localized-context')
    render(
      <HarborlineLocaleProvider locale="ar-SA" direction="rtl" numberFormatter={(value, request) => `${request.locale}:${value}`}>
        <Chart definition={{ categories: ['يناير', 'فبراير'], series: [{ name: 'الإيرادات', values: [12, 13] }] }} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('figure')).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Series', 'يناير', 'فبراير'])
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['ar-SA:12', 'ar-SA:13'])
  })

  it('chart.theme-motion changes intent without changing data', () => {
    fixture(sharedCases, 'chart.theme-motion')
    render(<div data-theme="dark"><Chart definition={{ ...line, motion: 'disabled' }} /></div>)
    expect(screen.getByRole('figure')).toHaveAttribute('data-motion', 'disabled')
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['91', '92'])
  })

  it('chart.accessible-data preserves ordered rows and values', () => {
    fixture(sharedCases, 'chart.accessible-data')
    render(<Chart definition={line} dataSummaryLabel="Revenue data" />)
    const details = screen.getByText('Revenue data').closest('details')
    expect(details).not.toBeNull()
    expect(within(details!).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Series', 'Jan', 'Feb'])
    expect(within(details!).getAllByRole('cell').map(cell => cell.textContent)).toEqual(['91', '92'])
  })

  it('chart.provider-isolation keeps public definition types provider neutral', () => {
    fixture(sharedCases, 'chart.provider-isolation')
    const definition: ChartDefinition = line
    render(<Chart definition={definition} />)
    expect(screen.getByRole('figure')).toHaveAttribute('data-chart-kind', 'line')
    expect(Object.keys(definition)).toEqual(['kind', 'categories', 'series'])
  })

  it('chart.empty-series draws a declared empty state instead of an invented range', () => {
    const shape = fixture(sharedCases, 'chart.empty-series').input as Record<string, ChartDefinition>
    const expected = fixture(sharedCases, 'chart.empty-series').expected as Record<string, unknown>
    for (const [name, definition] of Object.entries(shape)) {
      const rendered = render(<Chart definition={definition} />)
      expect(document.querySelector('.hl-chart__visual')).toHaveAttribute('data-chart-state', 'empty')
      expect(document.querySelectorAll('.hl-chart__axis')).toHaveLength(expected.axesDrawn as number)
      expect(document.querySelector('.hl-chart__line')).toBeNull()
      expect(document.querySelector('.hl-chart__slice')).toBeNull()
      expect(screen.getByText(expected.emptyText as string), name).toHaveAttribute('data-chart-empty', 'true')
      rendered.unmount()
    }
  })

  it('chart.empty-series prefers the caller string and then the catalog', () => {
    fixture(sharedCases, 'chart.empty-series')
    const empty: ChartDefinition = { kind: 'donut', slices: [] }
    const rendered = render(<Chart definition={empty} empty="Nothing measured yet" />)
    expect(screen.getByText('Nothing measured yet')).toHaveAttribute('data-chart-empty', 'true')
    rendered.rerender(
      <HarborlineLocaleProvider catalog={{ 'charts.empty': 'Aucune donnée' }} locale="fr">
        <Chart definition={empty} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByText('Aucune donnée')).toBeInTheDocument()
  })

  it('a chart that still has one plottable value keeps its axes', () => {
    render(<Chart definition={{ categories: ['Jan', 'Feb'], series: [{ name: 'A', values: [null, 3] }] }} />)
    expect(document.querySelector('.hl-chart__visual')).toHaveAttribute('data-chart-state', 'plotted')
    expect(document.querySelectorAll('.hl-chart__axis')).toHaveLength(2)
  })
})
