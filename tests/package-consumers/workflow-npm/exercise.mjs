// Workflows capability vertical — the RENDERER lane. Restores ONLY the packed
// @harborline-software/contracts artifact and runs the 13-case application-derived admission
// corpus through the packaged client admission mirror (validateWorkflowAdmission), writing
// its verdicts for the engine lane to cross-check pair by pair.
import { readFileSync, writeFileSync } from 'node:fs'

import assert from 'node:assert/strict'

import { readWorkflowWire, validateWorkflowAdmission, createWorkflowAuthorityResolver } from '@harborline-software/contracts'

const corpus = JSON.parse(readFileSync(new URL('./admission-mirror-cases.json', import.meta.url), 'utf8'))
const resolver = createWorkflowAuthorityResolver(corpus.registry)

const verdicts = corpus.cases.map(row => {
  const result = validateWorkflowAdmission(row.definition, resolver)
  return {
    id: row.id,
    isValid: result.isValid,
    codes: [...new Set(result.violations.map(violation => violation.code))].sort(),
  }
})

let mismatches = 0
for (const [index, verdict] of verdicts.entries()) {
  const expected = corpus.cases[index].expected
  if (verdict.isValid !== expected.isValid) mismatches++
  else if (!expected.isValid && expected.code && !verdict.codes.includes(expected.code)) mismatches++
}
if (mismatches > 0) {
  throw new Error(`packed client admission mirror disagreed with the pinned ledger on ${mismatches} case(s): ${JSON.stringify(verdicts)}`)
}

// T-488: the envelope, sealed role and narrowing standing reach the packed client mirror.
const window = { major: 1, minor: 0, oldestMajor: 1 }
const gated = {
  key: 'consumer', version: '1.0.0', status: 'Draft', tenant: 'acme', owner: { scheme: 'system', value: 'harborline' },
  title: { defaultLocale: 'en', values: { en: 'Consumer' } }, mutability: 'Locked', initialState: 'Open',
  states: [
    { id: 'Open', label: { defaultLocale: 'en', values: { en: 'Open' } }, kind: 'Normal' },
    { id: 'Closed', label: { defaultLocale: 'en', values: { en: 'Closed' } }, kind: 'Terminal' },
  ],
  transitions: [{ id: 'close', from: 'Open', on: 'approve', to: 'Closed', requiredRoles: [{ vocabulary: 'sys.roles', name: 'Approver' }], requiredStandings: [{ name: 'assigned-approver' }] }],
  triggers: [{ id: 'approve', kind: 'HumanAction', task: 'approval' }], actions: [], guards: [],
  envelope: {
    identity: 'consumer', version: '1.0.0', tenant: 'acme', cascadeLayer: 'TenantConfiguration', provenance: { source: 'consumer' },
    retentionClass: 'standard', legalHold: false, requires: [], contract: { major: 1, minor: 0 },
  },
  createdAt: '2026-11-07T00:00:00Z', updatedAt: '2026-11-07T00:00:00Z',
}
const emptyAuthority = createWorkflowAuthorityResolver({})
const admit = definition => validateWorkflowAdmission(readWorkflowWire('WorkflowDefinition', definition), emptyAuthority, undefined, window)
assert.deepEqual(readWorkflowWire('WorkflowDefinition', gated), gated)
assert.equal(admit(gated).isValid, true)
assert.ok(admit({ ...gated, envelope: { ...gated.envelope, contract: { major: 2, minor: 0 } } }).violations.some(violation => violation.code === 'definition.contract.out_of_window'))
assert.ok(admit({ ...gated, transitions: [{ ...gated.transitions[0], requiredRoles: [{ vocabulary: 'sys.roles', name: 'Owner' }] }] }).violations.some(violation => violation.code === 'workflow.admission.unknown_role_reference'))
assert.ok(admit({ ...gated, transitions: [{ ...gated.transitions[0], requiredStandings: [{ name: '*' }] }] }).violations.some(violation => violation.code === 'workflow.admission.invalid_standing'))

writeFileSync(new URL('./client-verdicts.json', import.meta.url), JSON.stringify(verdicts, null, 2))
console.log('WORKFLOW_CLIENT_PASS:' + JSON.stringify({ cases: verdicts.length, admitted: verdicts.filter(v => v.isValid).length, refused: verdicts.filter(v => !v.isValid).length }))

// T-488/state-declarations: the published wire retains state data and the Notify action's definition.
const stateWithDeclarations = { ...gated.states[0], fieldRequirements: [{ field: 'invoice.amount', condition: 'required.amount' }], locks: 'dates only', escalation: { schedule: 'review-overdue' } }
assert.deepEqual(readWorkflowWire('WorkflowState', stateWithDeclarations), stateWithDeclarations)
const notifyAction = { id: 'notify', on: { transition: 'close' }, kind: 'Notify', capabilityRef: 'notify.email', classification: 'AP', notification: { eventKey: 'invoice.issued', batchingWindow: 'PT10M', tier: 'observation' } }
assert.deepEqual(readWorkflowWire('WorkflowActionBinding', notifyAction), notifyAction)
assert.throws(() => readWorkflowWire('WorkflowState', { ...stateWithDeclarations, notification: {} }), /workflow.notification_on_state/)
