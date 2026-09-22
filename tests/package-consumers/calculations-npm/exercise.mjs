// Calculations capability vertical — the RENDERER lane. Installs ONLY the packed
// @harborline-software/rule-authoring and @harborline-software/rule-engine artifacts and drives
// the 12-case authoring-verdict corpus through the packaged bridge: author -> lint ->
// typed intent -> compile -> evaluate -> trace, writing its verdicts for the engine lane
// to cross-check case by case.
import { readFileSync, writeFileSync } from 'node:fs'
import assert from 'node:assert/strict'

import {
  evaluatePreview,
  isCompileError,
  validateRuleDefinitionJson,
  serializeRuleDefinition,
} from '@harborline-software/rule-authoring'
import { compile } from '@harborline-software/rule-engine'

const corpus = JSON.parse(readFileSync(new URL('./authoring-verdict-cases.json', import.meta.url), 'utf8'))
const intentCorpus = JSON.parse(readFileSync(new URL('./definition-intent-cases.json', import.meta.url), 'utf8'))
for (const row of intentCorpus.cases) {
  for (const phase of ['Author', 'Publish', 'Persisted']) {
    const result = validateRuleDefinitionJson(row.sourceJson, phase)
    assert.equal(result.document !== null, row.expected.valid, `${row.id}/${phase}`)
    if (row.expected.valid) {
      assert.deepEqual(result.diagnostics, [])
      const canonical = serializeRuleDefinition(result.document)
      assert.deepEqual(JSON.parse(canonical), JSON.parse(row.sourceJson))
      if (row.expected.canonicalJson) assert.equal(canonical, row.expected.canonicalJson)
    } else {
      assert.equal(result.diagnostics.length, 1)
      assert.equal(result.diagnostics[0].code, row.expected.code)
      assert.equal(result.diagnostics[0].location, row.expected.location)
      assert.equal(result.diagnostics[0].phase, phase)
    }
  }
}

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
    case 'definition-intent': {
      const result = validateRuleDefinitionJson(row.sourceJson, 'Publish')
      return { id: row.id, op: row.op, ok: result.document !== null, code: result.diagnostics[0]?.code ?? null }
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
  refusals: verdicts.filter(v => v.ok === false || v.op === 'compile-cycle').length,
}))
