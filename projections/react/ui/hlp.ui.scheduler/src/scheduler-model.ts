import { expandRecurrence } from './SchedulerRecurrence'
import type {
  SchedulerEvent,
  SchedulerModelFields,
  SchedulerViewType,
  SchedulerWorkDay,
  SchedulerWorkingHours,
} from './Scheduler.types'

export const DAY_MS = 86_400_000
export const HOUR_HEIGHT = 48
export const SLOT_MINUTES = 30
export const DEFAULT_WORK_DAYS: readonly SchedulerWorkDay[] = [1, 2, 3, 4, 5]

const FIELD_DEFAULTS: Required<SchedulerModelFields> = {
  id: 'id', title: 'title', start: 'start', end: 'end', allDay: 'allDay',
  description: 'description', color: 'color', resource: 'resource', recurrenceRule: 'recurrenceRule',
  recurrenceId: 'recurrenceId', recurrenceExceptions: 'recurrenceExceptions', originalStart: 'originalStart',
}

function dateValue(value: unknown): Date | undefined {
  if (value instanceof Date) return new Date(value.getTime())
  if (typeof value === 'string' || typeof value === 'number') {
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? undefined : date
  }
  return undefined
}

export function normalizeEvent(raw: Readonly<Record<string, unknown>>, fields?: SchedulerModelFields): SchedulerEvent {
  const mapping = { ...FIELD_DEFAULTS, ...fields }
  const id = raw[mapping.id]
  const title = raw[mapping.title]
  const start = dateValue(raw[mapping.start])
  const end = dateValue(raw[mapping.end])
  if ((typeof id !== 'string' && typeof id !== 'number') || id === '') throw new Error('invalid-model-field')
  if (typeof title !== 'string') throw new Error('invalid-model-field')
  if (!start || !end) throw new Error('invalid-model-field')
  if (end.getTime() < start.getTime()) throw new Error('invalid-event-range')
  const exceptions = raw[mapping.recurrenceExceptions]
  const event: Record<string, unknown> = {
    ...raw,
    id,
    title,
    start,
    end,
    allDay: raw[mapping.allDay],
    description: raw[mapping.description],
    color: raw[mapping.color],
    resource: raw[mapping.resource],
    recurrenceRule: raw[mapping.recurrenceRule],
    recurrenceId: raw[mapping.recurrenceId],
    recurrenceExceptions: Array.isArray(exceptions)
      ? exceptions.map(dateValue).filter((value): value is Date => value !== undefined)
      : undefined,
    originalStart: dateValue(raw[mapping.originalStart]),
  }
  return event as SchedulerEvent
}

export function normalizeEvents(
  data: readonly SchedulerEvent[] | readonly Record<string, unknown>[],
  fields?: SchedulerModelFields,
): readonly SchedulerEvent[] {
  const seen = new Set<string>()
  return data.map(raw => {
    const event = normalizeEvent(raw, fields)
    const key = `${typeof event.id}:${String(event.id)}:${event.originalStart?.toISOString() ?? event.start.toISOString()}`
    if (seen.has(key)) throw new Error('duplicate-event-key')
    seen.add(key)
    return event
  })
}

export function startOfDay(value: Date): Date {
  const result = new Date(value)
  result.setHours(0, 0, 0, 0)
  return result
}

export function startOfWeek(value: Date): Date {
  const result = startOfDay(value)
  result.setDate(result.getDate() - result.getDay())
  return result
}

export function addDays(value: Date, days: number): Date {
  const result = new Date(value)
  result.setDate(result.getDate() + days)
  return result
}

export function visibleRange(view: SchedulerViewType, date: Date): readonly [Date, Date] {
  if (view === 'day') return [startOfDay(date), addDays(startOfDay(date), 1)]
  if (view === 'week') {
    const start = startOfWeek(date)
    return [start, addDays(start, 7)]
  }
  if (view === 'month') {
    const start = new Date(date.getFullYear(), date.getMonth(), 1)
    return [start, new Date(date.getFullYear(), date.getMonth() + 1, 1)]
  }
  return [startOfDay(date), new Date(date.getFullYear(), date.getMonth() + 6, date.getDate())]
}

export function buildRenderedEvents(
  data: readonly SchedulerEvent[] | readonly Record<string, unknown>[],
  rangeStart: Date,
  rangeEnd: Date,
  fields?: SchedulerModelFields,
): readonly SchedulerEvent[] {
  const events = normalizeEvents(data, fields)
  const masters = events.filter(event => event.recurrenceRule && event.recurrenceId === undefined)
  const replacements = events.filter(event => event.recurrenceId !== undefined && event.originalStart)
  const singles = events.filter(event => !event.recurrenceRule && event.recurrenceId === undefined)
  const occurrences = new Map<string, SchedulerEvent>()
  for (const master of masters) {
    for (const event of expandRecurrence(master, rangeStart, rangeEnd)) {
      occurrences.set(`${String(master.id)}:${event.start.toISOString()}`, event)
    }
  }
  for (const replacement of replacements) {
    occurrences.set(`${String(replacement.recurrenceId)}:${replacement.originalStart!.toISOString()}`, replacement)
  }
  return [...singles, ...occurrences.values()]
    .filter(event => event.start < rangeEnd && event.end > rangeStart)
    .sort((a, b) => a.start.getTime() - b.start.getTime() || String(a.id).localeCompare(String(b.id)))
}

export function resolveWorkingHours(
  workingHours: SchedulerWorkingHours | undefined,
  start: number,
  end: number,
): SchedulerWorkingHours {
  const result = workingHours ?? { start, end }
  if (!Number.isFinite(result.start) || !Number.isFinite(result.end) || result.start < 0 || result.end > 24 || result.start >= result.end) {
    throw new Error('invalid-work-hours')
  }
  return result
}

export function validateWorkDays(days: readonly SchedulerWorkDay[]): readonly SchedulerWorkDay[] {
  if (days.some(day => !Number.isInteger(day) || day < 0 || day > 6)) throw new Error('invalid-work-day')
  return [...new Set(days)]
}

export interface OverlapPosition {
  readonly column: number
  readonly columns: number
}

export function computeOverlapLayout(events: readonly SchedulerEvent[]): ReadonlyMap<string, OverlapPosition> {
  const sorted = [...events].sort((a, b) => a.start.getTime() - b.start.getTime() || a.end.getTime() - b.end.getTime())
  const result = new Map<string, OverlapPosition>()
  let index = 0
  while (index < sorted.length) {
    const cluster: SchedulerEvent[] = [sorted[index]!]
    let end = sorted[index]!.end.getTime()
    let next = index + 1
    while (next < sorted.length && sorted[next]!.start.getTime() < end) {
      cluster.push(sorted[next]!)
      end = Math.max(end, sorted[next]!.end.getTime())
      next += 1
    }
    cluster.forEach((event, column) => result.set(eventKey(event), { column, columns: cluster.length }))
    index = next
  }
  return result
}

export function eventKey(event: SchedulerEvent): string {
  return `${typeof event.id}:${String(event.id)}:${event.originalStart?.toISOString() ?? event.start.toISOString()}`
}

export function monthDays(date: Date): readonly Date[] {
  const first = new Date(date.getFullYear(), date.getMonth(), 1)
  const start = addDays(first, -first.getDay())
  return Array.from({ length: 42 }, (_, index) => addDays(start, index))
}

export function navigateDate(date: Date, view: SchedulerViewType, delta: number): Date {
  const result = new Date(date)
  if (view === 'day') result.setDate(result.getDate() + delta)
  else if (view === 'week' || view === 'agenda') result.setDate(result.getDate() + delta * 7)
  else result.setMonth(result.getMonth() + delta)
  return result
}

export function snappedDate(day: Date, fractionalHour: number): Date {
  const snapped = Math.max(0, Math.min(23.5, Math.round(fractionalHour * 2) / 2))
  const result = startOfDay(day)
  result.setMinutes(snapped * 60)
  return result
}
