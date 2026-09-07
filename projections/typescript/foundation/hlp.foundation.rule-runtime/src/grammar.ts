/**
 * The neutral compiler's grammar-lowering half (SPINE-1 §1.2) — TS port of
 * `ScopeGrammar`. Lowers the scope-grammar into a canonical normalized AST (`field.` / `row.`
 * vars + the `agg` operator, plus the WF-KEY `wf.` / `timer.` process-context
 * prefixes which pass through canonically). Performed ONCE so both tiers consume
 * the identical normalized AST (the corpus pins `expectedAst`).
 */
import { Codes } from './codes.js'
import type { Json, OutputType, RuleActionKind, RuleScope } from './model.js'
import type { RuleEngineLimits } from './limits.js'

export interface LowerContext {
  scope: RuleScope
  scopeTarget: string
  sectionId: string | null
}

export type RuleRef =
  | { kind: 'field'; name: string }
  | { kind: 'row'; field: string }
  | { kind: 'agg'; section: string; fn: string; col: string }

export class CompileError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly ruleId?: string,
    readonly cyclePath?: string[],
  ) {
    super(message)
  }
}

export function outputTypeFor(action: RuleActionKind): OutputType {
  switch (action) {
    case 'Compute': return 'Value'
    case 'Validate': return 'Validity'
    case 'Presentation': return 'Presentation'
    case 'Options': return 'Options'
    default: return 'Visibility' // Visibility | Required | ReadOnly
  }
}

function isObj(n: Json): n is { [k: string]: Json } {
  return typeof n === 'object' && n !== null && !Array.isArray(n)
}

export function lower(expression: string | Json, ctx: LowerContext, ruleId: string): Json {
  let parsed: Json
  if (typeof expression === 'string') {
    try {
      parsed = JSON.parse(expression)
    } catch (e) {
      throw new CompileError(Codes.compileInvalidExpression,
        `rule '${ruleId}': expression is not valid JSON: ${(e as Error).message}`, ruleId)
    }
  } else {
    parsed = expression
  }
  return rewrite(parsed, ctx, ruleId)
}

function rewrite(node: Json, ctx: LowerContext, ruleId: string): Json {
  if (isObj(node) && Object.keys(node).length === 1 && 'var' in node) {
    const pathNode = node['var']
    let path: string
    let def: Json | undefined
    if (Array.isArray(pathNode)) {
      path = pathNode.length > 0 ? String(pathNode[0]) : ''
      def = pathNode.length > 1 ? pathNode[1] : undefined
    } else {
      path = typeof pathNode === 'string' ? pathNode : ''
    }
    if (path.startsWith('table.') && path.includes('(')) {
      return lowerAgg(path, ctx, ruleId)
    }
    const canonical = lowerVarPath(path, ctx, ruleId)
    return def !== undefined ? { var: [canonical, def] } : { var: canonical }
  }
  if (isObj(node)) {
    const result: { [k: string]: Json } = {}
    for (const [k, v] of Object.entries(node)) result[k] = rewrite(v, ctx, ruleId)
    return result
  }
  if (Array.isArray(node)) return node.map((x) => rewrite(x, ctx, ruleId))
  return node
}

function lowerAgg(path: string, ctx: LowerContext, ruleId: string): Json {
  const open = path.indexOf('(')
  const close = path.indexOf(')')
  if (close <= open) throw bad(ruleId, `malformed table aggregate '${path}'`)
  const fn = path.slice(6, open)
  const arg = path.slice(open + 1, close)
  let section: string
  let col: string
  const dot = arg.indexOf('.')
  if (dot >= 0) {
    section = arg.slice(0, dot)
    col = arg.slice(dot + 1)
  } else {
    if (ctx.sectionId === null) {
      throw bad(ruleId, `table aggregate '${path}' needs an explicit section (table.${fn}(section.${arg})) outside a row/table-scoped rule`)
    }
    section = ctx.sectionId
    col = arg
  }
  if (fn.length === 0 || section.length === 0 || col.length === 0) throw bad(ruleId, `malformed table aggregate '${path}'`)
  return { agg: [fn, section, col] }
}

function lowerVarPath(path: string, ctx: LowerContext, ruleId: string): string {
  if (path === 'self') {
    if (ctx.scope === 'Row') return 'row.' + rowFieldOf(ctx, ruleId)
    if (ctx.scope === 'Field') return 'field.' + ctx.scopeTarget
    throw bad(ruleId, "'self' is only valid in a Field- or Row-scoped rule")
  }
  if (path.startsWith('row.')) {
    if (ctx.scope !== 'Row') throw bad(ruleId, `'row.' reference '${path}' is only valid in a Row-scoped rule`)
    return path
  }
  if (path.startsWith('parent.')) return 'field.' + path.slice('parent.'.length)
  if (path.startsWith('field.')) return path
  if (path.startsWith('section.')) {
    const rest = path.slice('section.'.length)
    const firstDot = rest.indexOf('.')
    if (firstDot < 0) throw bad(ruleId, `malformed section reference '${path}' (expected section.<id>.<field>)`)
    return 'field.' + rest.slice(firstDot + 1)
  }
  // WF-KEY (ADR 0140) process-context prefixes: wf.state / wf.actor / wf.iteration /
  // timer.<id>. A workflow guard reads the PROCESS context bag, not a record cell, so
  // these pass through CANONICALLY (no `field.` rewrite) and are NOT extracted as field
  // deps (see `extractRefs` below — only field./row./agg participate in the form graph).
  // Additive: no form rule addresses a `wf.`/`timer.`-prefixed cell, so existing lowering
  // is byte-identical. The contract that authors these is @harborline-software/contracts WorkflowDefinition.
  if (path.startsWith('wf.') || path.startsWith('timer.')) {
    return path
  }
  return 'field.' + path
}

function rowFieldOf(ctx: LowerContext, ruleId: string): string {
  const slash = ctx.scopeTarget.indexOf('/')
  if (slash < 0) throw bad(ruleId, `Row rule ScopeTarget '${ctx.scopeTarget}' must be 'section/field'`)
  return ctx.scopeTarget.slice(slash + 1)
}

function bad(ruleId: string, message: string): CompileError {
  return new CompileError(Codes.compileBadGrammar, `rule '${ruleId}': ${message}`, ruleId)
}

// ── reference extraction ────────────────────────────────────────────────────

export function extractRefs(ast: Json, ruleId = ''): RuleRef[] {
  const refs: RuleRef[] = []
  walk(ast, refs, ruleId)
  return refs
}

function walk(node: Json, refs: RuleRef[], ruleId: string): void {
  if (isObj(node) && Object.keys(node).length === 1 && 'var' in node) {
    const path = varPathOf(node['var'])
    if (path.startsWith('field.')) refs.push({ kind: 'field', name: path.slice('field.'.length) })
    else if (path.startsWith('row.')) refs.push({ kind: 'row', field: path.slice('row.'.length) })
    if (Array.isArray(node['var']) && node['var'].length > 1) walk(node['var'][1], refs, ruleId)
    return
  }
  if (isObj(node) && Object.keys(node).length === 1 && 'agg' in node) {
    const a = node['agg']
    // Ticket 162 review (150-family direction): an agg node the graph cannot statically
    // register — wrong arity, or expression-valued/non-string args — used to be SKIPPED here,
    // leaving no fold cell. It then refused per-keystroke at runtime with no authoring-time
    // signal (and a dynamic arg registered a phantom String()-coerced cell). The compiler
    // refuses it instead: publish rejects it with a stable code; nothing un-saveable ships.
    if (!Array.isArray(a) || a.length !== 3 || a.some((part) => typeof part !== 'string')) {
      throw new CompileError(Codes.compileBadGrammar,
        `rule '${ruleId}': a table aggregate must be a static [fn, section, col] string triple`, ruleId)
    }
    refs.push({ kind: 'agg', fn: a[0] as string, section: a[1] as string, col: a[2] as string })
    return
  }
  if (isObj(node)) {
    for (const v of Object.values(node)) walk(v, refs, ruleId)
  } else if (Array.isArray(node)) {
    for (const item of node) walk(item, refs, ruleId)
  }
}

function varPathOf(varNode: Json): string {
  if (Array.isArray(varNode)) return varNode.length > 0 ? String(varNode[0]) : ''
  return typeof varNode === 'string' ? varNode : ''
}

// ── AST metrics (static bounds) ─────────────────────────────────────────────

export function measure(node: Json, limits: RuleEngineLimits, ruleId: string): number {
  let count = 1
  if (isObj(node)) {
    for (const v of Object.values(node)) count += measure(v, limits, ruleId)
  } else if (Array.isArray(node)) {
    for (const item of node) count += measure(item, limits, ruleId)
  } else if (typeof node === 'string' && node.length > limits.maxLiteralLength) {
    throw new CompileError(Codes.compileLiteralTooLong,
      `rule '${ruleId}': a string literal of length ${node.length} exceeds the bound ${limits.maxLiteralLength}`, ruleId)
  }
  return count
}
