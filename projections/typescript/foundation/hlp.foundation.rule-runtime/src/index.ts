/**
 * @harborline-software/rule-engine — SPINE-1 Tier-2 rule engine, the reactive client tier
 * (ADR 0140 D2 / ADR 0055 follow-up). The TS port of Harborline.Foundation.RuleEngine.
 *
 * The two tiers emit byte-identical {@link RuleOutcome} values; the shared conformance
 * corpus at `../foundation-rule-engine/conformance/corpus/*.json` (loaded by BOTH this
 * package's vitest suite and the .NET test project) is the load-bearing proof.
 */
export * from './model.js'
export * from './codes.js'
export * from './limits.js'
export { compile, type CompiledGraph, type CompiledRule } from './compiler.js'
export { CompileError, type RuleRef } from './grammar.js'
// ADR 0146 D2 authoring skins (compile layer): decision-table + formula representations lowering to a
// plain RuleDefinition the D1 core evaluates — one expression language, never a parallel evaluator.
export * from './skins/index.js'
export { RuleInstance, type RuleRow } from './instance.js'
export { FormRuleGraph, type RuleEvaluationResult } from './graph.js'
export { GuardEvaluator } from './guard.js'
export { serializeOutcome, write as canonicalJson } from './canonical.js'
// ADR 0146 D5 named-rule registry + version policies: resolve rules by (tenant, rule-key) under a
// latest | pinned | draft policy (draft-exclusion enforced at the resolve path — board F6), the S-8
// monotonic watermark, D7 instance pins, and the epoch-fenced compile/cache swap.
export * from './registry/index.js'
// ADR 0146 D10 interactive explainability traces: localizable code+param entries derived purely from the
// compiled rules' static field refs + the built outcomes (never a value — board F2 authority filter).
export {
  buildFormTrace,
  buildGuardTrace,
  RuleTraceCodes,
  passThroughTraceFilter,
  type RuleTraceEntry,
  type TraceAuthorityFilter,
  type TraceFieldDisclosure,
} from './trace.js'
// ADR 0146 D3 context-adapter seam: the contract a consumer pillar implements to map its own
// data shape into a resolver the interpreter reads (the operator set is sized once — own the
// seam here, scatter the concrete document/event/dataset adapters to their pillar programs).
export { ROOT_SCOPE, type ContextAdapter, type RuleEvalScope } from './context-adapter.js'
export type { RefValue, ValueResolver } from './eval-support.js'
// The non-authoritative liveness fault (D1 ratification 2026-07-01): evaluation throws this — never a
// divergent `rule.timeout` outcome — when the wall-clock ceiling or an AbortSignal trips. Consumers catch
// it as an infrastructure fault (retry / degrade), never as a rule verdict. .NET analog:
// RuleEngineTimeoutException.
export { RuleTimeout } from './eval-support.js'
