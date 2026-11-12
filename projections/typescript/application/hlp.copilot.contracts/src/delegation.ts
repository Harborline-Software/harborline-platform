import type { ProposalEnvelope } from './types.js'
import { freezeArgs } from './receipt.js'

export function serializeProposalCanonical(proposal: ProposalEnvelope): string {
  const snapshot = freezeArgs(proposal) as ProposalEnvelope
  if (snapshot === null || typeof snapshot !== 'object' || Array.isArray(snapshot)
    || Object.keys(snapshot).sort().join(',') !== 'args,command,schema,surface'
    || snapshot.schema !== 'pilot.proposal/3' || typeof snapshot.surface !== 'string' || typeof snapshot.command !== 'string') {
    throw new TypeError('invalid-proposal-envelope')
  }
  return canonical(snapshot)
}

export async function proposalDigest(proposal: ProposalEnvelope): Promise<string> {
  const bytes = new TextEncoder().encode(serializeProposalCanonical(proposal))
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes)
  return 'sha256:' + Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('')
}

function canonical(value: unknown): string {
  if (value === null || typeof value === 'boolean') return JSON.stringify(value)
  if (typeof value === 'string') {
    if (/[\uD800-\uDFFF]/u.test(value)) throw new TypeError('invalid-json-string')
    return JSON.stringify(value)
  }
  if (typeof value === 'number' && Number.isFinite(value)) return JSON.stringify(value)
  if (Array.isArray(value)) return '[' + Array.from(value, canonical).join(',') + ']'
  if (typeof value === 'object' && value !== null) {
    return '{' + Object.keys(value).sort().map(key => canonical(key) + ':' + canonical((value as Record<string, unknown>)[key])).join(',') + '}'
  }
  throw new TypeError('invalid-json-value')
}
