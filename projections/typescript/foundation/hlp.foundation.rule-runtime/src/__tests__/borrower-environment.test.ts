import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join, relative, sep } from 'node:path'
import {
  admitEnvironment, BorrowerEnvironmentCodes, builtInFunctions, compile, fieldReadEffect, FormRuleGraph, GuardEvaluator, lentGrammar,
  RuleContextSnapshot, RuleInstance, RuleRowSnapshot, RuleValueSnapshot, type BorrowerEnvironmentDeclaration, type EvaluationPhase, type RuleDefinition,
} from '../index.js'

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
