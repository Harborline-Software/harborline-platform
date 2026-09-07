import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Scheduler } from '../Scheduler'
import type { SchedulerEvent } from '../Scheduler.types'
import { qualityCases } from './fixtures'

const now = new Date('2026-08-11T09:30:00')
const event: SchedulerEvent = { id: 'a', title: 'Inspection', start: new Date('2026-08-11T09:00:00'), end: new Date('2026-08-11T10:00:00'), description: 'Hull review' }

describe('Scheduler React projection quality', () => {
  it('consumes every non-performance quality case', () => {
    expect(qualityCases).toHaveLength(17)
    expect(qualityCases.map(value => value.id)).toContain('scheduler.quality.crud-recurrence')
  })

  it('provides named structure, localized actions, event descriptions, and stable current time status', () => {
    render(
      <HarborlineLocaleProvider catalog={{ 'scheduler.today': 'Aujourd’hui', 'scheduler.agenda': 'Programme' }} locale="fr-FR">
        <Scheduler accessibleName="Calendrier" data={[event]} defaultDate={now} defaultView="agenda" now={now} readOnly />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('region', { name: 'Calendrier' })).toHaveAttribute('dir', 'ltr')
    expect(screen.getByRole('button', { name: 'Aujourd’hui' })).toBeInTheDocument()
    expect(screen.getByText('Hull review')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Programme', pressed: true })).toBeInTheDocument()
  })

  it('mirrors navigation in RTL and preserves caller localized view titles', () => {
    render(
      <HarborlineLocaleProvider direction="rtl" locale="ar-SA">
        <Scheduler data={[]} defaultDate={now} defaultView="day" now={now} views={[{ type: 'day', title: 'يوم' }, { type: 'agenda', title: 'جدول' }]} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('region')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('button', { name: 'Previous' })).toHaveTextContent('›')
    expect(screen.getByRole('button', { name: 'يوم' })).toBeInTheDocument()
  })

  it('supports keyboard event editing and Escape dismissal with focus restoration', async () => {
    render(<Scheduler data={[event]} defaultDate={now} defaultView="agenda" now={now} />)
    const edit = screen.getByRole('button', { name: 'Edit event' })
    edit.focus()
    await userEvent.setup().click(edit)
    expect(screen.getByRole('dialog', { name: 'Edit event' })).toBeInTheDocument()
    await userEvent.setup().keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(edit).toHaveFocus()
  })

  it('publishes semantic themes, reflow, forced colors, focus, and reduced motion', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-scheduler-surface')
    expect(css).toContain('--hl-scheduler-now')
    expect(css).toContain('--hl-scheduler-off-hours')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain("[data-theme='dark'] .hl-scheduler")
    expect(css).toContain('@media (max-width: 48rem)')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/focus-visible[\s\S]*outline:/)
  })
})
