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

/** Module-private: never re-exported from index.ts. Called only from classifyProposal. */
export function mintReceipt(input: MintInput, expectedContextKey: string): DispatchReceipt {
  const { surface, command, args, tier } = input
  if (tier === 'never') throw new Error('never-not-dispatchable')
  if (expectedContextKey === '') throw new Error('expected-context-key-required')
  const receipt: DispatchReceipt = Object.freeze({ surface, command, args, tier, expectedContextKey })
  minted.add(receipt)
  return receipt
}

/** First statement of any adapter `execute`. Throws unless the receipt was minted here. */
export function assertDispatched(receipt: DispatchReceipt): DispatchReceipt {
  if (typeof receipt !== 'object' || receipt === null || !minted.has(receipt)) throw new Error('undispatched-receipt')
  return receipt
}
