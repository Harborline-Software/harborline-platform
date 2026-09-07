import type {
  ParsedSchedulerRRule,
  SchedulerEvent,
  SchedulerWorkDay,
} from './Scheduler.types'

export const SCHEDULER_RECURRENCE_CAP = 1000

const DAY_CODES: Readonly<Record<string, SchedulerWorkDay>> = {
  SU: 0, MO: 1, TU: 2, WE: 3, TH: 4, FR: 5, SA: 6,
}

function parseUntil(value: string): Date | undefined {
  const dateTime = /^(\d{4})(\d{2})(\d{2})T(\d{2})(\d{2})(\d{2})Z$/.exec(value)
  if (dateTime) {
    const date = new Date(Date.UTC(
      Number(dateTime[1]), Number(dateTime[2]) - 1, Number(dateTime[3]),
      Number(dateTime[4]), Number(dateTime[5]), Number(dateTime[6]),
    ))
    return Number.isNaN(date.getTime()) ? undefined : date
  }
  const dateOnly = /^(\d{4})(\d{2})(\d{2})$/.exec(value)
  if (!dateOnly) return undefined
  const date = new Date(Date.UTC(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]), 23, 59, 59, 999))
  return Number.isNaN(date.getTime()) ? undefined : date
}

function integers(value: string | undefined, minimum: number, maximum: number): readonly number[] | undefined {
  if (!value) return undefined
  const parsed = value.split(',').map(Number).filter(candidate => Number.isInteger(candidate) && candidate >= minimum && candidate <= maximum)
  return parsed.length > 0 ? [...new Set(parsed)] : undefined
}

export function parseRRule(rule: string): ParsedSchedulerRRule | null {
  const parts = new Map<string, string>()
  for (const rawSegment of rule.trim().replace(/^RRULE:/i, '').split(';')) {
    const separator = rawSegment.indexOf('=')
    if (separator <= 0) continue
    parts.set(rawSegment.slice(0, separator).trim().toUpperCase(), rawSegment.slice(separator + 1).trim().toUpperCase())
  }
  const freq = parts.get('FREQ')
  if (freq !== 'DAILY' && freq !== 'WEEKLY' && freq !== 'MONTHLY' && freq !== 'YEARLY') return null
  const rawInterval = parts.get('INTERVAL')
  const interval = rawInterval === undefined ? 1 : Number(rawInterval)
  if (!Number.isInteger(interval) || interval < 1) return null
  const rawCount = parts.get('COUNT')
  const count = rawCount === undefined ? undefined : Number(rawCount)
  if (count !== undefined && (!Number.isInteger(count) || count < 1)) return null
  const rawUntil = parts.get('UNTIL')
  const until = rawUntil === undefined ? undefined : parseUntil(rawUntil)
  if (rawUntil !== undefined && until === undefined) return null

  const byDay = parts.get('BYDAY')?.split(',').flatMap(value => {
    const match = /^(-?\d+)?(SU|MO|TU|WE|TH|FR|SA)$/.exec(value)
    if (!match) return []
    const ordinal = match[1] === undefined ? undefined : Number(match[1])
    if (ordinal === 0 || (ordinal !== undefined && (ordinal < -5 || ordinal > 5))) return []
    const day = DAY_CODES[match[2]]
    return day === undefined ? [] : [{ ordinal, day }]
  })
  const byMonthDay = integers(parts.get('BYMONTHDAY'), 1, 28)
  const byMonth = integers(parts.get('BYMONTH'), 1, 12)
  return {
    freq,
    interval,
    ...(count === undefined ? {} : { count }),
    ...(until === undefined ? {} : { until }),
    ...(byDay && byDay.length > 0 ? { byDay } : {}),
    ...(byMonthDay === undefined ? {} : { byMonthDay }),
    ...(byMonth === undefined ? {} : { byMonth }),
  }
}

function clone(value: Date): Date {
  return new Date(value.getTime())
}

function withTime(value: Date, source: Date): Date {
  value.setHours(source.getHours(), source.getMinutes(), source.getSeconds(), source.getMilliseconds())
  return value
}

function nthWeekday(year: number, month: number, ordinal: number, weekday: SchedulerWorkDay): Date | undefined {
  if (ordinal > 0) {
    const first = new Date(year, month, 1)
    const day = 1 + (weekday - first.getDay() + 7) % 7 + (ordinal - 1) * 7
    return day > new Date(year, month + 1, 0).getDate() ? undefined : new Date(year, month, day)
  }
  const last = new Date(year, month + 1, 0)
  const day = last.getDate() - (last.getDay() - weekday + 7) % 7 - (Math.abs(ordinal) - 1) * 7
  return day < 1 ? undefined : new Date(year, month, day)
}

function candidates(cursor: Date, rule: ParsedSchedulerRRule, master: Date): readonly Date[] {
  if (rule.freq === 'DAILY') return [clone(cursor)]
  if (rule.freq === 'WEEKLY' && rule.byDay) {
    const week = clone(cursor)
    week.setDate(week.getDate() - week.getDay())
    return rule.byDay.map(value => {
      const date = clone(week)
      date.setDate(date.getDate() + value.day)
      return withTime(date, master)
    }).sort((a, b) => a.getTime() - b.getTime())
  }
  if (rule.freq === 'MONTHLY') {
    if (rule.byDay) {
      return rule.byDay.flatMap(value => {
        const date = nthWeekday(cursor.getFullYear(), cursor.getMonth(), value.ordinal ?? 1, value.day)
        return date ? [withTime(date, master)] : []
      }).sort((a, b) => a.getTime() - b.getTime())
    }
    if (rule.byMonthDay) {
      return rule.byMonthDay.map(day => withTime(new Date(cursor.getFullYear(), cursor.getMonth(), day), master))
    }
  }
  if (rule.freq === 'YEARLY') {
    const months = rule.byMonth ?? [master.getMonth() + 1]
    const days = rule.byMonthDay ?? [master.getDate()]
    return months.flatMap(month => days.flatMap(day => {
      const date = new Date(cursor.getFullYear(), month - 1, day)
      return date.getMonth() === month - 1 ? [withTime(date, master)] : []
    })).sort((a, b) => a.getTime() - b.getTime())
  }
  return [clone(cursor)]
}

function advance(cursor: Date, rule: ParsedSchedulerRRule): void {
  if (rule.freq === 'DAILY') cursor.setDate(cursor.getDate() + rule.interval)
  else if (rule.freq === 'WEEKLY') cursor.setDate(cursor.getDate() + rule.interval * 7)
  else if (rule.freq === 'MONTHLY') {
    const day = cursor.getDate()
    cursor.setDate(1)
    cursor.setMonth(cursor.getMonth() + rule.interval)
    cursor.setDate(Math.min(day, new Date(cursor.getFullYear(), cursor.getMonth() + 1, 0).getDate()))
  } else {
    const month = cursor.getMonth()
    cursor.setFullYear(cursor.getFullYear() + rule.interval)
    if (cursor.getMonth() !== month) cursor.setDate(0)
  }
}

function exceptionKeys(values: readonly Date[] | undefined): ReadonlySet<string> {
  return new Set((values ?? []).map(value => value.getHours() === 0 && value.getMinutes() === 0 && value.getSeconds() === 0
    ? `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
    : value.toISOString()))
}

function excluded(value: Date, keys: ReadonlySet<string>): boolean {
  const localDate = `${value.getFullYear()}-${String(value.getMonth() + 1).padStart(2, '0')}-${String(value.getDate()).padStart(2, '0')}`
  return keys.has(value.toISOString()) || keys.has(localDate)
}

export function expandRecurrence(master: SchedulerEvent, rangeStart: Date, rangeEnd: Date): SchedulerEvent[] {
  if (rangeEnd.getTime() <= rangeStart.getTime()) throw new RangeError('invalid-range')
  if (!master.recurrenceRule) return []
  const rule = parseRRule(master.recurrenceRule)
  if (!rule) return []
  const duration = master.end.getTime() - master.start.getTime()
  if (duration < 0) throw new Error('invalid-event-range')
  const exceptions = exceptionKeys(master.recurrenceExceptions)
  const results: SchedulerEvent[] = []
  const cursor = clone(master.start)
  let generated = 0
  let guard = 0

  while (generated < SCHEDULER_RECURRENCE_CAP && guard < SCHEDULER_RECURRENCE_CAP * 4) {
    guard += 1
    if (rule.count !== undefined && generated >= rule.count) break
    if (rule.until && cursor.getTime() > rule.until.getTime()) break
    if (cursor.getTime() >= rangeEnd.getTime() && !rule.count && !rule.until) break
    for (const start of candidates(cursor, rule, master.start)) {
      if (start.getTime() < master.start.getTime()) continue
      if (rule.count !== undefined && generated >= rule.count) break
      if (rule.until && start.getTime() > rule.until.getTime()) continue
      generated += 1
      const end = new Date(start.getTime() + duration)
      if (!excluded(start, exceptions) && start.getTime() < rangeEnd.getTime() && end.getTime() > rangeStart.getTime()) {
        results.push({
          ...master,
          start,
          end,
          recurrenceId: master.id,
          recurrenceRule: undefined,
          recurrenceExceptions: undefined,
          originalStart: undefined,
        })
      }
      if (generated >= SCHEDULER_RECURRENCE_CAP) break
    }
    advance(cursor, rule)
  }
  return results
}
