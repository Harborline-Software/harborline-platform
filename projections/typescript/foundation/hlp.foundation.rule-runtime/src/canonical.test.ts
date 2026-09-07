import { describe, expect, it } from 'vitest'
import { serializeOutcome, write } from './canonical.js'

describe('write', () => {
  it('writes stable object keys, nested values, and JSON escapes', () => {
    expect(write({ z: [true, null], a: 'line\n"\\\t', nested: { b: 2, a: 1 } })).toBe(
      '{"a":"line\\n\\"\\\\\\t","nested":{"a":1,"b":2},"z":[true,null]}',
    )
  })

  it('handles empty and invalid runtime values deterministically', () => {
    expect(write([])).toBe('[]')
    expect(write({})).toBe('{}')
    expect(write(undefined as never)).toBe('null')
  })
})

describe('serializeOutcome', () => {
  it('serializes resolved values and sorted error parameters', () => {
    expect(
      serializeOutcome({
        ruleId: 'r2',
        target: 'field:name',
        outputType: 'Value',
        value: { state: 'Error', error: { code: 'required', params: { z: '2', a: '1' } } },
      }),
    ).toBe(
      '{"outputType":"Value","ruleId":"r2","target":"field:name","value":{"error":{"code":"required","params":{"a":"1","z":"2"}},"state":"Error"}}',
    )
  })

  it('serializes each remaining outcome payload and empty boundaries', () => {
    expect(
      serializeOutcome({
        ruleId: 'r1', target: 'field:x', outputType: 'Visibility',
        visibility: { visible: false, required: true, readOnly: false },
      }),
    ).toContain('"visibility":{"readOnly":false,"required":true,"visible":false}')
    expect(
      serializeOutcome({ ruleId: 'r3', target: 'field:x', outputType: 'Options', options: { state: 'Resolved' } }),
    ).toContain('"options":{"options":[],"state":"Resolved"}')
  })
})
