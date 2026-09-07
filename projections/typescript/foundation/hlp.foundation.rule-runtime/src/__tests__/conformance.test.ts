/**
 * The dual-tier conformance harness, TS side (SPINE-1 §6.1, §6.4). Loads the SAME
 * shared JSON corpus the .NET test project loads, runs each case through the TS engine,
 * and asserts the lowered AST + per-rule outcomes match byte-for-byte (canonical JSON).
 * Both tiers asserting against the one corpus is what proves the two evaluators agree.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, writeFileSync, mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

import type { Json, RuleDefinition } from '../model.js'
import { compile } from '../compiler.js'
import { CompileError } from '../grammar.js'
import { FormRuleGraph } from '../graph.js'
import { GuardEvaluator } from '../guard.js'
import { RuleInstance } from '../instance.js'
import { serializeComputedValue, serializeOutcome, write } from '../canonical.js'
import {
  compileDecisionTable,
  compileFormula,
  type DecisionCell,
  type DecisionTableSkin,
  type FormulaSkin,
  type NoMatch,
} from '../skins/index.js'

const here = dirname(fileURLToPath(import.meta.url))
const corpusDir = join(here, '..', '..', '..', '..', '..', '..', 'conformance', 'hlp.foundation.rule-runtime', 'corpus')
const artifactDir = join(here, '..', '..', '..', '..', '..', '..', 'artifacts', 'rule-runtime')

interface CorpusCase {
  name: string
  clock?: string
  limits?: Record<string, number>
  definitionRules?: Array<Record<string, Json>>
  skin?: Record<string, Json>
  instance: Record<string, Json>
  expectedAst?: Record<string, Json>
  expectedCompiledExpression?: Record<string, Json>
  expectedOutcomes?: Record<string, Json>
  /** Ticket 162: the case pins a PUBLISH-TIME refusal — compile must throw this stable code. */
  expectedCompileError?: { code: string }
  /** Ticket 162: evaluate this rule id through GuardEvaluator.evaluateValue over `instance` as the context bag. */
  guardValue?: string
  expectedGuardValue?: Record<string, Json>
}

function allCases(): CorpusCase[] {
  const out: CorpusCase[] = []
  for (const f of readdirSync(corpusDir).filter((p) => p.endsWith('.json')).sort()) {
    const root = JSON.parse(readFileSync(join(corpusDir, f), 'utf8')) as { cases: CorpusCase[] }
    out.push(...root.cases)
  }
  return out
}

function parseRule(r: Record<string, Json>): RuleDefinition {
  return {
    id: r.id as string,
    tier: r.tier as RuleDefinition['tier'],
    scope: r.scope as RuleDefinition['scope'],
    scopeTarget: (r.scopeTarget as string) ?? '',
    expression: r.expression as Json,
    action: r.action as RuleDefinition['action'],
    presentation: r.presentation as RuleDefinition['presentation'],
  }
}

// ADR 0146 D2 — corpus skin authoring JSON → the typed skin model → RuleDefinition (mirror of the .NET
// CorpusLoader skin parsing). A skin case exercises the identical downstream compile + eval path.
function compileSkin(s: Record<string, Json>): RuleDefinition {
  const kind = s.kind as string
  if (kind === 'decision-table') return compileDecisionTable(parseDecisionTableSkin(s))
  if (kind === 'formula') return compileFormula(parseFormulaSkin(s))
  throw new Error(`unknown skin kind '${kind}'`)
}

function parseDecisionTableSkin(s: Record<string, Json>): DecisionTableSkin {
  return {
    ruleId: s.ruleId as string,
    scope: s.scope as DecisionTableSkin['scope'],
    scopeTarget: (s.scopeTarget as string) ?? '',
    action: s.action as DecisionTableSkin['action'],
    hitPolicy: s.hitPolicy as DecisionTableSkin['hitPolicy'],
    inputs: s.inputs as string[],
    rows: (s.rows as Array<Record<string, Json>>).map((r) => ({
      when: (r.when as Array<Record<string, Json>>).map(parseCell),
      output: r.output as Json,
      priority: (r.priority as number) ?? 0,
    })),
    noMatch: parseNoMatch(s.noMatch as Record<string, Json>),
  }
}

function parseCell(c: Record<string, Json>): DecisionCell {
  if ('any' in c) return { kind: 'any' }
  if ('op' in c) return { kind: 'compare', op: c.op as string, value: c.value as Json }
  if ('range' in c) {
    const r = c.range as Record<string, Json>
    return { kind: 'range', loInclusive: r.lo ?? null, hiExclusive: r.hi ?? null }
  }
  throw new Error('a decision-table cell must be one of { any | op/value | range }')
}

function parseNoMatch(nm: Record<string, Json>): NoMatch {
  if ('catchAll' in nm) return { kind: 'catch-all' }
  if ('default' in nm) return { kind: 'default', value: nm.default as Json }
  throw new Error('noMatch must declare a `default` value or `catchAll: true` (board F1 — no silent null)')
}

function parseFormulaSkin(s: Record<string, Json>): FormulaSkin {
  return {
    ruleId: s.ruleId as string,
    scope: s.scope as FormulaSkin['scope'],
    scopeTarget: (s.scopeTarget as string) ?? '',
    action: s.action as FormulaSkin['action'],
    inputs: (s.inputs as Array<Record<string, Json>>).map((i) => ({ ref: i.ref as string, type: (i.type as string) ?? 'any' })),
    expression: s.expression as Json,
  }
}

function clockOf(c: CorpusCase): () => Date {
  const clockMs = c.clock ? new Date(c.clock).getTime() : new Date('2026-06-30T00:00:00Z').getTime()
  return () => new Date(clockMs)
}

/**
 * The rules the case DECLARES — the exact list handed to the compiler. Ticket 193 review: the
 * runner needs this separately from `runCase` so it can assert the compiled graph introduced no
 * rule the case never declared.
 */
function declaredRules(c: CorpusCase): RuleDefinition[] {
  return c.skin ? [compileSkin(c.skin)] : (c.definitionRules ?? []).map(parseRule)
}

function runCase(c: CorpusCase) {
  const rules = declaredRules(c)
  const compiled = compile(rules)
  const graph = new FormRuleGraph(compiled, undefined, clockOf(c))
  const result = graph.evaluateInstance(RuleInstance.fromJson(c.instance))
  return { compiled, result }
}

// Ticket 162: a case pinning a publish-time refusal — return the stable code compile raises.
function compileRefusalCode(c: CorpusCase): string {
  try {
    compile((c.definitionRules ?? []).map(parseRule))
  } catch (e) {
    if (e instanceof CompileError) return e.code
    throw e
  }
  throw new Error(`case '${c.name}' expected a compile refusal and none was raised`)
}

// Ticket 162: a case evaluating one rule through the guard tier (flat context bag; no tables).
function guardValueOutcome(c: CorpusCase): string {
  const rule = (c.definitionRules ?? []).map(parseRule).find((r) => r.id === c.guardValue)
  if (!rule) throw new Error(`case '${c.name}': guardValue names unknown rule '${c.guardValue}'`)
  return serializeComputedValue(new GuardEvaluator(undefined, clockOf(c)).evaluateValue(rule, c.instance))
}

describe('SPINE-1 conformance corpus (TS tier — byte-identical to .NET)', () => {
  for (const c of allCases()) {
    it(c.name, () => {
      if (c.expectedCompileError) {
        expect(compileRefusalCode(c)).toBe(c.expectedCompileError.code)
        return
      }
      if (c.guardValue) {
        expect(guardValueOutcome(c)).toBe(write(c.expectedGuardValue as Json))
        return
      }
      const { compiled, result } = runCase(c)

      // ADR 0146 D2 — pin the skin compiler's pre-lowering expression byte-identical to the .NET tier.
      if (c.expectedCompiledExpression) {
        for (const [ruleId, expected] of Object.entries(c.expectedCompiledExpression)) {
          const rule = compiled.rules.find((r) => r.source.id === ruleId)
          expect(rule, `expectedCompiledExpression names unknown rule '${ruleId}'`).toBeDefined()
          const produced = typeof rule!.source.expression === 'string'
            ? (JSON.parse(rule!.source.expression) as Json)
            : (rule!.source.expression as Json)
          expect(write(produced)).toBe(write(expected))
        }
      }

      // EXACT compiled set, not a lower bound (ticket 193 review): expectedCompiledExpression /
      // expectedAst name only the rules a case cares to pin, so neither notices a rule the compiler
      // INVENTED. Every compiled rule must trace back to a rule the case declared, once. (Subset, not
      // equality: the compiler deliberately drops Tier-1 JsonSchema rules — the kernel validator owns
      // those — so a declared rule legitimately need not compile.)
      const declaredIds = new Set(declaredRules(c).map((r) => r.id))
      const compiledIds = compiled.rules.map((r) => r.source.id)
      expect(compiledIds.filter((id) => !declaredIds.has(id))).toEqual([])
      expect(new Set(compiledIds).size).toBe(compiledIds.length)

      if (c.expectedAst) {
        for (const [ruleId, expected] of Object.entries(c.expectedAst)) {
          const rule = compiled.rules.find((r) => r.source.id === ruleId)
          expect(rule, `expectedAst names unknown rule '${ruleId}'`).toBeDefined()
          expect(write(rule!.ast as Json)).toBe(write(expected))
        }
      }

      // The outcome key set is asserted WHOLE before the values (ticket 193 review): declared keys
      // were a lower bound, so a regression emitting an unintended extra outcome while satisfying
      // every declared expectation kept the corpus green.
      const expectedOutcomes = c.expectedOutcomes ?? {}
      expect([...result.byRule.keys()].sort()).toEqual(Object.keys(expectedOutcomes).sort())
      for (const [ruleKey, expected] of Object.entries(expectedOutcomes)) {
        expect(serializeOutcome(result.byRule.get(ruleKey)!)).toBe(write(expected))
      }
    })
  }

  it('emits the cross-tier artifact', () => {
    const artifact: Record<string, Record<string, string>> = {}
    for (const c of allCases()) {
      if (c.expectedCompileError) {
        artifact[c.name] = { '(compile)': compileRefusalCode(c) }
        continue
      }
      if (c.guardValue) {
        artifact[c.name] = { [c.guardValue]: guardValueOutcome(c) }
        continue
      }
      const { result } = runCase(c)
      // Ticket 193 review: iterate the PRODUCED keys, not the declared ones. Emitting only
      // `expectedOutcomes` made the cross-tier artifact diff (runners/run-shared.mjs) a lower bound
      // as well — an extra outcome on BOTH tiers was invisible to it twice over.
      const perCase: Record<string, string> = {}
      for (const ruleKey of [...result.byRule.keys()].sort()) {
        perCase[ruleKey] = serializeOutcome(result.byRule.get(ruleKey)!)
      }
      artifact[c.name] = perCase
    }
    try {
      mkdirSync(artifactDir, { recursive: true })
      writeFileSync(join(artifactDir, 'ts-outcomes.json'), JSON.stringify(artifact, null, 2))
    } catch {
      // best-effort
    }
  })
})
