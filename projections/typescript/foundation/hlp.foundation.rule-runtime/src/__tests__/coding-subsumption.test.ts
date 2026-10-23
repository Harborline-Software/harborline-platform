import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { CodingSubsumptionSnapshot } from '../index.js'
import { compile } from '../compiler.js'
import { deriveCoreWork } from '../core-work.js'
import { FormRuleGraph } from '../graph.js'
import { GuardEvaluator, RuleContextSnapshot } from '../guard.js'
import { RuleInstance, RuleRowSnapshot, RuleValueSnapshot } from '../instance.js'
import { DEFAULT_LIMITS, type RuleEngineLimits } from '../limits.js'
import type { Json, RuleDefinition } from '../model.js'
import { admitEnvironment, fieldReadEffect, lentGrammar } from '../environment.js'
import { builtInFunctions } from '../functions.js'

const clock = () => new Date('2026-10-22T00:00:00Z')
const limits = { ...DEFAULT_LIMITS, wallClockMs: 0 }
const admission = admitEnvironment({
  borrower: 'coding-tests', grammar: lentGrammar, variables: { field: 'test', row: 'test' },
  operations: builtInFunctions.map(f => f.key), effects: [fieldReadEffect],
  missingValues: 'missing-field-reads-null', timeSource: 'injected', timeZone: 'utc',
  phases: { AuthoringValidation: true, PublishValidation: true, Render: true, Submission: true, Run: true, SignOff: true }, replay: 'deterministic',
}).forPhase('Run')
interface Fixture {
  answersJsonText: string
  expectedBindingsJsonText: string
  alternative: { answersJsonText: string, expectedBindingsJsonText: string }
}
const fixture = (JSON.parse(readFileSync(new URL('../../../../../../conformance/hlp.foundation.rule-runtime/corpus/harborline-ops.json', import.meta.url), 'utf8')) as { codingFixture: Fixture }).codingFixture
const capture = () => CodingSubsumptionSnapshot.fromJsonText(fixture.answersJsonText, fixture.expectedBindingsJsonText)
const context = (code: Json) => RuleContextSnapshot.fromJsonText(JSON.stringify({ code }))
const coding = (code: string): Json => ({ system: 'asset-class', code })
function rule(expression: Json = { 'coding.is': [{ var: 'code' }, 'asset-class', 'root'] }, scope: RuleDefinition['scope'] = 'Field'): RuleDefinition {
  return { id: 'c', tier: 'JsonLogic', scope, scopeTarget: scope === 'Row' ? 'items/matched' : 'matched', action: 'Compute', expression }
}
const value = (code: Json, snapshot = capture(), customLimits: RuleEngineLimits = limits) =>
  new GuardEvaluator(clock, customLimits, snapshot).evaluateValue(rule(), context(code), admission)
const error = (code: string) => ({ state: 'Error', error: { code, params: {} } })
const digest = '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef'
const binding = ['s', 'vendor.domain.asset', '1.0.0', digest]
function wire(bindings: Json = [binding], answers: Json = [['s', 'a', 'd', true]], tenant = 'tenant-a') {
  return {
    expected: JSON.stringify(['harborline-taxonomy-coding-bindings/1', tenant, bindings]),
    actual: JSON.stringify(['harborline-taxonomy-coding-answers/1', tenant, bindings, answers]),
  }
}
const read = (pair: ReturnType<typeof wire>) => CodingSubsumptionSnapshot.fromJsonText(pair.actual, pair.expected)

describe('T-493 finite pinned coding reader', () => {
  it('owns the shared literal hierarchy answers and changes pins only on a new evaluator', () => {
    const first = capture()
    expect(Object.isFrozen(first)).toBe(true)
    expect(value(coding('leaf'), first)).toEqual({ state: 'Resolved', value: true })
    expect(value(coding('child'), first)).toEqual({ state: 'Resolved', value: true })
    expect(value(coding('missing'), first)).toEqual({ state: 'Resolved', value: false })
    const next = CodingSubsumptionSnapshot.fromJsonText(fixture.alternative.answersJsonText, fixture.alternative.expectedBindingsJsonText)
    expect(value(coding('leaf'), next)).toEqual({ state: 'Resolved', value: false })
    expect(value(coding('leaf'), first)).toEqual({ state: 'Resolved', value: true })
    expect(() => CodingSubsumptionSnapshot.fromJsonText(fixture.alternative.answersJsonText, fixture.expectedBindingsJsonText)).toThrow(TypeError)
  })

  it('rejects tenant, hash, definition, version and alias mismatches without normalization', () => {
    const pair = wire()
    for (const replacement of ['tenant-b', digest.toUpperCase(), 'vendor.domain.other', '2.0.0', 'S']) {
      const source = replacement === 'tenant-b' ? 'tenant-a' : replacement === digest.toUpperCase() ? digest : replacement === 'vendor.domain.other' ? 'vendor.domain.asset' : replacement === '2.0.0' ? '1.0.0' : '"s"'
      const target = replacement === 'S' ? '"S"' : replacement
      expect(() => CodingSubsumptionSnapshot.fromJsonText(pair.actual.replace(source, target), pair.expected)).toThrow(TypeError)
    }
    for (const version of ['latest', '1.*.0', '1.0', '-1.0.0', '4294967296.0.0', ' 1.0.0', '1.0.0 '])
      expect(() => read(wire([['s', 'vendor.domain.asset', version, digest]]))).toThrow(TypeError)
    for (const version of ['0.0.0', '4294967295.0.0', '0001.0.0'])
      expect(() => read(wire([['s', 'vendor.domain.asset', version, digest]]))).not.toThrow()
    expect(() => read(wire([['s', 'two.parts', '1.0.0', digest]]))).toThrow(TypeError)
  })

  it('requires exact tuple tags, arities, types, nonempty strings, order and uniqueness', () => {
    const invalidBindings: Json[] = [[], [binding, binding], [['z', ...binding.slice(1)], binding], [['', ...binding.slice(1)]], [['s', 'a..b', '1.0.0', digest]], [['s', 'a.b.c', 1, digest]]]
    for (const rows of invalidBindings) expect(() => read(wire(rows))).toThrow(TypeError)
    const invalidAnswers: Json[] = [[], [['s', 'a', 'd', 1]], [['s', 'a', 'd']], [['s', 'a', '', true]], [['other', 'a', 'd', true]], [['s', 'a', 'd', true], ['s', 'a', 'd', true]], [['s', 'z', 'd', true], ['s', 'a', 'd', false]]]
    for (const rows of invalidAnswers) expect(() => read(wire([binding], rows))).toThrow(TypeError)
    const pair = wire()
    expect(() => read({ ...pair, actual: pair.actual.replace('answers/1', 'answers/2') })).toThrow(TypeError)
    expect(() => read({ ...pair, expected: pair.expected.replace('bindings/1', 'bindings/2') })).toThrow(TypeError)
    expect(() => read({ ...pair, actual: '{}' })).toThrow(TypeError)
    expect(() => read({ ...pair, actual: pair.actual + 'null' })).toThrow(TypeError)
    expect(() => read(wire([binding, ['t', 'vendor.domain.asset', '2.0.0', digest]]))).not.toThrow()
  })

  it('bounds each document separately by UTF-8 bytes, depth and JSON values', () => {
    const pair = wire()
    const exactExpected = pair.expected + ' '.repeat(262144 - pair.expected.length)
    const exactActual = pair.actual + ' '.repeat(262144 - pair.actual.length)
    expect(() => read({ actual: exactActual, expected: exactExpected })).not.toThrow()
    expect(() => read({ ...pair, actual: exactActual + ' ' })).toThrow(TypeError)
    expect(() => read({ ...pair, expected: exactExpected + ' ' })).toThrow(TypeError)
    const oversized = ['"' + 'é'.repeat(131072) + '"', '['.repeat(65) + '0' + ']'.repeat(65), JSON.stringify(Array(5000).fill(null))]
    for (const text of oversized) {
      expect(() => read({ ...pair, actual: text })).toThrow(/ceiling/)
      expect(() => read({ ...pair, expected: text })).toThrow(/ceiling/)
      expect(() => read({ ...pair, actual: text })).toThrow(TypeError)
      expect(() => read({ ...pair, expected: text })).toThrow(TypeError)
    }
    // The answer envelope has ten fixed JSON values, then five per answer tuple.
    const answers = Array.from({ length: 998 }, (_, i) => ['s', 'a', String(i).padStart(4, '0'), true])
    expect(() => read(wire([binding], answers))).not.toThrow()
    expect(() => read(wire([binding], [...answers, ['s', 'a', '0998', true]]))).toThrow(TypeError)
  })

  it('rejects supplied null, undefined and forged objects without reading their properties', () => {
    let reads = 0
    const forged = new Proxy({}, { get() { reads++; throw new Error('read') }, ownKeys() { reads++; throw new Error('keys') }, getPrototypeOf() { reads++; throw new Error('prototype') } })
    for (const supplied of [null, undefined, {}, forged, Object.create(CodingSubsumptionSnapshot.prototype)]) {
      expect(() => Reflect.construct(GuardEvaluator, [clock, limits, supplied])).toThrow(TypeError)
      expect(() => Reflect.construct(FormRuleGraph, [compile([]), clock, admission, limits, supplied])).toThrow(TypeError)
    }
    expect(reads).toBe(0)
    expect(() => new GuardEvaluator(clock)).not.toThrow()
    expect(() => new FormRuleGraph(compile([]), clock, admission)).not.toThrow()
  })
})

describe('T-493 coding evaluation', () => {
  it('refuses absent bindings before empty input and absent pairs after a true array member', () => {
    const evaluator = new GuardEvaluator(clock, limits, capture())
    const missingBinding = rule({ 'coding.is': [[], 'other', 'root'] })
    expect(evaluator.evaluateValue(missingBinding, context(null), admission)).toEqual(error('rule.coding_binding_missing'))
    expect(value(coding('uncaptured'))).toEqual(error('rule.coding_answer_missing'))
    expect(value([coding('leaf'), coding('uncaptured')])).toEqual(error('rule.coding_answer_missing'))
    expect(value([coding('uncaptured'), coding('leaf')])).toEqual(error('rule.coding_answer_missing'))
    expect(value([coding('missing'), coding('leaf')])).toEqual({ state: 'Resolved', value: true })
    expect(value([null, {}, { system: 'other', code: 'uncaptured' }, { system: 'asset-class', code: 1 }, { system: 'asset-class', code: '' }])).toEqual({ state: 'Resolved', value: false })
    expect(value([])).toEqual({ state: 'Resolved', value: false })
  })

  it('passes the same owned capture to guard, full graph and reactive value paths', () => {
    const snapshot = capture()
    const evaluator = new GuardEvaluator(clock, limits, snapshot)
    expect(evaluator.evaluateGuard(rule(), context(coding('leaf')), admission)).toEqual({ ok: true })
    const graph = new FormRuleGraph(compile([rule()]), clock, admission, limits, snapshot)
    Object.assign(graph, { coding: null })
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{"code":{"system":"asset-class","code":"leaf"}}')).values.get('field:matched')).toEqual({ state: 'Resolved', value: true })
    expect(graph.reevaluate('code', RuleValueSnapshot.fromJsonText('{"system":"asset-class","code":"missing"}')).values.get('field:matched')).toEqual({ state: 'Resolved', value: false })
    expect(graph.reevaluate('code', RuleValueSnapshot.fromJsonText('{"system":"asset-class","code":"uncaptured"}')).values.get('field:matched')).toEqual(error('rule.coding_answer_missing'))
  })

  it('passes capture through full row and incremental row insertion', () => {
    const rowRule = rule({ 'coding.is': [{ var: 'row.code' }, 'asset-class', 'root'] }, 'Row')
    const graph = new FormRuleGraph(compile([rowRule]), clock, admission, limits, capture())
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{"items":[{"_id":"r1","code":{"system":"asset-class","code":"leaf"}}]}')).values.get('row:items/r1/matched')).toEqual({ state: 'Resolved', value: true })
    expect(graph.addRow('items', RuleRowSnapshot.fromJsonText('{"id":"r2","fields":{"code":{"system":"asset-class","code":"uncaptured"}}}')).values.get('row:items/r2/matched')).toEqual(error('rule.coding_answer_missing'))
    expect(graph.removeRow('items', 'r2').values.has('row:items/r2/matched')).toBe(false)
  })

  it('keeps legacy equality, coercions, unknown equality and array short circuit', () => {
    const evaluator = new GuardEvaluator(clock, limits)
    expect(evaluator.evaluateValue(rule(), context(coding('leaf')), admission)).toEqual({ state: 'Resolved', value: false })
    expect(evaluator.evaluateValue(rule(), context(coding('root')), admission)).toEqual({ state: 'Resolved', value: true })
    const legacy = rule({ 'coding.is': [{ var: 'code' }, '7', 'true'] })
    expect(evaluator.evaluateValue(legacy, context([{ system: 7, code: true }, { system: '7', code: 'uncaptured' }]), admission)).toEqual({ state: 'Resolved', value: true })
    expect(evaluator.evaluateValue(rule({ 'coding.is': [{ var: 'code' }, 'asset-class', 'unknown'] }), context(coding('unknown')), admission)).toEqual({ state: 'Resolved', value: true })
  })

  it('keeps taxonomy traversal and callback-like operators outside the grammar', () => {
    for (const op of ['taxonomy.subsumes', 'taxonomy.succeeds', 'coding.descendant'])
      expect(() => compile([rule({ [op]: ['x', 'y'] })])).toThrow()
  })
})

describe('T-493 work proof and runtime budget', () => {
  const expression: Json = { 'coding.is': [null, 's', 'a'] }
  const validation: RuleDefinition = { ...rule(expression), action: 'Validate' }
  const snapshot = () => read(wire())
  it.each([
    { name: 'null candidate', candidate: null, at: 12, below: [11, 9], expected: false },
    { name: 'matching candidate', candidate: { system: 's', code: 'd' }, at: 27, below: [26], expected: true },
    { name: 'other-system candidate', candidate: { system: 't', code: 'd' }, at: 17, below: [16], expected: false },
  ])('pins literal public guard/value budget boundaries for $name', ({ candidate, at, below, expected }) => {
    // Four AST steps + entry 3 + binding 4 + candidate 1 = 12. Matching adds 2 + 3 + 10; other-system adds 2 + 3.
    const bounded = rule({ 'coding.is': [candidate, 's', 'a'] })
    const admitted = new GuardEvaluator(clock, { ...limits, stepBudget: at }, snapshot())
    expect(admitted.evaluateValue(bounded, context(null), admission)).toEqual({ state: 'Resolved', value: expected })
    expect(admitted.evaluateGuard(bounded, context(null), admission)).toEqual(expected
      ? { ok: true }
      : { ok: false, error: { code: 'c', params: {} } })
    for (const stepBudget of below) {
      const refused = new GuardEvaluator(clock, { ...limits, stepBudget }, snapshot())
      expect(refused.evaluateValue(bounded, context(null), admission)).toEqual({ state: 'Error', error: { code: 'rule.budget_exceeded', params: { rule: 'c' } } })
      expect(refused.evaluateGuard(bounded, context(null), admission)).toEqual({ ok: false, error: { code: 'rule.budget_exceeded', params: { rule: 'c' } } })
    }
  })

  it('pins the adopted transfer literally and leaves legacy proof unchanged', () => {
    // Proposal002 transfer, with child result bounds 4 + 8 + 8 and child work 5 + 9 + 9.
    expect(deriveCoreWork(expression, 'c').work).toBe(105024n)
    expect(deriveCoreWork(expression, 'c', undefined, undefined, undefined, true).work).toBe(6554125000024n)
    const compiled = compile([validation])
    expect(compiled.workProof.maximumEvaluationWork).toBe(105029n)
    expect(new FormRuleGraph(compiled, clock, admission, limits, snapshot()).workProof.maximumEvaluationWork).toBe(6554125000029n)
  })

  it('admits exact static work and refuses one unit over in graph, guard and value', () => {
    const at = { ...limits, maxStaticWork: 6554125000029n }
    const below = { ...at, maxStaticWork: 6554125000028n }
    const compiled = compile([validation])
    expect(new FormRuleGraph(compiled, clock, admission, at, snapshot()).evaluateInstance(RuleInstance.fromJsonText('{}')).validations[0].ruleId).toBe('c')
    const refused = new FormRuleGraph(compiled, clock, admission, below, snapshot()).evaluateInstance(RuleInstance.fromJsonText('{}'))
    expect(refused.validations[0].validity).toEqual({ ok: false, error: { code: 'rule.compile.work_exceeded', params: {} } })
    expect(new GuardEvaluator(clock, at, snapshot()).evaluateValue(validation, context(null), admission)).toEqual({ state: 'Resolved', value: false })
    expect(new GuardEvaluator(clock, below, snapshot()).evaluateValue(validation, context(null), admission)).toEqual(error('rule.compile.work_exceeded'))
    expect(new GuardEvaluator(clock, below, snapshot()).evaluateGuard(validation, context(null), admission)).toEqual({ ok: false, error: { code: 'rule.compile.work_exceeded', params: {} } })
  })

  it('keeps borrower refusal ahead of strengthened proof and value capture', () => {
    const tiny = { ...limits, maxStaticWork: 105029n }
    const guard = new GuardEvaluator(clock, tiny, snapshot())
    let reads = 0
    const unowned = new Proxy({}, { get() { reads++; throw new Error('context read') } })
    expect(guard.evaluateValue(validation, unowned, null)).toEqual(error('rule.environment.not_admitted'))
    expect(guard.evaluateGuard(validation, unowned, null)).toEqual({ ok: false, error: { code: 'rule.environment.not_admitted', params: {} } })
    const graph = new FormRuleGraph(compile([validation]), clock, null, tiny, snapshot())
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{}')).validations[0].validity).toEqual({ ok: false, error: { code: 'rule.environment.not_admitted', params: {} } })
    expect(reads).toBe(0)
  })

  it('charges string work and every candidate against the existing deterministic budget', () => {
    expect(value(coding('leaf'), capture(), { ...limits, stepBudget: 10 })).toEqual({ state: 'Error', error: { code: 'rule.budget_exceeded', params: { rule: 'c' } } })
    expect(value(coding('leaf'), capture(), { ...limits, stepBudget: 100000 })).toEqual({ state: 'Resolved', value: true })
    const graph = new FormRuleGraph(compile([rule()]), clock, admission, { ...limits, stepBudget: 10 }, capture())
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{"code":{"system":"asset-class","code":"leaf"}}')).isSaveBlocked).toBe(true)
    const small = { ...limits, stepBudget: 50 }
    const manyCandidates = rule({ 'coding.is': [Array(100).fill(null), 's', 'a'] })
    expect(new GuardEvaluator(clock, small, snapshot()).evaluateValue(manyCandidates, context(null), admission)).toEqual({ state: 'Error', error: { code: 'rule.budget_exceeded', params: { rule: 'c' } } })
    const longAnswer = read(wire([binding], [['s', 'a', 'x'.repeat(100), true]]))
    const scan = rule({ 'coding.is': [{ var: 'code' }, 's', 'a'] })
    expect(new GuardEvaluator(clock, small, longAnswer).evaluateValue(scan, context({ system: 's', code: 'd' }), admission)).toEqual({ state: 'Error', error: { code: 'rule.budget_exceeded', params: { rule: 'c' } } })
  })
})

describe('T-493 reactive owned coding arrays', () => {
  it('keeps true then refuses an uncaptured member through public field updates', () => {
    const graph = new FormRuleGraph(compile([rule()]), clock, admission, limits, capture())
    expect(graph.evaluateInstance(RuleInstance.fromJsonText('{}')).values.get('field:matched')).toEqual({ state: 'Resolved', value: false })

    // Reactive value capture owns a field list; cold all-object root arrays remain child tables.
    const matched = graph.reevaluate('code', RuleValueSnapshot.fromJsonText('[{"system":"other","code":"leaf"},{"system":"asset-class","code":"leaf"}]'))
    expect(matched.values.get('field:matched')).toEqual({ state: 'Resolved', value: true })

    const missing = graph.reevaluate('code', RuleValueSnapshot.fromJsonText('[{"system":"asset-class","code":"leaf"},{"system":"asset-class","code":"uncaptured"}]'))
    expect(missing.values.get('field:matched')).toEqual({ state: 'Error', error: { code: 'rule.coding_answer_missing', params: {} } })
  })
})
