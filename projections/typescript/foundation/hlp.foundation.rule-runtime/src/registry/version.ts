/**
 * The per-rule-key monotonic-version comparator (ADR 0146 D5) — the TS mirror of the .NET
 * `RuleVersion`. Reuses the shipped S-8 monotonic-watermark SEMANTICS of foundation-packs'
 * `PackVersion`: parse `major.minor.patch` (extra numeric segments compared in order; a pre-release
 * suffix after '-' orders BEFORE the same core per SemVer §11; an unparseable version is the lowest
 * possible, fail-closed). NOT a new versioning primitive (D5: compose shipped patterns) — the pack
 * watermark applied per rule key. Kept byte-compatible with the .NET tier.
 *
 * `foundation-versioning.VersionVector` is deliberately NOT used — it is a federation handshake
 * check, not a last-writer-wins watermark (a false friend, per the substrate survey).
 */

function split(version: string): { core: number[]; pre: string } {
  if (!version || version.trim().length === 0) return { core: [0], pre: '' }
  const plus = version.indexOf('+')
  const trimmed = plus >= 0 ? version.slice(0, plus) : version
  const dash = trimmed.indexOf('-')
  const core = dash >= 0 ? trimmed.slice(0, dash) : trimmed
  const pre = dash >= 0 ? trimmed.slice(dash + 1) : ''
  const segments = core.split('.')
  const parsed = segments.map((s) => {
    // Match .NET int.TryParse(NumberStyles.None): digits only AND within Int32 range, else 0
    // (fail-closed). A segment > int.MaxValue (2147483647) fails .NET's parse and falls back to
    // the lowest value; the naive `Number.parseInt` here would otherwise fail OPEN (accept the
    // huge number as a real, higher segment) — clamp to match.
    if (!/^[0-9]+$/.test(s)) return 0
    const n = Number.parseInt(s, 10)
    return n > 2147483647 ? 0 : n
  })
  return { core: parsed, pre }
}

export const RuleVersion = {
  /**
   * Compares two rule versions. Returns <0 if `a` precedes `b`, 0 if equal, >0 if `a` follows `b`.
   * A total, deterministic order — two offline peers holding the same published set resolve the
   * identical "latest" regardless of sync order (monotonically convergent).
   */
  compare(a: string, b: string): number {
    const { core: coreA, pre: preA } = split(a)
    const { core: coreB, pre: preB } = split(b)
    const max = Math.max(coreA.length, coreB.length)
    for (let i = 0; i < max; i++) {
      const ai = i < coreA.length ? coreA[i] : 0
      const bi = i < coreB.length ? coreB[i] : 0
      if (ai !== bi) return ai < bi ? -1 : 1
    }
    const hasPreA = preA.length > 0
    const hasPreB = preB.length > 0
    if (hasPreA === hasPreB) {
      // Ordinal string compare (mirrors .NET string.CompareOrdinal).
      return preA < preB ? -1 : preA > preB ? 1 : 0
    }
    return hasPreA ? -1 : 1
  },

  /** True iff `candidate` is strictly below `watermark` (a downgrade, refused by default under S-8). */
  isDowngrade(watermark: string, candidate: string): boolean {
    return RuleVersion.compare(candidate, watermark) < 0
  },
}
