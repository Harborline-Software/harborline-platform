import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
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
