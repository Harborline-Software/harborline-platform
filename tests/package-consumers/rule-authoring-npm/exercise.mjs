import assert from 'node:assert/strict'
import {
  serializeRuleDefinition,
  validateRuleDefinitionJson,
} from '@harborline-software/rule-authoring'

// The browser package admits source. Only the shared store may commit a publication.
const source = {
  envelope: {
    id: 'route', version: '1.0.0', tenant: 'tenant-a',
    cascadeLayer: 'domain-package', provenance: { id: 'finance' }, requires: [],
  },
  name: 'Route',
  tier: 'JsonLogic',
  draft: {
    kind: 'Table', scope: 'Field', scopeTarget: 'route', outputType: 'Compute',
    hitPolicy: 'FirstMatch',
    columns: [{ id: 'amount', input: 'field.amount', valueType: 'Number' }],
    rows: [{ id: 'r1', cells: { amount: { kind: 'Range', lo: '0', hi: '100' } }, output: 'low', priority: 0 }],
    noMatch: { kind: 'Default', value: '' },
  },
}

for (const phase of ['Author', 'Publish', 'Persisted']) {
  const refused = validateRuleDefinitionJson(JSON.stringify(source), phase)
  assert.equal(refused.document, null)
  assert.equal(refused.diagnostics.length, 1)
  assert.equal(refused.diagnostics[0].code, 'rule.skin.no_match_unresolved')
  assert.equal(refused.diagnostics[0].location, '/draft/noMatch')
  assert.equal(refused.diagnostics[0].phase, phase)
}

source.draft.noMatch.value = 'high'
const admitted = validateRuleDefinitionJson(JSON.stringify(source), 'Publish')
assert.deepEqual(admitted.diagnostics, [])
assert.notEqual(admitted.document, null)
const canonical = serializeRuleDefinition(admitted.document)
assert.deepEqual(JSON.parse(canonical), source)
const persisted = validateRuleDefinitionJson(canonical, 'Persisted')
assert.deepEqual(persisted.diagnostics, [])
assert.equal(serializeRuleDefinition(persisted.document), canonical)

const malformed = structuredClone(source)
malformed.draft.rows[0].cells.amount.hi = 'not-a-number'
const rejectedEndpoint = validateRuleDefinitionJson(JSON.stringify(malformed), 'Publish')
assert.equal(rejectedEndpoint.document, null)
assert.equal(rejectedEndpoint.diagnostics.length, 1)
assert.equal(rejectedEndpoint.diagnostics[0].code, 'rule.skin.decision_table_bad_cell')
assert.equal(rejectedEndpoint.diagnostics[0].location, '/draft/rows/0/cells/amount/hi')

process.stdout.write('packed npm Rules intent refused invalid source and round-tripped admitted source; no local publication store\n')
