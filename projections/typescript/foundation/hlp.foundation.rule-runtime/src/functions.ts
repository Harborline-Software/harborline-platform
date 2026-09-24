/**
 * The discriminated function reference (DES-0018 rules-ck-29), TS tier. Mirrors the .NET
 * `FunctionReference`: exactly a built-in or a package function. Built-in references are the
 * register's own frozen objects; package data cannot produce one, and a look-alike literal is not
 * one. `package_id::function_key` is display only and nothing here parses it.
 */
import { builtInRegister, type BuiltInKey } from './jsonlogic.js'
import type { Json } from './model.js'

export interface BuiltInFunction { readonly kind: 'builtin'; readonly key: BuiltInKey }
export interface PackageFunction { readonly kind: 'package'; readonly packageId: string; readonly functionKey: string }
export type FunctionReference = BuiltInFunction | PackageFunction

/** One executable built-in as the palette sees it (the implementation stays inside the evaluator). */
export interface BuiltInFunctionDefinition {
  readonly reference: BuiltInFunction
  readonly key: BuiltInKey
  readonly category: string
  readonly minArity: number
  readonly maxArity: number
  readonly authorable: boolean
}

export const FunctionReferenceCodes = {
  builtInFromPackage: 'rule.function.builtin_from_package',
  fieldInvalid: 'rule.function.field_invalid',
  malformed: 'rule.function.malformed',
  unknownBuiltIn: 'rule.function.unknown_builtin',
} as const

export class FunctionReferenceError extends Error {
  constructor(readonly code: string, message: string) { super(message) }
}

const owned = new WeakSet<object>()

/** Every executable built-in, in register order. */
export const builtInFunctions: readonly BuiltInFunctionDefinition[] = Object.freeze(
  (Object.keys(builtInRegister) as BuiltInKey[]).map((key) => {
    const reference: BuiltInFunction = Object.freeze({ kind: 'builtin', key })
    owned.add(reference)
    const { category, minArity, maxArity, authorable } = builtInRegister[key]
    return Object.freeze({ reference, key, category, minArity, maxArity, authorable })
  }),
)

const byKey = new Map(builtInFunctions.map((f) => [f.key as string, f]))

/** The register's one reference for `key`. */
export function resolveBuiltIn(key: string): BuiltInFunction {
  const f = byKey.get(key)
  if (f === undefined) throw new FunctionReferenceError(FunctionReferenceCodes.unknownBuiltIn, `'${key}' is not a registered built-in`)
  return f.reference
}

/** True only for a reference the register handed out; a structural look-alike is refused. */
export function isRegisteredBuiltIn(value: unknown): value is BuiltInFunction {
  return typeof value === 'object' && value !== null && owned.has(value)
}

/** Creates a package reference; empty values and `:` refuse. */
export function packageFunction(packageId: string, functionKey: string): PackageFunction {
  return Object.freeze({ kind: 'package', packageId: field(packageId), functionKey: field(functionKey) })
}

/** Reads a reference supplied by package data: only the package arm can come out. */
export function readPackagePayload(node: Json): PackageFunction {
  const obj = asObject(node)
  if (obj.kind === 'builtin') throw new FunctionReferenceError(FunctionReferenceCodes.builtInFromPackage, 'package data cannot construct a built-in function reference')
  const read = readFunctionReference(obj)
  if (read.kind !== 'package') throw malformed()
  return read
}

/** Reads a platform-authored structured reference; a built-in resolves only through the register. */
export function readFunctionReference(node: Json): FunctionReference {
  const obj = asObject(node)
  const keys = Object.keys(obj).length
  if (obj.kind === 'builtin' && keys === 2 && typeof obj.key === 'string') return resolveBuiltIn(obj.key)
  if (obj.kind === 'package' && keys === 3 && typeof obj.packageId === 'string' && typeof obj.functionKey === 'string') {
    return packageFunction(obj.packageId, obj.functionKey)
  }
  throw malformed()
}

/** Sorted-key canonical JSON carrying the discriminant; byte-identical to the .NET tier. */
export function canonicalFunctionReference(ref: FunctionReference): string {
  if (ref.kind === 'builtin') {
    if (!isRegisteredBuiltIn(ref)) throw new FunctionReferenceError(FunctionReferenceCodes.builtInFromPackage, 'only the register constructs a built-in reference')
    return JSON.stringify({ key: ref.key, kind: 'builtin' })
  }
  return JSON.stringify({ functionKey: ref.functionKey, kind: 'package', packageId: ref.packageId })
}

/** Human-readable form; never a wire form. */
export function displayFunctionReference(ref: FunctionReference): string {
  return ref.kind === 'builtin' ? ref.key : `${ref.packageId}::${ref.functionKey}`
}

function field(value: string): string {
  if (value.length === 0 || value.includes(':')) {
    throw new FunctionReferenceError(FunctionReferenceCodes.fieldInvalid, "a function reference field must be non-empty and must not contain ':'")
  }
  return value
}

function asObject(node: Json): Record<string, Json> {
  if (typeof node !== 'object' || node === null || Array.isArray(node)) throw malformed()
  return node as Record<string, Json>
}

const malformed = () => new FunctionReferenceError(FunctionReferenceCodes.malformed, 'a function reference must be the structured kind form')
