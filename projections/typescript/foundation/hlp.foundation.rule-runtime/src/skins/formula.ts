/**
 * ADR 0146 D2 — the FORMULA authoring skin (compile layer), TS mirror of
 * `Harborline.Foundation.RuleEngine.Skins.FormulaSkin`.
 *
 * A formula is a named, typed-input expression compiling to the SAME AST as a
 * hand-authored rule (binding constraint #3). The compiler enforces that every `var`
 * the expression reads is a DECLARED input — an undeclared reference is a publish
 * rejection, not a silent free variable. v1 type enforcement is name/presence; the
 * declared `type` is authoring metadata + the seam a future static type checker keys off.
 *
 * Byte-identical to the .NET compiler — proven by the shared conformance corpus.
 */
import { CompileError } from '../grammar.js'
import type { Json, RuleActionKind, RuleDefinition, RuleScope } from '../model.js'
import { SkinCodes } from './codes.js'

/** One declared input — a var ref + its declared type (`number`/`string`/`boolean`/`any`). */
export interface FormulaInput {
  ref: string
  type?: string
}

/** The formula authoring skin. Compile with {@link compileFormula}. */
export interface FormulaSkin {
  ruleId: string
  scope: RuleScope
  scopeTarget: string
  action: RuleActionKind
  inputs: FormulaInput[]
  expression: Json
}

/** Lowers a formula skin to a `RuleDefinition`, enforcing every referenced var is a declared input. */
export function compileFormula(skin: FormulaSkin): RuleDefinition {
  const id = skin.ruleId

  if (skin.expression === undefined || skin.expression === null) {
    throw reject(SkinCodes.formulaEmpty, id, 'a formula must declare a non-empty expression')
  }

  const declared = new Set(skin.inputs.map((i) => i.ref))
  for (const referenced of collectVarPaths(skin.expression)) {
    if (!declared.has(referenced)) {
      throw reject(SkinCodes.formulaUndeclaredRef, id,
        `expression references '${referenced}', which is not a declared input (declared: ${[...declared].sort().join(', ')})`)
    }
  }

  return {
    id,
    tier: 'JsonLogic',
    scope: skin.scope,
    scopeTarget: skin.scopeTarget,
    expression: skin.expression,
    action: skin.action,
  }
}

function collectVarPaths(node: Json): string[] {
  const seen: string[] = []
  const set = new Set<string>()
  walk(node, seen, set)
  return seen
}

function walk(node: Json, seen: string[], set: Set<string>): void {
  if (isObj(node) && Object.keys(node).length === 1 && 'var' in node) {
    const path = varPathOf(node['var'])
    if (path.length > 0 && !set.has(path)) {
      set.add(path)
      seen.push(path)
    }
    const v = node['var']
    if (Array.isArray(v) && v.length > 1) walk(v[1], seen, set)
    return
  }
  if (isObj(node)) {
    for (const v of Object.values(node)) walk(v, seen, set)
  } else if (Array.isArray(node)) {
    for (const item of node) walk(item, seen, set)
  }
}

function isObj(n: Json): n is { [k: string]: Json } {
  return typeof n === 'object' && n !== null && !Array.isArray(n)
}

function varPathOf(varNode: Json): string {
  if (Array.isArray(varNode)) return varNode.length > 0 ? String(varNode[0]) : ''
  return typeof varNode === 'string' ? varNode : ''
}

function reject(code: string, ruleId: string, message: string): CompileError {
  return new CompileError(code, `formula skin '${ruleId}': ${message}`, ruleId)
}
