/**
 * ADR 0146 D2 — authoring skins (compile layer). Each skin lowers to a plain
 * `RuleDefinition` the ratified D1 core evaluates; skins are never a parallel evaluator
 * (binding constraint #3, "one expression language"). Byte-identical to the .NET
 * `Harborline.Foundation.RuleEngine.Skins` compilers (shared conformance corpus).
 */
export { SkinCodes } from './codes.js'
export {
  compileDecisionTable,
  type DecisionTableSkin,
  type DecisionRow,
  type DecisionCell,
  type HitPolicy,
  type NoMatch,
} from './decision-table.js'
export { compileFormula, type FormulaSkin, type FormulaInput } from './formula.js'
