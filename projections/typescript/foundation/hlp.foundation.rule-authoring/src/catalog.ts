/**
 * The named-rule REGISTRY (ADR 0146 D5) at the narrowed Harborline seam. The pinned Harborline App
 * client was an HONEST INTERIM — a per-tenant-device localStorage catalog awaiting node
 * routes that never existed at the pin. Harborline replaces that interim with a STORE PORT:
 * the registry's caller-observable semantics (create-refuses-existing-key, draft-open
 * lifecycle, append-only monotonic published versions with downgrade refusal, duplicate,
 * archive-not-delete, stable listing) are identical, while persistence is injected —
 * `InMemoryRuleCatalogStore` for tests/dev, a durable host adapter behind the same port in
 * production. Version semantics remain the shipped engine's (`RuleVersion` watermark).
 */

import { RuleVersion } from '@harborline-software/rule-engine'

import type { RuleDraft, RuleSkinType } from './model.js'

/** One published (immutable) version of a named rule. */
export interface StoredRuleVersion {
  version: string
  draft: RuleDraft
  publishedAt: string
}

/** The full persisted record for one named rule. */
export interface StoredRule {
  ruleKey: string
  /** The human display label (the authoring name). */
  name: string
  skinType: RuleSkinType
  /** The current WORKING draft (may carry unpublished edits past the latest published version). */
  draft: RuleDraft
  /** Published versions, append-only, monotonic (never mutated in place — the S-8 watermark
   * forbids downgrade). */
  versions: StoredRuleVersion[]
  /** True when `draft` carries edits not yet published (a draft-open state). */
  hasUnpublishedDraft: boolean
  archived: boolean
  updatedAt: string
}

/** The lifecycle status shown in the list (design §1.1). */
export type RuleStatus = 'published' | 'draft-open' | 'draft'

/** A front-door list row (design §1.1). */
export interface NamedRuleSummary {
  ruleKey: string
  name: string
  skinType: RuleSkinType
  /** Highest published version, or undefined if never published. */
  publishedVersion?: string
  /** The pending draft version label when an unpublished draft exists. */
  draftVersion?: string
  status: RuleStatus
  /** Count of consumers holding a pin (display-only; D7 pins are consumer-side). */
  pinnedConsumers: number
  archived: boolean
  updatedAt: string
}

/**
 * The persistence PORT the catalog composes — the Harborline seam the pinned localStorage
 * interim becomes. Implementations must return/accept whole records; the catalog owns every
 * registry semantic above this line.
 */
export interface RuleCatalogStore {
  read(ruleKey: string): Promise<StoredRule | null>
  write(rule: StoredRule): Promise<void>
  list(): Promise<StoredRule[]>
}

/** The in-process store — a faithful test/dev double of a durable adapter. */
export class InMemoryRuleCatalogStore implements RuleCatalogStore {
  private readonly rules = new Map<string, StoredRule>()

  read(ruleKey: string): Promise<StoredRule | null> {
    const rule = this.rules.get(ruleKey)
    return Promise.resolve(rule ? structuredClone(rule) : null)
  }

  write(rule: StoredRule): Promise<void> {
    this.rules.set(rule.ruleKey, structuredClone(rule))
    return Promise.resolve()
  }

  list(): Promise<StoredRule[]> {
    return Promise.resolve([...this.rules.values()].map((rule) => structuredClone(rule)))
  }
}

/** Highest published version by the S-8 watermark, or undefined. */
function highestPublished(rule: StoredRule): string | undefined {
  let best: string | undefined
  for (const v of rule.versions) {
    if (best === undefined || RuleVersion.compare(v.version, best) > 0) best = v.version
  }
  return best
}

/** The next version after the highest published (patch bump; `1.0.0` for the first). */
export function nextVersion(rule: StoredRule): string {
  const cur = highestPublished(rule)
  if (!cur) return '1.0.0'
  const [maj, min, patch] = cur.split('.').map((n) => Number(n) || 0)
  return `${maj}.${min}.${(patch ?? 0) + 1}`
}

function summarize(rule: StoredRule): NamedRuleSummary {
  const published = highestPublished(rule)
  const status: RuleStatus = rule.hasUnpublishedDraft ? (published ? 'draft-open' : 'draft') : 'published'
  return {
    ruleKey: rule.ruleKey,
    name: rule.name,
    skinType: rule.skinType,
    publishedVersion: published,
    draftVersion: rule.hasUnpublishedDraft ? nextVersion(rule) : undefined,
    status,
    pinnedConsumers: 0,
    archived: rule.archived,
    updatedAt: rule.updatedAt,
  }
}

/**
 * The named-rule catalog over an injected store port — the registry's public surface. The
 * function shapes mirror the pinned client one-for-one so callers (and the ported tests)
 * carry over unchanged apart from construction.
 */
export class RuleCatalog {
  constructor(
    private readonly store: RuleCatalogStore,
    private readonly clock: () => Date = () => new Date(),
  ) {}

  /** List the tenant's named rules (design §1.1 — the front-door source of truth). */
  async listRules(): Promise<NamedRuleSummary[]> {
    const rules = await this.store.list()
    return rules.map(summarize).sort((a, b) => a.name.localeCompare(b.name))
  }

  /** Load one named rule (its working draft + versions). Returns null if absent. */
  loadRule(ruleKey: string): Promise<StoredRule | null> {
    return this.store.read(ruleKey)
  }

  /** Mint a fresh named rule from a blank draft. Refuses an existing key (never clobbers). */
  async createRule(input: { ruleKey: string; name: string; skinType: RuleSkinType; draft: RuleDraft }): Promise<StoredRule> {
    const existing = await this.store.read(input.ruleKey)
    if (existing) throw new Error(`rule create failed: key '${input.ruleKey}' already exists`)
    const rule: StoredRule = {
      ruleKey: input.ruleKey,
      name: input.name,
      skinType: input.skinType,
      draft: input.draft,
      versions: [],
      hasUnpublishedDraft: true,
      archived: false,
      updatedAt: this.clock().toISOString(),
    }
    await this.store.write(rule)
    return rule
  }

  /** Persist the working draft (marks an unpublished-draft state). Does NOT publish. */
  async saveDraft(ruleKey: string, draft: RuleDraft): Promise<void> {
    const rule = await this.store.read(ruleKey)
    if (!rule) throw new Error(`rule save failed: '${ruleKey}' not found`)
    rule.draft = draft
    rule.hasUnpublishedDraft = true
    rule.updatedAt = this.clock().toISOString()
    await this.store.write(rule)
  }

  /** Append a published version (called by the admission fence AFTER the compile passes — never
   * directly by a UI). Advances the working state to "published, no pending draft". */
  async commitPublishedVersion(ruleKey: string, version: string, draft: RuleDraft): Promise<void> {
    const rule = await this.store.read(ruleKey)
    if (!rule) throw new Error(`rule publish failed: '${ruleKey}' not found`)
    // S-8 monotonic guard: refuse a downgrade (never mutate history in place).
    const cur = highestPublished(rule)
    if (cur && RuleVersion.isDowngrade(cur, version)) throw new Error(`rule publish refused: ${version} downgrades ${cur}`)
    rule.versions.push({ version, draft, publishedAt: this.clock().toISOString() })
    rule.hasUnpublishedDraft = false
    rule.updatedAt = this.clock().toISOString()
    await this.store.write(rule)
  }

  /** Duplicate a rule under a new key/name (forks the working draft). */
  async duplicateRule(sourceKey: string, newKey: string, newName: string): Promise<StoredRule> {
    const source = await this.loadRule(sourceKey)
    if (!source) throw new Error(`rule duplicate failed: '${sourceKey}' not found`)
    return this.createRule({ ruleKey: newKey, name: newName, skinType: source.skinType, draft: source.draft })
  }

  /** Archive / unarchive (list-visibility only — never a hard delete). */
  async setArchived(ruleKey: string, archived: boolean): Promise<void> {
    const rule = await this.store.read(ruleKey)
    if (!rule) return
    rule.archived = archived
    rule.updatedAt = this.clock().toISOString()
    await this.store.write(rule)
  }
}

/** Mint a stable, collision-free rule key from a display name (slug; rule versions are the
 * registry's job, so NO `.v1` suffix). */
export function mintRuleKey(name: string, existing: ReadonlySet<string>): string {
  const base =
    name
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '') || 'rule'
  let candidate = base
  let n = 2
  while (existing.has(candidate)) {
    candidate = `${base}-${n}`
    n += 1
  }
  return candidate
}
