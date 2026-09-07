import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Chart } from '../Chart'
import type { ChartLineSeries, LineChartDefinition } from '../Chart.types'
import { prepareChartDefinition } from '../chart-model'
import { fixture, performanceCases } from './fixtures'

describe('Chart deterministic Tier-C evidence', () => {
  it('chart.quality.large-data validates the seed maximum with linear work', () => {
    fixture(performanceCases, 'chart.quality.large-data')
    const categories = Array.from({ length: 5_000 }, (_, index) => `Category ${index}`)
    const series = Array.from({ length: 10 }, (_, seriesIndex) => ({
      name: `Series ${seriesIndex}`,
      values: categories.map((_, pointIndex) => pointIndex % 17 === 0 ? null : seriesIndex * 5_000 + pointIndex),
    })) as unknown as readonly [ChartLineSeries, ...ChartLineSeries[]]
    const prepared = prepareChartDefinition({ categories, series })
    expect(prepared.kind).toBe('line')
    expect(prepared.pointCount).toBe(50_000)
    expect(prepared.validationOperations).toBe(50_010)
    expect(prepared.definition.categories).toBe(categories)
    expect(prepared.definition.series).toBe(series)
  }, 30_000)

  it('chart.quality.repeated-update keeps only update 96 with no stale series', () => {
    fixture(performanceCases, 'chart.quality.repeated-update')
    const definition = (revision: number): LineChartDefinition => ({
      categories: [`Category ${revision}`],
      series: [{ name: `Series ${revision}`, values: [revision] }],
    })
    const rendered = render(<Chart definition={definition(0)} />)
    for (let revision = 1; revision <= 96; revision += 1) {
      rendered.rerender(<Chart definition={definition(revision)} />)
    }
    expect(screen.getByRole('columnheader', { name: 'Category 96' })).toBeInTheDocument()
    expect(screen.getByRole('rowheader', { name: 'Series 96' })).toBeInTheDocument()
    expect(screen.getByRole('cell', { name: '96' })).toBeInTheDocument()
    expect(screen.queryByText('Category 95')).toBeNull()
    expect(screen.queryByText('Series 95')).toBeNull()
    expect(document.querySelectorAll('[data-series-name]')).toHaveLength(1)
  }, 30_000)
})
