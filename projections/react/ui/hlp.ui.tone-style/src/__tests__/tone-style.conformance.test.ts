import { describe, expect, expectTypeOf, it } from 'vitest'
import type { LensTone } from '@harborline-platform/hlp.ui.aspect-lens'

import { toneStyle, type ToneStyle } from '../index'
import { fixture, qualityCases, sharedCases } from './fixtures'

const tones: readonly LensTone[] = [
  'accent',
  'warning',
  'danger',
  'success',
  'info',
  'muted',
  'sensitivity-none',
  'sensitivity-low',
  'sensitivity-medium',
  'sensitivity-high',
]

describe('toneStyle revision-1 shared fixtures', () => {
  it('consumes the complete frozen surface', () => {
    const expected = fixture(sharedCases, 'tone-style.complete-surface').expected as {
      exports: string[]
      count: number
    }
    expect(expected).toEqual({ exports: ['ToneStyle', 'toneStyle'], count: 2 })
    expect(typeof toneStyle).toBe('function')
    expectTypeOf<ToneStyle>().toEqualTypeOf<{
      swatch: string
      border: string
      softBg: string
      text: string
    }>()
  })

  it('resolves every closed tone to authored semantic variables only', () => {
    expect(fixture(sharedCases, 'tone-style.semantic-mappings').expected).toEqual({
      allFieldsSemanticVariables: true,
    })
    expect(fixture(sharedCases, 'tone-style.no-raw-palette').expected).toEqual({
      literalColors: 0,
      tailwindPaletteVariables: 0,
    })
    const semanticVariable = /^var\(--color-[a-z-]+\)$/
    const styles = tones.map(toneStyle)

    expect(styles).toHaveLength(tones.length)
    for (const style of styles) {
      expect(Object.values(style)).toHaveLength(4)
      expect(Object.values(style).every(value => semanticVariable.test(value))).toBe(true)
      expect(Object.values(style).some(value => /(?:blue|teal|amber|gray|red)-\d+/.test(value))).toBe(false)
    }
  })

  it('retains the authored info fallback', () => {
    expect(toneStyle('info')).toEqual(
      (fixture(sharedCases, 'tone-style.info-fallback').expected),
    )
  })

  it('escalates sensitivity through muted, accent, warning, and danger', () => {
    expect(fixture(sharedCases, 'tone-style.sensitivity-ramp').expected).toEqual({
      bands: ['muted', 'accent', 'warning', 'danger'],
    })
    expect([
      toneStyle('sensitivity-none'),
      toneStyle('sensitivity-low'),
      toneStyle('sensitivity-medium'),
      toneStyle('sensitivity-high'),
    ]).toEqual([
      toneStyle('muted'),
      toneStyle('accent'),
      toneStyle('warning'),
      toneStyle('danger'),
    ])
  })

  it('is total and deterministic for projection-equivalence inputs', () => {
    expect(tones.map(tone => toneStyle(tone))).toEqual(tones.map(tone => toneStyle(tone)))
    expect(fixture(sharedCases, 'tone-style.projection-equivalence').expected).toEqual({
      typescriptEqualsDotnet: true,
    })
  })
})

describe('toneStyle revision-1 quality fixtures', () => {
  it('pairs every swatch with a text signal and owns no user text', () => {
    expect(fixture(qualityCases, 'tone-style.quality.not-color-alone').expected).toEqual({
      consumerSignalRequired: true,
    })
    expect(tones.every(tone => toneStyle(tone).swatch.length > 0 && toneStyle(tone).text.length > 0)).toBe(true)
    expect(fixture(qualityCases, 'tone-style.quality.locale-independent').expected).toEqual({ ownedText: 0 })
  })

  it('delegates light, dark, and forced-color resolution to host semantic tokens', () => {
    expect(fixture(qualityCases, 'tone-style.quality.semantic-tokens').expected).toEqual({
      semanticVariablesOnly: true,
    })
    expect(fixture(qualityCases, 'tone-style.quality.light-dark').expected).toEqual({
      hostTokenResolution: true,
    })
    expect(fixture(qualityCases, 'tone-style.quality.forced-colors').expected).toEqual({
      hostOwnsOverride: true,
    })
  })
})
