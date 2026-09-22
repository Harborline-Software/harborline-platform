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
  const types = new Map(skin.inputs.map((i) => [i.ref, parseType(i.type, id)]))
  for (const referenced of collectVarPaths(skin.expression)) {
    if (!declared.has(referenced)) {
      throw reject(SkinCodes.formulaUndeclaredRef, id,
        `expression references '${referenced}', which is not a declared input (declared: ${[...declared].sort().join(', ')})`)
    }
  }
  infer(skin.expression, types, id)

  const definition: RuleDefinition = {
    id,
    tier: 'JsonLogic',
    scope: skin.scope,
    scopeTarget: skin.scopeTarget,
    expression: skin.expression,
    action: skin.action,
  }
  // The authoring bridge returns only rules admitted by the identical core fence that
  // publishes bare rules, not merely a syntactically lowered skin.
  // `RuleDefinition.expression` accepts either encoded JSON source or an AST. A scalar formula
  // string is AST data, whereas the core compiler interprets a bare string as encoded source.
  // Encode only this admission copy; retain the canonical authored AST on the returned definition.
  compile([{ ...definition, expression: JSON.stringify(definition.expression) }])
  return definition
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

type FormulaType = 'any' | 'null' | 'boolean' | 'number' | 'string' | 'array' | 'object' | 'coding' | 'money' | 'date'

function parseType(type: string | undefined, ruleId: string): FormulaType {
  const normalized = (type ?? 'any').toLowerCase()
  if (normalized === 'text') return 'string'
  if (['any', 'null', 'boolean', 'number', 'string', 'array', 'object', 'coding', 'money', 'date'].includes(normalized)) return normalized as FormulaType
  throw reject(SkinCodes.formulaTypeMismatch, ruleId, `input declares unknown type '${type}'`)
}

function infer(node: Json, declared: Map<string, FormulaType>, ruleId: string): FormulaType {
  if (node === null) return 'null'
  if (Array.isArray(node)) return 'array'
  if (typeof node === 'boolean') return 'boolean'
  if (typeof node === 'number') return 'number'
  if (typeof node === 'string') return 'string'
  if (!isObj(node) || Object.keys(node).length !== 1) return 'object'
  const [op, argument] = Object.entries(node)[0]
  const args = Array.isArray(argument) ? argument : [argument]
  const arg = (index: number): FormulaType => index < args.length ? infer(args[index], declared, ruleId) : 'null'
  const require = (index: number, ...accepted: FormulaType[]): void => {
    const actual = arg(index)
    if (actual !== 'any' && !accepted.includes(actual)) {
      throw reject(SkinCodes.formulaTypeMismatch, ruleId,
        `operator '${op}' argument ${index + 1} requires ${accepted.join('/')} but declared type is ${actual}`)
    }
  }
  switch (op) {
    case 'var': return declared.get(varPathOf(argument)) ?? 'any'
    case 'money.add': case 'money.sub': case 'money.mul':
      args.forEach((_, i) => require(i, 'string', 'number', 'money')); return 'money'
    case 'date.add':
      require(0, 'string', 'date'); require(1, 'number', 'string', 'boolean'); require(2, 'string'); return 'date'
    case 'date.diff': require(0, 'string', 'date'); require(1, 'string', 'date'); return 'number'
    case 'date.today': return 'date'
    case 'coding.is': require(0, 'coding', 'array', 'object'); require(1, 'string'); require(2, 'string'); return 'boolean'
    case '!': case '!!': case '==': case '!=': case '===': case '!==': case '>': case '>=': case '<': case '<=': case 'in': return 'boolean'
    case '+': case '-': case '*': case '/': case '%': case 'min': case 'max':
      args.forEach((_, i) => require(i, 'number', 'string', 'boolean')); return 'number'
    case 'cat': return 'string'
    case 'missing': case 'missing_some': return 'array'
    default: return 'any'
  }
}

function reject(code: string, ruleId: string, message: string): CompileError {
  return new CompileError(code, `formula skin '${ruleId}': ${message}`, ruleId)
}
