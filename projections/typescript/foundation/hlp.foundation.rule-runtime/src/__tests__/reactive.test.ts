/**
 * TS-tier unit coverage (SPINE-1 §6.3): the reactive re-eval front (transitive
 * dependents only + memoization soundness), incremental child-table row edits, the
 * async Pending path, identical static-cap rejection to .NET, and the guard evaluator.
 */
import { describe, it, expect } from 'vitest'

import type { RuleDefinition } from '../model.js'
import { compile, CompileError, RuleTimeout } from '../index.js'
import { FormRuleGraph } from '../graph.js'
import { GuardEvaluator, RuleContextSnapshot } from '../guard.js'
import { RuleInstance, RuleRowSnapshot, RuleValueSnapshot } from '../instance.js'
import { Codes } from '../codes.js'
import { DEFAULT_LIMITS } from '../limits.js'
import type { Json } from '../model.js'
import { INPUT_MAX_NODES, INPUT_MAX_UTF8_BYTES } from '../input-envelope.js'
import { deriveCoreTypes, type CoreJsonType } from '../core-types.js'

const fixedClock = () => new Date('2026-06-30T00:00:00.000Z')
const snapshot = (value: Record<string, Json>) => RuleContextSnapshot.fromJsonText(JSON.stringify(value))
const instance = (value: Record<string, Json>) => RuleInstance.fromJsonText(JSON.stringify(value))
const valueSnapshot = (value: Json) => RuleValueSnapshot.fromJsonText(JSON.stringify(value))
const rowSnapshot = (value: { id: string, fields: Record<string, Json> }) => RuleRowSnapshot.fromJsonText(JSON.stringify(value))

function rule(id: string, scopeTarget: string, action: RuleDefinition['action'], expression: Json, scope: RuleDefinition['scope'] = 'Field'): RuleDefinition {
  return { id, tier: 'JsonLogic', scope, scopeTarget, action, expression }
}

function graphOf(rules: RuleDefinition[], instance: Record<string, Json>) {
  const g = new FormRuleGraph(compile(rules), fixedClock)
  return { g, first: g.evaluateInstance(RuleInstance.fromJsonText(JSON.stringify(instance))) }
}

describe('business-clock pinning', () => {
  it('refuses graph and guard construction when an untyped consumer omits the required clock', () => {
    const ClocklessGraph = FormRuleGraph as unknown as new (compiled: ReturnType<typeof compile>) => FormRuleGraph
    const ClocklessGuard = GuardEvaluator as unknown as new () => GuardEvaluator

    expect(() => new ClocklessGraph(compile([]))).toThrow(/clock/i)
    expect(() => new ClocklessGuard()).toThrow(/clock/i)
  })

  it('samples an advancing clock once for an evaluation and once again for reactive re-evaluation', () => {
    const instants = [
      new Date('2026-06-30T23:59:59.000Z'),
      new Date('2026-07-01T00:00:01.000Z'),
    ]
    let reads = 0
    const clock = () => instants[reads++]
    const rules = [
      rule('c.first', 'first', 'Compute', { if: [{ var: 'toggle' }, { 'date.today': [] }, { 'date.today': [] }] }),
      rule('c.second', 'second', 'Compute', { if: [{ var: 'toggle' }, { 'date.today': [] }, { 'date.today': [] }] }),
    ]
    const graph = new FormRuleGraph(compile(rules), clock)

    const first = graph.evaluateInstance(instance({ toggle: true }))
    expect(reads).toBe(1)
    expect(first.values.get('field:first')).toEqual({ state: 'Resolved', value: '2026-06-30' })
    expect(first.values.get('field:second')).toEqual({ state: 'Resolved', value: '2026-06-30' })

    const next = graph.reevaluate('toggle', valueSnapshot(false))
    expect(reads).toBe(2)
    expect(next.values.get('field:first')).toEqual({ state: 'Resolved', value: '2026-07-01' })
    expect(next.values.get('field:second')).toEqual({ state: 'Resolved', value: '2026-07-01' })
  })

  it('treats date.today as an input without replacing genuinely unaffected outcomes', () => {
    const before = new Date('2026-06-30T23:59:59.000Z')
    const after = new Date('2026-07-01T00:00:01.000Z')
    let reads = 0
    const graph = new FormRuleGraph(compile([
      rule('c.today', 'today', 'Compute', { 'date.today': [] }),
      rule('c.label', 'label', 'Compute', { cat: [{ var: 'today' }, '/', { var: 'other' }] }),
      rule('c.stable', 'stable-output', 'Compute', { var: 'stable' }),
      rule('v.today', 'today-valid', 'Validate', { '==': [{ 'date.today': [] }, '2026-07-01'] }),
    ]), () => [before, after][reads++])
    const first = graph.evaluateInstance(instance({ other: 'before', stable: 'unchanged' }))
    const stableOutcome = first.byRule.get('c.stable')

    const incremental = graph.reevaluate('other', valueSnapshot('after'))
    const full = new FormRuleGraph(compile([
      rule('c.today', 'today', 'Compute', { 'date.today': [] }),
      rule('c.label', 'label', 'Compute', { cat: [{ var: 'today' }, '/', { var: 'other' }] }),
      rule('c.stable', 'stable-output', 'Compute', { var: 'stable' }),
      rule('v.today', 'today-valid', 'Validate', { '==': [{ 'date.today': [] }, '2026-07-01'] }),
    ]), () => after).evaluateInstance(instance({ other: 'after', stable: 'unchanged' }))

    expect(reads).toBe(2)
    expect(incremental.values.get('field:today')).toEqual(full.values.get('field:today'))
    expect(incremental.values.get('field:label')).toEqual(full.values.get('field:label'))
    expect(incremental.byRule.get('v.today')?.validity).toEqual(full.byRule.get('v.today')?.validity)
    expect(incremental.byRule.get('c.stable')).toBe(stableOutcome)
  })

  it('leaves literal date.today and var shapes inert across an unrelated edit and midnight', () => {
    const before = new Date('2026-06-30T23:59:59.000Z')
    const after = new Date('2026-07-01T00:00:01.000Z')
    let reads = 0
    const graph = new FormRuleGraph(compile([
      rule('c.literal', 'literal', 'Compute', { cat: [{ 'date.today': [], var: 'ignored' }, [{ 'date.today': [] }, { var: 'ignored' }]] }),
      rule('c.changed', 'changed', 'Compute', { var: 'unrelated' }),
    ]), () => [before, after][reads++])

    const first = graph.evaluateInstance(instance({ unrelated: 'before' }))
    const literal = first.byRule.get('c.literal')
    const next = graph.reevaluate('unrelated', valueSnapshot('after'))

    expect(first.values.get('field:literal')).toEqual({ state: 'Resolved', value: '{"date.today":[],"var":"ignored"}[{"date.today":[]},{"var":"ignored"}]' })
    expect(reads).toBe(2)
    expect(next.byRule.get('c.literal')).toBe(literal)
    expect(next.values.get('field:literal')).toEqual(first.values.get('field:literal'))
  })
})

describe('core outcome and dynamic missing scheduling', () => {
  it.each([false, true])('evaluates dynamic missing after computed producers in either source order (%s)', (reversed) => {
    const missing = rule('a.missing', 'missing', 'Compute', { missing: [{ var: 'keys' }] })
    const produced = rule('z.produced', 'produced', 'Compute', { if: [{ var: 'enabled' }, 1, null] })
    const graph = new FormRuleGraph(compile(reversed ? [produced, missing] : [missing, produced]), fixedClock)

    const first = graph.evaluateInstance(instance({ keys: ['produced'], enabled: true }))
    expect(first.values.get('field:missing')).toEqual({ state: 'Resolved', value: [] })
    expect(graph.reevaluate('enabled', valueSnapshot(false)).values.get('field:missing')).toEqual({ state: 'Resolved', value: ['produced'] })
    expect(graph.reevaluate('enabled', valueSnapshot(true)).values.get('field:missing')).toEqual({ state: 'Resolved', value: [] })
    expect(graph.reevaluate('keys', valueSnapshot([])).values.get('field:missing')).toEqual({ state: 'Resolved', value: [] })
  })

  it.each([false, true])('does not turn independent dynamic readers into a static cycle (%s)', (reversed) => {
    const a = rule('a.dynamic', 'a', 'Compute', { missing: [{ var: 'keys' }] })
    const b = rule('b.dynamic', 'b', 'Compute', { missing: [{ var: 'keys' }] })
    const result = graphOf(reversed ? [b, a] : [a, b], { keys: ['raw'], raw: 1 }).first
    expect(result.values.get('field:a')).toEqual({ state: 'Resolved', value: [] })
    expect(result.values.get('field:b')).toEqual({ state: 'Resolved', value: [] })
  })

  it('does not reverse a normal downstream dependency for dynamic missing', () => {
    const { g, first } = graphOf([
      rule('a.dynamic', 'a', 'Compute', { missing: [{ var: 'keys' }] }),
      rule('b.downstream', 'b', 'Compute', { '!!': [{ var: 'a' }] }),
    ], { keys: ['raw'], raw: 1 })
    expect(first.values.get('field:b')).toEqual({ state: 'Resolved', value: false })
    expect(g.reevaluate('raw', valueSnapshot(null)).values.get('field:b')).toEqual({ state: 'Resolved', value: true })
    expect(g.reevaluate('keys', valueSnapshot([])).values.get('field:b')).toEqual({ state: 'Resolved', value: false })
  })

  it('refuses a dynamic self read instead of using a raw shadow', () => {
    const graph = new FormRuleGraph(compile([rule('a.dynamic', 'a', 'Compute', { missing: [{ var: 'keys' }] })]), fixedClock)
    const first = graph.evaluateInstance(instance({ keys: ['a'], a: 1 }))
    expect(first.values.get('field:a')).toEqual({ state: 'Error', error: { code: Codes.cycle, params: { cell: 'field:a' } } })
    graph.evaluateInstance(instance({ keys: ['raw'], raw: 1 }))
    expect(graph.reevaluate('keys', valueSnapshot(['a'])).values.get('field:a')).toEqual({ state: 'Error', error: { code: Codes.cycle, params: { cell: 'field:a' } } })
  })

  it('demands row producers before folding a dynamically demanded aggregate', () => {
    const graph = new FormRuleGraph(compile([
      // Deliberately first: this is the public demand path, not a topo-order test.
      rule('a.missing', 'missing', 'Compute', { missing: [{ var: 'keys' }] }),
      rule('b.total', 'total', 'Compute', { if: [{ '==': [{ var: 'table.sum(items.calculated)' }, 10] }, 10, null] }),
      rule('z.calculated', 'items/calculated', 'Compute', { if: [{ var: 'enabled' }, 10, 0] }, 'Row'),
      rule('stable.copy', 'stableOut', 'Compute', { var: 'stable' }),
    ]), fixedClock)

    const first = graph.evaluateInstance(instance({ keys: ['total'], enabled: true, stable: 'unchanged', items: [{ _id: 'r1', calculated: 99 }] }))
    expect(first.values.get('field:total')).toEqual({ state: 'Resolved', value: 10 })
    expect(first.values.get('field:missing')).toEqual({ state: 'Resolved', value: [] })
    const stable = first.byRule.get('stable.copy')

    const absent = graph.reevaluate('enabled', valueSnapshot(false))
    expect(absent.values.get('field:total')).toEqual({ state: 'Resolved', value: null })
    expect(absent.values.get('field:missing')).toEqual({ state: 'Resolved', value: ['total'] })
    expect(absent.byRule.get('stable.copy')).toBe(stable)

    const present = graph.reevaluate('enabled', valueSnapshot(true))
    expect(present.values.get('field:total')).toEqual({ state: 'Resolved', value: 10 })
    expect(present.values.get('field:missing')).toEqual({ state: 'Resolved', value: [] })
    expect(present.byRule.get('stable.copy')).toBe(stable)
  })

  it('uses the declared dynamic edge bound on initial and incremental evaluation', () => {
    const dynamic = (id: string, target: string, key: string) => rule(id, target, 'Compute', { missing: [{ var: key }] })
    const limits = { ...DEFAULT_LIMITS, maxDependencyDepth: 2 }
    const exact = new FormRuleGraph(compile([
      dynamic('a.dynamic', 'a', 'keysA'),
      dynamic('b.dynamic', 'b', 'keysB'),
      dynamic('c.dynamic', 'c', 'keysC'),
    ], limits), fixedClock, limits)
    expect(exact.evaluateInstance(instance({ keysA: ['b'], keysB: ['c'], keysC: ['raw'], raw: 1 })).values.get('field:a')?.state).toBe('Resolved')
    expect(exact.reevaluate('keysA', valueSnapshot(['b'])).values.get('field:a')?.state).toBe('Resolved')

    const over = new FormRuleGraph(compile([
      dynamic('a.dynamic', 'a', 'keysA'),
      dynamic('b.dynamic', 'b', 'keysB'),
      dynamic('c.dynamic', 'c', 'keysC'),
      dynamic('d.dynamic', 'd', 'keysD'),
    ], limits), fixedClock, limits)
    expect(over.evaluateInstance(instance({ keysA: ['b'], keysB: ['c'], keysC: ['d'], keysD: ['raw'], raw: 1 })).values.get('field:a')?.state).toBe('Error')
    expect(over.reevaluate('keysA', valueSnapshot(['b'])).values.get('field:a')?.state).toBe('Error')

    // Zero is valid only for a no-dependency leaf; its initial evaluation still enters
    // the same scheduler and must not be treated as depth one.
    const zeroLimits = { ...DEFAULT_LIMITS, maxDependencyDepth: 0 }
    const zero = new FormRuleGraph(compile([rule('leaf.literal', 'leaf', 'Compute', 1)], zeroLimits), fixedClock, zeroLimits)
    expect(zero.evaluateInstance(instance({})).values.get('field:leaf')?.state).toBe('Resolved')
    expect(zero.reevaluate('unrelated', valueSnapshot(null)).values.get('field:leaf')?.state).toBe('Resolved')
  })

  it('re-evaluates dynamic Required and Validate plans when their actual target changes', () => {
    const { g, first } = graphOf([
      rule('required.dynamic', 'target', 'Required', { missing: [{ var: 'keys' }] }),
      rule('readonly.dynamic', 'restricted', 'ReadOnly', { missing: [{ var: 'keys' }] }),
      rule('validate.dynamic', 'target', 'Validate', { '!': [{ missing: [{ var: 'keys' }] }] }),
    ], { keys: ['raw'], raw: null })
    expect(first.visibility.get('field:target')?.required).toBe(true)
    expect(first.visibility.get('field:restricted')?.readOnly).toBe(true)
    expect(first.byRule.get('validate.dynamic')?.validity?.ok).toBe(false)
    const recovered = g.reevaluate('raw', valueSnapshot(1))
    expect(recovered.visibility.get('field:target')?.required).toBe(false)
    expect(recovered.visibility.get('field:restricted')?.readOnly).toBe(false)
    expect(recovered.byRule.get('validate.dynamic')?.validity?.ok).toBe(true)
  })

  it('demands a computed row producer named by a dynamic missing key', () => {
    const first = graphOf([
      rule('a.row-missing', 'items/missing', 'Compute', { missing: [{ var: 'rowKeys' }] }, 'Row'),
      rule('z.row-amount', 'items/amount', 'Compute', { if: [{ var: 'row.enabled' }, 1, null] }, 'Row'),
    ], { rowKeys: ['row.amount'], items: [{ _id: 'r1', enabled: true }] }).first
    expect(first.values.get('row:items/r1/missing')).toEqual({ state: 'Resolved', value: [] })
  })

  it('keeps a literal star field name as a normal static dependency', () => {
    const { g, first } = graphOf([
      rule('a.star', 'a', 'Compute', { var: 'field.*' }),
      rule('b.downstream', 'b', 'Compute', { var: 'a' }),
    ], { '*': 1 })
    expect(first.values.get('field:b')).toEqual({ state: 'Resolved', value: 1 })
    expect(g.reevaluate('*', valueSnapshot(2)).values.get('field:b')).toEqual({ state: 'Resolved', value: 2 })
  })

  it('uses row cells for missing_some keys and re-evaluates a threshold read', () => {
    const graph = new FormRuleGraph(compile([
      rule('a.row-missing', 'items/missing', 'Compute', { missing_some: [{ var: 'threshold' }, ['row.amount', 'row.other']] }, 'Row'),
      rule('z.row-amount', 'items/amount', 'Compute', { if: [{ var: 'row.enabled' }, 1, null] }, 'Row'),
    ]), fixedClock)

    const first = graph.evaluateInstance(instance({ threshold: 1, items: [{ _id: 'r1', enabled: true }] }))
    expect(first.values.get('row:items/r1/missing')).toEqual({ state: 'Resolved', value: [] })
    expect(graph.reevaluate('threshold', valueSnapshot(2)).values.get('row:items/r1/missing')).toEqual({ state: 'Resolved', value: ['row.other'] })
  })

  it('preserves actual empty if/and/or outcomes through a downstream computation', () => {
    const first = graphOf([
      rule('c.empty-if', 'emptyIf', 'Compute', { if: [] }),
      rule('c.one-if', 'oneIf', 'Compute', { if: [false, 1] }),
      rule('c.and', 'and', 'Compute', { and: [] }),
      rule('c.or', 'or', 'Compute', { or: [] }),
      rule('c.downstream', 'downstream', 'Compute', { '+': [{ var: 'emptyIf' }, 1] }),
    ], {}).first

    expect(first.values.get('field:emptyIf')).toEqual({ state: 'Resolved', value: null })
    expect(first.values.get('field:oneIf')).toEqual({ state: 'Resolved', value: null })
    expect(first.values.get('field:and')).toEqual({ state: 'Resolved', value: true })
    expect(first.values.get('field:or')).toEqual({ state: 'Resolved', value: false })
    expect(first.values.get('field:downstream')?.state).toBe('Error')
  })

  it.each([
    [{ if: [] }, {}, 'null', 'Resolved', false, false],
    [{ if: [false, 1] }, {}, 'null', 'Resolved', false, false],
    [{ and: [] }, {}, 'boolean', 'Resolved', false, false],
    [{ or: [] }, {}, 'boolean', 'Resolved', false, false],
    [{ '+': [{ var: 'value' }, 1] }, { value: {} }, 'number', 'Error', true, true],
    [{ '+': [{ var: 'value' }, 1] }, { value: { '@pending': true } }, 'number', 'Pending', true, true],
  ] as const)('keeps each actual transfer outcome in the derived result contract', (expression, input, type, state, canError, canPending) => {
    const derived = deriveCoreTypes(expression, 'c.transfer')
    const actual = graphOf([rule('c.transfer', 'transfer', 'Compute', expression)], input).first.values.get('field:transfer')!

    expect(derived.types.has(type as CoreJsonType)).toBe(true)
    expect(actual.state).toBe(state)
    expect(derived.canError).toBe(canError)
    expect(derived.canPending).toBe(canPending)
  })
})

describe('runtime-owned return and program boundaries', () => {
  it('refuses forged and proxied compiled graphs before any caller-owned property is read', () => {
    const forged = { rules: [compile([rule('c.safe', 'safe', 'Compute', 1)]).rules[0]] }
    expect(() => new FormRuleGraph(forged, fixedClock)).toThrow(Codes.contextSnapshotRequired)

    let invoked = false
    const proxied = new Proxy(forged, {
      get: () => {
        invoked = true
        throw new Error('compiled graph proxy ran')
      },
    })
    expect(() => new FormRuleGraph(proxied, fixedClock)).toThrow(Codes.contextSnapshotRequired)
    expect(invoked).toBe(false)
    expect(() => new FormRuleGraph(compile([rule('c.genuine', 'genuine', 'Compute', 1)]), fixedClock)).not.toThrow()
  })

  it('does not retain a caller mutation of an exposed computed object or compiled program', () => {
    const definition = rule('c.object', 'result', 'Compute', { if: [{ var: 'on' }, { answer: 1, ok: true }, { answer: 2, ok: true }] })
    const compiled = compile([definition])
    const graph = new FormRuleGraph(compiled, fixedClock)
    const first = graph.evaluateInstance(instance({ on: true }))
    const exposed = first.values.get('field:result')!.value as Record<string, Json>
    expect(() => { exposed.answer = 99 }).toThrow()

    expect(() => { (compiled.rules[0].ast as Record<string, Json>).if = [] }).toThrow()
    definition.expression = { answer: 99 }

    const second = graph.reevaluate('on', valueSnapshot(true))
    expect(second.values.get('field:result')).toEqual({ state: 'Resolved', value: { answer: 1, ok: true } })
  })

  it('returns a detached guard value after an accessor is planted on a previous result', () => {
    const guard = new GuardEvaluator(fixedClock)
    const definition = rule('g.object', 'result', 'Compute', { if: [true, { answer: 1, ok: true }, null] })
    const context = RuleContextSnapshot.fromJsonText('{}')
    const first = guard.evaluateValue(definition, context)
    const exposed = first.value as Record<string, Json>
    expect(() => Object.defineProperty(exposed, 'answer', { get: () => 99, configurable: true })).toThrow()

    expect(guard.evaluateValue(definition, context)).toEqual({ state: 'Resolved', value: { answer: 1, ok: true } })
  })
})

describe('public input envelope boundaries', () => {
  it.each([[63, true], [64, false]])('counts %i nested arrays beneath the root object', (arrays, admitted) => {
    const source = `{"x":${'['.repeat(arrays)}null${']'.repeat(arrays)}}`
    if (admitted) expect(() => RuleInstance.fromJsonText(source)).not.toThrow()
    else expect(() => RuleInstance.fromJsonText(source)).toThrow()
  })

  it.each(['null', '1', '"text"', 'true'])('counts only containers at the deepest scalar leaf (%s)', (leaf) => {
    const atLimit = `{"x":${'['.repeat(63)}${leaf}${']'.repeat(63)}}`
    const overLimit = `{"x":${'['.repeat(64)}${leaf}${']'.repeat(64)}}`
    expect(() => RuleInstance.fromJsonText(atLimit)).not.toThrow()
    expect(() => RuleInstance.fromJsonText(overLimit)).toThrow()
  })

  it('counts escaped top-level, section, and row member names at the exact byte boundary', () => {
    const fieldNames = [...Array.from({ length: 16 }, (_, i) => `f${i}`), 'f"é']
    const rowNames = [...Array.from({ length: 16 }, (_, i) => `r${i}`), 'r"é']
    const section = 's"é'
    const rowId = 'row"é'
    const document = (payload: string): string => `{${fieldNames.map((name) => `${JSON.stringify(name)}:""`).join(',')},"payload":"${payload}",${JSON.stringify(section)}:[{"_id":${JSON.stringify(rowId)},${rowNames.map((name) => `${JSON.stringify(name)}:""`).join(',')}}]}`
    const payload = 'a'.repeat(INPUT_MAX_UTF8_BYTES - new TextEncoder().encode(document('')).length)

    expect(new TextEncoder().encode(document(payload)).length).toBe(INPUT_MAX_UTF8_BYTES)
    expect(() => RuleInstance.fromJsonText(document(payload))).not.toThrow()
    expect(() => RuleInstance.fromJsonText(document(`${payload}a`))).toThrow()
  })

  it('evaluates an at-cap source with JSON.stringify unicode, control, escaped-name, and normalized-number bytes', () => {
    const source = (payload: string): string => `{"\\u006Eame\\u0022":"😀😀\u2028\\b\\f","numeric":1e+00,"negative":-0,"payload":"${payload}"}`
    const jsonStringifyDocument = (payload: string): string => `{"name\\"":"😀😀\u2028\\b\\f","numeric":1,"negative":0,"payload":"${payload}"}`
    const payload = 'a'.repeat(INPUT_MAX_UTF8_BYTES - new TextEncoder().encode(source('')).length)
    const graph = new FormRuleGraph(compile([rule('c.copy', 'copy', 'Compute', { var: 'payload' })]), fixedClock)

    expect(new TextEncoder().encode(source(payload)).length).toBe(INPUT_MAX_UTF8_BYTES)
    expect(new TextEncoder().encode(JSON.stringify(JSON.parse(source(payload)))).length).toBeLessThanOrEqual(INPUT_MAX_UTF8_BYTES)
    expect(() => RuleInstance.fromJsonText(source(`${payload}a`))).toThrow(RangeError)
    expect(graph.evaluateInstance(RuleInstance.fromJsonText(source(payload))).values.get('field:copy')?.state).toBe('Resolved')
    expect(new TextEncoder().encode(jsonStringifyDocument(payload)).length).toBeLessThanOrEqual(INPUT_MAX_UTF8_BYTES)
  })

  it('uses the same JSON.stringify byte envelope for reactive composition', () => {
    const source = (payload: string): string => `{"\\u006Eame\\u0022":"😀😀\u2028\\b\\f","numeric":1e+00,"negative":-0,"payload":"${payload}"}`
    const jsonStringifyDocument = (payload: string): string => `{"name\\"":"😀😀\u2028\\b\\f","numeric":1,"negative":0,"payload":"${payload}"}`
    const payload = 'a'.repeat(INPUT_MAX_UTF8_BYTES - new TextEncoder().encode(jsonStringifyDocument('')).length)
    const graph = new FormRuleGraph(compile([rule('c.copy', 'copy', 'Compute', { var: 'payload' })]), fixedClock)
    graph.evaluateInstance(RuleInstance.fromJsonText(source('')))

    expect(new TextEncoder().encode(jsonStringifyDocument(payload)).length).toBe(INPUT_MAX_UTF8_BYTES)
    expect(graph.reevaluate('payload', RuleValueSnapshot.fromJsonText(JSON.stringify(payload))).values.get('field:copy')?.state).toBe('Resolved')
    expect(graph.reevaluate('payload', RuleValueSnapshot.fromJsonText(JSON.stringify(`${payload}a`))).validations[0].validity?.error?.code).toBe(Codes.inputTooLarge)
  })

  it('admits a typed JSON null reactive value while instances and guard snapshots still require objects', () => {
    const graph = new FormRuleGraph(compile([rule('c.value', 'value', 'Compute', { var: 'a' })]), fixedClock)
    graph.evaluateInstance(instance({ a: 1 }))
    expect(graph.reevaluate('a', RuleValueSnapshot.fromJsonText('null')).values.get('field:value')).toEqual({ state: 'Resolved', value: null })
    expect(() => RuleInstance.fromJsonText('null')).toThrow()
    expect(() => RuleContextSnapshot.fromJsonText('null')).toThrow()
  })

  it('enforces ascii/non-ascii byte and node at/over boundaries for reactive values', () => {
    expect(() => RuleValueSnapshot.fromJsonText(`"${'a'.repeat(INPUT_MAX_UTF8_BYTES - 2)}"`)).not.toThrow()
    expect(() => RuleValueSnapshot.fromJsonText(`"${'é'.repeat((INPUT_MAX_UTF8_BYTES - 2) / 2)}"`)).not.toThrow()
    expect(() => RuleValueSnapshot.fromJsonText(`"${'a'.repeat(INPUT_MAX_UTF8_BYTES - 1)}"`)).toThrow()
    expect(() => RuleValueSnapshot.fromJsonText(`"${'é'.repeat((INPUT_MAX_UTF8_BYTES - 2) / 2 + 1)}"`)).toThrow()
    expect(() => RuleValueSnapshot.fromJsonText(`[${Array(INPUT_MAX_NODES - 1).fill('null').join(',')}]`)).not.toThrow()
    expect(() => RuleValueSnapshot.fromJsonText(`[${Array(INPUT_MAX_NODES).fill('null').join(',')}]`)).toThrow()
  })

  it('retains inferred row ids outside the input envelope across reactive edits', () => {
    const rows = Array.from({ length: 2000 }, () => ({ value: 1 }))
    const graph = new FormRuleGraph(compile([rule('c.copy', 'copy', 'Compute', { var: 'a' })]), fixedClock)
    expect(graph.evaluateInstance(instance({ a: 1, items: rows })).isSaveBlocked).toBe(false)
    expect(graph.reevaluate('a', valueSnapshot(2)).values.get('field:copy')).toEqual({ state: 'Resolved', value: 2 })
    expect(graph.addRow('other', rowSnapshot({ id: 'r1', fields: { v: 1 } })).values.get('field:copy')).toEqual({ state: 'Resolved', value: 2 })
  })

  it('atomically refuses a cumulative new-section row without disturbing cached unrelated output', () => {
    const body = `{${Array.from({ length: INPUT_MAX_NODES - 2 }, (_, i) => i === 0 ? '"a":1' : `"f${i}":null`).join(',')}}`
    const graph = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)
    graph.evaluateInstance(RuleInstance.fromJsonText(body))
    const refused = graph.addRow('new-section', RuleRowSnapshot.fromJsonText('{"id":"r1","fields":{"v":null}}'))
    expect(refused.isSaveBlocked).toBe(true)
    expect(refused.validations[0].validity?.error?.code).toBe(Codes.inputTooLarge)
    const changed = graph.reevaluate('a', valueSnapshot(2))
    const stable = changed.byRule.get('c.b')
    expect(changed.values.get('field:b')).toEqual({ state: 'Resolved', value: 3 })
    expect(graph.reevaluate('unused', valueSnapshot(null)).byRule.get('c.b')).toBe(stable)
  })

  it('atomically refuses cumulative non-ascii byte growth while preserving the prior graph', () => {
    const prefix = '{"a":1,"payload":"'
    const source = `${prefix}${'é'.repeat((INPUT_MAX_UTF8_BYTES - prefix.length - 2) / 2)}"}`
    const graph = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)
    graph.evaluateInstance(RuleInstance.fromJsonText(source))
    expect(graph.reevaluate('unused', valueSnapshot(null)).validations[0].validity?.error?.code).toBe(Codes.inputTooLarge)
    expect(graph.reevaluate('a', valueSnapshot(2)).values.get('field:b')).toEqual({ state: 'Resolved', value: 3 })
  })

  it('refuses an oversized dynamic member name before it can enter reactive state', () => {
    const graph = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)
    graph.evaluateInstance(instance({ a: 1 }))
    expect(graph.reevaluate('x'.repeat(INPUT_MAX_UTF8_BYTES + 1), valueSnapshot(null)).validations[0].validity?.error?.code).toBe(Codes.inputTooLarge)
    expect(graph.reevaluate('a', valueSnapshot(1)).values.get('field:b')).toEqual({ state: 'Resolved', value: 2 })
  })

  it('refuses non-string remove identifiers before property coercion can address graph state', () => {
    const graph = new FormRuleGraph(compile([rule('c.total', 'total', 'Compute', { var: 'table.sum(items.amount)' })]), fixedClock)
    graph.evaluateInstance(instance({ items: [{ amount: 1 }] }))
    const hostile = new Proxy({}, { get: () => { throw new Error('property coercion ran') } })
    expect(graph.removeRow(hostile as string, hostile as string).validations[0].validity?.error?.code).toBe(Codes.inputTooLarge)
    expect(graph.removeRow('items', 'missing').values.get('field:total')).toEqual({ state: 'Resolved', value: 1 })
  })
})

describe('reactive re-evaluation — transitive dependents only', () => {
  it('treats hostile JSON member names as inert own data across initial, row, and reactive capture', () => {
    const graph = new FormRuleGraph(compile([
      rule('copy-constructor', 'copied-constructor', 'Compute', { var: 'constructor' }),
      rule('copy-proto', 'copied-proto', 'Compute', { var: '__proto__' }),
      rule('row-to-string', 'items/copied', 'Compute', { var: 'row.toString' }, 'Row'),
    ]), fixedClock)

    const first = graph.evaluateInstance(RuleInstance.fromJsonText('{"constructor":"owned","__proto__":"proto","items":[{"_id":"r1","toString":"row-owned"}]}'))
    expect(first.values.get('field:copied-constructor')).toEqual({ state: 'Resolved', value: 'owned' })
    expect(first.values.get('field:copied-proto')).toEqual({ state: 'Resolved', value: 'proto' })
    expect(first.values.get('row:items/r1/copied')).toEqual({ state: 'Resolved', value: 'row-owned' })

    expect(graph.reevaluate('__proto__', RuleValueSnapshot.fromJsonText('"changed"')).values.get('field:copied-proto'))
      .toEqual({ state: 'Resolved', value: 'changed' })
    expect(graph.addRow('items', RuleRowSnapshot.fromJsonText('{"id":"r2","fields":{"toString":"second"}}')).values.get('row:items/r2/copied'))
      .toEqual({ state: 'Resolved', value: 'second' })
  })

  it('refuses proxied form data before a RuleInstance capture trap can run', () => {
    let invoked = false
    const input = new Proxy({ a: 1 } as Record<string, Json>, {
      ownKeys: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
    })

    expect(() => RuleInstance.fromJson(input)).toThrow(Codes.contextSnapshotRequired)
    expect(invoked).toBe(false)
  })

  it('does not read replacement fields or tables on a branded instance', () => {
    const input = RuleInstance.fromJsonText('{"a":1}')
    let invoked = false
    Object.defineProperty(input, 'fields', { get: () => {
      invoked = true
      throw new Error('replacement fields ran')
    } })
    Object.defineProperty(input, 'tables', { get: () => {
      invoked = true
      throw new Error('replacement tables ran')
    } })

    const graph = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)
    expect(graph.evaluateInstance(input).values.get('field:b')).toEqual({ state: 'Resolved', value: 2 })
    expect(invoked).toBe(false)
  })

  it('does not share mutable reactive state when two graphs receive one owned instance', () => {
    const rules = [rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]
    const source = instance({ a: 1 })
    const first = new FormRuleGraph(compile(rules), fixedClock)
    const second = new FormRuleGraph(compile(rules), fixedClock)
    first.evaluateInstance(source)
    second.evaluateInstance(source)

    first.reevaluate('a', valueSnapshot(99))
    const result = second.addRow('unrelated', rowSnapshot({ id: 'r1', fields: {} }))

    expect(result.values.get('field:b')).toEqual({ state: 'Resolved', value: 2 })
  })

  it('refuses proxied reactive values before evaluation can read them', () => {
    const { g } = graphOf([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })], { a: 1 })
    let invoked = false
    const value = new Proxy({} as Json, {
      get: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
    })

    expect(() => g.reevaluate('a', value as unknown as RuleValueSnapshot)).toThrow(Codes.contextSnapshotRequired)
    expect(invoked).toBe(false)
  })

  it('refuses proxied reactive rows before graph mutation can enumerate them', () => {
    const { g } = graphOf([rule('c.total', 'total', 'Compute', { var: 'table.sum(items.amount)' })], { items: [{ amount: 1 }] })
    let invoked = false
    const row = new Proxy({ id: 'r2', fields: { amount: 2 } }, {
      get: () => {
        invoked = true
        throw new Error('proxy trap ran')
      },
    })

    expect(() => g.addRow('items', row as unknown as RuleRowSnapshot)).toThrow(Codes.contextSnapshotRequired)
    expect(invoked).toBe(false)
  })

  it('re-evaluates only the dependent front; non-dependent outcomes are referentially unchanged', () => {
    const rules = [
      rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] }),
      rule('c.d', 'd', 'Compute', { '+': [{ var: 'x' }, 1] }),
    ]
    const { g, first } = graphOf(rules, { a: 10, x: 100 })
    expect(first.values.get('field:b')).toEqual({ state: 'Resolved', value: 11 })
    expect(first.values.get('field:d')).toEqual({ state: 'Resolved', value: 101 })

    const dOutcomeBefore = first.byRule.get('c.d')
    const next = g.reevaluate('a', valueSnapshot(20))

    expect(next.values.get('field:b')).toEqual({ state: 'Resolved', value: 21 })
    // c.d does not depend on a — its outcome object is the SAME reference (not re-evaluated).
    expect(next.byRule.get('c.d')).toBe(dOutcomeBefore)
  })
})

describe('numeric / collation determinism (D1 ratification fix 2)', () => {
  it('money avg aggregate fails closed — no inexact decimal division (exact-or-fail-closed, no rounding)', () => {
    // Symmetric to the .NET tier: avg over a money (decimal-string) column would need inexact decimal
    // division (undefined in v1), so it fails closed rather than silently using IEEE double. (The corpus
    // proves the exact money add/mul/sum byte-identity; this pins the deliberate avg refusal.)
    const { first } = graphOf([rule('c.avg', 'avg', 'Compute', { var: 'table.avg(items.price)' })], { items: [{ price: '0.1' }, { price: '0.2' }] })
    const agg = first.values.get('agg:items/avg/price')
    expect(agg?.state).toBe('Error')
    expect(agg?.error?.code).toBe(Codes.moneyAggUnsupported)
  })
})

describe('incremental child-table edit', () => {
  const rules = [rule('c.total', 'total', 'Compute', { var: 'table.sum(items.amount)' })]

  it('add a row re-links the aggregate', () => {
    const { g, first } = graphOf(rules, { items: [{ amount: 10 }, { amount: 20 }] })
    expect(first.values.get('field:total')).toEqual({ state: 'Resolved', value: 30 })
    const after = g.addRow('items', rowSnapshot({ id: 'r3', fields: { amount: 5 } }))
    expect(after.values.get('field:total')).toEqual({ state: 'Resolved', value: 35 })
  })

  it('does not retain a rejected over-limit reactive row', () => {
    const compiled = compile(rules, { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1 })
    const g = new FormRuleGraph(compiled, fixedClock, { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1 })
    g.evaluateInstance(instance({ items: [{ amount: 1 }] }))

    expect(g.addRow('items', rowSnapshot({ id: 'r2', fields: { amount: 2 } })).values.get('agg:items/sum/amount')).toEqual({
      state: 'Error', error: { code: Codes.tableTooLarge, params: { section: 'items' } },
    })
    const after = g.reevaluate('unrelated', valueSnapshot(null))

    expect(after.values.get('field:total')).toEqual({ state: 'Resolved', value: 1 })
    expect(after.values.get('agg:items/sum/amount')).toEqual({ state: 'Resolved', value: 1 })
  })

  it('accepts over-limit unused and row-only tables rather than silently dropping rows', () => {
    const limits = { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1 }
    const graph = new FormRuleGraph(compile([
      rule('c.copy', 'copy', 'Compute', { var: 'a' }),
      rule('c.row', 'rows/doubled', 'Compute', { '+': [{ var: 'row.value' }, 1] }, 'Row'),
    ], limits), fixedClock, limits)
    graph.evaluateInstance(instance({ a: 1, unused: [{ value: 1 }], rows: [{ value: 1 }] }))
    expect(graph.addRow('unused', rowSnapshot({ id: 'r2', fields: { value: 2 } })).isSaveBlocked).toBe(false)
    expect(graph.addRow('rows', rowSnapshot({ id: 'r3', fields: { value: 2 } })).values.get('row:rows/r3/doubled')).toEqual({ state: 'Resolved', value: 3 })
  })

  it('remove a row re-links the aggregate', () => {
    const { g } = graphOf(rules, { items: [{ _id: 'a', amount: 10 }, { _id: 'b', amount: 20 }] })
    const after = g.removeRow('items', 'a')
    expect(after.values.get('field:total')).toEqual({ state: 'Resolved', value: 20 })
  })
})

describe('async Pending path', () => {
  it('a pending dependency yields Pending, then Resolved on settle', () => {
    const rules = [rule('c.ship', 'ship', 'Compute', { cat: [{ var: 'city' }, '!'] })]
    const { g, first } = graphOf(rules, { city: { '@pending': true } })
    expect(first.values.get('field:ship')).toEqual({ state: 'Pending' })
    expect(first.hasPending).toBe(true)
    expect(first.isSaveBlocked).toBe(true)

    const settled = g.reevaluate('city', valueSnapshot('Paris'))
    expect(settled.values.get('field:ship')).toEqual({ state: 'Resolved', value: 'Paris!' })
    expect(settled.hasPending).toBe(false)
  })
})

describe('static-cap rejection (identical to the .NET integrity tier)', () => {
  it('rejects a cyclic definition at publish with the cycle path', () => {
    const rules = [
      rule('c.x', 'x', 'Compute', { '+': [{ var: 'y' }, 1] }),
      rule('c.y', 'y', 'Compute', { '+': [{ var: 'x' }, 1] }),
    ]
    const e = (() => { try { compile(rules); return null } catch (err) { return err } })()
    expect(e).toBeInstanceOf(CompileError)
    expect((e as CompileError).code).toBe(Codes.compileCycle)
    expect((e as CompileError).cyclePath?.length).toBeGreaterThan(0)
  })

  it('rejects a Power-Fx (demoted) rule', () => {
    const r: RuleDefinition = { id: 'c.p', tier: 'PowerFx', scope: 'Field', scopeTarget: 'p', action: 'Compute', expression: { var: 'a' } }
    expect(() => compile([r])).toThrow(CompileError)
  })

  it('rejects a too-deep dependency chain', () => {
    const rules: RuleDefinition[] = []
    rules.push(rule('c.f0', 'f0', 'Compute', { '+': [{ var: 'seed' }, 1] }))
    for (let i = 1; i <= 70; i++) rules.push(rule(`c.f${i}`, `f${i}`, 'Compute', { '+': [{ var: `f${i - 1}` }, 1] }))
    const e = (() => { try { compile(rules); return null } catch (err) { return err } })()
    expect((e as CompileError).code).toBe(Codes.compileDepthExceeded)
  })

  it('rejects a rule exceeding the max-references bound', () => {
    const refs = Array.from({ length: 70 }, (_, i) => ({ var: `r${i}` }))
    const r = rule('c.many', 'many', 'Compute', { '+': refs })
    expect(() => compile([r])).toThrow(CompileError)
  })

  it('enforces graph, aggregate-row, and step boundaries at and immediately over their configured limit', () => {
    const graphAt = new FormRuleGraph(compile([rule('graph-at', 'x', 'Compute', 1)]), fixedClock,
      { ...DEFAULT_LIMITS, maxGraphNodes: 1 })
    expect(graphAt.evaluateInstance(instance({})).values.get('field:x')).toEqual({ state: 'Resolved', value: 1 })
    const graphOver = new FormRuleGraph(compile([
      rule('graph-over-a', 'a', 'Compute', 1), rule('graph-over-b', 'b', 'Compute', 2),
    ]), fixedClock, { ...DEFAULT_LIMITS, maxGraphNodes: 1 })
    expect(graphOver.evaluateInstance(instance({})).validations[0].validity?.error?.code).toBe(Codes.graphTooLarge)

    const total = rule('table-total', 'total', 'Compute', { var: 'table.sum(items.amount)' })
    const tableAt = new FormRuleGraph(compile([total]), fixedClock, { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1 })
    expect(tableAt.evaluateInstance(instance({ items: [{ amount: 1 }] })).values.get('field:total')).toEqual({ state: 'Resolved', value: 1 })
    const tableOver = new FormRuleGraph(compile([total]), fixedClock, { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 1 })
    expect(tableOver.evaluateInstance(instance({ items: [{ amount: 1 }, { amount: 2 }] })).values.get('agg:items/sum/amount'))
      .toMatchObject({ state: 'Error', error: { code: Codes.tableTooLarge } })

    const stepAt = new FormRuleGraph(compile([rule('step-at', 'x', 'Compute', 1)]), fixedClock,
      { ...DEFAULT_LIMITS, stepBudget: 2 })
    expect(stepAt.evaluateInstance(instance({})).values.get('field:x')).toEqual({ state: 'Resolved', value: 1 })
    const stepOver = new FormRuleGraph(compile([rule('step-over', 'x', 'Compute', 1)]), fixedClock,
      { ...DEFAULT_LIMITS, stepBudget: 0 })
    expect(stepOver.evaluateInstance(instance({})).validations[0].validity?.error?.code).toBe(Codes.budgetExceeded)
  })

  it('fails closed when the per-instance step budget is exhausted', () => {
    const g = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock, { ...{
      maxGraphNodes: 5000, maxTableRowsPerAggregate: 2000, maxDependencyDepth: 64, maxReferencesPerRule: 64,
      maxAstNodes: 256, maxLiteralLength: 4096, stepBudget: 0, wallClockMs: 250,
    } })
    const res = g.evaluateInstance(instance({ a: 1 }))
    expect(res.isSaveBlocked).toBe(true)
    expect(res.validations[0].validity?.error?.code).toBe(Codes.budgetExceeded)
  })
})

describe('wall-clock is a non-authoritative liveness fault, not a divergent outcome (D1 fix 1)', () => {
  // The wall-clock / AbortSignal is time- and hardware-dependent: if it produced a `rule.timeout`
  // OUTCOME, a slow tier would disagree with a fast tier, breaking replay-determinism + the byte-identical
  // corpus. So it PROPAGATES as an infrastructure fault (RuleTimeout) rather than returning a result.
  // (An AbortSignal is the deterministic, hardware-independent way to trip the guard — the TS analog of
  // the .NET pre-cancelled CancellationToken.)
  const one = () => new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock)

  it('evaluateInstance PROPAGATES RuleTimeout on an aborted signal (never a rule.timeout result)', () => {
    const ac = new AbortController()
    ac.abort()
    expect(() => one().evaluateInstance(instance({ a: 1 }), ac.signal)).toThrow(RuleTimeout)
  })

  it('reevaluate PROPAGATES RuleTimeout on an aborted signal', () => {
    const g = one()
    g.evaluateInstance(instance({ a: 1 }))
    const ac = new AbortController()
    ac.abort()
    expect(() => g.reevaluate('a', valueSnapshot(2), ac.signal)).toThrow(RuleTimeout)
  })

  it('a guard PROPAGATES RuleTimeout on an aborted signal (not a Validity verdict)', () => {
    const ac = new AbortController()
    ac.abort()
    const gd = new GuardEvaluator(fixedClock)
    const r: RuleDefinition = { id: 'g.min', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Validate', expression: { '>': [{ var: 'amount' }, 50] } }
    expect(() => gd.evaluateGuard(r, snapshot({ amount: 100 }), ac.signal)).toThrow(RuleTimeout)
  })

  it('the op-budget, by contrast, STAYS an authoritative fail-closed OUTCOME (deterministic across tiers)', () => {
    // Distinct from the wall-clock: the op-budget is deterministic (same op count on both tiers), so it
    // remains an outcome-affecting fail-closed result — the two tiers reach it identically.
    const g = new FormRuleGraph(compile([rule('c.b', 'b', 'Compute', { '+': [{ var: 'a' }, 1] })]), fixedClock, {
      maxGraphNodes: 5000, maxTableRowsPerAggregate: 2000, maxDependencyDepth: 64, maxReferencesPerRule: 64,
      maxAstNodes: 256, maxLiteralLength: 4096, stepBudget: 0, wallClockMs: 250,
    })
    const res = g.evaluateInstance(instance({ a: 1 }))
    expect(res.isSaveBlocked).toBe(true)
    expect(res.validations[0].validity?.error?.code).toBe(Codes.budgetExceeded)
  })
})

describe('unavailable aggregate refuses — never a fabricated null (ticket 162)', () => {
  it('an agg node the compiler cannot statically register is a publish-time refusal, not runtime data', () => {
    // Arity-4 (and expression-valued/non-string-arg) agg nodes used to skip reference
    // extraction, register no fold cell, and then refuse per-keystroke at runtime with no
    // authoring-time signal — permanently un-saveable data. The compiler now refuses them
    // (150-family direction), identically in both tiers.
    const bad = (expression: Json) => (() => { try { compile([rule('c.dyn', 'dyn', 'Compute', expression)]); return null } catch (e) { return e } })()
    for (const expression of [
      { agg: ['sum', 'items', 'amount', 'extra'] } as Json, // arity 4
      { agg: ['sum', { var: 'field.section' }, 'amount'] } as Json, // expression-valued arg (phantom String() cell before)
    ]) {
      const e = bad(expression)
      expect(e).toBeInstanceOf(CompileError)
      expect((e as CompileError).code).toBe(Codes.compileBadGrammar)
    }
  })

  it('a compiled aggregate the evaluation context cannot provide refuses with the ONE shared shape', () => {
    // The guard tier's flat context bag carries no tables, so a well-formed agg reference is
    // unavailable data there: rule.bad_reference with params {agg: "section/fn/col"} — the
    // exact shape (and param order) the shared corpus pins byte-identically across tiers.
    const guard = new GuardEvaluator(fixedClock)
    const v: RuleDefinition = { id: 'g.total', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Compute', expression: { var: 'table.sum(items.amount)' } }
    expect(guard.evaluateValue(v, snapshot({}))).toEqual({
      state: 'Error',
      error: { code: Codes.badReference, params: { agg: 'items/sum/amount' } },
    })
  })
})

describe('guard evaluator (workflow transition guards)', () => {
  const guard = new GuardEvaluator(fixedClock)
  const g: RuleDefinition = { id: 'g.minAmount', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Validate', expression: { '>': [{ var: 'amount' }, 50] } }

  it('passes when the guard holds', () => {
    expect(guard.evaluateGuard(g, snapshot({ amount: 100 }))).toEqual({ ok: true })
  })

  it('fails closed with a stable code when the guard does not hold', () => {
    expect(guard.evaluateGuard(g, snapshot({ amount: 10 }))).toEqual({ ok: false, error: { code: 'g.minAmount', params: {} } })
  })

  it('evaluates a value expression', () => {
    const v: RuleDefinition = { id: 'g.fee', tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Compute', expression: { 'money.mul': ['10', '3'] } }
    expect(guard.evaluateValue(v, snapshot({}))).toEqual({ state: 'Resolved', value: '30' })
  })

  it('fails closed on a pending dependency (server tier)', () => {
    expect(guard.evaluateGuard(g, snapshot({ amount: { '@pending': true } }))).toEqual({ ok: false, error: { code: Codes.pendingAtSave, params: {} } })
  })
})
