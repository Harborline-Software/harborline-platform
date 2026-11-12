import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import * as contracts from '../dist/index.js'

const cases = JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.copilot/delegation-cases.json', import.meta.url), 'utf8'))

for (const envelope of cases.envelopes) {
  test(`pilot-ck-15 pilot-ck-18: ${envelope.name}; oracle: RFC 8785 fixture bytes and independent shasum literal`, async () => {
    assert.equal(contracts.serializeProposalCanonical(envelope.value), envelope.canonical)
    assert.equal(await contracts.proposalDigest(envelope.value), envelope.digest)
  })
}

test('pilot-ck-17 pilot-ck-18 pilot-ck-19 pilot-ck-21: delegation round trips; oracle: DES-0026 fixture, ADR-0090/0091 and L609/L1645', () => {
  for (const record of cases.delegations) {
    assert.deepEqual(contracts.parseDelegationRecord(JSON.parse(JSON.stringify(record))), record)
    if (record.actor !== null) {
      assert.deepEqual(Object.keys(contracts.parseDelegationRecord(record).actor).sort(),
        ['binding_key', 'binding_version', 'model_version', 'proposal_digest', 'provider_id'])
    }
  }
  assert.deepEqual(cases.delegations.map(record => record.disposition.kind), ['approve', 'dismiss', 'clarify', 'reject', 'standing', 'approve'])
  assert.equal(contracts.parseDelegationRecord(cases.delegations[5]).actor, null)
})

test('pilot-ck-17 pilot-ck-18: content cannot enter actor; oracle: five-member actor in ADR-0091 and L1286', () => {
  for (const member of ['content', 'context', 'args', 'proposal']) {
    const record = structuredClone(cases.delegations[0])
    record.actor[member] = 'secret'
    assert.throws(() => contracts.parseDelegationRecord(record))
  }
  for (const member of ['subject', 'actor', 'disposition']) {
    const record = structuredClone(cases.delegations[0])
    delete record[member]
    assert.throws(() => contracts.parseDelegationRecord(record))
  }
  for (const digest of ['secret', 'sha256:' + 'A'.repeat(64), 'sha256:' + '0'.repeat(63)]) {
    const record = structuredClone(cases.delegations[0])
    record.actor.proposal_digest = digest
    assert.throws(() => contracts.parseDelegationRecord(record))
  }
})

test('pilot-ck-19 pilot-ck-21: only the five disposition forms admit; oracle: DES-0026 and L1645', () => {
  for (const value of [null, {}, { kind: 'auto-apply' }, { kind: 'card' }, { kind: 'hide' }, { kind: 'standing' },
    { kind: 'approve', approver: 'alice' }, { kind: 'dismiss', content: 'secret' }]) {
    assert.throws(() => contracts.parseDelegationDisposition(value))
  }
})

test('pilot-ck-21: standing approvals preserve schedule and revocation; oracle: shared fixture, ADR-0107 and RFC 5545 section 3.3.10', () => {
  for (const approval of cases.standing_approvals) {
    assert.deepEqual(contracts.parseStandingApproval(JSON.parse(JSON.stringify(approval))), approval)
  }
})

test('pilot-ck-21: missing approver, expiry and empty commands refuse; oracle: ADR-0107 required scoped, expiring approval', () => {
  for (const member of ['id', 'approver', 'binding_key', 'scope', 'schedule', 'expires_at', 'revoked_at']) {
    const approval = structuredClone(cases.standing_approvals[0])
    delete approval[member]
    assert.throws(() => contracts.parseStandingApproval(approval))
  }
  for (const mutation of [approval => { approval.scope.commands = [] }, approval => { approval.approver = '' },
    approval => { approval.scope.commands = [''] }, approval => { approval.schedule = 42 },
    approval => { approval.expires_at = '2026-02-30T12:00:00Z' }, approval => { approval.expires_at = '2026-12-31T23:59:59+01:00' },
    approval => { approval.expires_at = null }, approval => { approval.revoked_at = 'yesterday' }]) {
    const approval = structuredClone(cases.standing_approvals[0])
    mutation(approval)
    assert.throws(() => contracts.parseStandingApproval(approval))
  }
})

test('pilot-ck-15 pilot-ck-18: canonical escaping, UTF-16 order and number spelling; oracle: RFC 8785 sections 3.2.2 and 3.2.3', () => {
  const proposal = { schema: 'pilot.proposal/3', surface: 'forms.builder', command: 'draft.edit', args: { '\ufb33': 1, '😀': 2, '\r': 3, '€': 4 } }
  assert.equal(contracts.serializeProposalCanonical(proposal), '{"args":{"\\r":3,"€":4,"😀":2,"דּ":1},"command":"draft.edit","schema":"pilot.proposal/3","surface":"forms.builder"}')
  assert.equal(contracts.serializeProposalCanonical({ ...proposal, args: [0, -0, 1e-7, 1e-6, 1e20, 1e21, -1.25] }),
    '{"args":[0,0,1e-7,0.000001,100000000000000000000,1e+21,-1.25],"command":"draft.edit","schema":"pilot.proposal/3","surface":"forms.builder"}')
  for (const args of [undefined, NaN, Infinity, 1n, () => {}, Symbol(), '\ud800']) {
    assert.throws(() => contracts.serializeProposalCanonical({ ...proposal, args }))
  }
})

test('pilot-ck-16: delegation exports add no receipt authority; oracle: runtime-identity-only receipt contract', () => {
  assert.equal(contracts.mintReceipt, undefined)
  assert.equal(contracts.parseDispatchReceipt, undefined)
  assert.equal(contracts.serializeDispatchReceipt, undefined)
})
