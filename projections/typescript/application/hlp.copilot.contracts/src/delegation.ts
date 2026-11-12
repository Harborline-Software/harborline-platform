import type { ProposalEnvelope } from './types.js'
import type { ParseOk } from './proposal.js'
import { freezeArgs } from './receipt.js'

export interface DelegationActor {
  readonly binding_key: string
  readonly binding_version: string
  readonly provider_id: string
  readonly model_version: string
  readonly proposal_digest: string
}

export interface ApprovalScope { readonly surface: string; readonly commands: readonly string[] }
export type DelegationDisposition =
  | { readonly kind: 'approve' | 'dismiss' | 'clarify' | 'reject' }
  | { readonly kind: 'standing'; readonly approval_id: string; readonly approver: string; readonly scope: ApprovalScope; readonly expires_at: string }
export interface DelegationRecord { readonly subject: string; readonly actor: DelegationActor | null; readonly disposition: DelegationDisposition }
export interface StandingApproval {
  readonly id: string
  readonly approver: string
  readonly binding_key: string
  readonly scope: ApprovalScope
  readonly schedule: string | null
  readonly expires_at: string
  readonly revoked_at: string | null
}

export function parseDelegationRecord(value: unknown): DelegationRecord {
  const record = object(freezeArgs(value), ['subject', 'actor', 'disposition'])
  let actor: DelegationActor | null = null
  if (record.actor !== null) {
    const source = object(record.actor, ['binding_key', 'binding_version', 'provider_id', 'model_version', 'proposal_digest'])
    const digest = text(source.proposal_digest)
    if (digest.length !== 71 || !/^sha256:[0-9a-f]{64}$/.test(digest)) throw new TypeError('invalid-proposal-digest')
    actor = { binding_key: text(source.binding_key), binding_version: text(source.binding_version),
      provider_id: text(source.provider_id), model_version: text(source.model_version), proposal_digest: digest }
  }
  return { subject: text(record.subject), actor, disposition: parseDelegationDisposition(record.disposition) }
}

export function parseDelegationDisposition(value: unknown): DelegationDisposition {
  value = freezeArgs(value)
  const kind = value !== null && typeof value === 'object' ? Object.getOwnPropertyDescriptor(value, 'kind')?.value : undefined
  if (kind === 'approve' || kind === 'dismiss' || kind === 'clarify' || kind === 'reject') {
    object(value, ['kind'])
    return { kind }
  }
  const record = object(value, ['kind', 'approval_id', 'approver', 'scope', 'expires_at'])
  if (kind !== 'standing') throw new TypeError('invalid-disposition')
  return { kind, approval_id: text(record.approval_id), approver: text(record.approver), scope: scope(record.scope), expires_at: instant(record.expires_at) }
}

export function parseStandingApproval(value: unknown): StandingApproval {
  const record = object(freezeArgs(value), ['id', 'approver', 'binding_key', 'scope', 'schedule', 'expires_at', 'revoked_at'])
  return { id: text(record.id), approver: text(record.approver), binding_key: text(record.binding_key), scope: scope(record.scope),
    schedule: record.schedule === null ? null : text(record.schedule), expires_at: instant(record.expires_at),
    revoked_at: record.revoked_at === null ? null : instant(record.revoked_at) }
}

function object(value: unknown, members: readonly string[]): Record<string, unknown> {
  if (value === null || typeof value !== 'object' || Array.isArray(value)
    || (Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null)
    || Reflect.ownKeys(value).length !== members.length
    || !members.every(member => {
      const descriptor = Object.getOwnPropertyDescriptor(value, member)
      return descriptor !== undefined && 'value' in descriptor && descriptor.enumerable
    })) throw new TypeError('invalid-wire-members')
  return value as Record<string, unknown>
}

function text(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '' || /[\uD800-\uDFFF]/u.test(value)) throw new TypeError('required-wire-string')
  return value
}

function scope(value: unknown): ApprovalScope {
  const record = object(value, ['surface', 'commands'])
  if (!Array.isArray(record.commands) || record.commands.length === 0) throw new TypeError('required-scope-commands')
  return { surface: text(record.surface), commands: Array.from(record.commands, text) }
}

function instant(value: unknown): string {
  const result = text(value)
  const parsed = new Date(result)
  if (result !== result.trim() || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(result)
    || !Number.isFinite(parsed.getTime()) || parsed.getUTCFullYear() === 0
    || parsed.toISOString().slice(0, 19) !== result.slice(0, 19)) throw new TypeError('required-utc-instant')
  return result
}

export function deserializeProposalEnvelope(json: string): ProposalEnvelope {
  const proposal = JSON.parse(json) as ProposalEnvelope
  serializeProposalCanonical(proposal)
  return freezeArgs(proposal) as ProposalEnvelope
}

export function serializeProposalCanonical(proposal: ProposalEnvelope): string {
  const snapshot = object(proposal, ['args', 'command', 'schema', 'surface'])
  if (snapshot.schema !== 'pilot.proposal/3' || typeof snapshot.surface !== 'string' || typeof snapshot.command !== 'string') {
    throw new TypeError('invalid-proposal-envelope')
  }
  return canonical(snapshot)
}

export async function proposalDigest(proposal: ProposalEnvelope | ParseOk): Promise<string> {
  const bytes = new TextEncoder().encode(serializeProposalCanonical('received' in proposal ? proposal.received : proposal))
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes)
  return 'sha256:' + Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('')
}

function canonical(value: unknown, depth = 0): string {
  if (depth > 64) throw new TypeError('json-depth-exceeded')
  if (value === null || typeof value === 'boolean') return JSON.stringify(value)
  if (typeof value === 'string') {
    if (/[\uD800-\uDFFF]/u.test(value)) throw new TypeError('invalid-json-string')
    return JSON.stringify(value)
  }
  if (typeof value === 'number' && Number.isFinite(value)) {
    if (Number.isInteger(value) && !Number.isSafeInteger(value)) throw new TypeError('args-integer-out-of-range')
    return JSON.stringify(value)
  }
  if (Array.isArray(value)) {
    if (Object.keys(value).length !== value.length || Reflect.ownKeys(value).length !== value.length + 1) throw new TypeError('invalid-json-array')
    return '[' + Object.keys(value).map((key, index) => {
      const descriptor = Object.getOwnPropertyDescriptor(value, key)!
      if (key !== String(index) || !('value' in descriptor)) throw new TypeError('invalid-json-array')
      return canonical(descriptor.value, depth + 1)
    }).join(',') + ']'
  }
  if (typeof value === 'object' && value !== null) {
    const keys = Object.keys(value).sort()
    if ((Object.getPrototypeOf(value) !== Object.prototype && Object.getPrototypeOf(value) !== null)
      || Reflect.ownKeys(value).length !== keys.length) throw new TypeError('invalid-json-members')
    return '{' + keys.map(key => {
      const descriptor = Object.getOwnPropertyDescriptor(value, key)!
      if (!('value' in descriptor)) throw new TypeError('invalid-json-member')
      return canonical(key, depth + 1) + ':' + canonical(descriptor.value, depth + 1)
    }).join(',') + '}'
  }
  throw new TypeError('invalid-json-value')
}
