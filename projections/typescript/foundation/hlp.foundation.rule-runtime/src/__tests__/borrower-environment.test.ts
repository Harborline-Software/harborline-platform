import { describe, it, expect, vi } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, relative, sep } from 'node:path'
import {
  admitEnvironment, BorrowerEnvironmentCodes, builtInFunctions, compile, fieldReadEffect, FormRuleGraph, GuardEvaluator, lentGrammar,
  RuleContextSnapshot, RuleInstance, RuleRowSnapshot, RuleValueSnapshot, type BorrowerEnvironmentDeclaration, type EvaluationPhase, type RuleDefinition,
} from '../index.js'

import { admissionRefusal, fenceResolver } from '../environment.js'
import type { Json } from '../model.js'
import { EvalBudget, refResolved, unavailableAggregate, type RefValue } from '../eval-support.js'
import { DEFAULT_LIMITS } from '../limits.js'
import { evaluate } from '../jsonlogic.js'

const keyFence = JSON.parse(readFileSync(new URL('../../../../../../conformance/hlp.foundation.rule-runtime/borrower-key-fence.json', import.meta.url), 'utf8')) as { cases: { id: string; group: string; expression: Json; bag: Record<string, Json>; refusal?: string; value?: Json }[] }

const clock = () => new Date('2026-09-24T00:00:00Z')
const allPhases = { AuthoringValidation: true, PublishValidation: true, Render: true, Submission: true, Run: true, SignOff: true }
const declaration = (over: Partial<BorrowerEnvironmentDeclaration> = {}): BorrowerEnvironmentDeclaration => ({
  borrower: 'ts-tests', grammar: lentGrammar, variables: { field: 't', row: 't' }, operations: builtInFunctions.map((f) => f.key),
  effects: [fieldReadEffect], missingValues: 'missing-field-reads-null', timeSource: 'injected', timeZone: 'utc', phases: allPhases, replay: 'deterministic', ...over,
})
const admitted = admitEnvironment(declaration()).forPhase('Run')
const code = (f: () => unknown) => { try { f() } catch (e) { return (e as { code?: string }).code } return undefined }
const rule = (id: string, scopeTarget: string, expression: unknown, action: RuleDefinition['action'] = 'Compute'): RuleDefinition =>
  ({ id, tier: 'JsonLogic', scope: 'Field', scopeTarget, action, expression: expression as RuleDefinition['expression'] })
const engineCode = (r: { validations: readonly { ruleId: string; validity?: { error?: { code: string } } }[] }) =>
  r.validations.find((o) => o.ruleId === 'rule.engine')?.validity?.error?.code

function assertKeyFenceCase(test: typeof keyFence.cases[number]) {
  const env = admitEnvironment(declaration({ variables: { field: 'subject fields' }, operations: ['var', 'missing', 'missing_some', 'cat', 'if'] }))
  const definition = rule(test.id, 'out', test.expression)
  const compiled = compile([definition])
  for (const phase of ['AuthoringValidation', 'PublishValidation'] as const) {
    expect(admissionRefusal(env.forPhase(phase), [test.expression])).toBe(test.group === 'dynamic' ? null : test.refusal ?? null)
    expect(admissionRefusal(env.forPhase(phase), compiled.rules.map((r) => r.ast))).toBe(test.group === 'dynamic' ? null : test.refusal ?? null)
  }
  const result = new GuardEvaluator(clock).evaluateValue(definition, RuleContextSnapshot.fromJsonText(JSON.stringify(test.bag)), env.forPhase('Run'))
  expect(result).toEqual(test.refusal
    ? { state: 'Error', error: { code: test.refusal, params: {} } }
    : { state: 'Resolved', value: test.value })
  const guard = new GuardEvaluator(clock).evaluateGuard(definition, RuleContextSnapshot.fromJsonText(JSON.stringify(test.bag)), env.forPhase('Run'))
  if (test.refusal) expect(guard.error?.code).toBe(test.refusal)
  else expect(guard.ok).toBe(test.group === 'flat' ? test.id !== 'field-alias' : (test.value as Json[]).length > 0)

}

describe('T-1232 borrower key fence', () => {
  it.each(keyFence.cases.filter((c) => c.group === 'literal'))('acceptance 1 / rules-ck-28 / rules-eng-26: $id', (test) => {
    assertKeyFenceCase(test)
  })

  it.each(keyFence.cases.filter((c) => c.group === 'dynamic'))('acceptance 2 / rules-eng-26: $id admits expressions and checks the resolved key', (test) => {
    assertKeyFenceCase(test)
  })

  const graphCases = ['missing', 'missing_some'].flatMap(operation =>
    (['Compute', 'Validate', 'Required'] as const).flatMap(action => [false, true].flatMap(computed =>
      [false, true].flatMap(declared => [false, true].map(dataDerived => ({ operation, action, computed, declared, dataDerived }))))))
  const phasedGraphCases = graphCases.flatMap(test => [false, true].map(incremental => ({ ...test, incremental })))
  it.each(phasedGraphCases)('acceptance 2 graph access ordering: $operation $action computed=$computed declared=$declared data=$dataDerived incremental=$incremental', ({ operation, action, computed, declared, dataDerived, incremental }) => {
    const keys: Json = dataDerived ? { var: 'field.keys' } : operation === 'missing' ? { cat: ['row.', 'x'] } : { if: [true, ['row.x'], []] }
    const missing: Json = operation === 'missing' ? { missing: [keys] } : { missing_some: [1, keys] }
    const definitions: RuleDefinition[] = [{ ...rule('probe', 'items/out', { if: [{ var: 'field.trigger' }, missing, []] }, action), scope: 'Row' }]
    if (computed) definitions.push({ ...rule('producer', 'items/x', 7), scope: 'Row' })
    const admission = admitEnvironment(declaration({ variables: declared ? { field: 't', row: 't' } : { field: 't' } })).forPhase('Run')
    const graph = new FormRuleGraph(compile(definitions), clock, admission)
    const instance = RuleInstance.fromJsonText('{"trigger":true,"keys":["row.x"],"items":[{"_id":"r1","x":7}]}')
    if (incremental) graph.evaluateInstance(instance)
    // Observe the real demand boundary and its raw fallback; retain the production implementation.
    type DemandBoundary = { resolveDemand(owner: string, plan: boolean, key: string, fallback: () => RefValue, ...rest: unknown[]): RefValue }
    const boundary = graph as unknown as DemandBoundary
    const resolveDemand = boundary.resolveDemand.bind(graph)
    let demands = 0, rawReads = 0, keyReads = 0
    const source = action === 'Compute' ? 'row:items/r1/out' : 'probe#r1'
    const observed = vi.spyOn(boundary, 'resolveDemand').mockImplementation((owner, plan, key, fallback, ...rest) => {
      if (owner === source && key === 'row:items/r1/x' && computed) demands++
      return resolveDemand(owner, plan, key, () => {
        if (owner === source && key === 'row:items/r1/x') rawReads++
        if (owner === source && key === 'field:keys') keyReads++
        return fallback()
      }, ...rest)
    })
    const check = (result: ReturnType<FormRuleGraph['evaluateInstance']>) => {
      const outcome = result.byRule.get('probe#r1')!
      if (!declared) expect((action === 'Compute' ? outcome.value : outcome.validity)?.error?.code).toBe('rule.environment.variable_not_admitted')
      else if (action === 'Compute') expect(outcome.value).toEqual({ state: 'Resolved', value: [] })
      else if (action === 'Validate') expect(outcome.validity?.error?.code).toBe('probe')
      else expect(result.visibility.get('row:items/r1/out')?.required).toBe(false)
      expect(demands).toBe(declared && computed ? 1 : 0)
      expect(rawReads).toBe(declared && !computed ? 1 : 0)
      expect(keyReads).toBe(dataDerived ? 1 : 0)
    }
    try {
      check(incremental ? graph.reevaluate('trigger', RuleValueSnapshot.fromJsonText('true')) : graph.evaluateInstance(instance))
    } finally { observed.mockRestore() }
  })

  it.each(keyFence.cases.filter(c => c.group === 'dynamic'))('acceptance 2 counting underlying resolver: $id', test => {
    const inner = { resolveVar: vi.fn((path: string) => refResolved(test.bag[path] ?? null)), resolveAgg: unavailableAggregate }
    const admission = admitEnvironment(declaration({ variables: { field: 't' } })).forPhase('Run')
    const context = { resolver: fenceResolver(admission, inner), now: clock(), budget: new EvalBudget(DEFAULT_LIMITS) }
    if (test.refusal) {
      expect(() => evaluate(test.expression, context)).toThrow(expect.objectContaining({ error: expect.objectContaining({ code: 'rule.environment.variable_not_admitted' }) }))
      expect(inner.resolveVar).toHaveBeenCalledTimes(0)
    } else {
      expect(evaluate(test.expression, context)).toEqual(test.value)
      expect(inner.resolveVar).toHaveBeenCalledTimes(1)
    }
  })

  it.each(keyFence.cases.filter((c) => c.group === 'flat'))('acceptance 3 / rules-ck-28: $id uses the production flat-bag resolver', (test) => {
    assertKeyFenceCase(test)
  })
})

describe('T-590 borrower environment admission (TS reactive tier)', () => {
  it('rules-ck-28: a typed borrower declaration is admitted; one missing a member, borrowing another grammar, asking for an effect, naming an unregistered function or leaving a phase unmarked refuses', () => {
    expect(admitEnvironment(declaration()).declaration.borrower).toBe('ts-tests')
    const refused: Partial<BorrowerEnvironmentDeclaration>[] = [
      { borrower: ' ' }, { grammar: 'harborline-jsonlogic/v2' }, { variables: {} }, { operations: ['cat', 'http.get'] },
      { effects: [fieldReadEffect, 'network'] }, { missingValues: '' }, { timeZone: '' },
      { phases: { Run: true } as unknown as Record<EvaluationPhase, boolean> }, { replay: '' },
    ]
    for (const over of refused) expect(code(() => admitEnvironment(declaration(over))), JSON.stringify(over)).toBe(BorrowerEnvironmentCodes.declarationRefused)
  })

  it('rules-eng-26: a phase the declaration marks inapplicable cannot be presented', () => {
    const env = admitEnvironment(declaration({ phases: { ...allPhases, Submission: false } }))
    expect(env.forPhase('Render').phase).toBe('Render')
    expect(code(() => env.forPhase('Submission'))).toBe(BorrowerEnvironmentCodes.phaseNotAdmitted)
  })

  it('rules-eng-26: guard and value overloads evaluate under an admitted declaration and refuse the unadmitted counterpart', () => {
    const guard = new GuardEvaluator(clock)
    const g = rule('g', 'ok', { '>': [{ var: 'amount' }, 10] }, 'Validate')
    const v = rule('v', 'out', { cat: ['a', { var: 'amount' }] })
    const context = RuleContextSnapshot.fromJsonText('{"amount":20}')
    expect(guard.evaluateGuard(g, context, admitted)).toEqual({ ok: true })
    expect(guard.evaluateValue(v, context, admitted)).toEqual({ state: 'Resolved', value: 'a20' })
    const notAdmitted = { code: BorrowerEnvironmentCodes.notAdmitted, params: {} }
    expect(guard.evaluateGuard(g, context, null)).toEqual({ ok: false, error: notAdmitted })
    expect(guard.evaluateValue(v, context, null)).toEqual({ state: 'Error', error: notAdmitted })
    // A structural look-alike is not evidence.
    expect(guard.evaluateGuard(g, context, { declaration: declaration(), phase: 'Run' })).toEqual({ ok: false, error: notAdmitted })
    const withoutCat = admitEnvironment(declaration({ operations: ['var', '>'] })).forPhase('Run')
    expect(guard.evaluateGuard(g, context, withoutCat)).toEqual({ ok: true })
    expect(guard.evaluateValue(v, context, withoutCat)).toEqual({ state: 'Error', error: { code: BorrowerEnvironmentCodes.operationNotAdmitted, params: {} } })
  })

  it('rules-eng-26: full graph evaluation evaluates under an admitted declaration and refuses the unadmitted counterpart', () => {
    const compiled = compile([rule('t', 'total', { '+': [{ var: 'a' }, 1] })])
    const instance = RuleInstance.fromJsonText('{"a":2}')
    expect(new FormRuleGraph(compiled, clock, admitted).evaluateInstance(instance).values.get('field:total')).toEqual({ state: 'Resolved', value: 3 })
    const refused = new FormRuleGraph(compiled, clock, null).evaluateInstance(instance)
    expect(engineCode(refused)).toBe(BorrowerEnvironmentCodes.notAdmitted)
    expect(refused.values.has('field:total')).toBe(false)
    const narrow = admitEnvironment(declaration({ operations: ['var'] })).forPhase('Run')
    expect(engineCode(new FormRuleGraph(compiled, clock, narrow).evaluateInstance(instance))).toBe(BorrowerEnvironmentCodes.operationNotAdmitted)
  })

  it('rules-eng-26: incremental graph evaluation (field change, row add, row remove) evaluates under admission and refuses the unadmitted counterpart', () => {
    const compiled = compile([rule('t', 'total', { var: 'table.sum(items.amount)' }), rule('d', 'double', { '*': [{ var: 'a' }, 2] })])
    const row = RuleRowSnapshot.fromJsonText('{"id":"r1","fields":{"amount":5}}')
    const graph = new FormRuleGraph(compiled, clock, admitted)
    graph.evaluateInstance(RuleInstance.fromJsonText('{"a":1}'))
    expect(graph.reevaluate('a', RuleValueSnapshot.fromJsonText('3')).values.get('field:double')).toEqual({ state: 'Resolved', value: 6 })
    expect(graph.addRow('items', row).values.get('field:total')).toEqual({ state: 'Resolved', value: 5 })
    expect(graph.removeRow('items', 'r1').values.get('field:total')).toEqual({ state: 'Resolved', value: 0 })
    const cases = [
      { admission: null, expected: BorrowerEnvironmentCodes.notAdmitted },
      { admission: admitEnvironment(declaration({ variables: { field: 't' } })).forPhase('Run'), expected: BorrowerEnvironmentCodes.variableNotAdmitted },
    ]
    for (const { admission, expected } of cases) {
      const refused = new FormRuleGraph(compiled, clock, admission)
      expect(engineCode(refused.evaluateInstance(RuleInstance.fromJsonText('{"a":1}')))).toBe(expected)
      expect(engineCode(refused.reevaluate('a', RuleValueSnapshot.fromJsonText('3')))).toBe(expected)
      expect(engineCode(refused.addRow('items', row))).toBe(expected)
      expect(engineCode(refused.removeRow('items', 'r1'))).toBe(expected)
    }
  })

  it('rules-eng-26: architecture fence — TS and React evaluation call sites are inventoried and each presents an admission', () => {
    const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..', '..', '..', '..')
    const inventoried = ['projections/typescript/foundation/hlp.foundation.rule-authoring/src/compile.ts']
    const entry = /new FormRuleGraph\(|\.evaluateGuard\(|\.evaluateValue\(/g
    const skip = new Set(['node_modules', 'dist', '__tests__', 'bin', 'obj', 'hlp.foundation.rule-runtime'])
    const found: string[] = []
    const walk = (dir: string) => {
      for (const name of readdirSync(dir)) {
        if (skip.has(name)) continue
        const path = join(dir, name)
        if (statSync(path).isDirectory()) { walk(path); continue }
        if (!/\.tsx?$/.test(name) || /\.test\.tsx?$/.test(name)) continue
        const source = readFileSync(path, 'utf8')
        const matches = [...source.matchAll(entry)]
        if (matches.length === 0) continue
        found.push(relative(root, path).split(sep).join('/'))
        for (const m of matches) {
          const line = source.slice(m.index, source.indexOf('\n', m.index))
          expect(line.includes('.forPhase(') || line.includes('admission'), `${path}: ${line}`).toBe(true)
        }
      }
    }
    walk(join(root, 'projections', 'typescript'))
    walk(join(root, 'projections', 'react'))
    expect(found.sort()).toEqual(inventoried)
  })
})
