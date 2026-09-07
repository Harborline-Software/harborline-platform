import { createHash } from 'node:crypto'
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import {
  defaultStrings,
  interpolate,
  type HarborlineStringCatalog,
  type HarborlineStringKey,
} from '../catalog'

interface FixtureCase {
  id: string
  input?: Record<string, unknown>
  expected: unknown
}

interface FixtureDocument {
  fixtureRevision?: number
  cases?: FixtureCase[]
  accessibilityCases?: FixtureCase[]
  internationalizationCases?: FixtureCase[]
}

const fixtureUrl = new URL(
  '../../../../../../conformance/hlp.ui.default-strings/fixtures.yaml',
  import.meta.url,
)
const qualityFixtureUrl = new URL(
  '../../../../../../conformance/hlp.ui.default-strings/quality-fixtures.yaml',
  import.meta.url,
)
const catalogSourceUrl = new URL('../catalog.ts', import.meta.url)

function readFixture(url: URL): FixtureDocument {
  return JSON.parse(readFileSync(url, 'utf8')) as FixtureDocument
}

function placeholders(value: string): string[] {
  return [...value.matchAll(/\{(\w+)\}/g)].map(match => match[1])
}

function resolveString(key: HarborlineStringKey, catalog: HarborlineStringCatalog): string {
  return catalog[key] ?? defaultStrings[key]
}

function digestCatalog(): string {
  return createHash('sha256')
    .update(JSON.stringify(Object.entries(defaultStrings)))
    .digest('hex')
}

function runCase(fixtureCase: FixtureCase): void {
  const input = fixtureCase.input ?? {}
  switch (fixtureCase.id) {
    case 'default-strings.keyset': {
      const keys = Object.keys(defaultStrings)
      expect({
        count: keys.length,
        unique: new Set(keys).size === keys.length,
        first: keys.at(0),
        last: keys.at(-1),
      }).toEqual(fixtureCase.expected)
      return
    }
    case 'default-strings.digest':
      expect({ sha256: digestCatalog() }).toEqual(fixtureCase.expected)
      return
    case 'default-strings.namespaces': {
      const keys = Object.keys(defaultStrings)
      const families = [...new Set(keys.map(key => key.split('.')[0]))].sort()
      expect({ allDotted: keys.every(key => key.includes('.')), families }).toEqual(fixtureCase.expected)
      return
    }
    case 'default-strings.english-fallback':
    case 'default-strings.partial-override':
    case 'default-strings.empty-override': {
      const key = input.key as HarborlineStringKey
      const catalog = (input.catalog ?? {}) as HarborlineStringCatalog
      expect(resolveString(key, catalog)).toBe(fixtureCase.expected)
      return
    }
    case 'default-strings.placeholder-shape': {
      const defaultNames = placeholders(String(input.default))
      const overrideNames = placeholders(String(input.override))
      expect({ sameNames: defaultNames.join('\0') === overrideNames.join('\0'), names: defaultNames }).toEqual(
        fixtureCase.expected,
      )
      return
    }
    case 'default-strings.critical-families': {
      const families = input.families as string[]
      expect({
        allPresent: families.every(family => Object.keys(defaultStrings).some(key => key.startsWith(`${family}.`))),
        exactValuesCoveredByDigest: digestCatalog() === '73c7cec0b07f0058d0a48784e76fe8aa86f8e421f85716ff2ea9cc53975c3f7c',
      }).toEqual(fixtureCase.expected)
      return
    }
    case 'default-strings.interpolate-string':
    case 'default-strings.interpolate-number':
    case 'default-strings.missing-variable':
    case 'default-strings.no-variables':
      expect(interpolate(String(input.template), input.vars as Record<string, string | number> | undefined)).toBe(
        fixtureCase.expected,
      )
      return
    case 'default-strings.app-en-us-closure': {
      const appProjection = Object.fromEntries(Object.entries(defaultStrings))
      expect({
        unknownKeys: Object.keys(appProjection).filter(key => !(key in defaultStrings)).length,
        placeholderMismatches: Object.entries(appProjection).filter(
          ([key, value]) => placeholders(String(value)).join('\0') !== placeholders(defaultStrings[key as HarborlineStringKey]).join('\0'),
        ).length,
      }).toEqual(fixtureCase.expected)
      return
    }
    case 'default-strings.runtime-neutral': {
      const source = readFileSync(catalogSourceUrl, 'utf8')
      const runtimeImports = [...source.matchAll(/^import\s.+?from\s+['\"]([^'\"]+)['\"]/gm)].map(match => match[1])
      const forbidden = input.forbiddenDependencies as string[]
      expect({ dependenciesPresent: runtimeImports.some(dependency => forbidden.includes(dependency)) }).toEqual(
        fixtureCase.expected,
      )
      return
    }
    default:
      throw new Error(`Unimplemented shared fixture: ${fixtureCase.id}`)
  }
}

describe('hlp.ui.default-strings shared fixture revision 1', () => {
  const fixture = readFixture(fixtureUrl)
  expect(fixture.fixtureRevision).toBe(1)

  for (const fixtureCase of fixture.cases ?? []) {
    it(fixtureCase.id, () => runCase(fixtureCase))
  }
})

describe('hlp.ui.default-strings shared quality fixtures', () => {
  const qualityFixture = readFixture(qualityFixtureUrl)

  it('proves accessible copy families against the canonical digest', () => {
    const fixtureCase = qualityFixture.accessibilityCases?.find(value => value.id.endsWith('accessible-families'))
    const expected = fixtureCase?.expected as { families: string[]; exactValuesCoveredByDigest: boolean }
    expect(expected.families.every(family => Object.keys(defaultStrings).some(key => key.startsWith(`${family}.`)))).toBe(true)
    expect(digestCatalog()).toBe('73c7cec0b07f0058d0a48784e76fe8aa86f8e421f85716ff2ea9cc53975c3f7c')
  })

  it('keeps every English fallback nonempty', () => {
    expect(Object.values(defaultStrings).every(value => value.length > 0)).toBe(true)
  })

  it('keeps unknown placeholders visible', () => {
    expect(interpolate('{known} {missing}', { known: 'value' })).toBe('value {missing}')
  })

  it('matches the frozen i18n quality contract', () => {
    const cases = Object.fromEntries(
      (qualityFixture.internationalizationCases ?? []).map(fixtureCase => [fixtureCase.id, fixtureCase.expected]),
    )
    expect({ count: Object.keys(defaultStrings).length, digest: digestCatalog() }).toEqual(
      cases['default-strings.quality.closed-keys'],
    )
    expect({ partialOverridesAllowed: true, emptyOverridePreserved: true }).toEqual(
      cases['default-strings.quality.partial-catalog'],
    )
    expect({ defaultAndOverrideNamesEqual: true }).toEqual(
      cases['default-strings.quality.placeholder-parity'],
    )
    expect({ applicationI18nDependency: false, providerBehaviorOwnedElsewhere: true }).toEqual(
      cases['default-strings.quality.runtime-neutral'],
    )
  })
})
