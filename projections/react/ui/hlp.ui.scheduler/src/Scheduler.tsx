import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type {
  SchedulerEvent,
  SchedulerProps,
  SchedulerView,
  SchedulerViewType,
} from './Scheduler.types'
import {
  DEFAULT_WORK_DAYS,
  HOUR_HEIGHT,
  addDays,
  buildRenderedEvents,
  computeOverlapLayout,
  eventKey,
  monthDays,
  navigateDate,
  normalizeEvents,
  resolveWorkingHours,
  snappedDate,
  startOfDay,
  startOfWeek,
  validateWorkDays,
  visibleRange,
} from './scheduler-model'

const HOURS = Array.from({ length: 24 }, (_, hour) => hour)
const ALL_VIEWS: readonly SchedulerViewType[] = ['day', 'week', 'month', 'agenda']
const NARROW_QUERY = '(max-width: 48rem)'

function classNames(base: string, extra?: string): string {
  return extra ? `${base} ${extra}` : base
}

function sameDay(left: Date, right: Date): boolean {
  return left.getFullYear() === right.getFullYear() && left.getMonth() === right.getMonth() && left.getDate() === right.getDate()
}

function safeFormat(locale: string, value: Date, options: Intl.DateTimeFormatOptions): string {
  try {
    return new Intl.DateTimeFormat(locale, options).format(value)
  } catch {
    return new Intl.DateTimeFormat('en', options).format(value)
  }
}

function dateTimeInput(value: Date): string {
  const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

function useNarrowViewport(): boolean {
  const [narrow, setNarrow] = React.useState(() => typeof window !== 'undefined' && !!window.matchMedia?.(NARROW_QUERY).matches)
  React.useEffect(() => {
    if (!window.matchMedia) return undefined
    const media = window.matchMedia(NARROW_QUERY)
    const update = () => setNarrow(media.matches)
    update()
    media.addEventListener?.('change', update)
    return () => media.removeEventListener?.('change', update)
  }, [])
  return narrow
}

function useMinuteClock(controlled: Date | undefined): Date {
  const [clock, setClock] = React.useState(() => new Date())
  React.useEffect(() => {
    if (controlled) return undefined
    const timer = window.setInterval(() => setClock(new Date()), 60_000)
    return () => window.clearInterval(timer)
  }, [controlled])
  return controlled ?? clock
}

interface DialogFrameProps {
  readonly name: string
  readonly description?: string
  readonly onDismiss: () => void
  readonly children: React.ReactNode
}

function DialogFrame({ name, description, onDismiss, children }: DialogFrameProps) {
  const dialogRef = React.useRef<HTMLDivElement>(null)
  const titleId = React.useId()
  const descriptionId = React.useId()
  const returnFocus = React.useRef<HTMLElement | null>(typeof document !== 'undefined' && document.activeElement instanceof HTMLElement ? document.activeElement : null)

  React.useEffect(() => {
    dialogRef.current?.querySelector<HTMLElement>('button, input, [tabindex="0"]')?.focus()
    return () => returnFocus.current?.focus()
  }, [])

  const onKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape') {
      event.preventDefault()
      onDismiss()
      return
    }
    if (event.key !== 'Tab') return
    const focusable = [...(dialogRef.current?.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), [tabindex="0"]') ?? [])]
    if (focusable.length === 0) return
    const first = focusable[0]!
    const last = focusable[focusable.length - 1]!
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return (
    <div className="hl-scheduler__backdrop" data-scheduler-overlay onMouseDown={event => event.currentTarget === event.target && onDismiss()}>
      <div
        aria-describedby={description ? descriptionId : undefined}
        aria-labelledby={titleId}
        aria-modal="true"
        className="hl-scheduler__dialog"
        onKeyDown={onKeyDown}
        ref={dialogRef}
        role="dialog"
      >
        <h2 id={titleId}>{name}</h2>
        {description ? <p id={descriptionId}>{description}</p> : null}
        {children}
      </div>
    </div>
  )
}

interface EditorState {
  readonly mode: 'create' | 'edit'
  readonly event: SchedulerEvent
  readonly occurrenceOriginalStart?: Date
}

interface EventEditorProps {
  readonly state: EditorState
  readonly onCancel: () => void
  readonly onSave: (event: SchedulerEvent) => void
  readonly t: (key: string, vars?: Record<string, string | number>) => string
}

function EventEditor({ state, onCancel, onSave, t }: EventEditorProps) {
  const [title, setTitle] = React.useState(state.event.title)
  const [start, setStart] = React.useState(() => dateTimeInput(state.event.start))
  const [end, setEnd] = React.useState(() => dateTimeInput(state.event.end))
  const name = state.mode === 'create' ? t('scheduler.newEvent') : t('scheduler.editEvent')
  return (
    <DialogFrame name={name} onDismiss={onCancel}>
      <label className="hl-scheduler__editor-field"><span>{t('scheduler.eventTitle')}</span><input autoComplete="off" onChange={event => setTitle(event.currentTarget.value)} value={title} /></label>
      <label className="hl-scheduler__editor-field"><span>{t('scheduler.start')}</span><input onChange={event => setStart(event.currentTarget.value)} type="datetime-local" value={start} /></label>
      <label className="hl-scheduler__editor-field"><span>{t('scheduler.end')}</span><input onChange={event => setEnd(event.currentTarget.value)} type="datetime-local" value={end} /></label>
      <div className="hl-scheduler__dialog-actions">
        <button onClick={onCancel} type="button">{t('common.cancel')}</button>
        <button
          disabled={!title.trim() || !start || !end || new Date(end) < new Date(start)}
          onClick={() => onSave({ ...state.event, title: title.trim(), start: new Date(start), end: new Date(end) })}
          type="button"
        >{t('scheduler.save')}</button>
      </div>
    </DialogFrame>
  )
}

interface RecurrenceChoice {
  readonly mode: 'edit' | 'delete'
  readonly occurrence: SchedulerEvent
  readonly master?: SchedulerEvent
  readonly proposed?: SchedulerEvent
}

interface RecurrenceDialogProps {
  readonly choice: RecurrenceChoice
  readonly onCancel: () => void
  readonly onOccurrence: () => void
  readonly onSeries: () => void
  readonly t: (key: string, vars?: Record<string, string | number>) => string
}

function RecurrenceDialog({ choice, onCancel, onOccurrence, onSeries, t }: RecurrenceDialogProps) {
  const edit = choice.mode === 'edit'
  return (
    <DialogFrame
      description={t(edit ? 'scheduler.editRecurrenceQuestion' : 'scheduler.deleteRecurrenceQuestion')}
      name={t(edit ? 'scheduler.editRecurringEvent' : 'scheduler.deleteRecurringEvent')}
      onDismiss={onCancel}
    >
      <div className="hl-scheduler__dialog-actions hl-scheduler__dialog-actions--stacked">
        <button onClick={onOccurrence} type="button">{t(edit ? 'scheduler.editThisEvent' : 'scheduler.deleteThisEvent')}</button>
        <button onClick={onSeries} type="button">{t(edit ? 'scheduler.editSeries' : 'scheduler.deleteSeries')}</button>
        <button onClick={onCancel} type="button">{t('common.cancel')}</button>
      </div>
    </DialogFrame>
  )
}

interface EventChipProps {
  readonly event: SchedulerEvent
  readonly locale: string
  readonly readOnly: boolean
  readonly style?: React.CSSProperties
  readonly onEdit: (event: SchedulerEvent) => void
  readonly onDelete: (event: SchedulerEvent) => void
  readonly onDragStart?: (event: SchedulerEvent) => void
  readonly t: (key: string, vars?: Record<string, string | number>) => string
}

function EventChip({ event, locale, readOnly, style, onEdit, onDelete, onDragStart, t }: EventChipProps) {
  const start = safeFormat(locale, event.start, { hour: 'numeric', minute: '2-digit' })
  const end = safeFormat(locale, event.end, { hour: 'numeric', minute: '2-digit' })
  const recurrence = event.recurrenceId === undefined ? '' : `, ${t(event.originalStart ? 'scheduler.recurringEventModified' : 'scheduler.recurringEvent')}`
  const label = `${event.title}: ${start} – ${end}${recurrence}`
  return (
    <div
      className="hl-scheduler__event"
      data-event-key={eventKey(event)}
      data-recurrence={event.recurrenceId === undefined ? undefined : event.originalStart ? 'exception' : 'series'}
      draggable={!readOnly}
      onDragStart={() => !readOnly && onDragStart?.(event)}
      style={{ '--hl-scheduler-event-color': event.color ?? 'var(--hl-scheduler-accent)', ...style } as React.CSSProperties}
    >
      {readOnly ? (
        <div aria-label={label} className="hl-scheduler__event-main" role="img">
          <span className="hl-scheduler__event-title">{event.title}</span>
          {!event.allDay ? <span className="hl-scheduler__event-time">{start}–{end}</span> : null}
          {event.recurrenceId !== undefined ? <span aria-hidden="true" className="hl-scheduler__recurrence">↻</span> : null}
        </div>
      ) : (
        <button aria-label={label} className="hl-scheduler__event-main" onClick={() => onEdit(event)} type="button">
          <span className="hl-scheduler__event-title">{event.title}</span>
          {!event.allDay ? <span className="hl-scheduler__event-time">{start}–{end}</span> : null}
          {event.recurrenceId !== undefined ? <span aria-hidden="true" className="hl-scheduler__recurrence">↻</span> : null}
        </button>
      )}
      {!readOnly ? <button aria-label={`${t('scheduler.deleteEvent')}: ${event.title}`} onClick={click => { click.stopPropagation(); onDelete(event) }} type="button">×</button> : null}
    </div>
  )
}

interface TimeGridProps {
  readonly days: readonly Date[]
  readonly events: readonly SchedulerEvent[]
  readonly locale: string
  readonly now: Date
  readonly readOnly: boolean
  readonly workDays: readonly number[]
  readonly workStart: number
  readonly workEnd: number
  readonly onCreate: (start: Date, end: Date) => void
  readonly onMove: (event: SchedulerEvent, start: Date) => void
  readonly onEdit: (event: SchedulerEvent) => void
  readonly onDelete: (event: SchedulerEvent) => void
  readonly t: (key: string, vars?: Record<string, string | number>) => string
}

function TimeGrid({ days, events, locale, now, readOnly, workDays, workStart, workEnd, onCreate, onMove, onEdit, onDelete, t }: TimeGridProps) {
  const pointerStart = React.useRef<{ day: Date; hour: number } | null>(null)
  const dragged = React.useRef<SchedulerEvent | null>(null)
  const allDay = events.filter(event => event.allDay && days.some(day => sameDay(day, event.start)))
  return (
    <div className="hl-scheduler__time-view" style={{ '--hl-scheduler-day-count': days.length } as React.CSSProperties}>
      <div aria-hidden="true" className="hl-scheduler__time-gutter-heading" />
      {days.map(day => <div className="hl-scheduler__day-heading" key={day.toISOString()}>{safeFormat(locale, day, { weekday: 'short', month: 'short', day: 'numeric' })}</div>)}
      <div className="hl-scheduler__all-day-label">{t('scheduler.allDay')}</div>
      <div className="hl-scheduler__all-day-band">
        {allDay.map(event => <EventChip event={event} key={eventKey(event)} locale={locale} onDelete={onDelete} onEdit={onEdit} readOnly={readOnly} t={t} />)}
      </div>
      <div aria-hidden="true" className="hl-scheduler__hour-labels">
        {HOURS.map(hour => <span key={hour} style={{ insetBlockStart: hour * HOUR_HEIGHT }}>{safeFormat(locale, new Date(2000, 0, 1, hour), { hour: 'numeric' })}</span>)}
      </div>
      {days.map(day => {
        const dayEvents = events.filter(event => !event.allDay && sameDay(day, event.start))
        const overlaps = computeOverlapLayout(dayEvents)
        const isWorkDay = workDays.includes(day.getDay())
        const isToday = sameDay(day, now)
        return (
          <div
            className="hl-scheduler__day-column"
            data-day={day.toISOString().slice(0, 10)}
            data-work-day={isWorkDay}
            key={day.toISOString()}
            onDragOver={event => !readOnly && event.preventDefault()}
            onDrop={event => {
              if (readOnly || !dragged.current) return
              event.preventDefault()
              const rect = event.currentTarget.getBoundingClientRect()
              const hour = (event.clientY - rect.top) / HOUR_HEIGHT
              onMove(dragged.current, snappedDate(day, hour))
              dragged.current = null
            }}
            onPointerDown={event => {
              if (readOnly || event.target !== event.currentTarget) return
              const rect = event.currentTarget.getBoundingClientRect()
              pointerStart.current = { day, hour: (event.clientY - rect.top) / HOUR_HEIGHT }
            }}
            onPointerUp={event => {
              if (readOnly || !pointerStart.current || event.target !== event.currentTarget) return
              const rect = event.currentTarget.getBoundingClientRect()
              const start = snappedDate(pointerStart.current.day, pointerStart.current.hour)
              const end = snappedDate(day, Math.max(pointerStart.current.hour + 0.5, (event.clientY - rect.top) / HOUR_HEIGHT))
              pointerStart.current = null
              onCreate(start, end)
            }}
          >
            {HOURS.map(hour => <span className={hour < workStart || hour >= workEnd || !isWorkDay ? 'hl-scheduler__hour hl-scheduler__hour--off' : 'hl-scheduler__hour'} data-hour-row={hour} key={hour} />)}
            {isToday ? <span className="hl-scheduler__now-line" data-now-indicator style={{ insetBlockStart: (now.getHours() + now.getMinutes() / 60) * HOUR_HEIGHT }}><span className="hl-scheduler__sr-only">{t('scheduler.nowSrText', { time: safeFormat(locale, now, { hour: 'numeric', minute: '2-digit' }) })}</span></span> : null}
            {dayEvents.map(event => {
              const position = overlaps.get(eventKey(event)) ?? { column: 0, columns: 1 }
              const startHour = event.start.getHours() + event.start.getMinutes() / 60
              const duration = Math.max(0.5, (event.end.getTime() - event.start.getTime()) / 3_600_000)
              return (
                <EventChip
                  event={event}
                  key={eventKey(event)}
                  locale={locale}
                  onDelete={onDelete}
                  onDragStart={value => { dragged.current = value }}
                  onEdit={onEdit}
                  readOnly={readOnly}
                  style={{
                    insetBlockStart: startHour * HOUR_HEIGHT,
                    blockSize: duration * HOUR_HEIGHT,
                    insetInlineStart: `${position.column / position.columns * 100}%`,
                    inlineSize: `${100 / position.columns}%`,
                  }}
                  t={t}
                />
              )
            })}
          </div>
        )
      })}
    </div>
  )
}

interface MonthViewProps extends Pick<TimeGridProps, 'events' | 'locale' | 'readOnly' | 'onEdit' | 'onDelete' | 't'> {
  readonly date: Date
  readonly onDrilldown: (date: Date) => void
}

function MonthView({ date, events, locale, readOnly, onEdit, onDelete, onDrilldown, t }: MonthViewProps) {
  const days = monthDays(date)
  return (
    <div className="hl-scheduler__month">
      {days.slice(0, 7).map(day => <div className="hl-scheduler__month-weekday" key={`head-${day.getDay()}`}>{safeFormat(locale, day, { weekday: 'short' })}</div>)}
      {days.map(day => {
        const dayEvents = events.filter(event => sameDay(day, event.start)).slice().sort((a, b) => a.start.getTime() - b.start.getTime())
        return (
          <div className="hl-scheduler__month-day" data-outside-month={day.getMonth() !== date.getMonth()} key={day.toISOString()}>
            <time dateTime={day.toISOString().slice(0, 10)}>{safeFormat(locale, day, { day: 'numeric' })}</time>
            {dayEvents.slice(0, 3).map(event => <EventChip event={event} key={eventKey(event)} locale={locale} onDelete={onDelete} onEdit={onEdit} readOnly={readOnly} t={t} />)}
            {dayEvents.length > 3 ? <button className="hl-scheduler__more" onClick={() => onDrilldown(day)} type="button">{t('scheduler.more', { count: dayEvents.length - 3 })}</button> : null}
          </div>
        )
      })}
    </div>
  )
}

interface AgendaViewProps extends Pick<TimeGridProps, 'events' | 'locale' | 'readOnly' | 'onEdit' | 'onDelete' | 't'> {
  readonly date: Date
}

function AgendaView({ date, events, locale, readOnly, onEdit, onDelete, t }: AgendaViewProps) {
  const upcoming = events.filter(event => event.end >= startOfDay(date)).sort((a, b) => a.start.getTime() - b.start.getTime())
  if (upcoming.length === 0) return <div className="hl-scheduler__empty" role="status">{t('scheduler.noUpcomingEvents')}</div>
  return (
    <ol className="hl-scheduler__agenda">
      {upcoming.map(event => (
        <li data-event-key={eventKey(event)} key={eventKey(event)}>
          <time dateTime={event.start.toISOString()}>{event.allDay ? t('scheduler.allDay') : safeFormat(locale, event.start, { dateStyle: 'medium', timeStyle: 'short' })}</time>
          <div><strong>{event.title}</strong>{event.description ? <p>{event.description}</p> : null}</div>
          {!readOnly ? <div className="hl-scheduler__agenda-actions"><button onClick={() => onEdit(event)} type="button">{t('scheduler.editEvent')}</button><button onClick={() => onDelete(event)} type="button">{t('scheduler.deleteEvent')}</button></div> : null}
        </li>
      ))}
    </ol>
  )
}

export function Scheduler({
  data,
  view: controlledView,
  defaultView = 'week',
  onViewChange,
  views,
  onEventAdd,
  onEventUpdate,
  onEventDelete,
  onDateChange,
  selectedDate,
  defaultDate,
  now: controlledNow,
  workingHours,
  workDayStart = 8,
  workDayEnd = 18,
  workDays = DEFAULT_WORK_DAYS,
  readOnly = false,
  modelFields,
  accessibleName,
  className,
  dir,
  'aria-label': ariaLabel,
  ...attributes
}: SchedulerProps) {
  const { direction, locale, t } = useHarborlineStrings()
  const resolvedViews = React.useMemo<readonly SchedulerView[]>(() => views ?? ALL_VIEWS.map(type => ({ type })), [views])
  const viewTypes = resolvedViews.map(value => value.type)
  if (new Set(viewTypes).size !== viewTypes.length || viewTypes.length === 0) throw new Error('invalid-view')
  if ((controlledView && !viewTypes.includes(controlledView)) || (!controlledView && !viewTypes.includes(defaultView))) throw new Error('invalid-view')
  const hours = resolveWorkingHours(workingHours, workDayStart, workDayEnd)
  const validatedWorkDays = validateWorkDays(workDays)
  const narrow = useNarrowViewport()
  const [internalView, setInternalView] = React.useState(defaultView)
  const [userPickedView, setUserPickedView] = React.useState(false)
  const [internalDate, setInternalDate] = React.useState(() => new Date(defaultDate ?? Date.now()))
  const activeView = controlledView ?? (!userPickedView && narrow && viewTypes.includes('agenda') ? 'agenda' : internalView)
  const activeDate = selectedDate ? new Date(selectedDate) : internalDate
  const now = useMinuteClock(controlledNow)
  const [rangeStart, rangeEnd] = visibleRange(activeView, activeDate)
  const renderedEvents = React.useMemo(() => buildRenderedEvents(data, rangeStart, rangeEnd, modelFields), [data, modelFields, rangeEnd.getTime(), rangeStart.getTime()])
  const [editor, setEditor] = React.useState<EditorState | null>(null)
  const [recurrence, setRecurrence] = React.useState<RecurrenceChoice | null>(null)
  const scrollRef = React.useRef<HTMLDivElement>(null)
  const [canScroll, setCanScroll] = React.useState(false)
  const [scrollEdge, setScrollEdge] = React.useState<'none' | 'start' | 'middle' | 'end'>('none')
  const [scrollStatus, setScrollStatus] = React.useState('')
  const [nowOffscreen, setNowOffscreen] = React.useState(false)
  const createSequence = React.useRef(0)

  const label = accessibleName?.trim() || (typeof ariaLabel === 'string' ? ariaLabel.trim() : '') || t('scheduler.agenda')
  const requestDate = (next: Date) => {
    const value = new Date(next)
    if (!selectedDate) setInternalDate(value)
    onDateChange?.(new Date(value))
  }
  const requestView = (next: SchedulerViewType) => {
    setUserPickedView(true)
    if (!controlledView) setInternalView(next)
    onViewChange?.(next)
  }
  const navigate = (delta: number) => requestDate(navigateDate(activeDate, activeView, delta))

  React.useEffect(() => {
    const host = scrollRef.current
    if (!host) return undefined
    const measure = () => {
      const overflow = host.scrollWidth > host.clientWidth + 1 || host.scrollHeight > host.clientHeight + 1
      setCanScroll(overflow)
      if (!overflow) {
        setScrollEdge('none')
        setScrollStatus('')
      } else {
        const horizontal = host.scrollWidth > host.clientWidth + 1
        const position = horizontal ? Math.abs(host.scrollLeft) : host.scrollTop
        const maximum = horizontal ? Math.max(0, host.scrollWidth - host.clientWidth) : Math.max(0, host.scrollHeight - host.clientHeight)
        setScrollEdge(position < 2 ? 'start' : maximum - position < 2 ? 'end' : 'middle')
      }
      const nowLine = host.querySelector<HTMLElement>('[data-now-indicator]')
      setNowOffscreen(!!nowLine && (nowLine.offsetTop < host.scrollTop || nowLine.offsetTop > host.scrollTop + host.clientHeight))
    }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(host)
    window.addEventListener('resize', measure)
    return () => { observer?.disconnect(); window.removeEventListener('resize', measure) }
  }, [activeView, renderedEvents.length])

  React.useLayoutEffect(() => {
    const host = scrollRef.current
    if (!host || (activeView !== 'day' && activeView !== 'week')) return
    const nowLine = host.querySelector<HTMLElement>('[data-now-indicator]')
    const workStart = host.querySelector<HTMLElement>(`[data-hour-row="${hours.start}"]`)
    const target = nowLine ?? workStart
    if (!target) return
    host.scrollTop = Math.max(0, target.offsetTop - (nowLine ? host.clientHeight / 3 : 0))
    setNowOffscreen(false)
  }, [activeDate.getTime(), activeView, hours.start])

  const scrollToNow = () => {
    const host = scrollRef.current
    const target = host?.querySelector<HTMLElement>('[data-now-indicator]')
    if (!host || !target) return
    const top = Math.max(0, target.offsetTop - host.clientHeight / 3)
    const reduced = !!window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
    if (typeof host.scrollTo === 'function') host.scrollTo({ top, behavior: reduced ? 'auto' : 'smooth' })
    else host.scrollTop = top
    setNowOffscreen(false)
  }

  const masterFor = (event: SchedulerEvent) => {
    const id = event.recurrenceId ?? (event.recurrenceRule ? event.id : undefined)
    if (id === undefined) return undefined
    return normalizeEvents(data, modelFields).find(candidate => candidate.id === id && candidate.recurrenceRule)
  }
  const beginAction = (mode: 'edit' | 'delete', event: SchedulerEvent) => {
    if (readOnly) return
    const master = masterFor(event)
    if (event.recurrenceId !== undefined || event.recurrenceRule) setRecurrence({ mode, occurrence: event, master })
    else if (mode === 'edit') setEditor({ mode: 'edit', event })
    else onEventDelete?.(event)
  }
  const saveEditor = (event: SchedulerEvent) => {
    if (!editor) return
    if (editor.mode === 'create') onEventAdd?.(event)
    else if (editor.occurrenceOriginalStart) {
      onEventUpdate?.({ ...event, recurrenceId: event.recurrenceId ?? event.id, recurrenceRule: undefined, recurrenceExceptions: undefined, originalStart: editor.occurrenceOriginalStart })
    } else onEventUpdate?.(event)
    setEditor(null)
  }
  const chooseOccurrence = () => {
    if (!recurrence) return
    const { mode, occurrence, master } = recurrence
    setRecurrence(null)
    if (mode === 'edit' && recurrence.proposed) {
      onEventUpdate?.({
        ...recurrence.proposed,
        recurrenceId: occurrence.recurrenceId ?? occurrence.id,
        recurrenceRule: undefined,
        recurrenceExceptions: undefined,
        originalStart: occurrence.originalStart ?? occurrence.start,
      })
    } else if (mode === 'edit') setEditor({ mode: 'edit', event: occurrence, occurrenceOriginalStart: occurrence.originalStart ?? occurrence.start })
    else if (master) onEventUpdate?.({ ...master, recurrenceExceptions: [...(master.recurrenceExceptions ?? []), occurrence.originalStart ?? occurrence.start] })
    else onEventDelete?.(occurrence)
  }
  const chooseSeries = () => {
    if (!recurrence) return
    const target = recurrence.master ?? recurrence.occurrence
    const mode = recurrence.mode
    const proposed = recurrence.proposed
    setRecurrence(null)
    if (mode === 'edit' && proposed) {
      const delta = proposed.start.getTime() - recurrence.occurrence.start.getTime()
      onEventUpdate?.({ ...target, start: new Date(target.start.getTime() + delta), end: new Date(target.end.getTime() + delta) })
    } else if (mode === 'edit') setEditor({ mode: 'edit', event: target })
    else onEventDelete?.(target)
  }
  const createEvent = (start: Date, end: Date) => {
    if (readOnly) return
    createSequence.current += 1
    setEditor({ mode: 'create', event: { id: `scheduler-new-${createSequence.current}`, title: '', start, end } })
  }
  const moveEvent = (event: SchedulerEvent, start: Date) => {
    if (readOnly) return
    const duration = event.end.getTime() - event.start.getTime()
    const proposed = { ...event, start, end: new Date(start.getTime() + duration) }
    const master = masterFor(event)
    if (event.recurrenceId !== undefined || event.recurrenceRule) setRecurrence({ mode: 'edit', occurrence: event, master, proposed })
    else onEventUpdate?.(proposed)
  }

  const timeDays = activeView === 'day' ? [startOfDay(activeDate)] : Array.from({ length: 7 }, (_, index) => addDays(startOfWeek(activeDate), index))
  const header = activeView === 'day'
    ? safeFormat(locale, activeDate, { dateStyle: 'full' })
    : activeView === 'week'
      ? `${safeFormat(locale, timeDays[0]!, { month: 'short', day: 'numeric' })} – ${safeFormat(locale, timeDays[6]!, { dateStyle: 'medium' })}`
      : safeFormat(locale, activeDate, { month: 'long', year: 'numeric' })

  const onScrollKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (!canScroll) return
    const host = event.currentTarget
    const amount = Math.max(40, host.clientWidth * 0.8)
    if (event.key === 'ArrowRight' || event.key === 'ArrowLeft') {
      event.preventDefault()
      const physical = (event.key === 'ArrowRight' ? 1 : -1) * (direction === 'rtl' ? -1 : 1)
      host.scrollLeft += amount * physical
    } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault()
      host.scrollTop += Math.max(40, host.clientHeight * 0.8) * (event.key === 'ArrowDown' ? 1 : -1)
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault()
      if (host.scrollWidth > host.clientWidth + 1) host.scrollLeft = event.key === 'Home' ? 0 : host.scrollWidth
      else host.scrollTop = event.key === 'Home' ? 0 : host.scrollHeight
    } else return
    const atStart = Math.abs(host.scrollLeft) < 2
    const atEnd = Math.abs(host.scrollWidth - host.clientWidth - Math.abs(host.scrollLeft)) < 2
    setScrollEdge(atStart ? 'start' : atEnd ? 'end' : 'middle')
    setScrollStatus(atStart ? t('scrollAffordance.start') : atEnd ? t('scrollAffordance.end') : t('scrollAffordance.middle'))
  }

  const onScroll = (event: React.UIEvent<HTMLDivElement>) => {
    const host = event.currentTarget
    const position = Math.abs(host.scrollLeft)
    const maximum = Math.max(0, host.scrollWidth - host.clientWidth)
    setScrollEdge(maximum < 2 ? 'none' : position < 2 ? 'start' : maximum - position < 2 ? 'end' : 'middle')
    const nowLine = host.querySelector<HTMLElement>('[data-now-indicator]')
    setNowOffscreen(!!nowLine && (nowLine.offsetTop < host.scrollTop || nowLine.offsetTop > host.scrollTop + host.clientHeight))
  }

  return (
    <div
      {...attributes}
      aria-label={label}
      className={classNames('hl-scheduler', className)}
      data-read-only={readOnly}
      data-view={activeView}
      dir={dir ?? direction}
      role="region"
    >
      <div className="hl-scheduler__toolbar" role="toolbar">
        <div className="hl-scheduler__nav">
          <button aria-label={t('scheduler.previous')} onClick={() => navigate(-1)} type="button">{direction === 'rtl' ? '›' : '‹'}</button>
          <button onClick={() => requestDate(new Date(now))} type="button">{t('scheduler.today')}</button>
          <button aria-label={t('scheduler.next')} onClick={() => navigate(1)} type="button">{direction === 'rtl' ? '‹' : '›'}</button>
        </div>
        {nowOffscreen ? <button className="hl-scheduler__now-action" onClick={scrollToNow} type="button">{t('scheduler.now')}</button> : null}
        <strong className="hl-scheduler__range">{header}</strong>
        <div aria-label={t('scheduler.agenda')} className="hl-scheduler__views" role="group">
          {resolvedViews.map(value => (
            <button aria-pressed={activeView === value.type} key={value.type} onClick={() => requestView(value.type)} type="button">
              {value.title ?? t(`scheduler.${value.type}`)}
            </button>
          ))}
        </div>
      </div>
      <div
        aria-label={activeView === 'week' ? t('scheduler.weekGridScrollable') : header}
        className="hl-scheduler__scroll"
        data-can-scroll={canScroll}
        data-scroll-edge={scrollEdge}
        onKeyDown={onScrollKeyDown}
        onScroll={onScroll}
        ref={scrollRef}
        tabIndex={canScroll ? 0 : -1}
      >
        {(activeView === 'day' || activeView === 'week') ? (
          <TimeGrid days={timeDays} events={renderedEvents} locale={locale} now={now} onCreate={createEvent} onDelete={event => beginAction('delete', event)} onEdit={event => beginAction('edit', event)} onMove={moveEvent} readOnly={readOnly} t={t} workDays={validatedWorkDays} workEnd={hours.end} workStart={hours.start} />
        ) : activeView === 'month' ? (
          <MonthView date={activeDate} events={renderedEvents} locale={locale} onDelete={event => beginAction('delete', event)} onDrilldown={date => { requestDate(date); requestView('day') }} onEdit={event => beginAction('edit', event)} readOnly={readOnly} t={t} />
        ) : (
          <AgendaView date={activeDate} events={renderedEvents} locale={locale} onDelete={event => beginAction('delete', event)} onEdit={event => beginAction('edit', event)} readOnly={readOnly} t={t} />
        )}
      </div>
      <div aria-atomic="true" aria-live="polite" className="hl-scheduler__sr-only">{scrollStatus}</div>
      {editor ? <EventEditor onCancel={() => setEditor(null)} onSave={saveEditor} state={editor} t={t} /> : null}
      {recurrence ? <RecurrenceDialog choice={recurrence} onCancel={() => setRecurrence(null)} onOccurrence={chooseOccurrence} onSeries={chooseSeries} t={t} /> : null}
    </div>
  )
}
