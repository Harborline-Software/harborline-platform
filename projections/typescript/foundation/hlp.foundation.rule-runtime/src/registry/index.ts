/**
 * The named-rule registry + version policies (ADR 0146 D5 / Wave 1) — the reactive tier's mirror of
 * the .NET `Harborline.Foundation.RuleEngine.Registry`. Resolve rules by `(tenant, rule-key)` under a
 * version policy, with the board-F6 draft-exclusion mechanism, the S-8 monotonic watermark, D7
 * instance pins, and the epoch-fenced compile/cache swap.
 */
export { RuleVersion } from './version.js'
export {
  RuleVersionPolicy,
  RuleRegistry,
  type RuleVersionPolicyKind,
  type RuleResolveScope,
  type RuleResolutionStatus,
  type RuleResolution,
  type RulePin,
  type PublishedRuleVersion,
} from './registry.js'
export { RuleCompileCache, StaleRulePublishError } from './compile-cache.js'
