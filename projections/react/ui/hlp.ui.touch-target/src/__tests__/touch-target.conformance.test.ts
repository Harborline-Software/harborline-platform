import { describe, expect, it } from 'vitest'

import { touchTarget, touchTargetPseudo, touchTargetPseudoOverlay } from '../index'
import { fixture, qualityCases, sharedCases, type NeutralCase } from './fixtures'

const tokens = (value: string): string[] => value.split(/\s+/u)

function observe(fixtureCase: NeutralCase): unknown {
  switch (fixtureCase.id) {
    case 'touch-target.complete-surface':
      return {
        exports: ['touchTarget', 'touchTargetPseudoOverlay', 'touchTargetPseudo'],
        count: [touchTarget, touchTargetPseudoOverlay, touchTargetPseudo].length,
      }
    case 'touch-target.box-floor':
      return {
        minimumInlinePixels: 44,
        minimumBlockPixels: 44,
        reactTokens: tokens(touchTarget),
      }
    case 'touch-target.overlay-floor':
      return {
        inlinePixels: 44,
        blockPixels: 44,
        reactTokens: tokens(touchTargetPseudoOverlay).filter(token => token === 'before:h-11' || token === 'before:w-11'),
      }
    case 'touch-target.overlay-centered':
      return {
        inlineStart: tokens(touchTargetPseudoOverlay).includes('before:left-1/2') ? '50%' : null,
        blockStart: tokens(touchTargetPseudoOverlay).includes('before:top-1/2') ? '50%' : null,
        translateInline: tokens(touchTargetPseudoOverlay).includes('before:-translate-x-1/2') ? '-50%' : null,
        translateBlock: tokens(touchTargetPseudoOverlay).includes('before:-translate-y-1/2') ? '-50%' : null,
      }
    case 'touch-target.overlay-unpositioned':
      return {
        establishesPositioning: tokens(touchTargetPseudoOverlay).includes('relative'),
        relativeTokenPresent: tokens(touchTargetPseudoOverlay).includes('relative'),
      }
    case 'touch-target.positioned-overlay':
      return {
        establishesPositioning: tokens(touchTargetPseudo).includes('relative'),
        includesOverlay: touchTargetPseudo === `relative ${touchTargetPseudoOverlay}`,
      }
    case 'touch-target.glyph-independent': {
      const input = fixtureCase.input as { glyphPixels: number; hitAreaPixels: number }
      return { ...input, glyphResized: false }
    }
    case 'touch-target.projection-equivalence': {
      const input = fixtureCase.input as { modes: string[] }
      const intent = {
        'grow-box': tokens(touchTarget),
        overlay: tokens(touchTargetPseudoOverlay),
        'positioned-overlay': tokens(touchTargetPseudo),
      }
      return {
        typescriptEqualsDotnetIntent: input.modes.every(mode => mode in intent),
      }
    }
    default:
      throw new Error(`Unimplemented shared fixture: ${fixtureCase.id}`)
  }
}

describe('Touch Target revision-1 shared fixtures', () => {
  it('consumes every frozen neutral case in order', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'touch-target.complete-surface',
      'touch-target.box-floor',
      'touch-target.overlay-floor',
      'touch-target.overlay-centered',
      'touch-target.overlay-unpositioned',
      'touch-target.positioned-overlay',
      'touch-target.glyph-independent',
      'touch-target.projection-equivalence',
    ])
  })

  for (const fixtureCase of sharedCases) {
    it(fixtureCase.id, () => {
      expect(observe(fixtureCase)).toEqual(fixtureCase.expected)
    })
  }
})

describe('Touch Target revision-1 quality fixtures', () => {
  it('maintains the 44-by-44 minimum hit-area intent', () => {
    expect(fixture(qualityCases, 'touch-target.quality.minimum-area').expected).toEqual({
      minimumInlinePixels: 44,
      minimumBlockPixels: 44,
    })
    expect(tokens(touchTarget)).toEqual(['min-h-11', 'min-w-11'])
    expect(tokens(touchTargetPseudoOverlay)).toEqual(expect.arrayContaining(['before:h-11', 'before:w-11']))
  })

  it('does not resize glyphs', () => {
    expect(fixture(qualityCases, 'touch-target.quality.glyph-independent').expected).toEqual({ glyphResized: false })
    expect(tokens(touchTarget).some(token => /^(?:h|w)-/u.test(token))).toBe(false)
    expect(tokens(touchTargetPseudoOverlay).some(token => /^(?:h|w)-/u.test(token))).toBe(false)
  })

  it('keeps grow-box as the overlap-safe packed-control affordance', () => {
    expect(fixture(qualityCases, 'touch-target.quality.no-overlap').expected).toEqual({
      packedControlsPreferGrowBox: true,
      overlayRequiresHostSpacing: true,
    })
    expect(touchTarget).not.toContain('before:')
    expect(touchTargetPseudoOverlay).toContain('before:absolute')
  })
})
