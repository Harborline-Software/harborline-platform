import { describe, expect, it } from 'vitest'

import { toneStyle } from '../index'

describe('toneStyle React native behavior', () => {
  it('returns a stable semantic-token record without per-call allocation', () => {
    const first = toneStyle('accent')
    expect(toneStyle('accent')).toBe(first)
    expect(first).toEqual({
      swatch: 'var(--color-accent)',
      border: 'var(--color-accent)',
      softBg: 'var(--color-accent-soft)',
      text: 'var(--color-accent-foreground)',
    })
  })

  it('keeps equivalent muted and accent sensitivity bands byte-identical', () => {
    expect(toneStyle('sensitivity-none')).toEqual(toneStyle('muted'))
    expect(toneStyle('sensitivity-low')).toEqual(toneStyle('accent'))
  })
})
