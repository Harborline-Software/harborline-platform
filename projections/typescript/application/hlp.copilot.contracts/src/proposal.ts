import type { CommandSpec, ProposalDisposition, ProposalEnvelope } from './types.js'
import { mintReceipt, type MintInput } from './receipt.js'
export const PILOT_PROPOSAL_SCHEMA = 'pilot.proposal/3' as const
export type ProposalRejectCode = 'not-object' | 'missing-schema' | 'unsupported-schema' | 'missing-surface' | 'unknown-surface' | 'unknown-command' | 'invalid-args'
export type ParseOk = { ok: true; proposal: ProposalEnvelope; spec: CommandSpec }
export type ParseResult = ParseOk | { ok: false; code: ProposalRejectCode }
/**
 * Module-private provenance AND the mint inputs snapshotted at parse time. The value, not the
 * result object, is what classification reads: the result is a caller-held reference, so anything
 * read off it at classify time is caller-controlled.
 */
const parsed = new WeakMap<ParseOk, MintInput>()
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)

export function parseProposal(raw: unknown, surface: string, specs: readonly CommandSpec[]): ParseResult {
  if (!object(raw)) return { ok: false, code: 'not-object' }
  if (raw.schema === undefined) return { ok: false, code: 'missing-schema' }
  if (raw.schema !== PILOT_PROPOSAL_SCHEMA) return { ok: false, code: 'unsupported-schema' }
  if (typeof raw.surface !== 'string' || raw.surface.trim() === '') return { ok: false, code: 'missing-surface' }
  if (raw.surface !== surface) return { ok: false, code: 'unknown-surface' }
  if (typeof raw.command !== 'string') return { ok: false, code: 'unknown-command' }
  const spec = specs.find(s => s.id === raw.command || s.aliases?.includes(raw.command as string))
  if (!spec) return { ok: false, code: 'unknown-command' }
  const args = spec.argsSchema(raw.args ?? {})
  if (!args.ok) return { ok: false, code: 'invalid-args' }
  const result: ParseOk = Object.freeze({ ok: true, proposal: Object.freeze({ schema: PILOT_PROPOSAL_SCHEMA, surface, command: spec.id, args: args.args }), spec })
  parsed.set(result, Object.freeze({ surface, command: spec.id, args: args.args, tier: spec.classification.tier }))
  return result
}

/**
 * Reads nothing off the result but its identity: the surface, command, args and tier come from the
 * frozen snapshot taken at parse time. A caller-authored spec, a look-alike result literal, and a
 * genuine result whose `spec` or `proposal` was rewritten after the parse all fail to move the mint.
 */
export function classifyProposal(result: ParseOk, contextKey: string | null): ProposalDisposition {
  const input = typeof result === 'object' && result !== null ? parsed.get(result) : undefined
  if (input === undefined) throw new Error('unparsed-proposal')
  if (input.tier === 'never') return { kind: 'reject', reason: 'never-exposed' }
  if (contextKey === null || contextKey === '') return { kind: 'clarify', reason: 'no-target' }
  const receipt = mintReceipt(input, contextKey)
  const kind: 'card' | 'auto-apply' = input.tier === 'cp' ? 'card' : 'auto-apply'
  return { kind, expectedContextKey: contextKey, receipt }
}

export class ProposalTextSplitter {
  private prose = ''; private fenced = false; private fence = ''
  push(delta: string): string {
    if (this.fenced) { this.fence += delta; return '' }
    this.prose += delta; const i = this.prose.indexOf('```')
    if (i >= 0) { const out = this.prose.slice(0, i); this.fence = this.prose.slice(i); this.prose = ''; this.fenced = true; return out }
    const partial = this.prose.match(/`{1,2}$/)?.[0].length ?? 0
    const out = this.prose.slice(0, this.prose.length - partial); this.prose = this.prose.slice(this.prose.length - partial); return out
  }
  end(): { text: string; json: unknown | null; raw: string } {
    if (!this.fenced) { const text = this.prose; this.prose = ''; return { text, json: parseJsonObject(text), raw: '' } }
    return { text: '', json: parseJsonObject(this.fence), raw: this.fence }
  }
}
export function parseJsonObject(text: string): unknown | null {
  const body = text.match(/```(?:json)?\s*([\s\S]*?)```/i)?.[1] ?? text
  const a = body.indexOf('{'), b = body.lastIndexOf('}'); if (a < 0 || b <= a) return null
  try { return JSON.parse(body.slice(a, b + 1)) as unknown } catch { return null }
}

