import type {
  GanttColumn,
  GanttDependency,
  GanttTask,
  GanttZoom,
} from './Gantt.types'

const DAY_MS = 86_400_000
const MAX_SCALE_MARKS = 128

export interface PreparedGanttTask {
  readonly task: GanttTask
  readonly index: number
  readonly startDay: number
  readonly endDay: number
}

export interface PreparedGanttDependency {
  readonly dependency: GanttDependency
  readonly fromIndex: number
  readonly toIndex: number
}

export interface PreparedGantt {
  readonly tasks: readonly PreparedGanttTask[]
  readonly dependencies: readonly PreparedGanttDependency[]
  readonly minimumDay: number
  readonly maximumDay: number
  readonly validationOperations: number
  readonly ignoredDependencies: number
}

export interface GanttScaleMark {
  readonly key: string
  readonly day: number
  readonly offsetPercent: number
}

interface CalendarParts {
  readonly year: number
  readonly month: number
  readonly day: number
}

function calendarParts(value: string): CalendarParts | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return null
  const year = Number(match[1])
  const month = Number(match[2])
  const day = Number(match[3])
  const date = new Date(0)
  date.setUTCHours(0, 0, 0, 0)
  date.setUTCFullYear(year, month - 1, day)
  if (date.getUTCFullYear() !== year || date.getUTCMonth() !== month - 1 || date.getUTCDate() !== day) return null
  return { year, month, day }
}

export function calendarDateToDay(value: string): number {
  const parts = calendarParts(value)
  if (!parts) throw new Error('invalid-task-range')
  const date = new Date(0)
  date.setUTCHours(0, 0, 0, 0)
  date.setUTCFullYear(parts.year, parts.month - 1, parts.day)
  return Math.floor(date.getTime() / DAY_MS)
}

export function dayToDate(day: number): Date {
  return new Date(day * DAY_MS)
}

function validateColumns(columns: readonly GanttColumn[] | null | undefined): number {
  if (columns === null || columns === undefined) return 0
  for (const column of columns) {
    if (column.width !== undefined && (!Number.isFinite(column.width) || column.width <= 0)) {
      throw new Error('invalid-column-width')
    }
  }
  return columns.length
}

export function prepareGantt(
  tasks: readonly GanttTask[],
  dependencies: readonly GanttDependency[],
  columns?: readonly GanttColumn[] | null,
): PreparedGantt {
  const ids = new Map<string, number>()
  const preparedTasks: PreparedGanttTask[] = []
  let minimumDay = Number.POSITIVE_INFINITY
  let maximumDay = Number.NEGATIVE_INFINITY
  let validationOperations = validateColumns(columns)

  tasks.forEach((task, index) => {
    validationOperations += 1
    if (task.id.trim().length === 0) throw new Error('task-id-required')
    if (ids.has(task.id)) throw new Error('duplicate-task-id')
    if (task.title.trim().length === 0) throw new Error('task-title-required')
    const startDay = calendarDateToDay(task.start)
    const endDay = calendarDateToDay(task.end)
    if (endDay < startDay) throw new Error('invalid-task-range')
    if (task.progress !== undefined && (!Number.isFinite(task.progress) || task.progress < 0 || task.progress > 100)) {
      throw new Error('invalid-progress')
    }
    ids.set(task.id, index)
    minimumDay = Math.min(minimumDay, startDay)
    maximumDay = Math.max(maximumDay, endDay)
    preparedTasks.push({ task, index, startDay, endDay })
  })

  const preparedDependencies: PreparedGanttDependency[] = []
  let ignoredDependencies = 0
  for (const dependency of dependencies) {
    validationOperations += 1
    const fromIndex = ids.get(dependency.fromId)
    const toIndex = ids.get(dependency.toId)
    if (fromIndex === undefined || toIndex === undefined) {
      ignoredDependencies += 1
      continue
    }
    preparedDependencies.push({ dependency, fromIndex, toIndex })
  }

  return {
    tasks: preparedTasks,
    dependencies: preparedDependencies,
    minimumDay: preparedTasks.length === 0 ? 0 : minimumDay,
    maximumDay: preparedTasks.length === 0 ? 0 : maximumDay,
    validationOperations,
    ignoredDependencies,
  }
}

function dayMarks(minimumDay: number, maximumDay: number, unitDays: number): GanttScaleMark[] {
  const totalDays = maximumDay - minimumDay + 1
  const rawCount = Math.ceil(totalDays / unitDays)
  const step = unitDays * Math.max(1, Math.ceil(rawCount / MAX_SCALE_MARKS))
  const marks: GanttScaleMark[] = []
  for (let day = minimumDay; day <= maximumDay; day += step) {
    marks.push({ key: String(day), day, offsetPercent: (day - minimumDay) / totalDays * 100 })
  }
  return marks
}

function monthMarks(minimumDay: number, maximumDay: number): GanttScaleMark[] {
  const minimum = dayToDate(minimumDay)
  const maximum = dayToDate(maximumDay)
  const minimumMonth = minimum.getUTCFullYear() * 12 + minimum.getUTCMonth()
  const maximumMonth = maximum.getUTCFullYear() * 12 + maximum.getUTCMonth()
  const monthCount = maximumMonth - minimumMonth + 1
  const step = Math.max(1, Math.ceil(monthCount / MAX_SCALE_MARKS))
  const totalDays = maximumDay - minimumDay + 1
  const marks: GanttScaleMark[] = []
  for (let value = minimumMonth; value <= maximumMonth; value += step) {
    const year = Math.floor(value / 12)
    const month = value % 12
    const date = new Date(0)
    date.setUTCHours(0, 0, 0, 0)
    date.setUTCFullYear(year, month, 1)
    const day = Math.max(minimumDay, Math.floor(date.getTime() / DAY_MS))
    marks.push({ key: `${year}-${month}`, day, offsetPercent: (day - minimumDay) / totalDays * 100 })
  }
  return marks
}

export function createGanttScale(minimumDay: number, maximumDay: number, zoom: GanttZoom): readonly GanttScaleMark[] {
  if (maximumDay < minimumDay) return []
  if (zoom === 'month') return monthMarks(minimumDay, maximumDay)
  return dayMarks(minimumDay, maximumDay, zoom === 'week' ? 7 : 1)
}

export function taskGeometry(task: PreparedGanttTask, minimumDay: number, maximumDay: number): {
  readonly offsetPercent: number
  readonly widthPercent: number
} {
  const totalDays = maximumDay - minimumDay + 1
  return {
    offsetPercent: (task.startDay - minimumDay) / totalDays * 100,
    widthPercent: (task.endDay - task.startDay + 1) / totalDays * 100,
  }
}
