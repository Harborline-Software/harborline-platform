/**
 * Lowers + validates a definition's rules at publish-time (SPINE-1 §2.2, §2.3, §4) —
 * TS port of `RuleCompiler`. Identical static bounds + fail-closed cycle detection to
 * the .NET tier, so the client rejects the same definitions the server does.
 */
import { Codes } from './codes.js'
import { cell } from './model.js'
import type { Json, OutputType, RuleDefinition } from './model.js'
import { DEFAULT_LIMITS, type RuleEngineLimits } from './limits.js'
import { CompileError, extractRefs, lower, measure, outputTypeFor, type LowerContext, type RuleRef } from './grammar.js'
import { declaredCoreType, deriveCoreTypes } from './core-types.js'
import { deriveGraphWork, type WorkProof } from './core-work.js'

export interface CompiledRule {
  source: RuleDefinition
  ast: Json
  outputType: OutputType
  references: RuleRef[]
  staticTarget: string | null
  rowSection: string | null
  rowField: string | null
}

export interface CompiledGraph {
  rules: readonly CompiledRule[]
  /** Compiler-owned, non-persisted finite transfer proof for the admitted program. */
  workProof: WorkProof
}

// The exported shape remains useful to consumers that inspect admitted programs, but a
// structural TypeScript type is not an evaluation admission credential. Keep the
// producer-owned rule array out-of-band so a forged object (or Proxy) is refused before
// the graph reads any caller-controlled property.
const compiledGraphBrand = new WeakSet<object>()
const compiledGraphData = new WeakMap<object, readonly CompiledRule[]>()

/** @internal Returns compiler-owned data without reflecting over an untrusted handle. */
export function ownedCompiledRulesOf(value: unknown): readonly CompiledRule[] | undefined {
  return typeof value === 'object' && value !== null && compiledGraphBrand.has(value)
    ? compiledGraphData.get(value)
    : undefined
}

const operators = new Set([
  'var', 'missing', 'missing_some',
  '==', '!=', '===', '!==', '!', '!!', 'and', 'or', 'if',
  '>', '>=', '<', '<=', '+', '-', '*', '/', '%', 'min', 'max', 'in', 'cat',
  'agg', 'money.add', 'money.sub', 'money.mul', 'date.add', 'date.diff', 'date.today', 'coding.is',
])

const actions = new Set(['Compute', 'Validate', 'Presentation', 'Options', 'Visibility', 'Required', 'ReadOnly'])

function validateOperators(node: Json, ruleId: string): void {
  // Match the evaluator boundary: arrays and multi-property objects are literal data.
  if (node === null || typeof node !== 'object' || Array.isArray(node)) return
  const entries = Object.entries(node)
  if (entries.length !== 1) return
  const [operator, argument] = entries[0]
  if (!operators.has(operator)) {
    throw new CompileError(Codes.compileInvalidExpression,
      `rule '${ruleId}': unsupported operator '${operator}'.`, ruleId)
  }
  const arguments_ = Array.isArray(argument) ? argument : [argument]
  validateArity(operator, arguments_.length, ruleId)
  for (const item of arguments_) validateOperators(item, ruleId)
}

function validateArity(operator: string, count: number, ruleId: string): void {
  const valid = (() => {
    switch (operator) {
      case 'var': return count === 1 || count === 2
      case 'missing': return count >= 0
      case 'missing_some': return count === 2
      case '==': case '!=': case '===': case '!==': case '>': case '>=': case '<': case '<=': case 'in': return count === 2
      case '!': case '!!': return count === 1
      case 'and': case 'or': case 'cat': return true
      // The decision-table lowerer uses a one-argument `if` for an otherwise-only table.
      case 'if': return true
      case '+': case '-': case '*': case '/': case '%': case 'min': case 'max':
      case 'money.add': case 'money.sub': case 'money.mul': return count >= 1
      case 'agg': case 'date.add': case 'coding.is': return count === 3
      case 'date.diff': return count === 2
      case 'date.today': return count === 0
      default: return false
    }
  })()
  if (!valid) {
    throw new CompileError(operator === 'agg' ? Codes.compileBadGrammar : Codes.compileInvalidExpression,
      `rule '${ruleId}': operator '${operator}' does not accept ${count} argument(s).`, ruleId)
  }
}

export function compile(rules: RuleDefinition[], limits: RuleEngineLimits = DEFAULT_LIMITS): CompiledGraph {
  const compiled: CompiledRule[] = []

  for (const rule of rules) {
    if (rule.tier === 'JsonSchema') continue
    if (rule.tier !== 'JsonLogic') {
      throw new CompileError(Codes.compileUnsupportedTier,
        `rule '${rule.id}': tier '${rule.tier}' is unsupported by the v1 evaluator.`, rule.id)
    }

    if (!actions.has(rule.action)) {
      throw new CompileError(Codes.compileUnknownAction, `rule '${rule.id}': unknown action '${rule.action}'.`, rule.id)
    }

    const scope = resolveScope(rule)
    const ast = lower(rule.expression, scope.ctx, rule.id)

    const nodes = measure(ast, limits, rule.id)
    if (nodes > limits.maxAstNodes) {
      throw new CompileError(Codes.compileAstTooLarge,
        `rule '${rule.id}': AST node count ${nodes} exceeds the bound ${limits.maxAstNodes}`, rule.id)
    }

    validateOperators(ast, rule.id)
    const references = extractRefs(ast, rule.id)
    if (references.length > limits.maxReferencesPerRule) {
      throw new CompileError(Codes.compileTooManyRefs,
        `rule '${rule.id}': reference count ${references.length} exceeds the bound ${limits.maxReferencesPerRule}`, rule.id)
    }

    compiled.push(freezeCompiledRule({
      source: cloneDefinition(rule),
      ast: cloneJson(ast),
      outputType: outputTypeFor(rule.action),
      references: references.map((reference) => ({ ...reference })),
      staticTarget: scope.staticTarget,
      rowSection: scope.rowSection,
      rowField: scope.rowField,
    }))
  }

  validateCoreTypes(compiled)
  detectCyclesAndDepth(compiled, limits)
  // This is admission, not a runtime fuel estimate.  It runs only after the closed
  // operator set and static DAG have been established, and remains out of the authored
  // RuleDefinition document.
  const workProof = deriveGraphWork(compiled, limits)
  const graph = Object.freeze({ rules: Object.freeze(compiled), workProof })
  compiledGraphBrand.add(graph)
  compiledGraphData.set(graph, compiled)
  return graph
}

function validateCoreTypes(rules: readonly CompiledRule[]): void {
  let fields = new Map<string, ReadonlySet<import('./core-types.js').CoreJsonType>>()
  for (const rule of rules) {
    if (rule.source.action === 'Compute' && rule.source.scope === 'Field')
      fields.set(rule.source.scopeTarget, deriveCoreTypes(rule.ast, rule.source.id).types)
  }
  for (let pass = 0; pass <= rules.length; pass++) {
    const next = new Map(fields)
    for (const rule of rules) {
      const result = deriveCoreTypes(rule.ast, rule.source.id,
        (path) => path.startsWith('field.') && fields.has(path.slice('field.'.length))
          ? fields.get(path.slice('field.'.length))! : declaredCoreType('any', rule.source.id))
      if (rule.source.action === 'Compute' && rule.source.scope === 'Field') next.set(rule.source.scopeTarget, result.types)
    }
    if (next.size === fields.size && [...next].every(([key, value]) => {
      const old = fields.get(key)
      return old !== undefined && old.size === value.size && [...old].every(type => value.has(type))
    })) return
    fields = next
  }
}

function cloneDefinition(rule: RuleDefinition): RuleDefinition {
  return {
    ...rule,
    expression: typeof rule.expression === 'string' ? rule.expression : cloneJson(rule.expression),
    errorMessage: rule.errorMessage ? JSON.parse(JSON.stringify(rule.errorMessage)) : undefined,
    presentation: rule.presentation ? JSON.parse(JSON.stringify(rule.presentation)) : undefined,
  }
}

function cloneJson(value: Json): Json {
  if (Array.isArray(value)) return value.map(cloneJson)
  if (value !== null && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, child]) => [key, cloneJson(child)]))
  return value
}

function freezeJson(value: Json): Json {
  if (Array.isArray(value)) { for (const child of value) freezeJson(child); return Object.freeze(value) as unknown as Json }
  if (value !== null && typeof value === 'object') { for (const child of Object.values(value)) freezeJson(child); return Object.freeze(value) }
  return value
}

function freezeCompiledRule(rule: CompiledRule): CompiledRule {
  freezeJson(rule.ast)
  if (typeof rule.source.expression !== 'string') freezeJson(rule.source.expression)
  if (rule.source.errorMessage) Object.freeze(rule.source.errorMessage)
  if (rule.source.presentation) Object.freeze(rule.source.presentation)
  Object.freeze(rule.source)
  Object.freeze(rule.references)
  return Object.freeze(rule)
}

interface ScopeResolution {
  ctx: LowerContext
  staticTarget: string | null
  rowSection: string | null
  rowField: string | null
}

function resolveScope(rule: RuleDefinition): ScopeResolution {
  switch (rule.scope) {
    case 'Field':
      return { ctx: { scope: rule.scope, scopeTarget: rule.scopeTarget, sectionId: null }, staticTarget: cell.field(rule.scopeTarget), rowSection: null, rowField: null }
    case 'Section':
      return { ctx: { scope: rule.scope, scopeTarget: rule.scopeTarget, sectionId: null }, staticTarget: cell.section(rule.scopeTarget), rowSection: null, rowField: null }
    case 'Schema':
      return { ctx: { scope: rule.scope, scopeTarget: '', sectionId: null }, staticTarget: cell.schema(), rowSection: null, rowField: null }
    case 'Row': {
      const parts = rule.scopeTarget.split('/')
      if (parts.length !== 2 || parts[0].length === 0 || parts[1].length === 0) {
        throw new CompileError(Codes.compileBadGrammar, `rule '${rule.id}': Row ScopeTarget '${rule.scopeTarget}' must be 'section/field'`, rule.id)
      }
      return { ctx: { scope: rule.scope, scopeTarget: rule.scopeTarget, sectionId: parts[0] }, staticTarget: null, rowSection: parts[0], rowField: parts[1] }
    }
    case 'Table': {
      const parts = rule.scopeTarget.split('/')
      if (parts.length !== 3 || parts.some((p) => p.length === 0)) {
        throw new CompileError(Codes.compileBadGrammar, `rule '${rule.id}': Table ScopeTarget '${rule.scopeTarget}' must be 'section/fn/col'`, rule.id)
      }
      return { ctx: { scope: rule.scope, scopeTarget: rule.scopeTarget, sectionId: parts[0] }, staticTarget: cell.agg(parts[0], parts[1], parts[2]), rowSection: null, rowField: null }
    }
  }
}

function abstractTarget(r: CompiledRule): string {
  switch (r.source.scope) {
    case 'Field': return 'F:' + r.source.scopeTarget
    case 'Section': return 'S:' + r.source.scopeTarget
    case 'Schema': return 'SCHEMA'
    case 'Row': return 'R:' + r.rowSection + '/' + r.rowField
    case 'Table': {
      const parts = r.source.scopeTarget.split('/')
      return 'A:' + parts[0] + '/' + parts[1] + '/' + parts[2]
    }
  }
}

function detectCyclesAndDepth(rules: CompiledRule[], limits: RuleEngineLimits): void {
  const dependsOn = new Map<string, Set<string>>()
  const ruleOfTarget = new Map<string, string>()
  const ensure = (n: string) => { if (!dependsOn.has(n)) dependsOn.set(n, new Set()) }
  const edge = (n: string, dep: string) => { ensure(n); ensure(dep); dependsOn.get(n)!.add(dep) }

  for (const r of rules) {
    const isValue = r.source.action === 'Compute'
    const target = isValue ? abstractTarget(r) : null
    if (target !== null) {
      ruleOfTarget.set(target, r.source.id)
      ensure(target)
    }
    for (const ref of r.references) {
      if (ref.kind === 'field' && target !== null) edge(target, 'F:' + ref.name)
      else if (ref.kind === 'row' && target !== null && r.rowSection !== null) edge(target, 'R:' + r.rowSection + '/' + ref.field)
      else if (ref.kind === 'agg') {
        const aggNode = 'A:' + ref.section + '/' + ref.fn + '/' + ref.col
        ensure(aggNode)
        edge(aggNode, 'R:' + ref.section + '/' + ref.col)
        if (target !== null) edge(target, aggNode)
      }
    }
  }

  // Cycle detection (DFS coloring) with path reconstruction.
  const color = new Map<string, number>() // 0 white, 1 gray, 2 black
  const stack: string[] = []
  const dfs = (node: string, descent: number): string[] | null => {
    // F9: bound recursion depth — a chain deeper than the cap is a depth violation; throw before the
    // recursion can run to full graph depth (defensive ahead of packet-carried definitions).
    if (descent > limits.maxDependencyDepth) {
      throw new CompileError(Codes.compileDepthExceeded, `dependency depth exceeds the bound ${limits.maxDependencyDepth}`)
    }
    color.set(node, 1)
    stack.push(node)
    for (const dep of dependsOn.get(node)!) {
      const c = color.get(dep) ?? 0
      if (c === 1) {
        const idx = stack.indexOf(dep)
        const cyc = stack.slice(idx)
        cyc.push(dep)
        return cyc
      }
      if (c === 0) {
        const found = dfs(dep, descent + 1)
        if (found !== null) return found
      }
    }
    color.set(node, 2)
    stack.pop()
    return null
  }
  for (const node of dependsOn.keys()) {
    if ((color.get(node) ?? 0) === 0) {
      const cyc = dfs(node, 0)
      if (cyc !== null) {
        const pathKeys = cyc.map((k) => (ruleOfTarget.has(k) ? `${k} (rule ${ruleOfTarget.get(k)})` : k))
        throw new CompileError(Codes.compileCycle, 'cyclic rule definition: ' + cyc.join(' -> '), undefined, pathKeys)
      }
    }
  }

  // Longest-path depth.
  const depthMemo = new Map<string, number>()
  const depth = (node: string, descent: number): number => {
    if (descent > limits.maxDependencyDepth) {
      throw new CompileError(Codes.compileDepthExceeded, `dependency depth exceeds the bound ${limits.maxDependencyDepth}`)
    }
    const m = depthMemo.get(node)
    if (m !== undefined) return m
    depthMemo.set(node, 0)
    let best = 0
    for (const dep of dependsOn.get(node)!) best = Math.max(best, 1 + depth(dep, descent + 1))
    depthMemo.set(node, best)
    return best
  }
  for (const node of dependsOn.keys()) {
    if (depth(node, 0) > limits.maxDependencyDepth) {
      throw new CompileError(Codes.compileDepthExceeded, `dependency depth at '${node}' exceeds the bound ${limits.maxDependencyDepth}`)
    }
  }
}
