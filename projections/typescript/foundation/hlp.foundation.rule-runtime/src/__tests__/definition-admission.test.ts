import { describe, expect, it } from 'vitest'

import { compile } from '../compiler.js'
import { Codes } from '../codes.js'
import type { Json, RuleDefinition } from '../model.js'

function compute(id: string, target: string, expression: Json): RuleDefinition {
  return { id, tier: 'JsonLogic', scope: 'Field', scopeTarget: target, action: 'Compute', expression }
}

describe('definition compiler admission', () => {
  it.each([
    { unknown_operator: [] },
    { regex: ['a', '.*'] },
    { if: [false, { unknown_operator: [] }, true] },
  ])('rejects unsupported executable syntax before any records are evaluated: %j', expression => {
    expect(() => compile([compute('closed-grammar', 'total', expression)])).toThrow(expect.objectContaining({
      code: Codes.compileInvalidExpression,
      ruleId: 'closed-grammar',
    }))
  })

  it.each([
    { expression: [{ unknown_operator: [] }] },
    { expression: { metadata: { unknown_operator: [] }, label: 'literal' } },
  ])('does not interpret literal data as executable syntax: %j', ({ expression }) => {
    expect(compile([compute('literal-data', 'result', expression)]).rules).toHaveLength(1)
  })

  it.each([
    { 'date.today': ['unexpected'] },
    { 'coding.is': [{ var: 'code' }, 'system'] },
  ])('rejects invalid executable arity before any records are evaluated: %j', expression => {
    expect(() => compile([compute('invalid-arity', 'result', expression)])).toThrow(expect.objectContaining({
      code: Codes.compileInvalidExpression,
      ruleId: 'invalid-arity',
    }))
  })

  it.each(['mystery', '', 99, null])('refuses unknown tier %s instead of interpreting it as JsonLogic', tier => {
    const source = { ...compute('unknown-tier', 'total', 1), tier } as RuleDefinition

    expect(() => compile([source])).toThrow(expect.objectContaining({
      code: Codes.compileUnsupportedTier,
      ruleId: 'unknown-tier',
    }))
  })

  it.each(['mystery', '', 99, null])('refuses unknown action %s instead of interpreting it as Visibility', action => {
    const source = { ...compute('unknown-action', 'total', true), action } as RuleDefinition

    expect(() => compile([source])).toThrow(expect.objectContaining({
      code: 'rule.compile.unknown_action',
      ruleId: 'unknown-action',
    }))
  })

  it.each([64, 65])('enforces the inclusive reference boundary at %i', count => {
    const expression = { '+': Array.from({ length: count }, (_, i) => ({ var: `r${i}` })) }
    const source = compute('reference-boundary', 'total', expression)

    if (count === 64) expect(compile([source]).rules).toHaveLength(1)
    else expect(() => compile([source])).toThrow(expect.objectContaining({ code: Codes.compileTooManyRefs }))
  })

  it.each([64, 65])('enforces the inclusive dependency boundary at %i', depth => {
    const sources = Array.from({ length: depth }, (_, i) =>
      compute(`dependency-${i}`, `f${i}`, { var: `f${i + 1}` }))

    if (depth === 64) expect(compile(sources).rules).toHaveLength(64)
    else expect(() => compile(sources)).toThrow(expect.objectContaining({ code: Codes.compileDepthExceeded }))
  })

  it('composes a known array-producing rule into a downstream scalar admission', () => {
    expect(() => compile([
      compute('array', 'array', []),
      compute('scalar', 'scalar', { '+': [{ var: 'array' }, 1] }),
    ])).toThrow(expect.objectContaining({ code: Codes.compileInvalidExpression, ruleId: 'scalar' }))
  })
})
