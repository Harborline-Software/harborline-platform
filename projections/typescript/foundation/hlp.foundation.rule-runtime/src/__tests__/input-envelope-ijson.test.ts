import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { RuleInstance } from '../instance.js'
import { RuleContextSnapshot } from '../guard.js'

// T-1143: the rule context is refused at the input envelope when it is not I-JSON in the two respects the owner ruled
// on 2026-11-07 (RFC 7493 §2.1 lone surrogates, raw or escaped; §2.3 repeated member names). The cases and their
// expected outcomes are the shared fixture the .NET tier also reads, written from RFC 7493 rather than from either tier.
const here = dirname(fileURLToPath(import.meta.url))
const fixture = JSON.parse(readFileSync(join(here, '..', '..', '..', '..', '..', '..', 'conformance', 'hlp.foundation.rule-runtime', 'input-envelope-ijson.json'), 'utf8')) as {
  cases: { name: string, text: string, refusal: string | null }[]
}

// The fixture writes a raw UTF-16 code unit as <U+D800>, because a JSON file cannot hold a lone one itself.
const rawUnits = (text: string) => text.replace(/<U\+([0-9A-F]{4})>/g, (_, hex: string) => String.fromCharCode(parseInt(hex, 16)))

describe('I-JSON at the input envelope (T-1143)', () => {
  it.each(fixture.cases)('rule instance: $name', ({ text, refusal }) => {
    const json = rawUnits(text)
    if (refusal === null) expect(() => RuleInstance.fromJsonText(json)).not.toThrow()
    else expect(() => RuleInstance.fromJsonText(json)).toThrow(new TypeError(`rule instance is not I-JSON: ${refusal}`))
  })

  it.each(fixture.cases)('rule context: $name', ({ text, refusal }) => {
    const json = rawUnits(text)
    if (refusal === null) expect(() => RuleContextSnapshot.fromJsonText(json)).not.toThrow()
    else expect(() => RuleContextSnapshot.fromJsonText(json)).toThrow(new TypeError(`rule context is not I-JSON: ${refusal}`))
  })
})
