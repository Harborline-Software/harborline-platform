import type {
  ChartDefinition,
  ChartLineSeries,
  ChartValue,
  DonutChartDefinition,
  DonutChartSlice,
  LineChartDefinition,
} from './Chart.types'

export interface PreparedLineChart {
  readonly kind: 'line'
  readonly definition: LineChartDefinition
  readonly empty: boolean
  readonly pointCount: number
  readonly validationOperations: number
}

export interface PreparedDonutChart {
  readonly kind: 'donut'
  readonly definition: DonutChartDefinition
  readonly empty: boolean
  readonly renderedSlices: readonly DonutChartSlice[]
  readonly pointCount: number
  readonly validationOperations: number
}

export type PreparedChart = PreparedLineChart | PreparedDonutChart

function requireLabel(value: string): void {
  if (value.trim().length === 0) throw new Error('label-required')
}

function requireFiniteOrNull(value: ChartValue): void {
  if (value !== null && !Number.isFinite(value)) throw new Error('non-finite-value')
}

function prepareLine(definition: LineChartDefinition): PreparedLineChart {
  const names = new Set<string>()
  let validationOperations = 0
  let pointCount = 0
  let plotted = 0

  for (const series of definition.series) {
    validationOperations += 1
    requireLabel(series.name)
    if (names.has(series.name)) throw new Error('duplicate-series-name')
    names.add(series.name)
    if (series.values.length !== definition.categories.length) {
      throw new Error('category-value-count-mismatch')
    }
    for (const value of series.values) {
      validationOperations += 1
      pointCount += 1
      if (value !== null) plotted += 1
      requireFiniteOrNull(value)
    }
  }

  return { kind: 'line', definition, empty: plotted === 0, pointCount, validationOperations }
}

function prepareDonut(definition: DonutChartDefinition): PreparedDonutChart {
  const renderedSlices: DonutChartSlice[] = []
  let validationOperations = 0

  for (const slice of definition.slices) {
    validationOperations += 1
    requireLabel(slice.label)
    requireFiniteOrNull(slice.value)
    if (slice.value !== null) renderedSlices.push(slice)
  }

  return {
    kind: 'donut',
    definition,
    empty: renderedSlices.length === 0,
    renderedSlices,
    pointCount: definition.slices.length,
    validationOperations,
  }
}

export function prepareChartDefinition(definition: ChartDefinition): PreparedChart {
  return definition.kind === 'donut' ? prepareDonut(definition) : prepareLine(definition)
}

export function chartSeries(definition: LineChartDefinition): readonly ChartLineSeries[] {
  return definition.series
}
