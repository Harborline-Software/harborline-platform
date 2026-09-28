// @ts-nocheck
import { describe, expect, it } from 'vitest'

import { assertBoundedMemberName, INPUT_MAX_DEPTH, INPUT_MAX_UTF8_BYTES, JsonStringifyByteCounter, parseBoundedJsonText } from './input-envelope.js'
import type { Json } from './model.js'

const utf8Bytes = (value: Json) => new TextEncoder().encode(JSON.stringify(value)).length

describe('JsonStringifyByteCounter', () => {
  it('admits exactly the JSON.stringify UTF-8 byte length for escaped strings', () => {
    // This includes every short escape, a control escape, each UTF-8 width, a paired
    // surrogate, and a lone surrogate. JSON.stringify is the published serialization contract.
    const value = '"\\\b\f\n\r\t\u0001A\u0080\u0800😀\ud800'
    const maximum = utf8Bytes(value)

    expect(() => new JsonStringifyByteCounter(maximum, 'value').countValue(value)).not.toThrow()
    expect(() => new JsonStringifyByteCounter(maximum - 1, 'value').countValue(value)).toThrow(RangeError)
  })

  it('counts nested arrays and named object members as the emitted JSON document', () => {
    const value: Json = {
      first: [null, true, false, 12.5, 'two'],
      second: { quoted: '"', escaped: '\\', control: '\u0001' },
    }
    const maximum = utf8Bytes(value)

    expect(() => new JsonStringifyByteCounter(maximum, 'value').countValue(value)).not.toThrow()
    expect(() => new JsonStringifyByteCounter(maximum - 1, 'value').countValue(value)).toThrow(RangeError)
  })

  it('escapes a lone low surrogate while retaining a paired surrogate as UTF-8', () => {
    const value = '\udfff😀'
    const maximum = utf8Bytes(value)

    expect(() => new JsonStringifyByteCounter(maximum, 'value').countValue(value)).not.toThrow()
    expect(() => new JsonStringifyByteCounter(maximum - 1, 'value').countValue(value)).toThrow(RangeError)
  })

  it('does not treat ordinary code units or an invalid surrogate sequence as a UTF-8 pair', () => {
    for (const value of ['\u0800\u0800', '\ud800\u0800']) {
      const maximum = utf8Bytes(value)
      expect(() => new JsonStringifyByteCounter(maximum, 'value').countValue(value)).not.toThrow()
      expect(() => new JsonStringifyByteCounter(maximum - 1, 'value').countValue(value)).toThrow(RangeError)
    }
  })

  it('rejects member names that are not strings before measuring them', () => {
    expect(() => assertBoundedMemberName(42, 'member name')).toThrow('member name exceeds byte ceiling')
  })

  it('uses UTF-8 transition widths at the byte ceiling', () => {
    const prefix = '{"x":"'
    const suffix = '"}'
    const overWithU0080 = `${prefix}${'a'.repeat(INPUT_MAX_UTF8_BYTES - prefix.length - suffix.length - 3)}\u0080\u0080${suffix}`
    const overWithU0800 = `${prefix}${'a'.repeat(INPUT_MAX_UTF8_BYTES - prefix.length - suffix.length - 3)}\u0800a${suffix}`
    const overWithNonSurrogatePair = `${prefix}${'a'.repeat(INPUT_MAX_UTF8_BYTES - prefix.length - suffix.length - 4)}\u0800\u0800${suffix}`
    const atLimitWithPair = `${prefix}${'a'.repeat(INPUT_MAX_UTF8_BYTES - prefix.length - suffix.length - 4)}😀${suffix}`

    expect(() => parseBoundedJsonText(overWithU0080, 'input')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(overWithU0800, 'input')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(overWithNonSurrogatePair, 'input')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(atLimitWithPair, 'input')).not.toThrow()
  })

  it('refuses a JSON container one level beyond the published nesting ceiling', () => {
    const nested = (depth: number) => `${'['.repeat(depth)}0${']'.repeat(depth)}`

    expect(() => parseBoundedJsonText(nested(INPUT_MAX_DEPTH), 'input')).not.toThrow()
    expect(() => parseBoundedJsonText(nested(INPUT_MAX_DEPTH + 1), 'input')).toThrow('input exceeds depth ceiling')
  })
})
