import type { Json } from './model.js'

/**
 * Finite host-capture envelope for runtime-owned JSON data.  These limits apply to
 * UTF-8 encoded source text, JSON value nesting, and JSON values (containers and
 * scalars alike); they are intentionally separate from authored-rule limits.
 */
export const INPUT_MAX_UTF8_BYTES = 262_144
export const INPUT_MAX_DEPTH = 64
export const INPUT_MAX_NODES = 5_000

export function parseBoundedJsonText(jsonText: string, label: string): Json {
  // Every UTF-8 code point needs at least one byte, so this rejects an obviously
  // oversized hostile string before an exact scan or JSON.parse allocation.
  if (jsonText.length > INPUT_MAX_UTF8_BYTES) throw new RangeError(`${label} exceeds byte ceiling`)
  if (utf8ByteLength(jsonText) > INPUT_MAX_UTF8_BYTES) throw new RangeError(`${label} exceeds byte ceiling`)
  let parsed: unknown
  try {
    parsed = JSON.parse(jsonText)
  } catch (error) {
    throw new TypeError(`${label} is not valid JSON`, { cause: error })
  }
  validateJsonEnvelope(parsed, label)
  return parsed as Json
}

/** Rejects hostile dynamic property names before graph state can coerce or store them. */
export function assertBoundedMemberName(value: unknown, label: string): asserts value is string {
  if (typeof value !== 'string' || value.length > INPUT_MAX_UTF8_BYTES || utf8ByteLength(value) > INPUT_MAX_UTF8_BYTES) {
    throw new RangeError(`${label} exceeds byte ceiling`)
  }
}

function validateJsonEnvelope(value: unknown, label: string): void {
  let nodes = 0
  const visit = (current: unknown, containerDepth: number): void => {
    if (++nodes > INPUT_MAX_NODES) throw new RangeError(`${label} exceeds node ceiling`)
    if (current === null || typeof current === 'string' || typeof current === 'number' || typeof current === 'boolean') return
    if (Array.isArray(current)) {
      if (containerDepth + 1 > INPUT_MAX_DEPTH) throw new RangeError(`${label} exceeds depth ceiling`)
      for (const child of current) visit(child, containerDepth + 1)
      return
    }
    if (typeof current === 'object') {
      // JSON.parse produces ordinary data records; no caller-owned accessor or Proxy is
      // inspected at this boundary.
      if (containerDepth + 1 > INPUT_MAX_DEPTH) throw new RangeError(`${label} exceeds depth ceiling`)
      for (const child of Object.values(current as Record<string, unknown>)) visit(child, containerDepth + 1)
      return
    }
    throw new TypeError(`${label} contains a non-JSON value`)
  }
  visit(value, 0)
}

function utf8ByteLength(value: string): number {
  let bytes = 0
  for (let i = 0; i < value.length; i++) {
    const unit = value.charCodeAt(i)
    if (unit < 0x80) bytes += 1
    else if (unit < 0x800) bytes += 2
    else if (unit >= 0xd800 && unit <= 0xdbff && i + 1 < value.length && value.charCodeAt(i + 1) >= 0xdc00 && value.charCodeAt(i + 1) <= 0xdfff) { bytes += 4; i++ }
    else bytes += 3
  }
  return bytes
}
