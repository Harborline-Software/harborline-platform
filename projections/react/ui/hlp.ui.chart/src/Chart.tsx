import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type {
  ChartDefinition,
  ChartLineSeries,
  ChartMotionIntent,
  ChartProps,
  ChartValue,
  DonutChartSlice,
  LineChartDefinition,
} from './Chart.types'
import { prepareChartDefinition } from './chart-model'

const WIDTH = 640
const HEIGHT = 320
const INSET = 28
const SERIES_COLORS = 6

function classNames(base: string, extra?: string): string {
  return extra ? `${base} ${extra}` : base
}

function firstNonblank(...values: readonly (string | undefined)[]): string | undefined {
  return values.find(value => value !== undefined && value.trim().length > 0)?.trim()
}

function effectiveMotion(definition: ChartDefinition): ChartMotionIntent {
  return definition.motion ?? 'host'
}

function seriesStyle(index: number): React.CSSProperties {
  return { '--hl-chart-series': `var(--hl-chart-series-${index % SERIES_COLORS + 1})` } as React.CSSProperties
}

function lineCoordinates(definition: LineChartDefinition): {
  readonly minimum: number
  readonly maximum: number
  readonly x: (index: number) => number
  readonly y: (value: number) => number
} {
  const finite = definition.series.flatMap(series => series.values.filter((value): value is number => value !== null))
  const minimum = finite.length === 0 ? 0 : Math.min(...finite)
  const maximum = finite.length === 0 ? 1 : Math.max(...finite)
  const span = maximum - minimum || 1
  const plotWidth = WIDTH - INSET * 2
  const plotHeight = HEIGHT - INSET * 2
  return {
    minimum,
    maximum,
    x: index => definition.categories.length <= 1
      ? WIDTH / 2
      : INSET + index * plotWidth / (definition.categories.length - 1),
    y: value => HEIGHT - INSET - (value - minimum) * plotHeight / span,
  }
}

function linePath(series: ChartLineSeries, x: (index: number) => number, y: (value: number) => number): string {
  let drawing = false
  const commands: string[] = []
  series.values.forEach((value, index) => {
    if (value === null) {
      drawing = false
      return
    }
    commands.push(`${drawing ? 'L' : 'M'} ${x(index)} ${y(value)}`)
    drawing = true
  })
  return commands.join(' ')
}

function LineGraphic({
  definition,
  tooltip,
  formatNumber,
}: {
  definition: LineChartDefinition
  tooltip: boolean
  formatNumber: (value: number) => string
}) {
  const coordinates = lineCoordinates(definition)
  const showPoints = definition.categories.length * definition.series.length <= 1_000

  return (
    <svg aria-hidden="true" className="hl-chart__graphic" focusable="false" viewBox={`0 0 ${WIDTH} ${HEIGHT}`}>
      <line className="hl-chart__axis" x1={INSET} x2={WIDTH - INSET} y1={HEIGHT - INSET} y2={HEIGHT - INSET} />
      <line className="hl-chart__axis" x1={INSET} x2={INSET} y1={INSET} y2={HEIGHT - INSET} />
      {definition.series.map((series, seriesIndex) => (
        <g className="hl-chart__series" data-series-name={series.name} key={series.name} style={seriesStyle(seriesIndex)}>
          <path
            className="hl-chart__line"
            d={linePath(series, coordinates.x, coordinates.y)}
            pathLength="1"
            strokeDasharray={seriesIndex % 3 === 0 ? undefined : seriesIndex % 3 === 1 ? '0.04 0.02' : '0.01 0.015'}
          />
          {showPoints ? series.values.map((value, pointIndex) => (
            <g
              data-category={definition.categories[pointIndex]}
              data-point-state={value === null ? 'missing' : 'present'}
              key={`${series.name}-${pointIndex}`}
            >
              {value === null ? null : (
                <circle className="hl-chart__point" cx={coordinates.x(pointIndex)} cy={coordinates.y(value)} r={seriesIndex % 2 === 0 ? 4 : 3}>
                  {tooltip ? <title>{`${series.name} · ${definition.categories[pointIndex]} · ${formatNumber(value)}`}</title> : null}
                </circle>
              )}
            </g>
          )) : null}
        </g>
      ))}
    </svg>
  )
}

function pointOnCircle(angle: number, radius: number): readonly [number, number] {
  return [WIDTH / 2 + Math.cos(angle) * radius, HEIGHT / 2 + Math.sin(angle) * radius]
}

function donutPath(start: number, end: number): string {
  const outer = 118
  const inner = 68
  if (end - start >= Math.PI * 2 - Number.EPSILON) {
    const [outerStartX, outerStartY] = pointOnCircle(-Math.PI / 2, outer)
    const [innerStartX, innerStartY] = pointOnCircle(-Math.PI / 2, inner)
    return [
      `M ${outerStartX} ${outerStartY}`,
      `A ${outer} ${outer} 0 1 1 ${WIDTH / 2 - 0.001} ${HEIGHT / 2 - outer}`,
      `A ${outer} ${outer} 0 1 1 ${outerStartX} ${outerStartY}`,
      `L ${innerStartX} ${innerStartY}`,
      `A ${inner} ${inner} 0 1 0 ${WIDTH / 2 - 0.001} ${HEIGHT / 2 - inner}`,
      `A ${inner} ${inner} 0 1 0 ${innerStartX} ${innerStartY}`,
      'Z',
    ].join(' ')
  }
  const [outerStartX, outerStartY] = pointOnCircle(start, outer)
  const [outerEndX, outerEndY] = pointOnCircle(end, outer)
  const [innerEndX, innerEndY] = pointOnCircle(end, inner)
  const [innerStartX, innerStartY] = pointOnCircle(start, inner)
  const largeArc = end - start > Math.PI ? 1 : 0
  return [
    `M ${outerStartX} ${outerStartY}`,
    `A ${outer} ${outer} 0 ${largeArc} 1 ${outerEndX} ${outerEndY}`,
    `L ${innerEndX} ${innerEndY}`,
    `A ${inner} ${inner} 0 ${largeArc} 0 ${innerStartX} ${innerStartY}`,
    'Z',
  ].join(' ')
}

function DonutGraphic({
  slices,
  tooltip,
  formatNumber,
}: {
  slices: readonly DonutChartSlice[]
  tooltip: boolean
  formatNumber: (value: number) => string
}) {
  const total = slices.reduce((sum, slice) => sum + Math.abs(slice.value ?? 0), 0)
  let angle = -Math.PI / 2
  return (
    <svg aria-hidden="true" className="hl-chart__graphic" focusable="false" viewBox={`0 0 ${WIDTH} ${HEIGHT}`}>
      {slices.map((slice, index) => {
        const sweep = total === 0 ? 0 : Math.abs(slice.value ?? 0) / total * Math.PI * 2
        const start = angle
        const end = angle + sweep
        angle = end
        return (
          <path
            className="hl-chart__slice"
            d={donutPath(start, end)}
            data-slice={slice.label}
            key={`${slice.label}-${index}`}
            style={seriesStyle(index)}
          >
            {tooltip ? <title>{`${slice.label} · ${formatNumber(slice.value ?? 0)}`}</title> : null}
          </path>
        )
      })}
    </svg>
  )
}

// A chart handed nothing to plot draws this instead of a coordinate space. The 0-1 fallback range in
// lineCoordinates put axes and a numeric scale over no data, which a reader cannot tell apart from
// "every value is zero" or "still loading". Both lanes render the same element and text.
function EmptyGraphic({ text }: { text: string }) {
  return <p className="hl-chart__empty" data-chart-empty="true">{text}</p>
}

function Legend({ definition }: { definition: ChartDefinition }) {
  const labels = definition.kind === 'donut'
    ? definition.slices.map(slice => slice.label)
    : definition.series.map(series => series.name)
  return (
    <ul className="hl-chart__legend" data-chart-legend="visible">
      {labels.map((label, index) => (
        <li className="hl-chart__legend-item" key={`${label}-${index}`}>
          <span aria-hidden="true" className="hl-chart__legend-key" data-pattern={index % 3} style={seriesStyle(index)} />
          <span>{label}</span>
        </li>
      ))}
    </ul>
  )
}

function valueText(value: ChartValue, formatNumber: (value: number) => string): string {
  return value === null ? 'null' : formatNumber(value)
}

function AccessibleData({
  definition,
  label,
  formatNumber,
  seriesLabel,
  itemLabel,
  valueLabel,
}: {
  definition: ChartDefinition
  label: string
  formatNumber: (value: number) => string
  seriesLabel: string
  itemLabel: string
  valueLabel: string
}) {
  return (
    <details className="hl-chart__data">
      <summary>{label}</summary>
      <div className="hl-chart__table-scroll">
        {definition.kind === 'donut' ? (
          <table>
            <thead><tr><th scope="col">{itemLabel}</th><th scope="col">{valueLabel}</th></tr></thead>
            <tbody>
              {definition.slices.map((slice, index) => (
                <tr data-chart-row={index} key={`${slice.label}-${index}`}>
                  <th scope="row">{slice.label}</th>
                  <td data-value-state={slice.value === null ? 'missing' : 'present'}>{valueText(slice.value, formatNumber)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ) : (
          <table>
            <thead><tr><th scope="col">{seriesLabel}</th>{definition.categories.map((category, index) => <th scope="col" key={`${category}-${index}`}>{category}</th>)}</tr></thead>
            <tbody>
              {definition.series.map((series, seriesIndex) => (
                <tr data-chart-row={seriesIndex} key={series.name}>
                  <th scope="row">{series.name}</th>
                  {series.values.map((value, valueIndex) => (
                    <td data-value-state={value === null ? 'missing' : 'present'} key={`${series.name}-${valueIndex}`}>{valueText(value, formatNumber)}</td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </details>
  )
}

export function Chart({ definition, className, dataSummaryLabel, empty }: ChartProps) {
  const { direction, formatNumber, t } = useHarborlineStrings()
  const prepared = prepareChartDefinition(definition)
  const accessibleName = firstNonblank(definition.accessibleName, definition.title, t('charts.chart')) ?? 'Chart'
  const legend = definition.legend ?? 'visible'
  const tooltip = definition.tooltip ?? 'enabled'
  const motion = effectiveMotion(definition)
  const summaryLabel = firstNonblank(dataSummaryLabel) ?? `${accessibleName}: ${t('charts.value')}`
  // t() echoes the key when no catalog and no default carries it, so a caller catalog still wins and
  // the English default stands in otherwise. Blazor's ResolveString resolution order is the same.
  const emptyKey = t('charts.empty')
  const emptyText = firstNonblank(empty, emptyKey === 'charts.empty' ? undefined : emptyKey) ?? 'No data to display'

  return (
    <figure
      aria-label={accessibleName}
      className={classNames('hl-chart', className)}
      data-chart-kind={prepared.kind}
      data-legend={legend}
      data-motion={motion}
      data-tooltip={tooltip}
      dir={direction}
    >
      {definition.title ? <figcaption className="hl-chart__title">{definition.title}</figcaption> : null}
      <div className="hl-chart__visual" data-chart-state={prepared.empty ? 'empty' : 'plotted'}>
        {prepared.empty
          ? <EmptyGraphic text={emptyText} />
          : prepared.kind === 'donut'
            ? <DonutGraphic formatNumber={formatNumber} slices={prepared.renderedSlices} tooltip={tooltip === 'enabled'} />
            : <LineGraphic definition={prepared.definition} formatNumber={formatNumber} tooltip={tooltip === 'enabled'} />}
      </div>
      {legend === 'visible' ? <Legend definition={definition} /> : null}
      <AccessibleData
        definition={definition}
        formatNumber={formatNumber}
        itemLabel={t('charts.item')}
        label={summaryLabel}
        seriesLabel={t('charts.series')}
        valueLabel={t('charts.value')}
      />
    </figure>
  )
}
