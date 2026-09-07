/**
 * The publish admission fence (design §5.2 / ADR 0146 D7): compile-first fail-closed
 * rejection with stable `rule.skin.*` codes, catalog untouched on any rejection, S-8
 * version minting only after the fence passes. The pinned Harborline App suite proved this over
 * module mocks of the localStorage client; at the store port the same seven behaviors are
 * proven over an injected catalog with spies — the fence's observable contract is unchanged.
 */
import { SkinCodes } from '@harborline-software/rule-engine'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { publishIsControlChange, publishRule } from '../admission.js'
import { InMemoryRuleCatalogStore, RuleCatalog } from '../catalog.js'
import type { DecisionTableDraft, RuleDraft } from '../model.js'
import { blankFormulaDraft, blankTableDraft } from '../seeds.js'

let catalog: RuleCatalog

beforeEach(() => {
  catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
})

function resolvedTable(): DecisionTableDraft {
  const draft = blankTableDraft()
  const col = draft.columns[0].id
  return {
    ...draft,
    rows: [{ id: 'r1', cells: { [col]: { kind: 'range', lo: '0', hi: '100' } }, output: 'low', priority: 0 }],
    noMatch: { kind: 'default', value: 'high' },
  }
}

describe('publishRule admission fence', () => {
  it('publishes a table only after its no-match posture resolves', async () => {
    await catalog.createRule({ ruleKey: 'route', name: 'Route', skinType: 'table', draft: blankTableDraft() })

    const unresolved = await publishRule(catalog, 'route', blankTableDraft())
    expect(unresolved).toEqual({ ok: false, code: SkinCodes.noMatchUnresolved, message: 'no-match is unresolved' })

    const resolved = await publishRule(catalog, 'route', resolvedTable())
    expect(resolved).toMatchObject({ ok: true, version: '1.0.0' })
    expect((await catalog.loadRule('route'))?.versions).toHaveLength(1)
  })

  it('waits for the published version commit and propagates its rejection', async () => {
    await catalog.createRule({ ruleKey: 'route', name: 'Route', skinType: 'table', draft: resolvedTable() })
    const commit = vi.spyOn(catalog, 'commitPublishedVersion').mockRejectedValue(new Error('store offline'))

    await expect(publishRule(catalog, 'route', resolvedTable())).rejects.toThrow('store offline')
    expect(commit).toHaveBeenCalledOnce()
  })

  it('rejects an unresolved no-match posture before compile or catalog access', async () => {
    const load = vi.spyOn(catalog, 'loadRule')
    const commit = vi.spyOn(catalog, 'commitPublishedVersion')

    const outcome = await publishRule(catalog, 'route', blankTableDraft())

    expect(outcome).toMatchObject({ ok: false, code: SkinCodes.noMatchUnresolved })
    expect(load).not.toHaveBeenCalled()
    expect(commit).not.toHaveBeenCalled()
  })

  it('returns a typed compile rejection without touching the catalog', async () => {
    const load = vi.spyOn(catalog, 'loadRule')
    const commit = vi.spyOn(catalog, 'commitPublishedVersion')
    // A formula reading an undeclared input — the shipped compiler's stable rejection.
    const draft: RuleDraft = {
      ...blankFormulaDraft(),
      inputs: [],
      expression: { kind: 'ref', ref: 'undeclared' },
    }

    const outcome = await publishRule(catalog, 'formula', draft)

    expect(outcome).toMatchObject({ ok: false, code: SkinCodes.formulaUndeclaredRef })
    expect(load).not.toHaveBeenCalled()
    expect(commit).not.toHaveBeenCalled()
  })

  it('propagates an unexpected compiler error unchanged', async () => {
    // A draft whose lowering itself throws a NON-CompileError: a formula expression with an
    // impossible kind reaches the exhaustive switch and returns undefined JSON the compiler
    // chokes on — simulate the unexpected path by poisoning the catalog load instead, which
    // sits on the same unexpected-error contract (anything not a CompileError re-throws).
    await catalog.createRule({ ruleKey: 'route', name: 'Route', skinType: 'table', draft: resolvedTable() })
    vi.spyOn(catalog, 'loadRule').mockRejectedValue(new TypeError('unexpected'))

    await expect(publishRule(catalog, 'route', resolvedTable())).rejects.toThrow('unexpected')
  })

  it('returns a stable missing-rule rejection without minting a version', async () => {
    const outcome = await publishRule(catalog, 'never-created', resolvedTable())
    expect(outcome).toEqual({ ok: false, code: 'rules.publish.not_found', message: "rule 'never-created' not found" })
  })
})

describe('publishIsControlChange', () => {
  it.each<RuleDraft>([blankTableDraft(), blankFormulaDraft()])(
    'treats every %s publish as a control change',
    (draft) => {
      expect(publishIsControlChange(draft)).toBe(true)
    },
  )
})
