/**
 * The closed `harborline-jsonlogic/v1` operator interpreter (SPINE-1 §1.4) — TS port
 * of `HarborlineJsonLogic`. A self-contained bounded evaluator (NOT a third-party
 * JsonLogic library) so operator semantics, the step budget, and AST node accounting
 * are byte-identical to the .NET tier. v1 set + exclusions: see the .NET docstring /
 * the README operator table.
 */
import { Codes } from './codes.js'
import { MoneyDecimal } from './money-decimal.js'
import { dateAdd, dateDiffDays, today } from './date-math.js'
import {
  EvalContext,
  RefValue,
  RuleBudget,
  RuleEvalError,
  RulePending,
  RuleTimeout,
  err,
} from './eval-support.js'
import type { Json } from './model.js'

function isOp(node: Json): node is { [k: string]: Json } {
  return typeof node === 'object' && node !== null && !Array.isArray(node) && Object.keys(node).length === 1
}

function argList(argNode: Json): Json[] {
  return Array.isArray(argNode) ? argNode : [argNode]
}

type Impl = (args: Json[], ctx: EvalContext, ev: (i: number) => Json) => Json
interface BuiltInImpl { readonly category: string; readonly minArity: number; readonly maxArity: number; readonly authorable: boolean; readonly invoke: Impl }
const def = (category: string, minArity: number, maxArity: number, invoke: Impl, authorable = true): BuiltInImpl =>
  ({ category, minArity, maxArity, authorable, invoke })
const all = (args: Json[], ev: (i: number) => Json): Json[] => args.map((_, i) => ev(i))

/**
 * The R1 built-in register (DES-0018 rules-eng-27), TS tier: the evaluator's only dispatch table,
 * the compiler's operator and arity source, and the origin of both editor palettes. Every entry
 * carries its implementation, so nothing is registered that cannot execute. Mirrors the .NET
 * `BuiltInFunctionRegister`; the operator-catalog guards pin both tiers to one set.
 */
export const builtInRegister = {
  var: def('data', 1, 2, (a, c) => evalVar(a, c), false),
  missing: def('data', 0, -1, (a, c) => evalMissing(a, c)),
  missing_some: def('data', 2, 2, (a, c) => evalMissingSome(a, c)),

  '==': def('logic', 2, 2, (_a, _c, ev) => looseEquals(ev(0), ev(1))),
  '!=': def('logic', 2, 2, (_a, _c, ev) => !looseEquals(ev(0), ev(1))),
  '===': def('logic', 2, 2, (_a, _c, ev) => strictEquals(ev(0), ev(1))),
  '!==': def('logic', 2, 2, (_a, _c, ev) => !strictEquals(ev(0), ev(1))),
  '!': def('logic', 1, 1, (_a, _c, ev) => !isTruthy(ev(0))),
  '!!': def('logic', 1, 1, (_a, _c, ev) => isTruthy(ev(0))),
  and: def('logic', 0, -1, (a, c) => evalAnd(a, c)),
  or: def('logic', 0, -1, (a, c) => evalOr(a, c)),
  // A one-argument `if` is the decision-table lowerer's otherwise-only shape.
  if: def('logic', 0, -1, (a, c) => evalIf(a, c)),

  '>': def('compare', 2, 2, (_a, _c, ev) => compare(ev(0), ev(1)) > 0),
  '>=': def('compare', 2, 2, (_a, _c, ev) => compare(ev(0), ev(1)) >= 0),
  '<': def('compare', 2, 2, (_a, _c, ev) => compare(ev(0), ev(1)) < 0),
  '<=': def('compare', 2, 2, (_a, _c, ev) => compare(ev(0), ev(1)) <= 0),

  '+': def('arithmetic', 1, -1, (a, _c, ev) => arith(all(a, ev), '+')),
  '-': def('arithmetic', 1, -1, (a, _c, ev) => arith(all(a, ev), '-')),
  '*': def('arithmetic', 1, -1, (a, _c, ev) => arith(all(a, ev), '*')),
  '/': def('arithmetic', 1, -1, (a, _c, ev) => arith(all(a, ev), '/')),
  '%': def('arithmetic', 1, -1, (a, _c, ev) => arith(all(a, ev), '%')),
  min: def('arithmetic', 1, -1, (a, _c, ev) => minMax(all(a, ev), true)),
  max: def('arithmetic', 1, -1, (a, _c, ev) => minMax(all(a, ev), false)),

  in: def('membership', 2, 2, (_a, _c, ev) => evalIn(ev(0), ev(1))),
  cat: def('text', 0, -1, (a, c) => evalCat(a, c)),

  agg: def('fold', 3, 3, (_a, c, ev) => evalAgg(ev(0), ev(1), ev(2), c)),
  'money.add': def('money', 1, -1, (a, c, ev) => money(all(a, ev), '+', c)),
  'money.sub': def('money', 1, -1, (a, c, ev) => money(all(a, ev), '-', c)),
  'money.mul': def('money', 1, -1, (a, c, ev) => money(all(a, ev), '*', c)),
  'date.add': def('date', 3, 3, (_a, _c, ev) => dateAddOp(ev(0), ev(1), ev(2))),
  'date.diff': def('date', 2, 2, (_a, _c, ev) => dateDiffOp(ev(0), ev(1))),
  'date.today': def('date', 0, 0, (_a, c) => today(c.now)),
  'coding.is': def('coding', 3, 3, (_a, _c, ev) => evalCodingIs(ev(0), asString(ev(1)) ?? '', asString(ev(2)) ?? '')),
} as const satisfies Record<string, BuiltInImpl>

/** The folds `agg` accepts over a bounded child collection (rules-bound-4, rules-eng-16); mirrors the .NET register. */
export const aggregateFolds = ['sum', 'count', 'avg', 'min', 'max', 'any', 'all'] as const

/** A key of the R1 built-in register. */
export type BuiltInKey = keyof typeof builtInRegister

/** The one registered implementation for `key`, or undefined when it is not a built-in. */
export function registeredBuiltIn(key: string): BuiltInImpl | undefined {
  return Object.hasOwn(builtInRegister, key) ? builtInRegister[key as BuiltInKey] : undefined
}

/** True when the register admits `count` arguments for `key`. */
export function admitsArity(key: string, count: number): boolean {
  const f = registeredBuiltIn(key)
  return f !== undefined && count >= f.minArity && (f.maxArity < 0 || count <= f.maxArity)
}

export function evaluate(node: Json, ctx: EvalContext): Json {
  ctx.budget.charge()

  if (!isOp(node)) return node

  const op = Object.keys(node)[0]
  const args = argList(node[op])
  const ev = (i: number): Json => (i < args.length ? evaluate(args[i], ctx) : null)

  // The register is the only dispatch table (rules-eng-27); an unregistered key refuses.
  const f = registeredBuiltIn(op)
  if (f === undefined) throw new RuleEvalError(err(Codes.unknownOperator, 'op', op))
  return f.invoke(args, ctx, ev)
}

// ── var / missing ───────────────────────────────────────────────────────────

function evalVar(args: Json[], ctx: EvalContext): Json {
  const path = asString(args.length > 0 ? evaluate(args[0], ctx) : null) ?? ''
  if (path.length === 0) return null
  const fallback = args.length > 1 ? evaluate(args[1], ctx) : null
  return unwrap(ctx.resolver.resolveVar(path), fallback)
}

function unwrap(rv: RefValue, fallback: Json): Json {
  if (rv.state === 'Pending') throw new RulePending()
  if (rv.state === 'Error') throw new RuleEvalError(rv.error ?? err(Codes.upstreamError))
  return rv.value === undefined || rv.value === null ? (fallback ?? rv.value ?? null) : rv.value
}

function resolveKey(k: string, ctx: EvalContext): RefValue {
  return ctx.resolver.resolveVar(k)
}

function evalMissing(args: Json[], ctx: EvalContext): Json {
  const first = args.length === 1 ? evaluate(args[0], ctx) : null
  const keys = args.length === 1 && Array.isArray(first)
    ? first.map((x) => asString(x) ?? '')
    : args.map((a) => asString(evaluate(a, ctx)) ?? '')
  const missing: Json[] = []
  for (const k of keys) {
    if (k.length === 0) continue
    const rv = resolveKey(k, ctx)
    if (rv.state === 'Pending') throw new RulePending()
    if (rv.state === 'Error') throw new RuleEvalError(rv.error ?? err(Codes.upstreamError))
    if (rv.value === undefined || rv.value === null) missing.push(k)
  }
  return missing
}

function evalMissingSome(args: Json[], ctx: EvalContext): Json {
  const min = Math.trunc(toNumber(evaluate(args[0], ctx)))
  const keysNode = evaluate(args[1], ctx)
  const keys = Array.isArray(keysNode) ? keysNode.map((x) => asString(x) ?? '') : []
  const missing: Json[] = []
  let present = 0
  for (const k of keys) {
    const rv = resolveKey(k, ctx)
    if (rv.state === 'Pending') throw new RulePending()
    if (rv.state === 'Error') throw new RuleEvalError(rv.error ?? err(Codes.upstreamError))
    if (rv.value === undefined || rv.value === null) missing.push(k)
    else present++
  }
  return present >= min ? [] : missing
}

// ── logic ─────────────────────────────────────────────────────────────────────

function evalAnd(args: Json[], ctx: EvalContext): Json {
  let last: Json = true
  for (const a of args) {
    last = evaluate(a, ctx)
    if (!isTruthy(last)) return last
  }
  return last
}

function evalOr(args: Json[], ctx: EvalContext): Json {
  let last: Json = false
  for (const a of args) {
    last = evaluate(a, ctx)
    if (isTruthy(last)) return last
  }
  return last
}

function evalIf(args: Json[], ctx: EvalContext): Json {
  let i = 0
  for (; i + 1 < args.length; i += 2) {
    if (isTruthy(evaluate(args[i], ctx))) return evaluate(args[i + 1], ctx)
  }
  return i < args.length ? evaluate(args[i], ctx) : null
}

// ── compare / arithmetic ────────────────────────────────────────────────────

function compare(a: Json, b: Json): number {
  const x = toNumber(a)
  const y = toNumber(b)
  return x < y ? -1 : x > y ? 1 : 0
}

function arith(values: Json[], op: string): Json {
  if (op === '-' && values.length === 1) return numResult(-toNumber(values[0]))
  if (op === '+' && values.length === 1) return numResult(toNumber(values[0]))
  if (values.length === 0) throw new RuleEvalError(err(Codes.typeError, 'op', op))

  let acc = toNumber(values[0])
  for (let i = 1; i < values.length; i++) {
    const v = toNumber(values[i])
    switch (op) {
      case '+': acc += v; break
      case '-': acc -= v; break
      case '*': acc *= v; break
      case '/':
        if (v === 0) throw new RuleEvalError(err(Codes.divByZero))
        acc /= v
        break
      case '%':
        if (v === 0) throw new RuleEvalError(err(Codes.divByZero))
        acc %= v
        break
    }
  }
  return numResult(acc)
}

function minMax(values: Json[], min: boolean): Json {
  if (values.length === 0) throw new RuleEvalError(err(Codes.typeError, 'op', min ? 'min' : 'max'))
  let best = toNumber(values[0])
  for (let i = 1; i < values.length; i++) {
    const v = toNumber(values[i])
    best = min ? Math.min(best, v) : Math.max(best, v)
  }
  return numResult(best)
}

// ── membership / string ─────────────────────────────────────────────────────

function evalIn(needle: Json, hay: Json): Json {
  if (Array.isArray(hay)) return hay.some((x) => looseEquals(x, needle))
  if (typeof hay === 'string') {
    const n = asString(needle)
    return n !== null && hay.includes(n)
  }
  return false
}

// ── earlier source extensions ─────────────────────────────────────────────────────

function evalAgg(fn: Json, section: Json, col: Json, ctx: EvalContext): Json {
  const f = asString(fn)
  const s = asString(section)
  const c = asString(col)
  if (f === null || s === null || c === null) throw new RuleEvalError(err(Codes.badReference, 'op', 'agg'))
  return unwrap(ctx.resolver.resolveAgg(f, s, c), null)
}

function evalCat(args: Json[], ctx: EvalContext): Json {
  let out = ''
  for (const a of args) {
    const piece = asString(evaluate(a, ctx)) ?? ''
    ctx.budget.chargeSize(piece.length) // charge concat work proportional to size (finding F4)
    out += piece
  }
  return out
}

function money(values: Json[], op: string, ctx: EvalContext): Json {
  try {
    if (values.length === 0) throw new RuleEvalError(err(Codes.typeError, 'op', 'money'))
    let acc = toMoney(values[0])
    ctx.budget.chargeSize(acc.size) // charge money work proportional to operand size (finding F4)
    for (let i = 1; i < values.length; i++) {
      const m = toMoney(values[i])
      ctx.budget.chargeSize(m.size)
      acc = op === '+' ? acc.add(m) : op === '-' ? acc.sub(m) : acc.mul(m)
      ctx.budget.chargeSize(acc.size)
    }
    return acc.toCanonicalString()
  } catch (e) {
    // budget / timeout fail-closed must propagate (NOT become a type error).
    if (e instanceof RuleEvalError || e instanceof RuleBudget || e instanceof RuleTimeout) throw e
    throw new RuleEvalError(err(Codes.typeError, 'op', 'money'))
  }
}

function toMoney(n: Json): MoneyDecimal {
  if (typeof n === 'string') return MoneyDecimal.parse(n)
  if (typeof n === 'number' && Number.isInteger(n)) return MoneyDecimal.fromInteger(n)
  throw new Error('money operand must be a decimal string or an integer')
}

function dateAddOp(date: Json, n: Json, unit: Json): Json {
  try {
    const d = asString(date)
    if (d === null) throw new Error('date')
    return dateAdd(d, Math.trunc(toNumber(n)), asString(unit) ?? 'day')
  } catch (e) {
    if (e instanceof RuleEvalError) throw e
    throw new RuleEvalError(err(Codes.typeError, 'op', 'date.add'))
  }
}

function dateDiffOp(a: Json, b: Json): Json {
  try {
    const x = asString(a)
    const y = asString(b)
    if (x === null || y === null) throw new Error('date')
    return dateDiffDays(x, y)
  } catch (e) {
    if (e instanceof RuleEvalError) throw e
    throw new RuleEvalError(err(Codes.typeError, 'op', 'date.diff'))
  }
}

function evalCodingIs(value: Json, system: string, code: string): Json {
  const match = (c: Json): boolean =>
    typeof c === 'object' && c !== null && !Array.isArray(c) &&
    asString((c as Record<string, Json>).system) === system &&
    asString((c as Record<string, Json>).code) === code
  if (Array.isArray(value)) return value.some(match)
  return match(value)
}

// ── coercion helpers (byte-identical to the .NET HarborlineJsonLogic) ──────────

export function isTruthy(n: Json): boolean {
  if (n === null || n === undefined) return false
  if (Array.isArray(n)) return n.length > 0
  if (typeof n === 'object') return Object.keys(n).length > 0
  if (typeof n === 'boolean') return n
  if (typeof n === 'string') return n.length > 0
  if (typeof n === 'number') return n !== 0
  return true
}

export function toNumber(n: Json): number {
  if (n === null || n === undefined) throw new RuleEvalError(err(Codes.typeError, 'reason', 'null-as-number'))
  if (typeof n === 'number') return n
  if (typeof n === 'boolean') return n ? 1 : 0
  if (typeof n === 'string') {
    const t = n.trim()
    if (t.length > 0) {
      const d = Number(t)
      if (!Number.isNaN(d) && Number.isFinite(d)) return d
    }
  }
  throw new RuleEvalError(err(Codes.typeError, 'reason', 'not-a-number'))
}

function numResult(d: number): Json {
  if (!Number.isFinite(d)) throw new RuleEvalError(err(Codes.typeError, 'reason', 'non-finite'))
  return d
}

export function asString(n: Json): string | null {
  if (n === null || n === undefined) return null
  if (typeof n === 'string') return n
  if (typeof n === 'boolean') return n ? 'true' : 'false'
  if (typeof n === 'number') return Number.isInteger(n) ? String(n) : String(n)
  return JSON.stringify(n)
}

function coerceNumber(n: Json): [boolean, number] {
  if (typeof n === 'number') return [true, n]
  if (typeof n === 'boolean') return [true, n ? 1 : 0]
  if (typeof n === 'string') {
    const t = n.trim()
    if (t.length > 0) {
      const d = Number(t)
      if (!Number.isNaN(d) && Number.isFinite(d)) return [true, d]
    }
  }
  return [false, 0]
}

function strictEquals(a: Json, b: Json): boolean {
  if (a === null || b === null) return a === null && b === null
  const an = typeof a === 'number'
  const bn = typeof b === 'number'
  if (an && bn) return a === b
  if (an !== bn) return false
  const ab = typeof a === 'boolean'
  const bb = typeof b === 'boolean'
  if (ab && bb) return a === b
  if (ab !== bb) return false
  if (typeof a === 'string' && typeof b === 'string') return a === b
  return JSON.stringify(a) === JSON.stringify(b)
}

function looseEquals(a: Json, b: Json): boolean {
  if (a === null || b === null) return a === null && b === null
  if (typeof a !== 'object' && typeof b !== 'object') {
    const [ac, an] = coerceNumber(a)
    const [bc, bn] = coerceNumber(b)
    if (ac && bc) return an === bn
    return asString(a) === asString(b)
  }
  return JSON.stringify(a) === JSON.stringify(b)
}
