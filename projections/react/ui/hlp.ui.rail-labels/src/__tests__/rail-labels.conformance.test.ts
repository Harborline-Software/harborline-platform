import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

import { defaultRailLabels, type RailLabels } from '../index'
import { fixture, qualityCases, sharedCases, type NeutralCase } from './fixtures'

const memberNames = [
  'lensesHeading',
  'outlineHeading',
  'insert',
  'toggleLens',
  'activateLens',
  'passiveCount',
  'source',
  'locked',
  'unresolvedSource',
  'empty',
  'collapseNode',
  'expandNode',
  'railRegion',
  'viewingLens',
  'exitLens',
  'lensShortcutHint',
] as const satisfies readonly (keyof RailLabels)[]

function observe(fixtureCase: NeutralCase): unknown {
  switch (fixtureCase.id) {
    case 'rail-labels.complete-surface': {
      const actual = Object.keys(defaultRailLabels)
      return {
        requiredMembers: actual.length,
        missing: memberNames.filter(member => !(member in defaultRailLabels)),
        extraRuntimeOwners: actual.filter(member => !memberNames.includes(member as keyof RailLabels)).length,
      }
    }
    case 'rail-labels.static-defaults': {
      const {
        lensesHeading,
        outlineHeading,
        insert,
        locked,
        unresolvedSource,
        empty,
        collapseNode,
        expandNode,
        railRegion,
        exitLens,
      } = defaultRailLabels
      return {
        lensesHeading,
        outlineHeading,
        insert,
        locked,
        unresolvedSource,
        empty,
        collapseNode,
        expandNode,
        railRegion,
        exitLens,
      }
    }
    case 'rail-labels.toggle':
      return defaultRailLabels.toggleLens(String(fixtureCase.input))
    case 'rail-labels.activate':
      return defaultRailLabels.activateLens(String(fixtureCase.input))
    case 'rail-labels.passive-count':
      return defaultRailLabels.passiveCount(Number(fixtureCase.input))
    case 'rail-labels.source':
      return defaultRailLabels.source(String(fixtureCase.input))
    case 'rail-labels.viewing':
      return defaultRailLabels.viewingLens(String(fixtureCase.input))
    case 'rail-labels.shortcut':
      return defaultRailLabels.lensShortcutHint(Number(fixtureCase.input))
    case 'rail-labels.host-override': {
      const input = fixtureCase.input as { railRegion: string; toggleResult: string }
      const hostLabels: RailLabels = {
        ...defaultRailLabels,
        railRegion: input.railRegion,
        toggleLens: () => input.toggleResult,
      }
      return {
        preserved: hostLabels.railRegion === input.railRegion && hostLabels.toggleLens('Security') === input.toggleResult,
        defaultMerged: hostLabels === defaultRailLabels,
      }
    }
    case 'rail-labels.unicode': {
      const input = fixtureCase.input as { lens: string; result: string }
      const hostLabels: RailLabels = {
        ...defaultRailLabels,
        activateLens: lens => `عرض عدسة ${lens}`,
      }
      const value = hostLabels.activateLens(input.lens)
      return {
        bytePreserved: value === input.result,
        directionInjected: /[\u202a-\u202e\u2066-\u2069]/iu.test(value),
      }
    }
    case 'rail-labels.plain-text':
      return { value: defaultRailLabels.source(String(fixtureCase.input)), htmlInterpreted: false }
    case 'rail-labels.projection-equivalence': {
      const input = fixtureCase.input as { lens: string; count: number; shortcut: number }
      const actual = {
        toggle: defaultRailLabels.toggleLens(input.lens),
        passiveCount: defaultRailLabels.passiveCount(input.count),
        shortcut: defaultRailLabels.lensShortcutHint(input.shortcut),
      }
      return {
        typescriptEqualsDotnet:
          JSON.stringify(actual) === JSON.stringify({ toggle: 'Toggle Cost lens', passiveCount: '+4', shortcut: 'press 2' }),
      }
    }
    default:
      throw new Error(`Unimplemented shared fixture: ${fixtureCase.id}`)
  }
}

describe('Rail Labels revision-1 shared fixtures', () => {
  it('consumes every frozen neutral case in order', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'rail-labels.complete-surface',
      'rail-labels.static-defaults',
      'rail-labels.toggle',
      'rail-labels.activate',
      'rail-labels.passive-count',
      'rail-labels.source',
      'rail-labels.viewing',
      'rail-labels.shortcut',
      'rail-labels.host-override',
      'rail-labels.unicode',
      'rail-labels.plain-text',
      'rail-labels.projection-equivalence',
    ])
  })

  for (const fixtureCase of sharedCases) {
    it(fixtureCase.id, () => {
      expect(observe(fixtureCase)).toEqual(fixtureCase.expected)
    })
  }
})

describe('Rail Labels revision-1 quality fixtures', () => {
  it('keeps all rail interaction names complete and plain text', () => {
    expect(fixture(qualityCases, 'rail-labels.quality.complete-names').expected).toEqual({
      allInteractiveRailAffordancesNamed: true,
    })
    const names = [
      defaultRailLabels.toggleLens('Validation'),
      defaultRailLabels.activateLens('Validation'),
      defaultRailLabels.collapseNode,
      defaultRailLabels.expandNode,
      defaultRailLabels.railRegion,
      defaultRailLabels.exitLens,
      defaultRailLabels.lensShortcutHint(2),
    ]
    expect(names.every(value => value.length > 0)).toBe(true)
    expect(fixture(qualityCases, 'rail-labels.quality.plain-text').expected).toEqual({ htmlInterpreted: false })
    expect(defaultRailLabels.source('<strong>tenant</strong>')).toBe('<strong>tenant</strong>')
  })

  it('preserves host-localized and Unicode values without owning an i18n runtime', () => {
    const source = readFileSync(new URL('../index.ts', import.meta.url), 'utf8')
    const unicode = fixture(qualityCases, 'rail-labels.quality.unicode').expected as {
      value: string
      bytePreserved: boolean
    }
    expect({ moduleRuntimeAbsent: !/^import\s/gmu.test(source), callerValuesPreserved: true }).toEqual(
      fixture(qualityCases, 'rail-labels.quality.host-localized').expected,
    )
    expect({ value: defaultRailLabels.source(unicode.value), bytePreserved: true }).toEqual(unicode)
  })

  it('leaves locale-sensitive formatting to the host', () => {
    expect(fixture(qualityCases, 'rail-labels.quality.formatting-applicability').expected).toEqual({
      disposition: 'not-applicable',
      requiresRationale: true,
    })
    expect(defaultRailLabels.source('١٬٢٣٤٫٥٦')).toBe('١٬٢٣٤٫٥٦')
  })
})
