import { describe, expect, it } from 'vitest'

import { dateAdd, dateDiffDays, epochDay, today } from './date-math.js'

describe('epochDay', () => {
  it('uses the Unix epoch as zero and accounts for leap days', () => {
    expect(epochDay(1970, 1, 1)).toBe(0)
    expect(epochDay(2024, 3, 1) - epochDay(2024, 2, 28)).toBe(2)
  })
})

describe('today', () => {
  it('formats the UTC calendar date without local-time influence', () => {
    expect(today(new Date('2024-02-29T23:59:59.000Z'))).toBe('2024-02-29')
  })
})

describe('dateAdd', () => {
  it('adds days across year boundaries and parses date-time input as a date', () => {
    expect(dateAdd('2023-12-31', 1, 'day')).toBe('2024-01-01')
    expect(dateAdd('2024-02-29T12:00:00Z', 1, 'day')).toBe('2024-03-01')
  })

  it('clamps month and year additions to the target month length', () => {
    expect(dateAdd('2024-01-31', 1, 'month')).toBe('2024-02-29')
    expect(dateAdd('2024-02-29', 1, 'year')).toBe('2025-02-28')
  })

  it('characterizes current behavior for runtime invalid date inputs', () => {
    // characterizes current behavior — undocumented
    const parsedUnit: unknown = JSON.parse('"week"')
    // characterizes current behavior — undocumented
    const numericDate: unknown = 20240101

    expect(() => dateAdd('2024-01-01', 1, parsedUnit as string)).toThrow("unknown date unit 'week'")
    expect(() => dateAdd(numericDate as string, 1, 'day')).toThrow(TypeError)
  })
})

describe('dateDiffDays', () => {
  it('returns a signed count of whole calendar days', () => {
    expect(dateDiffDays('2024-03-01', '2024-02-28')).toBe(2)
    expect(dateDiffDays('2024-02-28', '2024-03-01')).toBe(-2)
  })
})
