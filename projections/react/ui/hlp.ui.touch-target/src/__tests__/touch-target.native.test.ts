import { describe, expect, it } from 'vitest'

import { touchTarget, touchTargetPseudo, touchTargetPseudoOverlay } from '../index'

describe('Touch Target native behavior', () => {
  it('preserves the exact pinned grow-box utility string', () => {
    expect(touchTarget).toBe('min-h-11 min-w-11')
  })

  it('preserves the exact pinned unpositioned overlay utility string', () => {
    expect(touchTargetPseudoOverlay).toBe(
      "before:content-[''] before:absolute before:left-1/2 before:top-1/2 " +
        'before:h-11 before:w-11 before:-translate-x-1/2 before:-translate-y-1/2',
    )
    expect(touchTargetPseudoOverlay.split(/\s+/u)).not.toContain('relative')
  })

  it('adds only the pinned relative positioning token to the overlay', () => {
    expect(touchTargetPseudo).toBe(`relative ${touchTargetPseudoOverlay}`)
    expect(touchTargetPseudo.slice('relative '.length)).toBe(touchTargetPseudoOverlay)
  })

  it('contains no glyph-size, text, theme, or event semantics', () => {
    const all = `${touchTarget} ${touchTargetPseudoOverlay} ${touchTargetPseudo}`
    expect(all).not.toMatch(/(?:^|\s)(?:h|w)-(?:3\.5|4|5|6)(?:\s|$)/u)
    expect(all).not.toMatch(/(?:aria|role|focus|click|pointer|text-|bg-|color)/u)
  })
})
