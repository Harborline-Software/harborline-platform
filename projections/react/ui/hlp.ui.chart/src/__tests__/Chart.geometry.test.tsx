import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chart } from '../Chart'
import type { ChartValue } from '../Chart.types'

// External oracle: JS-CHART-GEOMETRY-GRANT-001, written quality properties 1-4.
// These relationships belong to this React renderer, not a public pixel/scale contract.
// No production geometry helper, coordinate, inset, path or scaling formula supplies an oracle.

interface Point {
  readonly x: number
  readonly y: number
}

function graphic(): SVGSVGElement {
  const svg = screen.getByRole('figure').querySelector<SVGSVGElement>('svg')
  if (svg === null) throw new Error('expected-a-nonempty-line-graphic')
  return svg
}

function point(svg: SVGSVGElement, series: string, category: string): Point {
  const circles = svg.querySelectorAll<SVGCircleElement>(`[data-series-name="${series}"] [data-category="${category}"] circle`)
  expect(circles).toHaveLength(1)
  const circle = circles.item(0)
  const coordinate = (attribute: 'cx' | 'cy'): number => {
    const raw = circle.getAttribute(attribute)
    // Number(null) and Number('') are zero, so presence must be checked before conversion.
    if (raw === null || raw.trim().length === 0) throw new Error(`missing-${attribute}`)
    const value = Number(raw)
    expect(Number.isFinite(value)).toBe(true)
    return value
  }
  return { x: coordinate('cx'), y: coordinate('cy') }
}

function viewport(svg: SVGSVGElement) {
  const raw = svg.getAttribute('viewBox')
  if (raw === null || raw.trim().length === 0) throw new Error('missing-viewBox')
  // Read only the declared viewport's four numbers; no SVG path interpretation or layout engine.
  const parts = raw.trim().split(/\s+/u)
  expect(parts).toHaveLength(4)
  const left = Number(parts[0])
  const top = Number(parts[1])
  const width = Number(parts[2])
  const height = Number(parts[3])
  expect([left, top, width, height].every(Number.isFinite)).toBe(true)
  expect(width).toBeGreaterThan(0)
  expect(height).toBeGreaterThan(0)
  const right = left + width
  const bottom = top + height
  expect([right, bottom].every(Number.isFinite)).toBe(true)
  return { left, top, right, bottom }
}

const finiteCases: ReadonlyArray<{
  label: string
  categories: readonly string[]
  values: readonly ChartValue[]
  present: readonly string[]
}> = [
  { label: 'positive values with a gap', categories: ['first', 'gap', 'last'], values: [2, null, 8], present: ['first', 'last'] },
  { label: 'negative values', categories: ['first', 'middle', 'last'], values: [-8, -4, -2], present: ['first', 'middle', 'last'] },
  { label: 'mixed-sign values', categories: ['first', 'middle', 'last'], values: [-3, 0, 5], present: ['first', 'middle', 'last'] },
  { label: 'equal nonzero values', categories: ['first', 'middle', 'last'], values: [4, 4, 4], present: ['first', 'middle', 'last'] },
  { label: 'zero values', categories: ['first', 'middle', 'last'], values: [0, 0, 0], present: ['first', 'middle', 'last'] },
  { label: 'a singleton', categories: ['only'], values: [7], present: ['only'] },
]

describe('Chart React renderer written geometry quality properties', () => {
  it.each(finiteCases)('keeps $label finite, nonempty and inside its declared viewport', ({ categories, values, present }) => {
    render(
      <HarborlineLocaleProvider direction="ltr">
        <Chart definition={{ kind: 'line', categories, series: [{ name: 'A', values }] }} />
      </HarborlineLocaleProvider>,
    )
    const svg = graphic()
    const box = viewport(svg)
    expect(present.length).toBeGreaterThan(0)
    expect(svg.querySelectorAll('circle')).toHaveLength(present.length)
    for (const category of present) {
      const observed = point(svg, 'A', category)
      expect(observed.x).toBeGreaterThanOrEqual(box.left)
      expect(observed.x).toBeLessThanOrEqual(box.right)
      expect(observed.y).toBeGreaterThanOrEqual(box.top)
      expect(observed.y).toBeLessThanOrEqual(box.bottom)
    }
    for (const category of categories.filter(value => !present.includes(value))) {
      expect(svg.querySelectorAll(`[data-series-name="A"] [data-category="${category}"] circle`)).toHaveLength(0)
    }
  })

  it('uses increasing LTR category positions and ordinal numeric heights across series', () => {
    render(
      <HarborlineLocaleProvider direction="ltr">
        <Chart definition={{
          kind: 'line',
          categories: ['first', 'middle', 'last'],
          series: [
            { name: 'A', values: [-5, 0, 9] },
            { name: 'B', values: [9, 0, -5] },
          ],
        }} />
      </HarborlineLocaleProvider>,
    )
    const svg = graphic()
    for (const series of ['A', 'B']) {
      expect(point(svg, series, 'first').x).toBeLessThan(point(svg, series, 'middle').x)
      expect(point(svg, series, 'middle').x).toBeLessThan(point(svg, series, 'last').x)
    }

    // Caller values define only order/equality; neither a scale nor particular y values is assumed.
    const samples = [
      { series: 'A', category: 'first', value: -5 },
      { series: 'A', category: 'middle', value: 0 },
      { series: 'A', category: 'last', value: 9 },
      { series: 'B', category: 'first', value: 9 },
      { series: 'B', category: 'middle', value: 0 },
      { series: 'B', category: 'last', value: -5 },
    ].map(sample => ({ ...sample, observed: point(svg, sample.series, sample.category) }))
    for (const left of samples) {
      for (const right of samples) {
        if (left.value > right.value) expect(left.observed.y).toBeLessThan(right.observed.y)
        if (left.value === right.value) expect(left.observed.y).toBe(right.observed.y)
      }
    }
  })

  it.each([
    { label: 'positive', values: [2, 4, 8] },
    { label: 'negative', values: [-8, -4, -2] },
  ] as const)('appending an all-null series does not move existing $label points', ({ values }) => {
    const categories = ['first', 'middle', 'last']
    const view = render(
      <HarborlineLocaleProvider direction="ltr">
        <Chart definition={{ kind: 'line', categories, series: [{ name: 'Original', values }] }} />
      </HarborlineLocaleProvider>,
    )
    const before = new Map(categories.map(category => [category, point(graphic(), 'Original', category)] as const))
    view.rerender(
      <HarborlineLocaleProvider direction="ltr">
        <Chart definition={{
          kind: 'line',
          categories,
          series: [
            { name: 'Original', values },
            { name: 'Missing', values: [null, null, null] },
          ],
        }} />
      </HarborlineLocaleProvider>,
    )
    const after = graphic()
    for (const category of categories) {
      expect(point(after, 'Original', category)).toEqual(before.get(category))
    }
    expect(after.querySelectorAll('[data-series-name="Missing"] circle')).toHaveLength(0)
    expect(after.querySelectorAll('circle')).toHaveLength(3)
  })
})
