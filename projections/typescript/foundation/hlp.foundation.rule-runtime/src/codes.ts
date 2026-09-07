/**
 * Stable diagnostic codes — byte-identical to the .NET `RuleEngineCodes`
 * (SPINE-1 design §2.3, §4). Clients localize off these, never off prose.
 */
export const Codes = {
  // runtime
  divByZero: 'rule.div_by_zero',
  typeError: 'rule.type_error',
  upstreamError: 'rule.upstream_error',
  unknownOperator: 'rule.unknown_operator',
  badReference: 'rule.bad_reference',
  cycle: 'rule.cycle',
  budgetExceeded: 'rule.budget_exceeded',
  timeout: 'rule.timeout',
  graphTooLarge: 'rule.graph_too_large',
  tableTooLarge: 'rule.table_too_large',
  pendingAtSave: 'rule.pending_at_save',
  moneyAggUnsupported: 'rule.money_agg_unsupported',
  computeScopeInvalid: 'rule.compute_scope_invalid',
  optionsNotArray: 'rule.options_not_array',
  // compile (publish-time)
  compileCycle: 'rule.compile.cycle',
  compileDepthExceeded: 'rule.compile.depth_exceeded',
  compileTooManyRefs: 'rule.compile.too_many_refs',
  compileAstTooLarge: 'rule.compile.ast_too_large',
  compileLiteralTooLong: 'rule.compile.literal_too_long',
  compileInvalidExpression: 'rule.compile.invalid_expression',
  compileUnsupportedTier: 'rule.compile.unsupported_tier',
  compileBadGrammar: 'rule.compile.bad_grammar',
} as const
