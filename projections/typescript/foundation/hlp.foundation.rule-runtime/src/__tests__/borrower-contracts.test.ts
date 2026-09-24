import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import {
  admitEnvironment, BorrowerEnvironmentCodes, canonicalDeclaration, compile, GuardEvaluator, RuleContextSnapshot,
  type BorrowerEnvironmentDeclaration, type EvaluationPhase, type Json, type RuleDefinition,
} from '../index.js'

interface BorrowerProbe {
  edge: string; declaration: BorrowerEnvironmentDeclaration; canonical: string; admittedPhase: EvaluationPhase; inapplicablePhase: EvaluationPhase
  admitted: { expression: Json; context: Record<string, Json>; expectedOk: boolean }
  refused: { expression: Json; code: string }
}
const here = dirname(fileURLToPath(import.meta.url))
const fixture = JSON.parse(readFileSync(join(here, '..', '..', '..', '..', '..', '..', 'conformance', 'hlp.foundation.rule-runtime', 'borrower-contracts.json'), 'utf8')) as {
  borrowers: BorrowerProbe[]
  probes: { boundedAgg: { refused: Json[]; refusedCode: string } }
}
const guard = new GuardEvaluator(() => new Date('2026-09-24T00:00:00Z'))
const rule = (id: string, expression: Json): RuleDefinition => ({ id, tier: 'JsonLogic', scope: 'Schema', scopeTarget: '', action: 'Validate', expression })

describe('T-590 released borrower contract fixtures (TS reactive tier)', () => {
  it('rules-eng-26, rules-ck-28: every borrow-grammar edge’s released declaration is admitted, exports canonically byte-identical to .NET, evaluates its admitted probe and refuses the one outside it', () => {
    expect(fixture.borrowers).toHaveLength(14)
    for (const b of fixture.borrowers) {
      expect(canonicalDeclaration(b.declaration), b.edge).toBe(b.canonical)
      const environment = admitEnvironment(b.declaration)
      const admission = environment.forPhase(b.admittedPhase)
      expect(guard.evaluateGuard(rule('admitted', b.admitted.expression), RuleContextSnapshot.fromJsonText(JSON.stringify(b.admitted.context)), admission).ok, b.edge).toBe(b.admitted.expectedOk)
      const refused = guard.evaluateGuard(rule('refused', b.refused.expression), RuleContextSnapshot.fromJsonText('{}'), admission)
      expect(refused.ok ? undefined : refused.error?.code, b.edge).toBe(b.refused.code)
      expect(() => environment.forPhase(b.inapplicablePhase), b.edge).toThrow(expect.objectContaining({ code: BorrowerEnvironmentCodes.phaseNotAdmitted }))
    }
  })

  it('rules-eng-16: the released bounded-agg probe refuses an unregistered fold and a dynamic collection at compile (TS tier)', () => {
    for (const expression of fixture.probes.boundedAgg.refused) {
      expect(() => compile([{ id: 'agg', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'out', action: 'Compute', expression }]))
        .toThrow(expect.objectContaining({ code: fixture.probes.boundedAgg.refusedCode }))
    }
  })
})
