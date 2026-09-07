/**
 * The PUBLISH ADMISSION fence for named rules (ADR 0146 D7 — design §5.2). Publishing a rule
 * is a CONTROL change: callers NEVER commit a version directly — they route through this
 * fence, which (1) COMPILES the skin through the shipped compiler (so an F1 no-match /
 * undeclared-ref violation is a publish rejection carrying the stable `rule.skin.*` code),
 * then (2) mints the next version through the injected catalog (S-8 watermark;
 * refuse-downgrade). The pinned Harborline App fence ran this compile locally while awaiting node
 * routes; at the Harborline seam the SAME fence runs over the catalog port, so the
 * authoritative admission and the persistence commit share one transactional surface.
 */

import { SkinCodes, type RuleDefinition } from '@harborline-software/rule-engine'

import type { RuleCatalog } from './catalog.js'
import { nextVersion } from './catalog.js'
import { compileDraft, isCompileError } from './compile.js'
import { noMatchResolved } from './lint.js'
import type { DecisionTableDraft, RuleDraft } from './model.js'

/** The outcome of a publish attempt. A failure carries the stable skin code the compiler raised. */
export type PublishOutcome =
  | { ok: true; version: string; definition: RuleDefinition }
  | { ok: false; code: string; message: string }

/**
 * Publishes a rule's current draft through the admission fence. Compiles the skin FIRST
 * (fail-closed: any `rule.skin.*` rejection blocks publish and returns its code) and only
 * then mints + commits the next version. Publish is a control change — callers gate this
 * behind a human CP confirm (§5.2).
 */
export async function publishRule(catalog: RuleCatalog, ruleKey: string, draft: RuleDraft): Promise<PublishOutcome> {
  // Surface-level F1 gate (design §2.3): a BLANK Otherwise default compiles (an empty string
  // is a legal else-value), so the skin compiler alone does not reject it — the fence must.
  // Block publish with the same stable code the compiler raises for the catch-all path.
  if (draft.skin === 'table' && !noMatchResolved(draft as DecisionTableDraft)) {
    return { ok: false, code: SkinCodes.noMatchUnresolved, message: 'no-match is unresolved' }
  }

  let definition: RuleDefinition
  try {
    // The admission compile fence — the SINGLE source of F1 / undeclared-ref rejection.
    definition = compileDraft(draft, ruleKey)
  } catch (e) {
    if (isCompileError(e)) return { ok: false, code: e.code, message: e.message }
    throw e
  }

  const rule = await catalog.loadRule(ruleKey)
  if (!rule) return { ok: false, code: 'rules.publish.not_found', message: `rule '${ruleKey}' not found` }
  const version = nextVersion(rule)
  await catalog.commitPublishedVersion(ruleKey, version, draft)
  return { ok: true, version, definition }
}

/**
 * Whether publishing this draft is a CONTROL change requiring a human CP confirm (design
 * §5.2). Under D7 every rule publish is treated as a control change; kept as a function so a
 * CP-reachability litmus is a drop-in.
 */
export function publishIsControlChange(_draft: RuleDraft): boolean {
  return true
}
