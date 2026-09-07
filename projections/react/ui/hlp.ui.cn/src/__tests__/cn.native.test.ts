import { describe, expect, it } from 'vitest'
import { cn } from '../cn'

describe('cn native behavior', () => {
  it('matches clsx flattening for strings, numbers, arrays, and object maps', () => {
    expect(cn('root', 12, ['child', ['leaf']], { enabled: true, disabled: false })).toBe(
      'root 12 child leaf enabled',
    )
  })

  it('drops false, null, undefined, and empty values', () => {
    expect(cn(false, null, undefined, '', 0, Number.NaN)).toBe('')
  })

  it('uses tailwind-merge last-applicable-token conflict resolution', () => {
    expect(cn('p-2', 'hover:p-3', 'p-4', 'hover:p-6', 'focus:p-8')).toBe(
      'p-4 hover:p-6 focus:p-8',
    )
  })

  it('preserves arbitrary classes and resolves arbitrary utility conflicts', () => {
    expect(cn('tenant-theme', 'w-[12px]', 'tenant-theme', 'w-[2rem]')).toBe(
      'tenant-theme tenant-theme w-[2rem]',
    )
  })

  it('does not mutate nested class inputs', () => {
    const nested = ['root', ['child', { selected: true }]] as const
    const before = JSON.stringify(nested)
    expect(cn(nested)).toBe('root child selected')
    expect(JSON.stringify(nested)).toBe(before)
  })
})
