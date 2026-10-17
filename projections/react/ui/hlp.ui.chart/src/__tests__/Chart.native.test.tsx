import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chart } from '../Chart'
import { qualityCases } from './fixtures'

describe('Chart React projection quality', () => {
  it('consumes every non-performance quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'chart.quality.name-summary',
      'chart.quality.contrast',
      'chart.quality.reflow',
      'chart.quality.locale',
      'chart.quality.rtl',
      'chart.quality.number-format',
      'chart.quality.pseudo',
      'chart.quality.light-dark',
      'chart.quality.tokens',
      'chart.quality.runtime-switch',
      'chart.quality.visual-parity',
      'chart.quality.keyboard-equivalent',
      'chart.quality.focus-visible',
      'chart.quality.reduced-motion',
    ])
  })

  it('provides a named figure, semantic summary, and keyboard-accessible data disclosure', async () => {
    render(<Chart definition={{ accessibleName: 'Monthly occupancy', categories: ['Jan'], series: [{ name: 'Occupancy', values: [91] }] }} />)
    expect(screen.getByRole('figure', { name: 'Monthly occupancy' })).toBeInTheDocument()
    const summary = screen.getByText('Monthly occupancy: Value')
    summary.focus()
    expect(summary).toHaveFocus()
    expect(summary.tagName).toBe('SUMMARY')
    await userEvent.setup().click(summary)
    expect(summary.closest('details')).toHaveAttribute('open')
    expect(screen.getByRole('table')).toBeInTheDocument()
  })

  it('preserves long caller labels, RTL DOM order, and host number formatting', () => {
    const longTitle = `[!! ${'Revenue expansion '.repeat(12)} !!]`
    render(
      <HarborlineLocaleProvider locale="ar-SA" direction="rtl" numberFormatter={value => `localized-${value}`}>
        <Chart definition={{ title: longTitle, categories: ['الأول', 'الثاني'], series: [{ name: 'الإيرادات', values: [1234.5, null] }] }} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('figure')).toHaveAttribute('dir', 'rtl')
    expect(document.querySelector('.hl-chart__title')?.textContent).toBe(longTitle)
    expect(screen.getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Series', 'الأول', 'الثاني'])
    expect(screen.getByRole('cell', { name: 'localized-1234.5' })).toBeInTheDocument()
    expect(document.querySelector('svg title')).toHaveTextContent('localized-1234.5')
  })

  it('supports explicit legend and tooltip suppression without removing accessible data', () => {
    render(<Chart definition={{
      kind: 'donut',
      legend: 'hidden',
      tooltip: 'disabled',
      slices: [{ label: 'Palm', value: 10 }],
    }} />)
    expect(screen.getByRole('figure')).toHaveAttribute('data-legend', 'hidden')
    expect(screen.getByRole('figure')).toHaveAttribute('data-tooltip', 'disabled')
    expect(document.querySelector('[data-chart-legend]')).toBeNull()
    expect(document.querySelector('svg title')).toBeNull()
    expect(screen.getByRole('rowheader', { name: 'Palm' })).toBeInTheDocument()
  })

  // Revision-1 interface errors and chart.labels-and-values apply to both series and slice labels.
  it.each(['', ' ', '\t\n'])('rejects a blank donut slice label %j with the public error code', label => {
    expect(() => render(<Chart definition={{ kind: 'donut', slices: [{ label, value: 10 }] }} />)).toThrow('label-required')
  })

  it('preserves two positive donut slices, localized data headings, and enabled formatted tooltips', () => {
    // Palm10/Bay20 is the independent chart.donut fixture; the catalog and formatter are test-owned oracles.
    render(
      <HarborlineLocaleProvider
        catalog={{ 'charts.item': 'Item label', 'charts.value': 'Measured value' }}
        locale="fr-FR"
        numberFormatter={value => `amount:${value}`}
      >
        <Chart definition={{
          kind: 'donut',
          tooltip: 'enabled',
          slices: [{ label: 'Palm', value: 10 }, { label: 'Bay', value: 20 }],
        }} />
      </HarborlineLocaleProvider>,
    )

    expect(screen.getByRole('figure')).toHaveAttribute('data-tooltip', 'enabled')
    expect(document.querySelector('.hl-chart__visual')).toHaveAttribute('data-chart-state', 'plotted')
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('columnheader').map(cell => cell.textContent)).toEqual(['Item label', 'Measured value'])
    expect(within(table).getAllByRole('rowheader').map(cell => cell.textContent)).toEqual(['Palm', 'Bay'])
    expect(within(table).getAllByRole('cell').map(cell => cell.textContent)).toEqual(['amount:10', 'amount:20'])
    expect(within(table).getAllByRole('cell').map(cell => cell.getAttribute('data-value-state'))).toEqual(['present', 'present'])

    const slices = [...document.querySelectorAll<SVGPathElement>('[data-slice]')]
    expect(slices.map(slice => slice.getAttribute('data-slice'))).toEqual(['Palm', 'Bay'])
    const tooltips = slices.map(slice => slice.querySelector('title')?.textContent ?? '')
    expect(tooltips).toEqual([expect.stringContaining('Palm'), expect.stringContaining('Bay')])
    expect(tooltips).toEqual([expect.stringContaining('amount:10'), expect.stringContaining('amount:20')])

    // Private native-renderer validity only: no coordinate constants, formulas, or cross-projection geometry.
    for (const slice of slices) {
      const path = slice.getAttribute('d') ?? ''
      expect(path).toMatch(/^[Mm](?:\s|,)/)
      expect(path).toMatch(/[Zz]$/)
      const numbers = path.replace(/[MmZzLlHhVvCcSsQqTtAa]/g, ' ').trim().split(/[,\s]+/).filter(Boolean).map(Number)
      expect(numbers.length).toBeGreaterThan(0)
      expect(numbers.every(Number.isFinite)).toBe(true)
    }
  })

  it('continues each line segment while keeping a null gap missing', () => {
    render(<Chart definition={{
      categories: ['Jan', 'Feb', 'Mar', 'Apr', 'May'],
      series: [{ name: 'Inspected', values: [1, 2, null, 3, 4] }],
    }} />)

    // Native command kinds describe continuity around a gap, without freezing numeric coordinates.
    const path = document.querySelector('.hl-chart__line')?.getAttribute('d') ?? ''
    expect(path.match(/[ML]/g)).toEqual(['M', 'L', 'M', 'L'])
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['1', '2', 'null', '3', '4'])
    expect(screen.getByRole('cell', { name: 'null' })).toHaveAttribute('data-value-state', 'missing')
    expect(document.querySelector('[data-category="Mar"]')).toHaveAttribute('data-point-state', 'missing')
  })

  it('disables line tooltips while retaining formatted and missing accessible values', () => {
    render(
      <HarborlineLocaleProvider numberFormatter={value => `amount:${value}`}>
        <Chart definition={{
          tooltip: 'disabled',
          categories: ['Jan', 'Feb'],
          series: [{ name: 'Inspected', values: [12, null] }],
        }} />
      </HarborlineLocaleProvider>,
    )

    expect(screen.getByRole('figure')).toHaveAttribute('data-tooltip', 'disabled')
    expect(document.querySelector('svg title')).toBeNull()
    expect(screen.getByRole('rowheader', { name: 'Inspected' })).toBeInTheDocument()
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['amount:12', 'null'])
    expect(screen.getByRole('cell', { name: 'null' })).toHaveAttribute('data-value-state', 'missing')
  })

  it('retains caller classes alongside the authoritative chart styling hook', () => {
    render(<Chart className="report-chart highlighted" definition={{ categories: ['Jan'], series: [{ name: 'Inspected', values: [12] }] }} />)
    expect(screen.getByRole('figure')).toHaveClass('hl-chart', 'report-chart', 'highlighted')
  })

  it('publishes semantic themes, non-color patterns, reflow, focus, forced-color, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-chart-series-1')
    expect(css).toContain("[data-pattern='1']")
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('overflow-x: auto')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-chart")
    expect(css).toContain("[data-motion='disabled']")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/summary:focus-visible[\s\S]*outline:/)
  })
})
