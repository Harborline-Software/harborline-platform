import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Gantt } from '../Gantt'
import type { GanttTask } from '../Gantt.types'
import { prepareGantt } from '../gantt-model'
import { fixture, sharedCases } from './fixtures'

const tasks: readonly GanttTask[] = [
  { id: 't2', title: 'Inspect hull', start: '2026-08-01', end: '2026-08-03', progress: 45, color: 'var(--hl-color-success)' },
  { id: 't1', title: 'Repair deck', start: '2026-08-04', end: '2026-08-06', progress: 20 },
  { id: 't3', title: 'Close work', start: '2026-08-07', end: '2026-08-07' },
]

describe('Gantt revision-1 shared fixtures', () => {
  it('consumes every frozen case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'gantt.empty',
      'gantt.defaults',
      'gantt.columns-default',
      'gantt.columns-custom',
      'gantt.task-order',
      'gantt.progress-color',
      'gantt.dependencies',
      'gantt.zoom-scales',
      'gantt.zoom-controlled',
      'gantt.read-only',
      'gantt.localized-dates',
      'gantt.rtl',
      'gantt.host-attributes',
      'gantt.replacement',
      'gantt.invalid-input',
      'gantt.provider-isolation',
      'gantt.projection-equivalence',
    ])
  })

  it('gantt.empty and gantt.defaults render localized read-only defaults', () => {
    fixture(sharedCases, 'gantt.empty')
    fixture(sharedCases, 'gantt.defaults')
    render(
      <HarborlineLocaleProvider catalog={{ 'dataGrid.noResults': 'Aucune tâche.' }} locale="fr-FR">
        <Gantt accessibleName="Planning" />
      </HarborlineLocaleProvider>,
    )
    const root = screen.getByRole('region', { name: 'Planning' })
    expect(root).toHaveAttribute('data-zoom', 'day')
    expect(root).toHaveAttribute('data-read-only', 'true')
    expect(root).toHaveStyle({ '--hl-gantt-row-height': '36px' })
    expect(screen.getByText('Aucune tâche.')).toBeInTheDocument()
    expect(document.querySelectorAll('[data-task-bar]')).toHaveLength(0)
    expect(document.querySelector('[data-dependency-from]')).toBeNull()
    expect(screen.queryByRole('combobox')).toBeNull()
  })

  it('gantt.columns-default and gantt.columns-custom replace pane columns', () => {
    fixture(sharedCases, 'gantt.columns-default')
    fixture(sharedCases, 'gantt.columns-custom')
    const rendered = render(<Gantt accessibleName="Schedule" tasks={tasks.slice(0, 1)} />)
    expect(screen.getAllByRole('columnheader').map(node => node.getAttribute('aria-label') ?? node.querySelector('.hl-gantt__timeline-title')?.textContent ?? node.textContent?.trim())).toEqual(['Title', 'Start', 'End', 'Name'])
    rendered.rerender(<Gantt accessibleName="Schedule" columns={[{ field: 'title', title: 'Work', width: 240 }]} tasks={tasks.slice(0, 1)} />)
    const headers = screen.getAllByRole('columnheader')
    expect(headers.map(node => node.getAttribute('aria-label') ?? node.querySelector('.hl-gantt__timeline-title')?.textContent ?? node.textContent?.trim())).toEqual(['Work', 'Name'])
    expect(headers[0]).toHaveStyle({ inlineSize: '240px' })
    expect(screen.queryByRole('columnheader', { name: 'Start' })).toBeNull()
  })

  it('gantt.task-order and gantt.progress-color preserve order, progress text, and semantic color', () => {
    fixture(sharedCases, 'gantt.task-order')
    fixture(sharedCases, 'gantt.progress-color')
    render(<Gantt accessibleName="Schedule" tasks={tasks} />)
    expect([...document.querySelectorAll('[data-task-id]')].map(node => node.getAttribute('data-task-id'))).toEqual(['t2', 't1', 't3'])
    expect([...document.querySelectorAll('[data-task-bar]')].map(node => node.getAttribute('data-task-bar'))).toEqual(['t2', 't1', 't3'])
    const bar = document.querySelector<HTMLElement>('[data-task-bar="t2"]')
    expect(bar?.style.getPropertyValue('--hl-gantt-bar-color')).toBe('var(--hl-color-success)')
    expect(within(bar!).getByText('45%')).toBeInTheDocument()
  })

  it('gantt.dependencies preserves valid edge order and ignores unknown endpoints', () => {
    fixture(sharedCases, 'gantt.dependencies')
    render(<Gantt accessibleName="Schedule" tasks={tasks.slice(0, 2)} dependencies={[
      { fromId: 't2', toId: 't1' },
      { fromId: 'missing', toId: 't1' },
    ]} />)
    const edges = document.querySelectorAll('[data-dependency-from]')
    expect(edges).toHaveLength(1)
    expect(edges[0]).toHaveAttribute('data-dependency-from', 't2')
    expect(edges[0]).toHaveAttribute('data-dependency-to', 't1')
    expect(edges[0]?.closest('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  it('gantt.zoom-scales preserves dates across all controlled scales', () => {
    fixture(sharedCases, 'gantt.zoom-scales')
    const rendered = render(<Gantt accessibleName="Schedule" tasks={tasks} zoom="day" />)
    const initialDates = screen.getAllByRole('cell').filter(cell => /2026/.test(cell.textContent ?? '')).map(cell => cell.textContent)
    for (const zoom of ['week', 'month'] as const) {
      rendered.rerender(<Gantt accessibleName="Schedule" tasks={tasks} zoom={zoom} />)
      expect(screen.getByRole('region', { name: 'Schedule' })).toHaveAttribute('data-zoom', zoom)
      expect(screen.getAllByRole('cell').filter(cell => /2026/.test(cell.textContent ?? '')).map(cell => cell.textContent)).toEqual(initialDates)
      expect(document.querySelector('[data-scale-count]')).not.toHaveAttribute('data-scale-count', '0')
    }
  })

  it('gantt.zoom-controlled requests a value without mutating the supplied zoom', async () => {
    fixture(sharedCases, 'gantt.zoom-controlled')
    const changed = vi.fn()
    render(<Gantt accessibleName="Schedule" onZoomChange={changed} showZoomPicker tasks={tasks} zoom="week" />)
    await userEvent.setup().selectOptions(screen.getByRole('combobox', { name: 'Zoom' }), 'month')
    expect(changed).toHaveBeenCalledOnce()
    expect(changed).toHaveBeenCalledWith('month')
    expect(screen.getByRole('region', { name: 'Schedule' })).toHaveAttribute('data-zoom', 'week')
    expect(screen.getByRole('combobox')).toHaveValue('week')
  })

  it('gantt.read-only exposes no editing or dependency mutation controls', () => {
    fixture(sharedCases, 'gantt.read-only')
    render(<Gantt accessibleName="Schedule" tasks={tasks} />)
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
    fireEvent.pointerDown(document.querySelector('[data-task-bar="t2"]')!)
    expect(screen.getByRole('region', { name: 'Schedule' })).toHaveAttribute('data-read-only', 'true')
  })

  it('gantt.localized-dates and gantt.rtl use locale calendar dates and logical direction', () => {
    fixture(sharedCases, 'gantt.localized-dates')
    fixture(sharedCases, 'gantt.rtl')
    render(
      <HarborlineLocaleProvider direction="rtl" locale="fr-FR">
        <Gantt accessibleName="Calendrier" tasks={tasks.slice(0, 1)} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('region', { name: 'Calendrier' })).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByRole('cell').some(cell => cell.textContent?.includes('août'))).toBe(true)
    expect(document.querySelector('[data-task-id]')).toHaveAttribute('data-task-id', 't2')
  })

  it('gantt.host-attributes preserves root class and host attributes', () => {
    fixture(sharedCases, 'gantt.host-attributes')
    render(<Gantt aria-label="App schedule" className="schedule" data-owner="app" tasks={tasks.slice(0, 1)} />)
    const root = screen.getByRole('region', { name: 'App schedule' })
    expect(root).toHaveClass('hl-gantt', 'schedule')
    expect(root).toHaveAttribute('data-owner', 'app')
  })

  it('gantt.replacement removes stale rows, bars, and connectors', () => {
    fixture(sharedCases, 'gantt.replacement')
    const rendered = render(<Gantt accessibleName="Schedule" tasks={tasks.slice(0, 2)} dependencies={[{ fromId: 't2', toId: 't1' }]} />)
    rendered.rerender(<Gantt accessibleName="Schedule" tasks={[tasks[2]!]} dependencies={[{ fromId: 't2', toId: 't1' }]} />)
    expect([...document.querySelectorAll('[data-task-id]')].map(node => node.getAttribute('data-task-id'))).toEqual(['t3'])
    expect([...document.querySelectorAll('[data-task-bar]')].map(node => node.getAttribute('data-task-bar'))).toEqual(['t3'])
    expect(document.querySelector('[data-dependency-from]')).toBeNull()
    expect(screen.queryByText('Inspect hull')).toBeNull()
  })

  it('gantt.invalid-input emits every frozen stable error code', () => {
    fixture(sharedCases, 'gantt.invalid-input')
    const valid = tasks[0]!
    expect(() => prepareGantt([{ ...valid, id: ' ' }], [])).toThrow('task-id-required')
    expect(() => prepareGantt([valid, { ...valid }], [])).toThrow('duplicate-task-id')
    expect(() => prepareGantt([{ ...valid, title: ' ' }], [])).toThrow('task-title-required')
    expect(() => prepareGantt([{ ...valid, start: '2026-08-04', end: '2026-08-03' }], [])).toThrow('invalid-task-range')
    expect(() => prepareGantt([{ ...valid, progress: Number.NaN }], [])).toThrow('invalid-progress')
    expect(() => prepareGantt([valid], [], [{ field: 'title', width: 0 }])).toThrow('invalid-column-width')
  })

  it('gantt.provider-isolation and projection-equivalence keep the public seam semantic', () => {
    fixture(sharedCases, 'gantt.provider-isolation')
    fixture(sharedCases, 'gantt.projection-equivalence')
    const publicFiles = ['Gantt.types.ts', 'index.ts'].map(name => readFileSync(resolve(import.meta.dirname, `../${name}`), 'utf8')).join('\n')
    expect(publicFiles).not.toMatch(/telerik|tanstack|svg|canvas|webgl|renderer|coordinate/i)
    render(<Gantt accessibleName="Schedule" tasks={tasks.slice(0, 1)} zoom="month" />)
    expect(screen.getByRole('table', { name: 'Schedule' })).toHaveAttribute('aria-rowcount', '1')
    expect(document.querySelector('[data-task-bar="t2"]')).toBeInTheDocument()
  })
})
