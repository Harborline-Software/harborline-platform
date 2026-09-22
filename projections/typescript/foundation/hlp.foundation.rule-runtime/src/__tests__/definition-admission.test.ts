import { describe, expect, it } from 'vitest'

import { compile } from '../compiler.js'
import { Codes } from '../codes.js'
import { FormRuleGraph } from '../graph.js'
import { RuleInstance } from '../instance.js'
import { DEFAULT_LIMITS } from '../limits.js'
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

  it('enforces AST and literal boundaries at and immediately over their configured limit', () => {
    const astLimits = { ...DEFAULT_LIMITS, maxAstNodes: 1 }
    expect(compile([compute('ast-at', 'x', 1)], astLimits).rules).toHaveLength(1)
    expect(() => compile([compute('ast-over', 'x', { '!': [true] })], astLimits))
      .toThrow(expect.objectContaining({ code: Codes.compileAstTooLarge }))

    const literalLimits = { ...DEFAULT_LIMITS, maxLiteralLength: 1 }
    expect(compile([compute('literal-at', 'x', '"a"')], literalLimits).rules).toHaveLength(1)
    expect(() => compile([compute('literal-over', 'x', '"ab"')], literalLimits))
      .toThrow(expect.objectContaining({ code: Codes.compileLiteralTooLong }))
  })

  it('derives empty, literal, and short dependency programs without host-depth iteration', () => {
    const large = { ...DEFAULT_LIMITS, maxDependencyDepth: 2_147_483_647 }
    expect(compile([], large).workProof.maximumEvaluationWork).toBeGreaterThanOrEqual(0n)
    expect(compile([compute('literal', 'x', 1)], large).workProof.maximumResultBytes).toBeGreaterThanOrEqual(1n)
    expect(compile([compute('a', 'a', { var: 'b' }), compute('b', 'b', 1)], large).rules).toHaveLength(2)
  })

  it('instantiates a proof for independently configured graph structural dimensions', () => {
    const compiled = compile([compute('row-aware', 'x', { var: 'table.sum(items.amount)' })],
      { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1, maxGraphNodes: 2 })
    const graph = new FormRuleGraph(compiled, () => new Date('2026-09-22T00:00:00Z'),
      { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 2, maxGraphNodes: 3 })
    graph.evaluateInstance(RuleInstance.fromJsonText('{}'))
    expect(graph.workProof.maximumResultBytes).toBeGreaterThanOrEqual(compiled.workProof.maximumResultBytes)
  })

  it('captures mutable runtime structural limits with its instantiated proof', () => {
    const sourceLimits = { ...DEFAULT_LIMITS, maxGraphNodes: 1, maxTableRowsPerAggregate: 1 }
    const graph = new FormRuleGraph(compile([compute('one', 'x', 1)]), () => new Date('2026-09-22T00:00:00Z'), sourceLimits)
    const proof = graph.workProof
    sourceLimits.maxGraphNodes = 0
    sourceLimits.maxTableRowsPerAggregate = 0

    expect(graph.workProof).toBe(proof)
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{}')).values.get('field:x')).toEqual({ state: 'Resolved', value: 1 })
  })

  it('composes a known array-producing rule into a downstream scalar admission', () => {
    expect(() => compile([
      compute('array', 'array', []),
      compute('scalar', 'scalar', { '+': [{ var: 'array' }, 1] }),
    ])).toThrow(expect.objectContaining({ code: Codes.compileInvalidExpression, ruleId: 'scalar' }))
  })

  it('publishes a finite compositional result/work proof that bounds an executed copying expression', () => {
    const graph = compile([
      compute('copying-cat', 'result', { cat: ['ab', { cat: ['cd', 'ef'] }] }),
    ])
    const evaluated = new FormRuleGraph(graph, () => new Date('2026-09-22T00:00:00.000Z'))
      .evaluateInstance(RuleInstance.fromJsonText('{}'))
    const value = evaluated.values.get('field:result')

    expect(value).toEqual({ state: 'Resolved', value: 'abcdef' })
    expect(graph.workProof.maximumResultBytes).toBeGreaterThanOrEqual(6)
    expect(graph.workProof.maximumEvaluationWork).toBeGreaterThan(0)
  })

  it('bounds the public large aligned-money concatenation in serialized JSON bytes', () => {
    const integer = '9'.repeat(4090)
    const fraction = `0.${'0'.repeat(4088)}1`
    const graph = compile([
      compute('aligned-money-cat', 'result', {
        cat: Array.from({ length: 60 }, () => ({ 'money.add': [integer, fraction] })),
      }),
    ])
    // Deliberately diagnostic-only: this is not a production limit change.
    const limits = { ...DEFAULT_LIMITS, stepBudget: 2_000_000, wallClockMs: 5000 }
    const evaluated = new FormRuleGraph(graph, () => new Date('2026-09-22T00:00:00.000Z'), limits)
      .evaluateInstance(RuleInstance.fromJsonText('{}'))
    const value = evaluated.values.get('field:result')

    expect(value?.state).toBe('Resolved')
    const actualBytes = new TextEncoder().encode(JSON.stringify(value?.value)).length
    expect(actualBytes).toBe(490802)
    expect(graph.workProof.maximumResultBytes).toBeGreaterThanOrEqual(BigInt(actualBytes))
  })

  it('bounds JSON re-escaping and the empty boolean/date identities through execution', () => {
    const graph = compile([
      compute('quoted-container-cat', 'quoted', { cat: [{ label: '"quoted"', values: ['x'] }, { 'date.today': [] }] }),
      compute('empty-cat', 'emptyCat', { cat: [] }),
      compute('empty-and', 'emptyAnd', { and: [] }),
      compute('empty-or', 'emptyOr', { or: [] }),
      compute('strict-inequality', 'different', { '!==': [1, '1'] }),
    ])
    const result = new FormRuleGraph(graph, () => new Date('2026-09-22T00:00:00.000Z'))
      .evaluateInstance(RuleInstance.fromJsonText('{}'))
    const values = ['field:quoted', 'field:emptyCat', 'field:emptyAnd', 'field:emptyOr', 'field:different']
      .map(key => result.values.get(key)?.value)

    expect(values.slice(1)).toEqual(['', true, false, true])
    expect(graph.workProof.maximumResultBytes).toBeGreaterThanOrEqual(BigInt(Math.max(
      ...values.map(value => new TextEncoder().encode(JSON.stringify(value)).length),
    )))
  })

  it('accepts trimmed money decimals but refuses exponent text through the executed evaluator', () => {
    const graph = new FormRuleGraph(compile([
      compute('trimmed-money', 'trimmed', { 'money.add': [' 1.20 ', '2.30'] }),
      compute('exponent-money', 'exponent', { 'money.add': ['1e2', '1'] }),
    ]), () => new Date('2026-09-22T00:00:00.000Z'))
    const result = graph.evaluateInstance(RuleInstance.fromJsonText('{}'))

    expect(result.values.get('field:trimmed')).toEqual({ state: 'Resolved', value: '3.5' })
    expect(result.values.get('field:exponent')).toMatchObject({ state: 'Error', error: { code: Codes.typeError } })
  })

  it('refuses all-whitespace money text through the public evaluator', () => {
    const graph = new FormRuleGraph(compile([
      compute('blank-money', 'blank', { 'money.add': ['   ', '1'] }),
    ]), () => new Date('2026-09-22T00:00:00.000Z'))
    const result = graph.evaluateInstance(RuleInstance.fromJsonText('{}'))

    expect(result.values.get('field:blank')).toMatchObject({ state: 'Error', error: { code: Codes.typeError } })
  })
})
