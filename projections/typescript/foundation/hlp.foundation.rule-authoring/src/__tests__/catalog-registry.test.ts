/**
 * Registry-flow + publish-fence proof (design R1/R5 gates): create → save draft → publish through the
 * admission fence → version mint (S-8 monotonic) → duplicate → archive. The publish fence rejects an
 * unresolved decision table with the stable `rule.skin.no_match_unresolved` code (never a silent
 * commit).
 */
import { beforeEach, describe, it, expect } from 'vitest'
import { SkinCodes } from '@harborline-software/rule-engine'

import { InMemoryRuleCatalogStore, RuleCatalog, mintRuleKey, nextVersion } from '../catalog.js'
import { publishRule as publishThroughFence } from '../admission.js'
import { blankFormulaDraft, blankTableDraft } from '../seeds.js'
import type { DecisionTableDraft } from '../model.js'

let catalog: RuleCatalog

beforeEach(() => {
  catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
})

function resolvedTable(): DecisionTableDraft {
  const d = blankTableDraft()
  const col = d.columns[0].id
  return {
    ...d,
    rows: [{ id: 'r1', cells: { [col]: { kind: 'range', lo: '0', hi: '100' } }, output: 'low', priority: 0 }],
    noMatch: { kind: 'default', value: 'high' },
  }
}

describe('rulesClient registry flows', () => {
  it('creates, lists, and opens a named rule (draft status)', async () => {
    await catalog.createRule({ ruleKey: 'invoice-route', name: 'Invoice route', skinType: 'table', draft: blankTableDraft() })
    const list = await catalog.listRules()
    expect(list.map((r) => r.ruleKey)).toContain('invoice-route')
    expect(list.find((r) => r.ruleKey === 'invoice-route')?.status).toBe('draft')
    expect(await catalog.loadRule('invoice-route')).not.toBeNull()
  })

  it('refuses a duplicate key (never clobbers)', async () => {
    await catalog.createRule({ ruleKey: 'k', name: 'K', skinType: 'formula', draft: blankFormulaDraft() })
    await expect(catalog.createRule({ ruleKey: 'k', name: 'K2', skinType: 'formula', draft: blankFormulaDraft() })).rejects.toThrow()
  })

  it('publishes a resolved table through the admission fence and mints v1.0.0 → v1.0.1', async () => {
    await catalog.createRule({ ruleKey: 'route', name: 'Route', skinType: 'table', draft: resolvedTable() })
    const first = await publishThroughFence(catalog, 'route', resolvedTable())
    expect(first.ok && first.version).toBe('1.0.0')
    let summary = (await catalog.listRules()).find((r) => r.ruleKey === 'route')!
    expect(summary.status).toBe('published')
    expect(summary.publishedVersion).toBe('1.0.0')

    // an edit → new draft → publish mints the next patch version
    await catalog.saveDraft('route', resolvedTable())
    const second = await publishThroughFence(catalog, 'route', resolvedTable())
    expect(second.ok && second.version).toBe('1.0.1')
    summary = (await catalog.listRules()).find((r) => r.ruleKey === 'route')!
    expect(summary.publishedVersion).toBe('1.0.1')
  })

  it('publish fence REJECTS an unresolved no-match table (no silent commit)', async () => {
    const unresolved = { ...resolvedTable(), noMatch: { kind: 'default' as const, value: '' } }
    await catalog.createRule({ ruleKey: 'bad', name: 'Bad', skinType: 'table', draft: unresolved })
    const outcome = await publishThroughFence(catalog, 'bad', unresolved)
    expect(outcome.ok).toBe(false)
    expect(!outcome.ok && outcome.code).toBe(SkinCodes.noMatchUnresolved)
    // nothing was committed
    expect((await catalog.loadRule('bad'))!.versions).toHaveLength(0)
  })

  it('duplicates and archives', async () => {
    await catalog.createRule({ ruleKey: 'orig', name: 'Orig', skinType: 'table', draft: resolvedTable() })
    await catalog.duplicateRule('orig', 'orig-copy', 'Orig copy')
    expect((await catalog.listRules()).map((r) => r.ruleKey)).toContain('orig-copy')
    await catalog.setArchived('orig-copy', true)
    expect((await catalog.listRules()).find((r) => r.ruleKey === 'orig-copy')?.archived).toBe(true)
  })
})
