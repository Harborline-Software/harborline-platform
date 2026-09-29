// T-818: the static work proof is an admission ceiling, refused fail-closed at compile and again
// at graph construction under the graph's own limits. The .NET twin is StaticWorkCeilingTests.
import { describe, expect, it } from 'vitest'

import { compile } from '../compiler.js'
import { Codes } from '../codes.js'
import { BorrowerEnvironmentCodes, admitEnvironment as admitTestEnvironment, fieldReadEffect as testFieldRead, lentGrammar as testGrammar } from '../environment.js'
import { builtInFunctions as testBuiltIns } from '../functions.js'
import { CompileError } from '../grammar.js'
import { FormRuleGraph, type RuleEvaluationResult } from '../graph.js'
import { RuleInstance, RuleRowSnapshot, RuleValueSnapshot } from '../instance.js'
import { DEFAULT_LIMITS, type RuleEngineLimits } from '../limits.js'
import type { Json, RuleDefinition } from '../model.js'

const testAdmission = admitTestEnvironment({ borrower: 'rule-engine-tests', grammar: testGrammar, variables: { field: 'test', row: 'test', wf: 'test', timer: 'test' }, operations: testBuiltIns.map((f) => f.key), effects: [testFieldRead], missingValues: 'missing-field-reads-null', timeSource: 'injected-test-clock', timeZone: 'utc', phases: { AuthoringValidation: true, PublishValidation: true, Render: true, Submission: true, Run: true, SignOff: true }, replay: 'deterministic' }).forPhase('Run')
const clock = () => new Date('2026-06-30T00:00:00Z')

function compute(id: string, target: string, expression: Json, scope: RuleDefinition['scope'] = 'Field'): RuleDefinition {
  return { id, tier: 'JsonLogic', scope, scopeTarget: target, action: 'Compute', expression }
}

const literal = [compute('literal', 'x', 1)]
const dynamicRow = (i: number) => compute(`row${i}`, `items/c${i}`, { missing: [{ var: 'rowKeys' }] }, 'Row')
const rows = (count: number) => Array.from({ length: count }, (_, i) => dynamicRow(i))
const fields = (count: number) => Array.from({ length: count }, (_, i) => compute(`f${i}`, `f${i}`, { missing: [{ var: 'keys' }] }))

function refusalOf(action: () => unknown): CompileError {
  try { action() } catch (error) { if (error instanceof CompileError) return error; throw error }
  throw new Error('expected a compile refusal')
}

function expectFailClosed(result: RuleEvaluationResult, code: string): void {
  expect(result.isSaveBlocked).toBe(true)
  expect([...result.byRule.keys()]).toEqual(['rule.engine'])
  expect(result.validations).toHaveLength(1)
  expect(result.validations[0].validity).toEqual({ ok: false, error: { code, params: {} } })
}

function everyEntryPoint(graph: FormRuleGraph): RuleEvaluationResult[] {
  return [
    graph.evaluateInstance(RuleInstance.fromJsonText('{"y":1}')),
    graph.reevaluate('y', RuleValueSnapshot.fromJsonText('2')),
    graph.addRow('items', RuleRowSnapshot.fromJsonText('{"id":"r1","fields":{}}')),
    graph.removeRow('items', 'r1'),
  ]
}

describe('static work ceiling at compile', () => {
  const proof = compile(literal).workProof.maximumEvaluationWork

  it('admits a program exactly at the ceiling and refuses it one unit below', () => {
    expect(proof).toBe(130n)
    expect(compile(literal, { ...DEFAULT_LIMITS, maxStaticWork: proof }).workProof.maximumEvaluationWork).toBe(proof)

    const refusal = refusalOf(() => compile(literal, { ...DEFAULT_LIMITS, maxStaticWork: proof - 1n }))

    expect(refusal.code).toBe(Codes.compileWorkExceeded)
    expect(refusal.code).toBe('rule.compile.work_exceeded')
    expect(refusal.params).toEqual({ proof: '130', ceiling: '129' })
    expect(refusal.ruleId).toBeUndefined()
    expect(refusal.message).toBe('graph work proof 130 exceeds the static work ceiling 129 (more than 1x); '
      + 'lower maxTableRowsPerAggregate or maxGraphNodes, or author fewer dynamic Row or missing reads and fewer aggregate references')
  })

  it('reports the factor by which the proof exceeds the ceiling, and no factor for a zero ceiling', () => {
    expect(refusalOf(() => compile(literal, { ...DEFAULT_LIMITS, maxStaticWork: 1n })).message).toContain('ceiling 1 (more than 130x);')
    const zero = refusalOf(() => compile(literal, { ...DEFAULT_LIMITS, maxStaticWork: 0n }))
    expect(zero.params).toEqual({ proof: '130', ceiling: '0' })
    expect(zero.message).toContain('ceiling 0; lower')
  })

  it('leaves the other compile refusals without params', () => {
    expect(refusalOf(() => compile([compute('bad', 'x', { unknown_operator: [] })])).params).toEqual({})
  })
})

describe('static work ceiling default', () => {
  it('is 10^26 proof units and leaves the runtime step budget unchanged', () => {
    expect(DEFAULT_LIMITS.maxStaticWork).toBe(10n ** 26n)
    expect(DEFAULT_LIMITS.stepBudget).toBe(250_000)
  })

  it('admits the calibration diagnostics and refuses the smallest over-default dynamic Row program', () => {
    expect(compile(fields(64)).workProof.maximumEvaluationWork.toString()).toBe('1631738386921228193152')
    expect(compile(rows(1)).workProof.maximumEvaluationWork.toString()).toBe('3983736295385685821324000')
    expect(compile(rows(25)).workProof.maximumEvaluationWork.toString()).toBe('99593407384642145533100000')

    const refusal = refusalOf(() => compile(rows(26)))

    expect(refusal.code).toBe(Codes.compileWorkExceeded)
    expect(refusal.params).toEqual({ proof: '103577143680027831354424000', ceiling: '100000000000000000000000000' })
  })
})

describe('static work ceiling at graph construction', () => {
  const compiled = compile(literal)
  const at = (maxStaticWork: bigint): RuleEngineLimits => ({ ...DEFAULT_LIMITS, maxStaticWork })

  it('fails closed at every entry point when the graph limits put its proof over the ceiling', () => {
    const graph = new FormRuleGraph(compiled, clock, testAdmission, at(129n))

    expect(graph.workProof.maximumEvaluationWork).toBe(130n)
    for (const result of everyEntryPoint(graph)) expectFailClosed(result, Codes.compileWorkExceeded)
  })

  it('evaluates normally at exactly its own proof', () => {
    const results = everyEntryPoint(new FormRuleGraph(compiled, clock, testAdmission, at(130n)))

    for (const result of results) {
      expect(result.isSaveBlocked).toBe(false)
      expect(result.values.get('field:x')).toEqual({ state: 'Resolved', value: 1 })
    }
  })

  it('keeps the admission refusal first when both apply', () => {
    for (const result of everyEntryPoint(new FormRuleGraph(compiled, clock, null, at(0n))))
      expectFailClosed(result, BorrowerEnvironmentCodes.notAdmitted)
  })

  it('recomputes the proof under its own structural limits instead of trusting the compile-time proof', () => {
    const row = compile(rows(1))
    expect(row.workProof.maximumEvaluationWork < DEFAULT_LIMITS.maxStaticWork).toBe(true)

    const graph = new FormRuleGraph(row, clock, testAdmission, { ...DEFAULT_LIMITS, maxTableRowsPerAggregate: 200_000 })

    expect(graph.workProof.maximumEvaluationWork.toString()).toBe('984112130658328582132400000')
    expectFailClosed(graph.evaluateInstance(RuleInstance.fromJsonText('{}')), Codes.compileWorkExceeded)
  })
})
