/**
 * The named-rule registry + version policies (ADR 0146 D5) — the TS mirror of the .NET
 * `RuleRegistry`. Resolve a rule by `(tenant, rule-key)` under a version policy
 * (`latest` | `pinned:<v>` | `draft`). Composes shipped patterns, no new primitive: the S-8
 * monotonic watermark ({@link RuleVersion}) for offline "latest", D7 instance pins, and (in the
 * sibling compile-cache) the HomeEpochFence read-tip-reject-stale discipline.
 */
import type { RuleDefinition } from '../model.js'
import { RuleVersion } from './version.js'

/** The version policy a caller declares when resolving a named rule. */
export type RuleVersionPolicyKind = 'latest' | 'pinned' | 'draft'

/** A caller-declared version policy: `latest` | `pinned:<v>` | `draft`. */
export interface RuleVersionPolicy {
  readonly kind: RuleVersionPolicyKind
  /** The pinned version — present iff `kind === 'pinned'`. */
  readonly version?: string
}

/** Factories for the three policies (mirrors the .NET `RuleVersionPolicy.Latest/Pinned/Draft`). */
export const RuleVersionPolicy = {
  /** Resolve the highest published (non-draft) version. */
  latest: { kind: 'latest' } as RuleVersionPolicy,
  /** Resolve exactly `version` (re-runs admission on any pin change). */
  pinned: (version: string): RuleVersionPolicy => ({ kind: 'pinned', version }),
  /** Resolve the latest draft — reachable ONLY through a sandbox-scoped resolve. */
  draft: { kind: 'draft' } as RuleVersionPolicy,
}

/**
 * The trust boundary a resolve runs under (board F6). The draft-exclusion is a MECHANISM, not
 * prose: a `production` resolve NEVER returns a draft — drafts are reachable only through `sandbox`.
 */
export type RuleResolveScope = 'production' | 'sandbox'

/** The outcome of a resolve. */
export type RuleResolutionStatus = 'Resolved' | 'NotFound' | 'DraftRefused'

/** The result of resolving a named rule to a version + its definition. */
export interface RuleResolution {
  readonly status: RuleResolutionStatus
  /** The resolved (or refused) version, when known. */
  readonly version?: string
  /** The resolved definition — present iff `status === 'Resolved'`. */
  readonly definition?: RuleDefinition
}

const notFound: RuleResolution = { status: 'NotFound' }
const resolved = (version: string, definition: RuleDefinition): RuleResolution => ({ status: 'Resolved', version, definition })
const draftRefused = (version?: string): RuleResolution => ({ status: 'DraftRefused', version })

/**
 * A D7 instance pin: the exact rule version captured at consumer-instance creation. Resolving
 * through the pin replays deterministically against that version; re-admission happens on re-pin.
 */
export interface RulePin {
  readonly tenant: string
  readonly ruleKey: string
  readonly version: string
}

/** One published rule version in the registry. */
export interface PublishedRuleVersion {
  readonly ruleKey: string
  readonly version: string
  /** True for a draft — reachable only through a sandbox-scoped resolve. */
  readonly isDraft: boolean
  readonly definition: RuleDefinition
}

/**
 * In-memory named-rule registry (ADR 0146 D5 / Wave 1). Holds published versions per
 * `(tenant, rule-key)` and resolves under the D5 version policies. Exercised in isolation: no
 * production CRDT sync feeds it yet — that is the Wave-2 cascade (`PackContentKind.RuleDefinition`,
 * D8). Single-threaded (JS event loop), so each resolve/publish is atomic without a lock.
 */
export class RuleRegistry {
  // tenant -> ruleKey -> version -> published (idempotent upsert). Structural nesting — NOT a
  // delimited composite string key — mirrors the .NET tier's `(tenant, ruleKey)` ValueTuple key
  // exactly: no delimiter means no delimiter-collision surface (a fold of two review findings —
  // a NUL-byte delimiter that made git treat this file as binary, and a plain-space delimiter in
  // the sibling compile-cache that let `(tenant="t a", key="b")` alias `(tenant="t", key="a b")`).
  private readonly store = new Map<string, Map<string, Map<string, PublishedRuleVersion>>>()

  private static ruleVersions(
    store: Map<string, Map<string, Map<string, PublishedRuleVersion>>>,
    tenant: string,
    ruleKey: string,
  ): Map<string, PublishedRuleVersion> | undefined {
    return store.get(tenant)?.get(ruleKey)
  }

  /**
   * Publishes (idempotent upsert) a rule version. A CRDT-synced record may arrive in any order —
   * "latest" is resolved by the S-8 watermark, so a late older record simply never wins.
   */
  publish(tenant: string, published: PublishedRuleVersion): void {
    let byRuleKey = this.store.get(tenant)
    if (!byRuleKey) {
      byRuleKey = new Map<string, Map<string, PublishedRuleVersion>>()
      this.store.set(tenant, byRuleKey)
    }
    let versions = byRuleKey.get(published.ruleKey)
    if (!versions) {
      versions = new Map<string, PublishedRuleVersion>()
      byRuleKey.set(published.ruleKey, versions)
    }
    versions.set(published.version, published)
  }

  /**
   * Resolves a named rule under `policy`. A `scope` of `'production'` NEVER returns a draft
   * (board F6).
   */
  resolve(tenant: string, ruleKey: string, policy: RuleVersionPolicy, scope: RuleResolveScope): RuleResolution {
    const versions = RuleRegistry.ruleVersions(this.store, tenant, ruleKey)
    if (!versions || versions.size === 0) return notFound

    switch (policy.kind) {
      case 'pinned': {
        const pinned = policy.version !== undefined ? versions.get(policy.version) : undefined
        if (!pinned) return notFound
        // A pinned DRAFT is refused on the production path — the draft-exclusion mechanism.
        if (pinned.isDraft && scope === 'production') return draftRefused(pinned.version)
        return resolved(pinned.version, pinned.definition)
      }
      case 'draft': {
        // The draft policy is reachable ONLY through a sandbox-scoped resolve (board F6).
        if (scope === 'production') return draftRefused()
        return highest([...versions.values()].filter((v) => v.isDraft))
      }
      default: {
        // Production excludes drafts (the mechanism); sandbox sees every version.
        const pool = scope === 'production' ? [...versions.values()].filter((v) => !v.isDraft) : [...versions.values()]
        return highest(pool)
      }
    }
  }

  /**
   * Captures a D7 instance pin by resolving `policy` once and freezing the version; returns null if
   * nothing resolved (a refused/absent rule cannot be pinned).
   */
  pin(tenant: string, ruleKey: string, policy: RuleVersionPolicy, scope: RuleResolveScope): RulePin | null {
    const r = this.resolve(tenant, ruleKey, policy, scope)
    return r.status === 'Resolved' ? { tenant, ruleKey, version: r.version! } : null
  }

  /** Replays a D7 pin — resolves the exact pinned version (deterministic replay). */
  resolvePinned(pin: RulePin, scope: RuleResolveScope): RuleResolution {
    return this.resolve(pin.tenant, pin.ruleKey, RuleVersionPolicy.pinned(pin.version), scope)
  }
}

// Picks the highest version in the pool by the S-8 monotonic watermark — order-independent, so two
// offline peers holding the same set converge on the identical "latest".
function highest(pool: PublishedRuleVersion[]): RuleResolution {
  let best: PublishedRuleVersion | null = null
  for (const candidate of pool) {
    if (best === null || RuleVersion.compare(candidate.version, best.version) > 0) best = candidate
  }
  return best === null ? notFound : resolved(best.version, best.definition)
}
