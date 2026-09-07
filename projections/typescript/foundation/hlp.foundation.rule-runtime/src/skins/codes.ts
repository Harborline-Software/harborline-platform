/**
 * Stable skin rejection codes — byte-identical to the .NET `SkinCodes`
 * (ADR 0146 D2). Clients localize off these, never off prose.
 */
export const SkinCodes = {
  // decision-table
  decisionTableNoInputs: 'rule.skin.decision_table_no_inputs',
  decisionTableEmpty: 'rule.skin.decision_table_empty',
  decisionTableBadRow: 'rule.skin.decision_table_bad_row',
  decisionTableBadCell: 'rule.skin.decision_table_bad_cell',
  decisionTableInvalidHitPolicy: 'rule.skin.decision_table_invalid_hit_policy',
  noMatchUnresolved: 'rule.skin.no_match_unresolved',
  // formula
  formulaEmpty: 'rule.skin.formula_empty',
  formulaUndeclaredRef: 'rule.skin.formula_undeclared_ref',
} as const
