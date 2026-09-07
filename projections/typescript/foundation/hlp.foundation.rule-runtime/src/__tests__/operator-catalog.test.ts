/**
 * D1 ratification fix 3 (2026-07-01): the operator-catalog erosion guard — TS tier.
 *
 * A STANDING arch-test pinning the CLOSED `harborline-jsonlogic/v1` operator set + the regex-exclusion
 * invariant, so a new operator (or a regex/pattern construct) cannot slip into the evaluator at a
 * version bump without tripping this test — the "one convenience operator erodes A into B, one PR at
 * a time" (Form.io) trajectory the council named. It source-scans `jsonlogic.ts` so it is
 * author-independent. The .NET tier pins the IDENTICAL set in OperatorCatalogArchTests.cs; the two
 * frozen sets below must match, which (with each tier verifying its own source) guarantees both tiers
 * implement the same closed set.
 */
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

// The frozen, closed harborline-jsonlogic/v1 operator set (design §1.4 / README). DELIBERATELY EXCLUDED:
// any regex/pattern operator (Decision DG); map/filter/reduce/merge (deferred — folds use agg).
const FROZEN_V1_OPERATORS: ReadonlySet<string> = new Set([
  'var', 'missing', 'missing_some',
  '==', '!=', '===', '!==', '!', '!!', 'and', 'or', 'if',
  '>', '>=', '<', '<=',
  '+', '-', '*', '/', '%', 'min', 'max',
  'in', 'cat',
  'agg', 'money.add', 'money.sub', 'money.mul',
  'date.add', 'date.diff', 'date.today', 'coding.is',
])

const here = dirname(fileURLToPath(import.meta.url))
const evaluatorSrc = readFileSync(join(here, '..', 'jsonlogic.ts'), 'utf8')

describe('operator-catalog erosion guard (D1 fix 3 — TS tier)', () => {
  it('the evaluator implements EXACTLY the frozen v1 operator set', () => {
    // Each operator arm is `case '<op>':`. Capture the single-quoted labels (the `default:` has none).
    const implemented = new Set(
      [...evaluatorSrc.matchAll(/case\s+'((?:[^'\\]|\\.)*)':/g)].map((m) => m[1]),
    )
    expect(implemented.size, 'extracted no `case` arms — the scan regex or switch shape changed').toBeGreaterThan(0)

    const added = [...implemented].filter((op) => !FROZEN_V1_OPERATORS.has(op)).sort()
    const removed = [...FROZEN_V1_OPERATORS].filter((op) => !implemented.has(op)).sort()

    expect(added, `NEW operator(s) added without deliberate review: [${added.join(', ')}]. The harborline-jsonlogic/v1 set is CLOSED (D1) — if intended, amend FROZEN_V1_OPERATORS + the .NET arch-test + the README + design §1.4.`).toEqual([])
    expect(removed, `operator(s) removed from the evaluator: [${removed.join(', ')}]. Removing an operator breaks the closed v1 set — amend FROZEN_V1_OPERATORS deliberately.`).toEqual([])
  })

  it('the evaluator uses NO regex construct (ReDoS structurally absent — Decision DG)', () => {
    // Scoped to jsonlogic.ts (the operator dispatcher). NB: money-decimal.ts legitimately uses a `/0+$/`
    // trailing-zero trim on its OWN canonical output — that is internal formatting, not a user-pattern
    // operator, so it is deliberately out of scope here.
    for (const banned of ['RegExp', '.test(', '.exec(', '.match(', '.matchAll(', '.search(']) {
      expect(
        evaluatorSrc.includes(banned),
        `the Tier-2 evaluator must not use '${banned}': regex/pattern is excluded from v1 (Decision DG) so the ReDoS class is structurally absent. Pattern validation stays in Tier-1 JSON-Schema.`,
      ).toBe(false)
    }
  })

  it('no frozen operator names a regex/pattern construct', () => {
    for (const op of FROZEN_V1_OPERATORS) {
      for (const banned of ['regex', 'pattern', 'match', 'search']) {
        expect(op.toLowerCase().includes(banned), `operator '${op}' names a regex/pattern construct — excluded from v1 (Decision DG).`).toBe(false)
      }
    }
  })
})
