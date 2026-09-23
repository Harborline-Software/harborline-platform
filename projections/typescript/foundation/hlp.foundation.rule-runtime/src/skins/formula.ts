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
import { compile } from '../compiler.js'
import { declaredCoreType, deriveCoreTypes } from '../core-types.js'
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
  // The returned RuleDefinition has no persisted runtime input guard.  Validate declared
  // spellings but deliberately broaden evaluation proof to AnyJson rather than certifying a
  // type which a normal graph invocation can contradict.
  for (const input of skin.inputs) declaredCoreType(input.type, id, SkinCodes.formulaTypeMismatch)
  for (const referenced of collectVarPaths(skin.expression)) {
    if (!declared.has(referenced)) {
      throw reject(SkinCodes.formulaUndeclaredRef, id,
        `expression references '${referenced}', which is not a declared input (declared: ${[...declared].sort().join(', ')})`)
    }
  }
  deriveCoreTypes(skin.expression, id, undefined, SkinCodes.formulaTypeMismatch)

  const definition: RuleDefinition = {
    id,
    tier: 'JsonLogic',
    scope: skin.scope,
    scopeTarget: skin.scopeTarget,
    // RuleDefinition treats a string as encoded JSON source; preserve a scalar formula
    // as its JSON literal so compile([compileFormula(skin)]) evaluates it, not a raw parser token.
    expression: typeof skin.expression === 'string' ? JSON.stringify(skin.expression) : skin.expression,
    action: skin.action,
  }
  // The authoring bridge returns only rules admitted by the identical core fence that
  // publishes bare rules, not merely a syntactically lowered skin.
  // `RuleDefinition.expression` accepts either encoded JSON source or an AST. A scalar formula
  // string is AST data, whereas the core compiler interprets a bare string as encoded source.
  // Encode only this admission copy; retain the canonical authored AST on the returned definition.
  compile([definition])
  return definition
}

function collectVarPaths(node: Json): string[] {
  const seen: string[] = []
  const set = new Set<string>()
  walk(node, seen, set)
  return seen
}

function walk(node: Json, seen: string[], set: Set<string>): void {
  if (isObj(node) && Object.keys(node).length === 1 && ('missing' in node || 'missing_some' in node)) {
    const raw = Object.values(node)[0]
    const args = Array.isArray(raw) ? raw : [raw]
    if ('missing_some' in node && args.length > 0) walk(args[0], seen, set)
    for (const source of ('missing_some' in node ? args.slice(1) : args)) collectMissingPaths(source, seen, set)
    return
  }
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
  if (isObj(node) && Object.keys(node).length === 1) {
    const argument = Object.values(node)[0]
    if (Array.isArray(argument)) for (const item of argument) walk(item, seen, set)
    else walk(argument, seen, set)
  }
}

function collectMissingPaths(node: Json, seen: string[], set: Set<string>): void {
  if (Array.isArray(node)) { for (const item of node) collectMissingPaths(item, seen, set); return }
  if (typeof node === 'string' && node.length > 0) { if (!set.has(node)) { set.add(node); seen.push(node) }; return }
  if (isObj(node) && Object.keys(node).length === 1) walk(node, seen, set)
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
