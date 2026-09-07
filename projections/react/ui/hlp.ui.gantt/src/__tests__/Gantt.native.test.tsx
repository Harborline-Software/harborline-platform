import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Gantt } from '../Gantt'
import type { GanttTask } from '../Gantt.types'
import { qualityCases } from './fixtures'

const tasks: readonly GanttTask[] = [
  { id: 'a', title: 'Survey', start: '2026-08-01', end: '2026-08-02', progress: 25 },
  { id: 'b', title: 'Repair', start: '2026-08-03', end: '2026-08-05', progress: 75 },
]

describe('Gantt React projection quality', () => {
  it('consumes every non-performance quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'gantt.quality.named-grid',
      'gantt.quality.non-color-progress',
      'gantt.quality.reflow',
      'gantt.quality.locale-dates',
      'gantt.quality.catalog',
      'gantt.quality.pseudo',
      'gantt.quality.light-dark',
      'gantt.quality.tokens',
      'gantt.quality.forced-colors',
      'gantt.quality.visual-parity',
      'gantt.quality.grid-navigation',
      'gantt.quality.zoom-picker',
      'gantt.quality.focus-visible',
      'gantt.quality.rtl',
      'gantt.quality.reduced-motion',
      'gantt.quality.controlled-zoom',
    ])
  })

  it('provides a named region and table, non-color progress, and hidden dependency geometry', () => {
    render(<Gantt accessibleName="Dock schedule" dependencies={[{ fromId: 'a', toId: 'b' }]} tasks={tasks} />)
    expect(screen.getByRole('region', { name: 'Dock schedule' })).toBeInTheDocument()
    expect(screen.getByRole('table', { name: 'Dock schedule' })).toHaveAttribute('aria-rowcount', '2')
    expect(screen.getByText('25%')).toBeInTheDocument()
    expect(document.querySelector('.hl-gantt__dependencies')).toHaveAttribute('aria-hidden', 'true')
  })

  it('keeps the scroll viewport keyboard reachable when the schedule is empty', () => {
    render(<Gantt accessibleName="Dock schedule" tasks={[]} />)
    const viewport = document.querySelector('.hl-gantt__scroll')
    expect(viewport).toHaveAttribute('role', 'group')
    expect(viewport).toHaveAttribute('aria-label', 'Dock schedule')
    expect(viewport).toHaveAttribute('tabindex', '0')
  })

  it('moves focus across adjacent rows and columns without editing data', async () => {
    render(<Gantt accessibleName="Dock schedule" tasks={tasks} />)
    const first = document.querySelector<HTMLElement>('[data-gantt-cell="0:0"]')!
    first.focus()
    expect(first).toHaveFocus()
    await userEvent.setup().keyboard('{ArrowDown}')
    expect(document.querySelector('[data-gantt-cell="1:0"]')).toHaveFocus()
    await userEvent.setup().keyboard('{End}')
    expect(document.querySelector('[data-gantt-cell="1:3"]')).toHaveFocus()
    await userEvent.setup().keyboard('{ArrowUp}{Home}')
    expect(document.querySelector('[data-gantt-cell="0:0"]')).toHaveFocus()
  })

  it('supports keyboard zoom requests while retaining controlled state', async () => {
    const changed = vi.fn()
    render(<Gantt accessibleName="Dock schedule" onZoomChange={changed} showZoomPicker tasks={tasks} zoom="day" />)
    const picker = screen.getByRole('combobox', { name: 'Zoom' })
    picker.focus()
    await userEvent.setup().selectOptions(picker, 'week')
    expect(changed).toHaveBeenCalledWith('week')
    expect(screen.getByRole('region', { name: 'Dock schedule' })).toHaveAttribute('data-zoom', 'day')
  })

  it('preserves pseudo-localized catalog content and RTL task order', () => {
    const long = `[!! ${'Schedule timeline '.repeat(8)} !!]`
    render(
      <HarborlineLocaleProvider catalog={{ 'gantt.title': long, 'gantt.start': '[Start]', 'gantt.end': '[End]' }} direction="rtl" locale="ar-SA">
        <Gantt tasks={tasks} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('region', { name: /Schedule timeline/ })).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('columnheader', { name: '[Start]' })).toBeInTheDocument()
    expect([...document.querySelectorAll('[data-task-id]')].map(node => node.getAttribute('data-task-id'))).toEqual(['a', 'b'])
  })

  it('publishes reflow, semantic themes, focus, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-gantt-surface')
    expect(css).toContain('--hl-gantt-accent')
    expect(css).toContain('overflow: auto')
    expect(css).toContain('overflow-wrap: anywhere')
    // The reflow floor is still 48rem, but it is now DERIVED from the two variables that define the
    // layout rather than duplicated as a literal that could drift from them. Assert the derivation
    // and the operands, so the guarantee stays checked instead of merely restated: a pane of 24rem
    // plus a timeline of at least 24rem cannot compose to less than the 48rem this test defends.
    expect(css).toContain('inline-size: max(100%, calc(var(--hl-gantt-pane-width) + var(--hl-gantt-timeline-width)))')
    expect(css).toContain('--hl-gantt-pane-width: 24rem')
    expect(css).toMatch(/--hl-gantt-timeline-width:\s*max\(24rem,/)
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-gantt")
    expect(css).toContain('@media (max-width: 48rem)')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/td:focus-visible[\s\S]*outline:/)
    expect(css).toMatch(/__scroll:focus-visible[\s\S]*outline:/)
  })
})
