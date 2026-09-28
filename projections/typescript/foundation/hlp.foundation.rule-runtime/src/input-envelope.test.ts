import { describe, expect, it } from 'vitest'

import {
  assertBoundedMemberName,
  INPUT_MAX_UTF8_BYTES,
  JsonStringifyByteCounter,
  parseBoundedJsonText,
} from './input-envelope.js'
import type { Json } from './model.js'

function expectCountValueToExceed(value: Json, maximum: number): void {
  const counter = new JsonStringifyByteCounter(maximum, 'test value')
  expect(() => counter.countValue(value)).toThrow(RangeError)
}

function expectCountStringToExceed(value: string, maximum: number): void {
  const counter = new JsonStringifyByteCounter(maximum, 'test string')
  expect(() => counter.countString(value)).toThrow(RangeError)
}

function expectCountStringToFit(value: string, maximum: number): void {
  const counter = new JsonStringifyByteCounter(maximum, 'test string')
  expect(() => counter.countString(value)).not.toThrow()
}

function expectCountValueToFit(value: Json, maximum: number): void {
  const counter = new JsonStringifyByteCounter(maximum, 'test value')
  expect(() => counter.countValue(value)).not.toThrow()
}

describe('JsonStringifyByteCounter', () => {
  it('refuses string representations whose escapes and UTF-8 code-unit boundaries exceed the ceiling', () => {
    expectCountStringToExceed('\\', 3)
    expectCountStringToExceed('\u0000', 7)
    expectCountStringToExceed('\u0080', 3)
    expectCountStringToExceed('\u0800', 4)
    expectCountStringToExceed('😀', 5)
    expectCountStringToExceed('\ud800a', 8)
    expectCountStringToExceed('\ud800\ud800', 13)
    expectCountStringToExceed('\udc00', 7)
    expectCountStringToExceed('\udfff', 7)
  })

  it('refuses scalar, array, and object JSON representations whose punctuation or children exceed the ceiling', () => {
    expectCountValueToExceed(null, 3)
    expectCountValueToExceed(true, 3)
    expectCountValueToExceed(false, 4)
    expectCountValueToExceed([null, true], 10)
    expectCountValueToExceed({ x: null }, 9)
    expectCountValueToExceed({ x: null, y: false }, 19)
  })

  it('preserves JSON punctuation boundaries when arrays and objects fit exactly', () => {
    expectCountValueToFit([null], 6)
    expectCountValueToFit([null, true], 11)
    expectCountValueToFit({ x: null }, 10)
    expectCountValueToFit({ x: null, y: false }, 20)
  })

  it('accepts a literal space and a paired surrogate at their exact JSON byte counts', () => {
    expectCountStringToFit(' ', 3)
    expectCountStringToFit('😀', 6)
  })

  it('rejects negative additions but permits a zero-byte punctuation addition', () => {
    const counter = new JsonStringifyByteCounter(1, 'punctuation')

    expect(() => counter.addPunctuation(0)).not.toThrow()
    expect(() => counter.addPunctuation(-1)).toThrow('punctuation exceeds byte ceiling')
    expect(() => counter.addPunctuation(2)).toThrow('punctuation exceeds byte ceiling')
  })
})

describe('input envelope boundaries', () => {
  it('rejects non-strings and non-ASCII member names whose UTF-8 encoding exceeds the ceiling', () => {
    expect(() => assertBoundedMemberName(42, 'member name')).toThrow(RangeError)
    expect(() => assertBoundedMemberName('é'.repeat(INPUT_MAX_UTF8_BYTES / 2 + 1), 'member name')).toThrow(RangeError)
  })

  it('accepts an ASCII member name at the byte ceiling and names overflow failures', () => {
    expect(() => assertBoundedMemberName('a'.repeat(INPUT_MAX_UTF8_BYTES), 'member name')).not.toThrow()
    expect(() => assertBoundedMemberName('a'.repeat(INPUT_MAX_UTF8_BYTES + 1), 'member name')).toThrow('member name exceeds byte ceiling')
  })

  it('labels malformed JSON failures and retains the parse failure as their cause', () => {
    try {
      parseBoundedJsonText('{', 'payload')
      throw new Error('expected malformed JSON to fail')
    } catch (error) {
      expect(error).toBeInstanceOf(TypeError)
      expect((error as Error).message).toBe('payload is not valid JSON')
      expect((error as Error & { cause?: unknown }).cause).toBeInstanceOf(SyntaxError)
    }
  })

  it('rejects source text over both code-unit and UTF-8 byte ceilings with its label', () => {
    expect(() => parseBoundedJsonText(' '.repeat(INPUT_MAX_UTF8_BYTES + 1), 'code units')).toThrow('code units exceeds byte ceiling')
    expect(() => parseBoundedJsonText(`"${'é'.repeat(INPUT_MAX_UTF8_BYTES / 2)}"`, 'UTF-8')).toThrow('UTF-8 exceeds byte ceiling')
  })

  it('rejects object nesting beyond the JSON depth ceiling', () => {
    const tooDeep = `${'{"child":'.repeat(65)}null${'}'.repeat(65)}`
    const tooDeepArray = `${'['.repeat(65)}null${']'.repeat(65)}`

    expect(() => parseBoundedJsonText(tooDeep, 'nested object')).toThrow('nested object exceeds depth ceiling')
    expect(() => parseBoundedJsonText(tooDeepArray, 'nested array')).toThrow('nested array exceeds depth ceiling')
  })

  it('accepts containers at the JSON depth ceiling and counts arrays beneath the node ceiling', () => {
    const atDepth = `${'{"child":'.repeat(64)}null${'}'.repeat(64)}`
    const arrayAtNodeLimit = JSON.stringify(Array.from({ length: 4_999 }, () => 0))

    expect(() => parseBoundedJsonText(atDepth, 'nested object')).not.toThrow()
    expect(() => parseBoundedJsonText(arrayAtNodeLimit, 'array nodes')).not.toThrow()
    expect(() => parseBoundedJsonText(JSON.stringify(Array.from({ length: 5_000 }, () => 0)), 'array nodes')).toThrow('array nodes exceeds node ceiling')
  })

  it('keeps long scalar JSON strings scalar rather than recursively visiting their characters', () => {
    expect(() => parseBoundedJsonText(`"${'a'.repeat(5_001)}"`, 'scalar')).not.toThrow()
  })

  it('uses UTF-8 boundaries for U+0080, U+0800, and surrogate-pair JSON source text', () => {
    const atLimitEmoji = `"${'😀'.repeat(65_535)}"`
    const overLimitEmoji = `"${'😀'.repeat(65_536)}"`
    const overLimitU0080 = `"${'\u0080'.repeat(131_072)}"`
    const overLimitU0800 = `"${'\u0800'.repeat(87_382)}"`
    const malformedPairs = `"${'\ud800\ud800'.repeat(43_691)}"`

    expect(() => parseBoundedJsonText(atLimitEmoji, 'emoji')).not.toThrow()
    expect(() => parseBoundedJsonText(overLimitEmoji, 'emoji')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(overLimitU0080, 'U+0080')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(overLimitU0800, 'U+0800')).toThrow(RangeError)
    expect(() => parseBoundedJsonText(malformedPairs, 'malformed pairs')).toThrow(RangeError)
  })

  it('accepts surrogate-pair source text at the exact UTF-8 ceiling', () => {
    const atExactLimit = `"${'😀'.repeat(65_535)}aa"`

    // 65,535 scalar pairs (four UTF-8 bytes each), two ASCII bytes, and two JSON quotes.
    expect(atExactLimit).toHaveLength(131_074)
    expect(() => parseBoundedJsonText(atExactLimit, 'exact emoji')).not.toThrow()
  })
})

// Exact-count oracles: the counter must equal JSON.stringify's UTF-8 length and the member-name
// bound must equal the string's own UTF-8 length, so both an over- and an under-count fail.
// Independent oracle: one to four bytes per code point, a lone surrogate encoding as U+FFFD (three).
function utf8Bytes(text: string): number {
  let bytes = 0
  for (const character of text) {
    const point = character.codePointAt(0) ?? 0
    bytes += point < 0x80 ? 1 : point < 0x800 ? 2 : point < 0x10000 ? 3 : 4
  }
  return bytes
}

const codeUnitSamples = [
  [0x22], [0x5c], [0x08], [0x0c], [0x0a], [0x0d], [0x09], [0x01], [0x1f], [0x20], [0x7f], [0x80], [0x7ff], [0x800], [0xffff],
  [0xd7ff], [0xe000], [0xd800], [0xdbff], [0xdc00], [0xdfff], [0xd800, 0xdc00], [0xdbff, 0xdfff], [0xdc00, 0xd800],
  [0xd800, 0x61], [0x61, 0xdfff], [0xd800, 0xd800, 0xdc00], [0xd83d, 0xde00], [0x20ac], [0xe9], [0xd800, 0xe000], [0x0800, 0xdc00], [0xd7ff, 0xdc00],
].map(units => String.fromCharCode(...units))

describe('exact UTF-8 counts', () => {
  it.each(codeUnitSamples)('counts JSON.stringify bytes exactly for %j', sample => {
    const value = sample.repeat(3)
    const exact = utf8Bytes(JSON.stringify(value))
    expectCountStringToFit(value, exact)
    expectCountStringToExceed(value, exact - 1)
  })

  it.each(codeUnitSamples)('bounds a member name by its exact UTF-8 length for %j', sample => {
    const tail = sample.repeat(3)
    const atCeiling = 'a'.repeat(INPUT_MAX_UTF8_BYTES - utf8Bytes(tail)) + tail
    expect(() => assertBoundedMemberName(atCeiling, 'name')).not.toThrow()
    expect(() => assertBoundedMemberName(`a${atCeiling}`, 'name')).toThrow(RangeError)
  })
})
