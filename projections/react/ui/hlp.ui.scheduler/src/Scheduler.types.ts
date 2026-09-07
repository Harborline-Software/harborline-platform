import type * as React from 'react'

export type SchedulerViewType = 'day' | 'week' | 'month' | 'agenda'
export type SchedulerEventId = string | number

export interface SchedulerView {
  readonly type: SchedulerViewType
  readonly title?: string
}

export interface SchedulerEvent {
  readonly id: SchedulerEventId
  readonly title: string
  readonly start: Date
  readonly end: Date
  readonly allDay?: boolean
  readonly color?: string
  readonly description?: string
  readonly resource?: SchedulerEventId
  readonly recurrenceRule?: string
  readonly recurrenceId?: SchedulerEventId
  readonly recurrenceExceptions?: readonly Date[]
  readonly originalStart?: Date
  readonly [key: string]: unknown
}

export interface SchedulerModelFields {
  readonly id?: string
  readonly title?: string
  readonly start?: string
  readonly end?: string
  readonly allDay?: string
  readonly description?: string
  readonly color?: string
  readonly resource?: string
  readonly recurrenceRule?: string
  readonly recurrenceId?: string
  readonly recurrenceExceptions?: string
  readonly originalStart?: string
}

export interface SchedulerWorkingHours {
  readonly start: number
  readonly end: number
}

/** Date#getDay convention: Sunday is 0 and Saturday is 6. */
export type SchedulerWorkDay = 0 | 1 | 2 | 3 | 4 | 5 | 6

export interface SchedulerProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'onChange'> {
  readonly data: readonly SchedulerEvent[] | readonly Record<string, unknown>[]
  readonly view?: SchedulerViewType
  readonly defaultView?: SchedulerViewType
  readonly onViewChange?: (view: SchedulerViewType) => void
  readonly views?: readonly SchedulerView[]
  readonly onEventAdd?: (event: SchedulerEvent) => void
  readonly onEventUpdate?: (event: SchedulerEvent) => void
  readonly onEventDelete?: (event: SchedulerEvent) => void
  readonly onDateChange?: (date: Date) => void
  readonly selectedDate?: Date
  readonly defaultDate?: Date
  readonly now?: Date
  readonly workingHours?: SchedulerWorkingHours
  readonly workDayStart?: number
  readonly workDayEnd?: number
  readonly workDays?: readonly SchedulerWorkDay[]
  readonly readOnly?: boolean
  readonly modelFields?: SchedulerModelFields
  readonly accessibleName?: string
}

export interface ParsedSchedulerRRule {
  readonly freq: 'DAILY' | 'WEEKLY' | 'MONTHLY' | 'YEARLY'
  readonly interval: number
  readonly count?: number
  readonly until?: Date
  readonly byDay?: readonly { readonly ordinal?: number; readonly day: SchedulerWorkDay }[]
  readonly byMonthDay?: readonly number[]
  readonly byMonth?: readonly number[]
}
