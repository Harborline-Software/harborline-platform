// Calculations capability vertical — the RENDERER lane. Installs ONLY the packed
// @harborline-software/rule-authoring and @harborline-software/rule-engine artifacts and drives
// the 12-case authoring-verdict corpus through the packaged bridge: author -> lint ->
// publish fence -> compile -> evaluate -> trace, writing its verdicts for the engine lane
// to cross-check case by case.
import { readFileSync, writeFileSync } from 'node:fs'

import {
  InMemoryRuleCatalogStore,
  RuleCatalog,
  evaluatePreview,
  isCompileError,
  lintTable,
  nextVersion,
  noMatchResolved,
  publishRule,
  RuleLintCodes,
} from '@harborline-software/rule-authoring'
import { compile } from '@harborline-software/rule-engine'

const corpus = JSON.parse(readFileSync(new URL('./authoring-verdict-cases.json', import.meta.url), 'utf8'))

function draftOf(row) {
  const base = row.draft ?? structuredClone(corpus.cases.find(c => c.id === row.draftRef).draft)
  return row.draftOverrides ? { ...base, ...row.draftOverrides } : base
}

async function verdictOf(row) {
  switch (row.op) {
    case 'preview': {
      const draft = draftOf(row)
      const r = evaluatePreview(draft, row.ruleId, row.sample)
      return {
        id: row.id,
        op: row.op,
        value: r.value,
        firedRowId: draft.skin === 'table' ? r.firedRowId : null,
        traceCodes: [...new Set(r.trace.map(entry => entry.code))].sort(),
      }
    }
    case 'publish-refusal': {
      const draft = draftOf(row)
      const catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
      await catalog.createRule({ ruleKey: row.ruleKey, name: row.ruleKey, skinType: draft.skin, draft })
      // The fence's surface gate and the advisory lint must agree on the blank-Otherwise case.
      if (draft.skin === 'table' && !noMatchResolved(draft)
          && !lintTable(draft).some(f => f.code === RuleLintCodes.noMatchUnresolved)) {
        throw new Error(`lint and fence disagree on no-match for ${row.id}`)
      }
      const outcome = await publishRule(catalog, row.ruleKey, draft)
      const stored = await catalog.loadRule(row.ruleKey)
      if (stored.versions.length !== 0) throw new Error(`refused publish still committed a version for ${row.id}`)
      return { id: row.id, op: row.op, ok: outcome.ok, code: outcome.ok ? null : outcome.code }
    }
    case 'publish-mint': {
      const draft = draftOf(row)
      const catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
      await catalog.createRule({ ruleKey: row.ruleKey, name: row.ruleKey, skinType: draft.skin, draft })
      const first = await publishRule(catalog, row.ruleKey, draft)
      await catalog.saveDraft(row.ruleKey, draft)
      const second = await publishRule(catalog, row.ruleKey, draft)
      if (!first.ok || !second.ok) throw new Error(`monotonic mint publish failed for ${row.id}`)
      const stored = await catalog.loadRule(row.ruleKey)
      return { id: row.id, op: row.op, versions: stored.versions.map(v => v.version) }
    }
    case 'downgrade': {
      const draft = draftOf(row)
      const catalog = new RuleCatalog(new InMemoryRuleCatalogStore())
      await catalog.createRule({ ruleKey: row.ruleKey, name: row.ruleKey, skinType: draft.skin, draft })
      for (const version of row.seedVersions) await catalog.commitPublishedVersion(row.ruleKey, version, draft)
      let refused = false
      try {
        await catalog.commitPublishedVersion(row.ruleKey, row.downgrade, draft)
      } catch {
        refused = true
      }
      const stored = await catalog.loadRule(row.ruleKey)
      return {
        id: row.id,
        op: row.op,
        refused,
        versions: stored.versions.map(v => v.version),
        nextVersion: nextVersion(stored),
      }
    }
    case 'compile-cycle': {
      try {
        compile(row.rules)
        return { id: row.id, op: row.op, code: null }
      } catch (error) {
        if (!isCompileError(error)) throw error
        return { id: row.id, op: row.op, code: error.code }
      }
    }
    default:
      throw new Error(`unknown corpus op: ${row.op}`)
  }
}

const verdicts = []
for (const row of corpus.cases) verdicts.push(await verdictOf(row))

let mismatches = 0
for (const [index, verdict] of verdicts.entries()) {
  const expected = corpus.cases[index].expected
  for (const [key, value] of Object.entries(expected)) {
    if (JSON.stringify(verdict[key] ?? null) !== JSON.stringify(value)) mismatches++
  }
}
if (mismatches > 0) {
  throw new Error(`packed authoring bridge disagreed with the pinned corpus on ${mismatches} field(s): ${JSON.stringify(verdicts)}`)
}

writeFileSync(new URL('./client-verdicts.json', import.meta.url), JSON.stringify(verdicts, null, 2))
console.log('CALCULATIONS_CLIENT_PASS:' + JSON.stringify({
  cases: verdicts.length,
  previews: verdicts.filter(v => v.op === 'preview').length,
  refusals: verdicts.filter(v => v.op === 'publish-refusal' || v.op === 'downgrade' || v.op === 'compile-cycle').length,
}))
