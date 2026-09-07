import type { DispatchReceipt } from './receipt.js'

export type PilotRole = 'system' | 'user' | 'assistant'
export interface PilotMessage { readonly id: string; readonly role: PilotRole; readonly content: string }
export type PilotChunk = { readonly kind: 'text'; readonly text: string } | { readonly kind: 'done' }

export type NeverArchetype = 'irreversible-bulk' | 'security-access-control' | 'engine-parked' | 'human-authority-gate' | 'agent-self-repoint'
export type CommandClassification =
  | { readonly tier: 'ap' }
  | { readonly tier: 'cp' }
  | { readonly tier: 'never'; readonly archetype: NeverArchetype; readonly justification: string }
export type ArgsResult<A> = { readonly ok: true; readonly args: A } | { readonly ok: false; readonly code: string }
export interface CommandSpec<A = unknown> {
  readonly id: string
  readonly summary: string
  readonly argsHint: string
  readonly argsSchema: (raw: unknown) => ArgsResult<A>
  readonly classification: CommandClassification
  readonly undoable: boolean
  readonly aliases?: readonly string[]
  readonly palette?: { readonly labelKey: string; readonly shortcut?: string; readonly keywords?: readonly string[] }
}
export interface SurfaceContext { readonly contextKey: string; readonly serialized: string }
export interface SurfaceDescriptor {
  readonly surface: string
  readonly matchesRoute: (pathname: string) => boolean
  readonly specs: readonly CommandSpec[]
  readonly buildManifest: () => string
  readonly readContext: (pathname: string) => SurfaceContext | null
}
export type ProposalEnvelope = { readonly schema: 'pilot.proposal/3'; readonly surface: string; readonly command: string; readonly args: unknown }
export type ProposalDisposition =
  | { readonly kind: 'auto-apply'; readonly expectedContextKey: string; readonly receipt: DispatchReceipt }
  | { readonly kind: 'card'; readonly expectedContextKey: string; readonly receipt: DispatchReceipt }
  | { readonly kind: 'reject'; readonly reason: string }
  | { readonly kind: 'clarify'; readonly reason: string }

/** App-owned seam. Implementations are effectful; Platform never supplies one in Wave 1. */
export interface PilotEffectAdapter {
  currentContextKey(surface: string): string | null
  /** Must call `assertDispatched(receipt)` as its first statement; a receipt exists only for AP/CP. */
  execute(receipt: DispatchReceipt): Promise<{ ok: true; undoToken?: string } | { ok: false; code: string }>
}

