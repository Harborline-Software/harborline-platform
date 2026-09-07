/**
 * Canonical (deterministic) JSON serialization — byte-identical to the .NET
 * `CanonicalJson` (SPINE-1 §6.1). Object keys sorted; integral numbers without a
 * decimal point; the same string-escape set as the .NET writer. This is the
 * cross-tier comparison surface the conformance corpus asserts on.
 */
import type { ComputedValue, Json, RuleError, RuleOutcome } from './model.js'

export function serializeOutcome(outcome: RuleOutcome): string {
  const obj: Record<string, Json> = {
    ruleId: outcome.ruleId,
    target: outcome.target,
    outputType: outcome.outputType,
  }
  switch (outcome.outputType) {
    case 'Value': {
      const cv = outcome.value!
      const o: Record<string, Json> = { state: cv.state }
      if (cv.state === 'Resolved') o.value = cv.value ?? null
      if (cv.state === 'Error' && cv.error) o.error = serializeError(cv.error)
      obj.value = o
      break
    }
    case 'Validity': {
      const v = outcome.validity!
      const o: Record<string, Json> = { ok: v.ok }
      if (v.error) o.error = serializeError(v.error)
      obj.validity = o
      break
    }
    case 'Visibility': {
      const vis = outcome.visibility!
      obj.visibility = { visible: vis.visible, required: vis.required, readOnly: vis.readOnly }
      break
    }
    case 'Presentation': {
      const p = outcome.presentation!
      const o: Record<string, Json> = { severity: p.severity ?? null }
      if (p.styleToken !== undefined && p.styleToken !== null) o.styleToken = p.styleToken
      if (p.badge) {
        const values: Record<string, Json> = {}
        for (const k of Object.keys(p.badge.values).sort()) values[k] = p.badge.values[k]
        o.badge = { defaultLocale: p.badge.defaultLocale, values }
      }
      obj.presentation = o
      break
    }
    case 'Options': {
      const oo = outcome.options!
      const o: Record<string, Json> = { state: oo.state }
      if (oo.state === 'Resolved') o.options = oo.options ?? []
      if (oo.state === 'Error' && oo.error) o.error = serializeError(oo.error)
      obj.options = o
      break
    }
  }
  return write(obj)
}

/** Canonical serialization of a bare ComputedValue (the guard-value corpus lane; ticket 162). */
export function serializeComputedValue(cv: ComputedValue): string {
  const o: Record<string, Json> = { state: cv.state }
  if (cv.state === 'Resolved') o.value = cv.value ?? null
  if (cv.state === 'Error' && cv.error) o.error = serializeError(cv.error)
  return write(o)
}

function serializeError(e: RuleError): Json {
  const params: Record<string, Json> = {}
  for (const k of Object.keys(e.params).sort()) params[k] = e.params[k]
  return { code: e.code, params }
}

export function write(node: Json): string {
  if (node === null || node === undefined) return 'null'
  if (Array.isArray(node)) return '[' + node.map(write).join(',') + ']'
  if (typeof node === 'object') {
    const keys = Object.keys(node).sort()
    return '{' + keys.map((k) => writeString(k) + ':' + write(node[k])).join(',') + '}'
  }
  if (typeof node === 'boolean') return node ? 'true' : 'false'
  if (typeof node === 'number') return Number.isInteger(node) ? String(node) : String(node)
  return writeString(node)
}

function writeString(s: string): string {
  let out = '"'
  for (const c of s) {
    switch (c) {
      case '"': out += '\\"'; break
      case '\\': out += '\\\\'; break
      case '\n': out += '\\n'; break
      case '\r': out += '\\r'; break
      case '\t': out += '\\t'; break
      default:
        if (c.charCodeAt(0) < 0x20) out += '\\u' + c.charCodeAt(0).toString(16).padStart(4, '0')
        else out += c
        break
    }
  }
  return out + '"'
}
