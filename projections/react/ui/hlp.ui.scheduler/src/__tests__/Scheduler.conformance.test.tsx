import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Scheduler } from '../Scheduler'
import { expandRecurrence, parseRRule } from '../SchedulerRecurrence'
import type { SchedulerEvent } from '../Scheduler.types'
import { buildRenderedEvents, computeOverlapLayout, eventKey, normalizeEvents } from '../scheduler-model'
import { fixture, sharedCases } from './fixtures'

const now = new Date('2026-08-11T09:30:00')
const events: readonly SchedulerEvent[] = [
  { id: 'a', title: 'Inspection', start: new Date('2026-08-11T09:00:00'), end: new Date('2026-08-11T11:00:00'), description: 'Hull review' },
  { id: 'b', title: 'Repair', start: new Date('2026-08-11T10:00:00'), end: new Date('2026-08-11T12:00:00'), color: 'var(--hl-color-success)' },
  { id: 'c', title: 'All hands', start: new Date('2026-08-11T00:00:00'), end: new Date('2026-08-12T00:00:00'), allDay: true },
]

describe('Scheduler revision-1 shared fixtures', () => {
  it('consumes every frozen case', () => {
    expect(sharedCases).toHaveLength(30)
    expect(sharedCases[0]?.id).toBe('scheduler.defaults-empty')
    expect(sharedCases.at(-1)?.id).toBe('scheduler.lane-classes')
  })

  it('renders defaults, all four views, all-day separation, overlap columns, and work calendar', () => {
    fixture(sharedCases, 'scheduler.defaults-empty')
    fixture(sharedCases, 'scheduler.day-week-layout')
    fixture(sharedCases, 'scheduler.all-day')
    fixture(sharedCases, 'scheduler.overlap')
    fixture(sharedCases, 'scheduler.work-calendar')
    render(<Scheduler accessibleName="Dock calendar" data={events} defaultDate={now} now={now} />)
    const root = screen.getByRole('region', { name: 'Dock calendar' })
    expect(root).toHaveAttribute('data-view', 'week')
    expect(screen.getAllByRole('button', { pressed: false }).map(button => button.textContent)).toEqual(['Day', 'Month', 'Agenda'])
    expect(document.querySelectorAll('.hl-scheduler__day-column')).toHaveLength(7)
    expect(document.querySelector('.hl-scheduler__all-day-band [data-event-key]')).toHaveTextContent('All hands')
    const layout = computeOverlapLayout(events.slice(0, 2))
    expect(layout.get(eventKey(events[0]!))?.columns).toBe(2)
    expect(document.querySelectorAll('[data-hour-row]')).toHaveLength(168)
  })

  it('preserves controlled view and date while emitting exactly one request', async () => {
    fixture(sharedCases, 'scheduler.controlled-view')
    fixture(sharedCases, 'scheduler.controlled-date')
    fixture(sharedCases, 'scheduler.navigation')
    const viewChange = vi.fn()
    const dateChange = vi.fn()
    const rendered = render(<Scheduler data={[]} now={now} onDateChange={dateChange} onViewChange={viewChange} selectedDate={now} view="day" />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Month' }))
    expect(viewChange).toHaveBeenCalledOnce()
    expect(viewChange).toHaveBeenCalledWith('month')
    expect(screen.getByRole('region')).toHaveAttribute('data-view', 'day')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Next' }))
    expect(dateChange).toHaveBeenCalledOnce()
    expect(dateChange.mock.calls[0]?.[0]).toEqual(new Date('2026-08-12T09:30:00'))
    expect(screen.getByText(/August 11/)).toBeInTheDocument()
    rendered.rerender(<Scheduler data={[]} now={now} onDateChange={dateChange} onViewChange={viewChange} selectedDate={new Date('2026-08-18T09:30:00')} view="day" />)
    expect(screen.getByText(/August 18/)).toBeInTheDocument()
  })

  it('owns uncontrolled view/date and Today follows deterministic now', async () => {
    fixture(sharedCases, 'scheduler.uncontrolled-view')
    const dateChange = vi.fn()
    render(<Scheduler data={[]} defaultDate={new Date('2026-07-01T09:00:00')} defaultView="day" now={now} onDateChange={dateChange} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Month' }))
    expect(screen.getByRole('region')).toHaveAttribute('data-view', 'month')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Today' }))
    expect(dateChange).toHaveBeenLastCalledWith(now)
    expect(screen.getByText('August 2026')).toBeInTheDocument()
  })

  it('limits custom views and drills month overflow into day view', async () => {
    fixture(sharedCases, 'scheduler.custom-views')
    fixture(sharedCases, 'scheduler.month-more-drilldown')
    const crowded = Array.from({ length: 5 }, (_, index): SchedulerEvent => ({
      id: index,
      title: `Event ${index}`,
      start: new Date(2026, 7, 11, index + 8),
      end: new Date(2026, 7, 11, index + 9),
    }))
    render(<Scheduler data={crowded} defaultDate={now} defaultView="month" now={now} views={[{ type: 'month' }, { type: 'day' }]} />)
    expect(screen.queryByRole('button', { name: 'Week' })).toBeNull()
    await userEvent.setup().click(screen.getByRole('button', { name: '+2 more' }))
    expect(screen.getByRole('region')).toHaveAttribute('data-view', 'day')
    expect(document.querySelectorAll('.hl-scheduler__day-column')).toHaveLength(1)
  })

  it('normalizes aliased fields, preserves extensions, and merges recurrence exceptions', () => {
    fixture(sharedCases, 'scheduler.event-normalization')
    fixture(sharedCases, 'scheduler.model-fields')
    fixture(sharedCases, 'scheduler.recurrence-exceptions')
    const normalized = normalizeEvents([{ key: 'x', name: 'Mapped', begins: '2026-08-11T09:00:00Z', finishes: '2026-08-11T10:00:00Z', extra: 'keep' }], { id: 'key', title: 'name', start: 'begins', end: 'finishes' })[0]!
    expect(normalized).toMatchObject({ id: 'x', title: 'Mapped', extra: 'keep' })
    expect(normalized.start).toBeInstanceOf(Date)
    const master: SchedulerEvent = { id: 'r', title: 'Round', start: new Date('2026-08-10T09:00:00Z'), end: new Date('2026-08-10T10:00:00Z'), recurrenceRule: 'FREQ=DAILY;COUNT=3' }
    const replacement: SchedulerEvent = { id: 'r-ex', title: 'Changed', start: new Date('2026-08-11T12:00:00Z'), end: new Date('2026-08-11T13:00:00Z'), recurrenceId: 'r', originalStart: new Date('2026-08-11T09:00:00Z') }
    const rendered = buildRenderedEvents([master, replacement], new Date('2026-08-10T00:00:00Z'), new Date('2026-08-13T00:00:00Z'))
    expect(rendered).toHaveLength(3)
    expect(rendered.filter(value => value.title === 'Changed')).toHaveLength(1)
    expect(rendered.some(value => value.start.toISOString() === '2026-08-11T09:00:00.000Z')).toBe(false)
  })

  it('parses and expands the bounded RRULE subset without mutating the master', () => {
    fixture(sharedCases, 'scheduler.rrule-parse')
    fixture(sharedCases, 'scheduler.recurrence-expand')
    expect(parseRRule('freq=weekly;byday=MO,WE,FR')?.byDay).toHaveLength(3)
    expect(parseRRule('FREQ=MONTHLY;BYDAY=-1FR')?.byDay?.[0]).toEqual({ ordinal: -1, day: 5 })
    expect(parseRRule('FREQ=HOURLY')).toBeNull()
    const master: SchedulerEvent = { id: 'r', title: 'Round', start: new Date('2026-08-10T09:00:00Z'), end: new Date('2026-08-10T10:00:00Z'), recurrenceRule: 'FREQ=DAILY;COUNT=3', extra: 'keep' }
    const occurrences = expandRecurrence(master, new Date('2026-08-01T00:00:00Z'), new Date('2026-09-01T00:00:00Z'))
    expect(occurrences).toHaveLength(3)
    expect(occurrences.every(value => value.end.getTime() - value.start.getTime() === 3_600_000 && value.recurrenceId === 'r' && value.extra === 'keep')).toBe(true)
    expect(master.recurrenceRule).toBe('FREQ=DAILY;COUNT=3')
    const weekly = expandRecurrence({ ...master, recurrenceRule: 'FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=6' }, new Date('2026-08-01'), new Date('2026-10-01'))
    expect(weekly.map(value => value.start.getDay())).toEqual([1, 3, 5, 1, 3, 5])
    const monthly = expandRecurrence({ ...master, start: new Date('2026-08-28T09:00:00'), end: new Date('2026-08-28T10:00:00'), recurrenceRule: 'FREQ=MONTHLY;BYDAY=-1FR;COUNT=3' }, new Date('2026-08-01'), new Date('2027-01-01'))
    expect(monthly).toHaveLength(3)
    expect(monthly.every(value => value.start.getDay() === 5)).toBe(true)
    const yearly = expandRecurrence({ ...master, start: new Date('2026-01-15T09:00:00'), end: new Date('2026-01-15T10:00:00'), recurrenceRule: 'FREQ=YEARLY;BYMONTH=1;BYMONTHDAY=15;COUNT=2' }, new Date('2026-01-01'), new Date('2028-01-01'))
    expect(yearly.map(value => [value.start.getMonth(), value.start.getDate()])).toEqual([[0, 15], [0, 15]])
  })

  it('emits one add, move, edit, and delete request with neutral values', async () => {
    const add = vi.fn()
    const update = vi.fn()
    const remove = vi.fn()
    const rendered = render(<Scheduler data={[events[0]!]} defaultDate={now} defaultView="day" now={now} onEventAdd={add} onEventDelete={remove} onEventUpdate={update} />)
    const day = document.querySelector<HTMLElement>('.hl-scheduler__day-column')!
    fireEvent.pointerDown(day, { clientY: 432 })
    fireEvent.pointerUp(day, { clientY: 456 })
    const createDialog = screen.getByRole('dialog', { name: 'New event' })
    await userEvent.setup().type(within(createDialog).getByRole('textbox'), 'Survey')
    await userEvent.setup().click(within(createDialog).getByRole('button', { name: 'Save' }))
    expect(add).toHaveBeenCalledOnce()
    expect(add.mock.calls[0]?.[0]).toMatchObject({ title: 'Survey' })
    expect(add.mock.calls[0]?.[0].start).toBeInstanceOf(Date)

    const eventNode = document.querySelector<HTMLElement>('[data-event-key]')!
    fireEvent.dragStart(eventNode)
    fireEvent.dragOver(day)
    fireEvent.drop(day, { clientY: 480 })
    expect(update).toHaveBeenCalledOnce()
    expect(update.mock.calls[0]?.[0]).toMatchObject({ id: 'a', title: 'Inspection' })

    await userEvent.setup().click(screen.getByRole('button', { name: /Inspection:/ }))
    const editDialog = screen.getByRole('dialog', { name: 'Edit event' })
    const title = within(editDialog).getByRole('textbox')
    await userEvent.setup().clear(title)
    await userEvent.setup().type(title, 'Updated inspection')
    await userEvent.setup().click(within(editDialog).getByRole('button', { name: 'Save' }))
    expect(update).toHaveBeenCalledTimes(2)
    expect(update.mock.calls[1]?.[0]).toMatchObject({ id: 'a', title: 'Updated inspection' })

    await userEvent.setup().click(screen.getByRole('button', { name: 'Delete event: Inspection' }))
    expect(remove).toHaveBeenCalledOnce()
    expect(remove).toHaveBeenCalledWith(events[0])
    rendered.unmount()
  })

  it('suppresses every mutation path in read-only mode, including month and agenda', async () => {
    fixture(sharedCases, 'scheduler.readonly')
    const add = vi.fn()
    const update = vi.fn()
    const remove = vi.fn()
    const rendered = render(<Scheduler data={events} defaultDate={now} defaultView="agenda" now={now} onEventAdd={add} onEventDelete={remove} onEventUpdate={update} readOnly />)
    expect(screen.queryByRole('button', { name: 'Edit event' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Delete event' })).toBeNull()
    rendered.rerender(<Scheduler data={events} defaultDate={now} defaultView="month" now={now} onEventAdd={add} onEventDelete={remove} onEventUpdate={update} readOnly />)
    fireEvent.doubleClick(document.querySelector('[data-event-key]')!)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(add).not.toHaveBeenCalled()
    expect(update).not.toHaveBeenCalled()
    expect(remove).not.toHaveBeenCalled()
  })

  it('scheduler.anchor-now-action', async () => {
    // 299: the authority styles hl-scheduler__now-action but only this lane emitted it, and this
    // row never asserted the button. It now asserts the class, the accessible name and the scroll
    // in this lane; the Blazor case asserts the same row in the other.
    const expected = fixture(sharedCases, 'scheduler.anchor-now-action').expected as {
      nowActionClass: string
      nowActionName: string
      scrollBehavior: string
      remeasureOnViewOrDateChange: boolean
      reanchorOnDateChange: boolean
      anchoredAtLoad: boolean
      dataUpdateKeepsScrollAndAction: boolean
    }
    const reduced = vi.fn().mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })
    Object.defineProperty(window, 'matchMedia', { configurable: true, value: reduced, writable: true })
    const { rerender } = render(<Scheduler accessibleName="Day schedule" data={events} defaultDate={now} now={now} readOnly view="day" />)
    // 299 review 3: the anchor is a layout effect, so the page opens anchored with no Now action.
    expect(expected.anchoredAtLoad).toBe(true)
    expect(screen.queryByRole('button', { name: expected.nowActionName })).toBeNull()

    const host = document.querySelector<HTMLElement>('.hl-scheduler__scroll')!
    const line = document.querySelector<HTMLElement>('[data-now-indicator]')!
    Object.defineProperty(line, 'offsetTop', { configurable: true, value: 500 })
    Object.defineProperty(host, 'clientHeight', { configurable: true, value: 120 })
    const scrollTo = vi.fn()
    host.scrollTo = scrollTo
    fireEvent.scroll(host)

    const action = screen.getByRole('button', { name: expected.nowActionName })
    expect(action.className).toBe(expected.nowActionClass)
    await userEvent.setup().click(action)
    expect(scrollTo).toHaveBeenCalledWith({ top: 500 - 120 / 3, behavior: expected.scrollBehavior })
    expect(screen.queryByRole('button', { name: expected.nowActionName })).toBeNull()

    // 299 review 1: the condition itself, not only the rendering. Neither Next nor Today scrolls or
    // resizes the host, so a lane that measures on scroll and resize alone keeps a dead action.
    expect(expected.remeasureOnViewOrDateChange).toBe(true)
    fireEvent.scroll(host)
    expect(screen.getByRole('button', { name: expected.nowActionName })).toBeTruthy()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Next' }))
    expect(document.querySelector('[data-now-indicator]')).toBeNull()
    expect(screen.queryByRole('button', { name: expected.nowActionName })).toBeNull()

    // 299 review 2: the date change re-anchors the host onto the now line and clears the flag, so a
    // stale scroll position cannot resurrect the action. Both steps observe the rendered DOM only.
    expect(expected.reanchorOnDateChange).toBe(true)
    host.scrollTop = 900
    await userEvent.setup().click(screen.getByRole('button', { name: 'Previous' }))
    expect(document.querySelector('[data-now-indicator]')).toBeTruthy()
    expect(screen.queryByRole('button', { name: expected.nowActionName })).toBeNull()

    await userEvent.setup().click(screen.getByRole('button', { name: 'Next' }))
    host.scrollTop = 900
    await userEvent.setup().click(screen.getByRole('button', { name: 'Today' }))
    expect(document.querySelector('[data-now-indicator]')).toBeTruthy()
    expect(screen.queryByRole('button', { name: expected.nowActionName })).toBeNull()

    // 299 review 3: the anchor effect excludes the rendered event count, so a data update leaves the
    // scroll host where the user put it and keeps the action they are looking at; only the measure
    // effect re-runs. The Blazor case asserts the same row in the other lane.
    expect(expected.dataUpdateKeepsScrollAndAction).toBe(true)
    host.scrollTop = 900
    fireEvent.scroll(host)
    expect(screen.getByRole('button', { name: expected.nowActionName })).toBeTruthy()
    const added: SchedulerEvent = { id: 'added', title: 'Added', start: now, end: new Date(now.getTime() + 3_600_000) }
    rerender(<Scheduler accessibleName="Day schedule" data={[...events, added]} defaultDate={now} now={now} readOnly view="day" />)
    expect(host.scrollTop).toBe(900)
    expect(screen.getByRole('button', { name: expected.nowActionName })).toBeTruthy()
  })

  it('scheduler.lane-classes', async () => {
    // 282 s8: the Blazor lane spelled the scroll box __body, the month date a __month-date button,
    // month chips __month-event, the agenda __agenda-event/__agenda-main/__delete and the stacked
    // recurrence choices __recurrence-actions. This asserts the one spelling in this lane; the
    // Blazor case asserts the same fixture row in the other.
    const expected = fixture(sharedCases, 'scheduler.lane-classes').expected as {
      scrollClasses: string[]
      monthClasses: string[]
      outsideMonthAttribute: string
      activeViewSelector: string
      agendaClasses: string[]
      editorFieldClasses: string[]
      retiredClasses: string[]
    }
    const crowded = Array.from({ length: 5 }, (_, index): SchedulerEvent => ({
      id: index,
      title: `Event ${index}`,
      start: new Date(2026, 7, 11, index + 8),
      end: new Date(2026, 7, 11, index + 9),
    }))
    render(<Scheduler data={crowded} defaultDate={now} now={now} onEventDelete={vi.fn()} onEventUpdate={vi.fn()} view="month" />)
    for (const className of [...expected.scrollClasses, ...expected.monthClasses]) {
      expect(document.querySelectorAll(`.${className}`).length).toBeGreaterThan(0)
    }
    expect(document.querySelector('.hl-scheduler__month-day')).toHaveAttribute(expected.outsideMonthAttribute)
    expect(document.querySelector('.hl-scheduler__month-day > time')).not.toBeNull()
    expect(document.querySelector(expected.activeViewSelector)).not.toBeNull()

    render(<Scheduler data={crowded} defaultDate={now} now={now} onEventDelete={vi.fn()} onEventUpdate={vi.fn()} view="agenda" />)
    for (const className of expected.agendaClasses) {
      expect(document.querySelectorAll(`.${className}`).length).toBeGreaterThan(0)
    }
    expect(document.querySelector('.hl-scheduler__agenda')?.tagName).toBe('OL')

    await userEvent.setup().click(screen.getAllByRole('button', { name: 'Edit event' })[0]!)
    for (const className of expected.editorFieldClasses) {
      expect(document.querySelectorAll(`.${className}`).length).toBeGreaterThan(0)
    }
    for (const className of expected.retiredClasses) {
      expect(document.body.innerHTML).not.toContain(className)
    }
  })

  it('emits provider-neutral delete and recurrence occurrence/series requests once', async () => {
    fixture(sharedCases, 'scheduler.event-create-move-edit-delete')
    fixture(sharedCases, 'scheduler.recurrence-dialog')
    const update = vi.fn()
    const remove = vi.fn()
    const master: SchedulerEvent = { id: 'r', title: 'Round', start: new Date('2026-08-11T09:00:00'), end: new Date('2026-08-11T10:00:00'), recurrenceRule: 'FREQ=DAILY;COUNT=2' }
    render(<Scheduler data={[master]} defaultDate={now} defaultView="agenda" now={now} onEventDelete={remove} onEventUpdate={update} />)
    const deleteButtons = screen.getAllByRole('button', { name: 'Delete event' })
    await userEvent.setup().click(deleteButtons[0]!)
    const dialog = screen.getByRole('dialog', { name: 'Delete recurring event' })
    await userEvent.setup().click(within(dialog).getByRole('button', { name: 'Delete this event' }))
    expect(update).toHaveBeenCalledOnce()
    expect(update.mock.calls[0]?.[0]).toMatchObject({ id: 'r', recurrenceRule: 'FREQ=DAILY;COUNT=2' })
    expect(update.mock.calls[0]?.[0].recurrenceExceptions).toHaveLength(1)
  })
})
