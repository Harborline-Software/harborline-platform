/**
 * The per-node compiled-AST cache with the HomeEpochFence read-tip-reject-stale discipline
 * (ADR 0146 D5) — the TS mirror of the .NET `RuleCompileCache`. A compile/cache swap for a rule key
 * is MONOTONIC: reading the currently cached tip version, a publish whose version is a downgrade of
 * the cached tip is REFUSED — so a stale publish arriving late can never overwrite a newer,
 * already-compiled AST. This mirrors the shipped `HomeEpochFence` (read-the-tip, reject-if-stale)
 * rather than inventing a new primitive (D5). The read-compare-swap is synchronous — atomic under
 * the single-threaded JS event loop (the .NET tier holds a lock for the same read-through-write
 * atomicity). Cached in isolation for Wave 1: no production node drives the swap from a live sync
 * feed yet (Wave-2 cascade, D8).
 */
import type { CompiledGraph } from '../compiler.js'
import { RuleVersion } from './version.js'

/** Thrown when a swap would install a STALE (downgrade) AST over a newer one cached for the key. */
export class StaleRulePublishError extends Error {
  constructor(
    readonly cacheKey: string,
    readonly staleVersion: string,
    readonly currentVersion: string,
  ) {
    super(`rule compile cache '${cacheKey}': refused a stale publish (version '${staleVersion}' is below the cached tip '${currentVersion}')`)
    this.name = 'StaleRulePublishError'
  }
}

interface CachedGraph {
  readonly version: string
  readonly graph: CompiledGraph
}

export class RuleCompileCache {
  // tenant -> ruleKey -> cached graph. Structural nesting — NOT a delimited composite string key
  // — mirrors the .NET tier's `(tenant, ruleKey)` ValueTuple key: a plain-space (or any) string
  // delimiter here is a cross-(tenant,key) collision risk (`(tenant="t a", key="b")` would alias
  // `(tenant="t", key="a b")`); nesting removes the delimiter entirely. Folded together with the
  // sibling registry.ts fix (a NUL-byte delimiter there made git treat that file as binary) — one
  // consistent collision-safe scheme across both files.
  private readonly cache = new Map<string, Map<string, CachedGraph>>()

  /** Reads the cached compiled AST for `(tenant, ruleKey)`, or null. */
  tryGet(tenant: string, ruleKey: string): CachedGraph | null {
    return this.cache.get(tenant)?.get(ruleKey) ?? null
  }

  /**
   * Installs a freshly-compiled AST for `version`, MONOTONICALLY. A version that is a downgrade of
   * the cached tip is a stale publish and is REFUSED ({@link StaleRulePublishError}); the newer
   * cached AST stands. An equal or higher version installs (equal re-installs are idempotent).
   */
  swap(tenant: string, ruleKey: string, version: string, graph: CompiledGraph): CompiledGraph {
    let byRuleKey = this.cache.get(tenant)
    if (!byRuleKey) {
      byRuleKey = new Map<string, CachedGraph>()
      this.cache.set(tenant, byRuleKey)
    }
    const cur = byRuleKey.get(ruleKey)
    if (cur && RuleVersion.isDowngrade(cur.version, version)) {
      throw new StaleRulePublishError(`${tenant}/${ruleKey}`, version, cur.version)
    }
    byRuleKey.set(ruleKey, { version, graph })
    return graph
  }
}
