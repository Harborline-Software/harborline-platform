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
  stepBudget: 250000,
  wallClockMs: 250,
}
