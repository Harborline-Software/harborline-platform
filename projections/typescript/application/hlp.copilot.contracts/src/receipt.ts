import type { CommandClassification } from './types.js'

/** The mint inputs, snapshotted and frozen by `parseProposal`. Never built from caller-held state. */
export interface MintInput {
  readonly surface: string
  readonly command: string
  readonly args: unknown
  readonly tier: CommandClassification['tier']
}

/**
 * Proof that a dispatch was produced by this module's classification path.
 *
 * Validity is runtime object identity against the module-private `minted` set, not a type brand:
 * a hand-built literal, a structural clone and a JSON round-trip all fail closed, and nothing
 * outside this package can mint one because `mintReceipt` is not on the package's public surface.
 * This contains the model, the provider and every platform-side path — not the app author, who owns
 * the adapter; the real boundary is the api process (see adapter-contract.md).
 */
export interface DispatchReceipt {
  readonly surface: string
  readonly command: string
  readonly args: unknown
  readonly tier: 'ap' | 'cp'
  readonly expectedContextKey: string
}

const minted = new WeakSet<DispatchReceipt>()

/** App-owned Apply handlers create these; this is not proof against the app author. */
export interface GestureProof { readonly contextKey: string }
const gestures = new WeakMap<GestureProof, DispatchReceipt>()

/** Call only from a distinct human Apply handler, with the target visible at click time. */
export function confirmDispatch(receipt: DispatchReceipt, liveContextKey: string | null): GestureProof {
  assertReceipt(receipt, liveContextKey)
  const proof = Object.freeze({ contextKey: receipt.expectedContextKey })
  gestures.set(proof, receipt)
  return proof
}

function assertReceipt(receipt: DispatchReceipt, liveContextKey: string | null): void {
  if (typeof receipt !== 'object' || receipt === null || !minted.has(receipt)) throw new Error('undispatched-receipt')
  if (typeof receipt.expectedContextKey !== 'string' || receipt.expectedContextKey === '') throw new Error('expected-context-key-required')
  if (receipt.expectedContextKey !== liveContextKey) throw new Error('stale-target')
}

/** Copy JSON data before freezing: caller references and accessors cannot change validated args. */
export function freezeArgs(value: unknown, seen = new Map<object, unknown>()): unknown {
  if (value === null || typeof value !== 'object') {
    if (typeof value === 'function' || typeof value === 'symbol') throw new Error('invalid-args')
    return value
  }
  if (seen.has(value)) return seen.get(value)
  const prototype = Object.getPrototypeOf(value)
  if (!Array.isArray(value) && prototype !== Object.prototype && prototype !== null) throw new Error('invalid-args')
  const copy: Record<string, unknown> | unknown[] = Array.isArray(value) ? [] : Object.create(prototype)
  seen.set(value, copy)
  for (const key of Reflect.ownKeys(value)) {
    if (Array.isArray(value) && key === 'length') continue
    const descriptor = Object.getOwnPropertyDescriptor(value, key)!
    if (!('value' in descriptor)) throw new Error('invalid-args')
    Object.defineProperty(copy, key, { value: freezeArgs(descriptor.value, seen), enumerable: descriptor.enumerable ?? false })
  }
  return Object.freeze(copy)
}

/** Module-private: never re-exported from index.ts. Called only from classifyProposal. */
export function mintReceipt(input: MintInput, expectedContextKey: string): DispatchReceipt {
  const { surface, command, args, tier } = input
  if (tier === 'never') throw new Error('never-not-dispatchable')
  if (typeof expectedContextKey !== 'string' || expectedContextKey === '') throw new Error('expected-context-key-required')
  const receipt: DispatchReceipt = Object.freeze({ surface, command, args, tier, expectedContextKey })
  minted.add(receipt)
  return receipt
}

/** First statement of any adapter `execute`. Throws unless the receipt was minted here. */
export function assertDispatched(receipt: DispatchReceipt, liveContextKey: string | null, gesture?: GestureProof): DispatchReceipt {
  assertReceipt(receipt, liveContextKey)
  if (receipt.tier === 'cp') {
    if (gesture === undefined || gestures.get(gesture) !== receipt || gesture.contextKey !== liveContextKey) throw new Error('human-gesture-required')
    gestures.delete(gesture)
  }
  return receipt
}
