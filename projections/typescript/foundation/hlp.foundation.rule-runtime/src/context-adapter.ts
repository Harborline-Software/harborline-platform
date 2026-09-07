/**
 * The context-adapter seam (ADR 0146 D3, board F3) — the TS mirror of the .NET
 * `IContextAdapter`. The contract a consumer pillar implements to map its own data shape
 * into the rule engine's scope grammar: given an evaluation scope, produce the
 * {@link ValueResolver} the interpreter resolves a compiled rule's `var`/`agg` references
 * against.
 *
 * The operator set + interpreter are sized ONCE (D3); each pillar adapts its data HERE
 * rather than growing per-pillar operators. This interface — the seam — is owned by ADR 0146
 * Wave 1; the forms + workflow-guard contexts are its first (migrated) implementations. The
 * three concrete new adapters (document-merge, event-payload, dataset-row) land with their
 * pillar programs (#111/#112/#113) implementing this same contract — own the seam, scatter
 * the implementations.
 */
import type { ValueResolver } from './eval-support.js'

/**
 * The evaluation scope a resolver is produced for. Carries the optional child-collection row
 * (`rowSection` + `rowId`) that a rule's `row.` references resolve against; a pillar with no
 * repeating sub-collection evaluates at {@link ROOT_SCOPE} (a `row.` reference then resolves
 * to a bad-reference, as the flat workflow guard bag does).
 */
export interface RuleEvalScope {
  readonly rowSection: string | null
  readonly rowId: string | null
}

/** The top-level (non-row) scope. */
export const ROOT_SCOPE: RuleEvalScope = { rowSection: null, rowId: null }

/** Maps a pillar's data shape into a {@link ValueResolver} for a given scope (ADR 0146 D3). */
export interface ContextAdapter {
  createResolver(scope: RuleEvalScope): ValueResolver
}
