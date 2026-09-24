import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import {
  builtInFunctions, canonicalFunctionReference, compile, displayFunctionReference, FunctionReferenceCodes,
  isRegisteredBuiltIn, packageFunction, readFunctionReference, readPackagePayload, resolveBuiltIn, CompileError,
} from '../index.js'
import { evaluate } from '../jsonlogic.js'
import { EvalBudget, RuleEvalError, type EvalContext } from '../eval-support.js'
import { DEFAULT_LIMITS } from '../limits.js'

const code = (f: () => unknown) => { try { f() } catch (e) { return (e as { code?: string }).code } return undefined }

describe('T-590 R1 register and discriminated function reference (TS tier)', () => {
  it('rules-ck-29, rules-bound-3: a package payload cannot construct the built-in discriminant', () => {
    expect(code(() => readPackagePayload({ kind: 'builtin', key: 'cat' }))).toBe(FunctionReferenceCodes.builtInFromPackage)
    const pkg = readPackagePayload({ kind: 'package', packageId: 'finance', functionKey: 'cat' })
    expect(pkg.kind).toBe('package')
    // A structural look-alike is not the register's reference and cannot be serialized as one.
    const forged = { kind: 'builtin', key: 'cat' } as const
    expect(isRegisteredBuiltIn(forged)).toBe(false)
    expect(code(() => canonicalFunctionReference(forged))).toBe(FunctionReferenceCodes.builtInFromPackage)
    expect(canonicalFunctionReference(pkg)).not.toBe(canonicalFunctionReference(resolveBuiltIn('cat')))
  })

  it('rules-ck-29: canonical form carries the discriminant and structured fields, byte-identical to .NET; display is never parsed', () => {
    const builtIn = resolveBuiltIn('money.add')
    const pkg = packageFunction('finance', 'money.add')
    expect(canonicalFunctionReference(builtIn)).toBe('{"key":"money.add","kind":"builtin"}')
    expect(canonicalFunctionReference(pkg)).toBe('{"functionKey":"money.add","kind":"package","packageId":"finance"}')
    expect(displayFunctionReference(pkg)).toBe('finance::money.add')
    expect(code(() => packageFunction('fin:ance', 'x'))).toBe(FunctionReferenceCodes.fieldInvalid)
    expect(code(() => packageFunction('finance', 'a:b'))).toBe(FunctionReferenceCodes.fieldInvalid)
    expect(code(() => readFunctionReference('finance::money.add'))).toBe(FunctionReferenceCodes.malformed)
    expect(readFunctionReference(JSON.parse(canonicalFunctionReference(builtIn)))).toBe(builtIn)
    expect(readFunctionReference(JSON.parse(canonicalFunctionReference(pkg)))).toEqual(pkg)
  })

  it('rules-eng-27: each built-in resolves exactly once from the register and every registered key executes', () => {
    const keys = builtInFunctions.map((f) => f.key)
    expect(new Set(keys).size).toBe(keys.length)
    for (const f of builtInFunctions) {
      expect(resolveBuiltIn(f.key)).toBe(f.reference)
      const ctx: EvalContext = { resolver: { resolveVar: () => ({ state: 'Resolved', value: null }), resolveAgg: () => ({ state: 'Resolved', value: 0 }) },
        now: new Date(0), budget: new EvalBudget(DEFAULT_LIMITS) }
      try { evaluate({ [f.key]: Array.from({ length: f.minArity }, () => '1') }, ctx) } catch (e) {
        if (e instanceof RuleEvalError) expect(e.error.code).not.toBe('rule.unknown_operator')
        else throw e
      }
    }
    expect(code(() => resolveBuiltIn('regex.match'))).toBe(FunctionReferenceCodes.unknownBuiltIn)
  })

  it('rules-eng-27: the evaluator and the compiler read the register rather than a closed switch', () => {
    const here = dirname(fileURLToPath(import.meta.url))
    const evaluator = readFileSync(join(here, '..', 'jsonlogic.ts'), 'utf8')
    const compiler = readFileSync(join(here, '..', 'compiler.ts'), 'utf8')
    expect(evaluator).not.toContain("case 'coding.is'")
    expect(compiler).not.toContain("'coding.is'")
    const refused = code(() => compile([{ id: 'r', tier: 'JsonLogic', scope: 'Field', scopeTarget: 'a', action: 'Compute', expression: '{"shadow":[1]}' }]))
    expect(refused).toBe('rule.compile.invalid_expression')
    expect(CompileError).toBeDefined()
  })
})
