import { describe, expect, it } from 'vitest'

import type { GanttTask } from '../Gantt.types'
import { calendarDateToDay, createGanttScale, dayToDate, prepareGantt } from '../gantt-model'

const task: GanttTask = {
  id: 'a',
  title: 'Inspect hull',
  start: '1970-01-01',
  end: '1970-01-03',
}

describe('Gantt calendar and preparation behavior', () => {
  // Oracle: interface revision 1 requires inclusive, finite progress from zero through one hundred.
  it.each([0, 100])('accepts the inclusive progress endpoint %s', progress => {
    const prepared = prepareGantt([{ ...task, progress }], [])
    expect(prepared.tasks[0]?.task.progress).toBe(progress)
  })

  it.each([-1, 101, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'rejects progress outside the finite inclusive interval: %s',
    progress => {
      expect(() => prepareGantt([{ ...task, progress }], [])).toThrow('invalid-progress')
    },
  )

  // Oracle: Gregorian calendar dates and the Unix epoch, independent of the conversion helpers.
  it('preserves literal calendar dates on either side of the Unix epoch', () => {
    expect(calendarDateToDay('1969-12-31')).toBe(-1)
    expect(calendarDateToDay('1970-01-01')).toBe(0)
    expect(calendarDateToDay('1970-01-02')).toBe(1)
    expect(dayToDate(1).toISOString()).toBe('1970-01-02T00:00:00.000Z')
    expect(() => calendarDateToDay('2024-02-29')).not.toThrow()
  })

  it.each([
    '2026-02-29',
    '2026-02-30',
    '2026-00-01',
    '2026-13-01',
    '2026-01-00',
    '2026-01-32',
    'x1970-01-01',
    '1970-01-01x',
  ])('rejects a value that is not one complete valid calendar date: %s', value => {
    expect(() => calendarDateToDay(value)).toThrow('invalid-task-range')
  })

  it('preserves valid dependency order while excluding every kind of unknown endpoint', () => {
    const prepared = prepareGantt([
      task,
      { id: 'b', title: 'Repair deck', start: '1970-01-02', end: '1970-01-03' },
      { id: 'c', title: 'Close work', start: '1970-01-03', end: '1970-01-03' },
    ], [
      { fromId: 'c', toId: 'a' },
      { fromId: 'missing', toId: 'b' },
      { fromId: 'a', toId: 'missing' },
      { fromId: 'missing', toId: 'also-missing' },
      { fromId: 'a', toId: 'b' },
    ])

    // Oracle: interface invariant 3 and gantt.dependencies, with literal caller order and indices.
    expect(prepared.dependencies).toEqual([
      { dependency: { fromId: 'c', toId: 'a' }, fromIndex: 2, toIndex: 0 },
      { dependency: { fromId: 'a', toId: 'b' }, fromIndex: 0, toIndex: 1 },
    ])
    expect(prepared.ignoredDependencies).toBe(3)
    expect(prepared.tasks.map(value => value.task.id)).toEqual(['a', 'b', 'c'])
  })

  it('uses inclusive day marks and seven-day week spacing', () => {
    // Oracle: literal elapsed calendar-day offsets, not output from calendarDateToDay.
    expect(createGanttScale(0, 2, 'day').map(mark => mark.day)).toEqual([0, 1, 2])
    expect(createGanttScale(0, 14, 'week').map(mark => mark.day)).toEqual([0, 7, 14])
    expect(createGanttScale(0, 0, 'day').map(mark => mark.day)).toEqual([0])
  })

  it('places month marks at literal January, February, and March boundaries', () => {
    // Oracle: January has 31 days and February 1970 has 28, so these month starts are days 0, 31, 59.
    expect(createGanttScale(0, 59, 'month').map(mark => mark.day)).toEqual([0, 31, 59])
  })
})
