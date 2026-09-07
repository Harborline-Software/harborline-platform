/**
 * Cross-tier operator-catalog AGREEMENT guard — the meta-guard over the two
 * per-tier operator-catalog guards (SCOUT #116 §2.1, the highest-value missing
 * guard in the fleet's structural-guard registry).
 *
 * The .NET tier (`OperatorCatalogArchTests.cs`, `FrozenV1Operators`) and the TS
 * tier (`operator-catalog.test.ts`, `FROZEN_V1_OPERATORS`) each pin the CLOSED
 * `harborline-jsonlogic/v1` operator set as an INDEPENDENT hand-maintained literal,
 * with doc comments saying "the other tier pins the IDENTICAL set / must match".
 * Each tier verifies its OWN evaluator source against its OWN literal — but until
 * now NO test diffed the two literals against each other. That is exactly the
 * two-copies-of-one-fact drift shape that already bit `mutationVerbs.ts`
 * (PR #1827 F1) and was fixed there by extraction to a shared constant. Cross-
 * language, extraction to a single shared source is heavier (a C# HashSet + a TS
 * Set both parsing one data file, touching two battle-tested guards), so this
 * closes the gap the surgical way the scout names: a meta-guard that reads BOTH
 * frozen literals from source and asserts set-equality. A silent edit to one
 * tier's set that isn't mirrored in the other now goes RED.
 *
 * NB: the conformance corpus (rule-engine-conformance.yml) proves BEHAVIORAL
 * agreement on the fixture cases; this proves the two FROZEN SETS themselves
 * agree — it catches an operator added to one tier's `case`/`switch` + its frozen
 * literal with no matching corpus row (which the corpus alone would miss).
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const here = dirname(fileURLToPath(import.meta.url))
// this file:  packages/rule-engine/src/__tests__/operator-catalog-crosstier.test.ts
const TS_CATALOG = join(here, 'operator-catalog.test.ts')
const DOTNET_CATALOG = join(
  here,
  '..',
  '..',
  '..',
  '..',
  '..',
  '..',
  'projections',
  'dotnet',
  'foundation',
  'hlp.foundation.rule-runtime.tests',
  'OperatorCatalogArchTests.cs',
)

/**
 * Extract the quoted string members of a named initializer block. Scoped to the
 * block between `open` and the first `close` after the `anchor` so unrelated
 * quoted strings elsewhere in the file (error messages, doc comments) can't leak
 * in.
 */
function extractFrozenSet(
  source: string,
  anchor: string,
  open: string,
  close: string,
  quote: "'" | '"',
): Set<string> {
  const at = source.indexOf(anchor)
  if (at < 0) throw new Error(`operator-catalog anchor not found: ${anchor}`)
  const openAt = source.indexOf(open, at)
  const closeAt = openAt >= 0 ? source.indexOf(close, openAt + open.length) : -1
  if (openAt < 0 || closeAt < 0) {
    throw new Error(`operator-catalog block delimiters not found for: ${anchor}`)
  }
  const body = source.slice(openAt + open.length, closeAt)
  const re =
    quote === "'" ? /'((?:[^'\\]|\\.)*)'/g : /"([^"\\]+)"/g
  return new Set([...body.matchAll(re)].map((m) => m[1]))
}

describe('operator-catalog cross-tier agreement (SCOUT #116 §2.1 meta-guard)', () => {
  it('the .NET and TS frozen operator sets are identical', () => {
    const tsSrc = readFileSync(TS_CATALOG, 'utf8')
    const dotnetSrc = readFileSync(DOTNET_CATALOG, 'utf8')

    const tsSet = extractFrozenSet(tsSrc, 'FROZEN_V1_OPERATORS', 'new Set([', '])', "'")
    const dotnetSet = extractFrozenSet(
      dotnetSrc,
      'FrozenV1Operators = new HashSet',
      '{',
      '};',
      '"',
    )

    // Neither extraction went empty — a structure change that broke extraction
    // would otherwise make two empty sets falsely "agree".
    expect(
      tsSet.size,
      'extracted no operators from the TS FROZEN_V1_OPERATORS literal — the Set shape changed',
    ).toBeGreaterThan(20)
    expect(
      dotnetSet.size,
      'extracted no operators from the .NET FrozenV1Operators literal — the HashSet shape changed',
    ).toBeGreaterThan(20)

    const onlyTs = [...tsSet].filter((op) => !dotnetSet.has(op)).sort()
    const onlyDotnet = [...dotnetSet].filter((op) => !tsSet.has(op)).sort()

    expect(
      onlyTs,
      `operator(s) in the TS frozen set but NOT the .NET set: [${onlyTs.join(', ')}]. ` +
        'The two tiers’ harborline-jsonlogic/v1 sets have DRIFTED — amend BOTH ' +
        'FROZEN_V1_OPERATORS (TS) and FrozenV1Operators (.NET) together (SCOUT #116 §2.1).',
    ).toEqual([])
    expect(
      onlyDotnet,
      `operator(s) in the .NET frozen set but NOT the TS set: [${onlyDotnet.join(', ')}]. ` +
        'The two tiers’ harborline-jsonlogic/v1 sets have DRIFTED — amend BOTH ' +
        'FrozenV1Operators (.NET) and FROZEN_V1_OPERATORS (TS) together (SCOUT #116 §2.1).',
    ).toEqual([])
  })
})
