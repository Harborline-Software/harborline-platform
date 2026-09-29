/**
 * Fail-closed resource bounds — byte-identical defaults to the .NET
 * `RuleEngineLimits` (SPINE-1 design §4). Both tiers enforce the identical
 * STATIC caps (so the client rejects the same definitions the server does).
 */
export interface RuleEngineLimits {
  maxGraphNodes: number
  maxTableRowsPerAggregate: number
  maxDependencyDepth: number
  maxReferencesPerRule: number
  maxAstNodes: number
  maxLiteralLength: number
  /**
   * Max static `workProof.maximumEvaluationWork` (structural proof units, not steps or milliseconds).
   * Publish-time (static): compile and graph construction refuse a larger proof with
   * `rule.compile.work_exceeded`; a proof equal to it is admitted (T-818).
   */
  maxStaticWork: bigint
  stepBudget: number
  /** Per-evaluation wall-clock ceiling, milliseconds (advisory UX on the client tier). */
  wallClockMs: number
}

export const DEFAULT_LIMITS: RuleEngineLimits = {
  maxGraphNodes: 5000,
  maxTableRowsPerAggregate: 2000,
  maxDependencyDepth: 64,
  maxReferencesPerRule: 64,
  maxAstNodes: 256,
  maxLiteralLength: 4096,
  // T-818: 10^(d+1) for the 25-digit largest proof in the supported calibration set.
  maxStaticWork: 10n ** 26n,
  stepBudget: 250000,
  wallClockMs: 250,
}
