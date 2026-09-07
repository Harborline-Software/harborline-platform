export type ChartValue = number | null
export type ChartLegendIntent = 'visible' | 'hidden'
export type ChartTooltipIntent = 'enabled' | 'disabled'
export type ChartMotionIntent = 'host' | 'disabled'

export interface ChartDefinitionBase {
  readonly accessibleName?: string
  readonly title?: string
  readonly legend?: ChartLegendIntent
  readonly tooltip?: ChartTooltipIntent
  readonly motion?: ChartMotionIntent
}

export interface ChartLineSeries {
  readonly name: string
  readonly values: readonly ChartValue[]
}

export interface LineChartDefinition extends ChartDefinitionBase {
  readonly kind?: 'line'
  readonly categories: readonly string[]
  readonly series: readonly ChartLineSeries[]
}

export interface DonutChartSlice {
  readonly label: string
  readonly value: ChartValue
}

export interface DonutChartDefinition extends ChartDefinitionBase {
  readonly kind: 'donut'
  readonly slices: readonly DonutChartSlice[]
}

export type ChartDefinition = LineChartDefinition | DonutChartDefinition

export interface ChartProps {
  readonly definition: ChartDefinition
  readonly className?: string
  readonly dataSummaryLabel?: string
  readonly empty?: string
}
