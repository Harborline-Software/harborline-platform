import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { InMemoryRuleCatalogStore, RuleCatalog, mintRuleKey, nextVersion } from '../catalog.js'
import { blankFormulaDraft } from '../seeds.js'

const INITIAL_TIME = new Date('2026-07-16T00:00:00.000Z')

let catalog: RuleCatalog

beforeEach(() => {
  catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
  vi.useFakeTimers()
  vi.setSystemTime(INITIAL_TIME)
})

afterEach(() => {
  vi.restoreAllMocks()
  vi.useRealTimers()
})

describe('rulesClient local catalog', () => {
  it('round-trips create, draft, duplicate, and archive operations through the store port', async () => {
    const initialDraft = blankFormulaDraft()
    const created = await catalog.createRule({
      ruleKey: 'invoice-total',
      name: 'Invoice total',
      skinType: 'formula',
      draft: initialDraft,
    })

    expect(created.updatedAt).toBe(INITIAL_TIME.toISOString())
    
    await expect(catalog.loadRule('invoice-total')).resolves.toEqual(created)
    await expect(catalog.listRules()).resolves.toEqual([
      expect.objectContaining({
        ruleKey: 'invoice-total',
        publishedVersion: undefined,
        draftVersion: '1.0.0',
        status: 'draft',
      }),
    ])

    const savedDraft = { ...initialDraft, scopeTarget: 'saved-total' }
    vi.setSystemTime('2026-07-16T00:01:00.000Z')
    await catalog.saveDraft('invoice-total', savedDraft)
    expect(await catalog.loadRule('invoice-total')).toMatchObject({
      draft: savedDraft,
      hasUnpublishedDraft: true,
      updatedAt: '2026-07-16T00:01:00.000Z',
    })

    await catalog.duplicateRule('invoice-total', 'invoice-total-copy', 'Invoice total copy')
    await catalog.setArchived('invoice-total-copy', true)
    expect(await catalog.loadRule('invoice-total-copy')).toMatchObject({
      name: 'Invoice total copy',
      draft: savedDraft,
      archived: true,
    })
  })

  // The pinned 'degrades safely when browser storage is unavailable' case is the
  // localStorage interim's own failure semantics — replaced at the store port (the
  // ledger's replaced-at-new-seam rulesClient rows); the port has no browser storage.

  it('rejects a published-version downgrade without changing stored history', async () => {
    const draft = blankFormulaDraft()
    await catalog.createRule({
      ruleKey: 'watermarked',
      name: 'Watermarked',
      skinType: 'formula',
      draft,
    })
    await catalog.commitPublishedVersion('watermarked', '2.4.9', draft)
    await catalog.commitPublishedVersion('watermarked', '2.5.0', draft)

    await expect(catalog.commitPublishedVersion('watermarked', '2.4.10', draft)).rejects.toThrow(
      'rule publish refused: 2.4.10 downgrades 2.5.0',
    )

    const stored = await catalog.loadRule('watermarked')
    expect(stored?.versions.map(({ version }) => version)).toEqual(['2.4.9', '2.5.0'])
    expect(stored && nextVersion(stored)).toBe('2.5.1')
  })

  it('preserves the documented null, no-op, and error paths', async () => {
    const draft = blankFormulaDraft()
    await expect(catalog.loadRule('missing')).resolves.toBeNull()
    await expect(catalog.setArchived('missing', true)).resolves.toBeUndefined()
    await expect(catalog.saveDraft('missing', draft)).rejects.toThrow("rule save failed: 'missing' not found")
    await expect(catalog.commitPublishedVersion('missing', '1.0.0', draft)).rejects.toThrow(
      "rule publish failed: 'missing' not found",
    )
    await expect(catalog.duplicateRule('missing', 'copy', 'Copy')).rejects.toThrow(
      "rule duplicate failed: 'missing' not found",
    )

    await catalog.createRule({ ruleKey: 'existing', name: 'Existing', skinType: 'formula', draft })
    await expect(catalog.createRule({
      ruleKey: 'existing',
      name: 'Replacement',
      skinType: 'formula',
      draft,
    })).rejects.toThrow("rule create failed: key 'existing' already exists")
  })
})
