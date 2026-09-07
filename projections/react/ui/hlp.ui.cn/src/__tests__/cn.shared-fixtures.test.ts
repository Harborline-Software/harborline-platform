import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import type { ClassValue } from 'clsx'
import { cn } from '../cn'

interface FixtureCase {
  id: string
  input?: unknown
  expected: string | { value?: string }
}

interface FixtureDocument {
  fixtureRevision?: number
  cases?: FixtureCase[]
  accessibilityCases?: FixtureCase[]
  internationalizationCases?: FixtureCase[]
}

const fixtureUrl = new URL(
  '../../../../../../conformance/hlp.ui.cn/fixtures.yaml',
  import.meta.url,
)
const qualityFixtureUrl = new URL(
  '../../../../../../conformance/hlp.ui.cn/quality-fixtures.yaml',
  import.meta.url,
)

function readFixture(url: URL): FixtureDocument {
  return JSON.parse(readFileSync(url, 'utf8')) as FixtureDocument
}

function decodeClassValue(value: unknown): ClassValue {
  if (Array.isArray(value)) return value.map(decodeClassValue)
  if (value && typeof value === 'object') {
    const tagged = value as { kind?: string; value?: unknown; entries?: [string, boolean][] }
    if (tagged.kind === 'null') return null
    if (tagged.kind === 'undefined') return undefined
    if (tagged.kind === 'boolean') return Boolean(tagged.value)
    if (tagged.kind === 'string') return String(tagged.value ?? '')
    if (tagged.kind === 'object-entries') return Object.fromEntries(tagged.entries ?? [])
  }
  return value as ClassValue
}

function execute(input: unknown): string {
  if (Array.isArray(input)) return cn(...input.map(decodeClassValue))
  return cn(decodeClassValue(input))
}

function expectedValue(expected: FixtureCase['expected']): string {
  if (typeof expected === 'string') return expected
  if (typeof expected.value === 'string') return expected.value
  throw new Error('Fixture has no class-string expectation')
}

describe('hlp.ui.cn shared fixture revision 1', () => {
  const fixture = readFixture(fixtureUrl)
  expect(fixture.fixtureRevision).toBe(1)

  for (const fixtureCase of fixture.cases ?? []) {
    it(fixtureCase.id, () => {
      expect(execute(fixtureCase.input)).toBe(expectedValue(fixtureCase.expected))
    })
  }
})

describe('hlp.ui.cn shared quality fixtures', () => {
  const qualityFixture = readFixture(qualityFixtureUrl)
  const executableCases = [
    ...(qualityFixture.accessibilityCases ?? []),
    ...(qualityFixture.internationalizationCases ?? []),
  ].filter(fixtureCase => fixtureCase.input !== undefined)

  for (const fixtureCase of executableCases) {
    it(fixtureCase.id, () => {
      expect(execute(fixtureCase.input)).toBe(expectedValue(fixtureCase.expected))
    })
  }
})
