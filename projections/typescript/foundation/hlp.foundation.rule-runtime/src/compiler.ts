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
  rules: CompiledRule[]
}

export function compile(rules: RuleDefinition[], limits: RuleEngineLimits = DEFAULT_LIMITS): CompiledGraph {
  const compiled: CompiledRule[] = []

  for (const rule of rules) {
    if (rule.tier === 'JsonSchema') continue
    if (rule.tier === 'PowerFx') {
      throw new CompileError(Codes.compileUnsupportedTier,
        `rule '${rule.id}': Power Fx (Tier-3) is demoted in v1 — not evaluated (ADR 0140; SPINE-1).`, rule.id)
    }

    const scope = resolveScope(rule)
    const ast = lower(rule.expression, scope.ctx, rule.id)

    const nodes = measure(ast, limits, rule.id)
    if (nodes > limits.maxAstNodes) {
      throw new CompileError(Codes.compileAstTooLarge,
        `rule '${rule.id}': AST node count ${nodes} exceeds the bound ${limits.maxAstNodes}`, rule.id)
    }

    const references = extractRefs(ast, rule.id)
    if (references.length > limits.maxReferencesPerRule) {
      throw new CompileError(Codes.compileTooManyRefs,
        `rule '${rule.id}': reference count ${references.length} exceeds the bound ${limits.maxReferencesPerRule}`, rule.id)
    }

    compiled.push({
      source: rule,
      ast,
      outputType: outputTypeFor(rule.action),
      references,
      staticTarget: scope.staticTarget,
      rowSection: scope.rowSection,
      rowField: scope.rowField,
    })
  }

  detectCyclesAndDepth(compiled, limits)
  return { rules: compiled }
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
