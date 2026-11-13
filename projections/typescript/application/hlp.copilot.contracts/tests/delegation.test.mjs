import test from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import * as contracts from '../dist/index.js'

const cases = JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.copilot/delegation-cases.json', import.meta.url), 'utf8'))

for (const item of cases.blank_strings) {
  test(`pilot-ck-17 pilot-ck-18: ${item.name}; oracle: M11 control 1378, T-1107 and R-0125 fixture literals`, () => {
    for (const member of ['subject', 'binding_key', 'binding_version', 'provider_id', 'model_version']) {
      const record = structuredClone(cases.delegations[0])
      if (member === 'subject') record[member] = item.value
      else record.actor[member] = item.value
      if (item.admitted) assert.deepEqual(contracts.deserializeDelegationRecord(JSON.stringify(record)), record)
      else assert.throws(() => contracts.deserializeDelegationRecord(JSON.stringify(record)), { message: 'required-wire-string' })
    }
  })
}

test('pilot-ck-17 pilot-ck-19 pilot-ck-21: runtime shapes have cross-lane canonical bytes; oracle: hand-ordered RFC 8785 fixture literals', () => {
  const codecs = {
    delegation: [contracts.deserializeDelegationRecord, contracts.serializeDelegationRecordCanonical],
    disposition: [contracts.deserializeDelegationDisposition, contracts.serializeDelegationDispositionCanonical],
    standing: [contracts.deserializeStandingApproval, contracts.serializeStandingApprovalCanonical]
  }
  for (const item of cases.runtime_canonical) {
    const [deserialize, serialize] = codecs[item.shape]
    assert.equal(serialize(deserialize(item.json)), item.canonical)
    assert.equal(serialize(deserialize(item.canonical)), item.canonical)
  }
})

test('pilot-ck-15 pilot-ck-17 pilot-ck-19 pilot-ck-21: raw JSON refuses duplicate names before they disappear; oracle: RFC 7493 section 2.3 shared literals', () => {
  const deserialize = { proposal: contracts.deserializeProposalEnvelope, delegation: contracts.deserializeDelegationRecord,
    disposition: contracts.deserializeDelegationDisposition, standing: contracts.deserializeStandingApproval }
  for (const item of cases.raw_json_cases) {
    if (item.valid) assert.deepEqual(deserialize[item.shape](item.json), JSON.parse(item.json))
    else assert.throws(() => deserialize[item.shape](item.json), { message: 'duplicate-wire-member' })
  }
})

for (const envelope of cases.envelopes) {
  test(`pilot-ck-15 pilot-ck-18: ${envelope.name}; oracle: RFC 8785 fixture bytes and independent shasum literal`, async () => {
    assert.deepEqual(contracts.deserializeProposalEnvelope(JSON.stringify(envelope.value)), envelope.value)
    assert.equal(contracts.serializeProposalCanonical(envelope.value), envelope.canonical)
    assert.equal(await contracts.proposalDigest(envelope.value), envelope.digest)
  })
}

test('pilot-ck-15 pilot-ck-18: unsafe integers refuse before digesting; oracle: M11 2026-11-12 ruling and RFC 7493 section 2.2 fixture', async () => {
  for (const item of cases.integer_refusals) {
    const json = '{"schema":"pilot.proposal/3","surface":"forms.builder","command":"draft.edit","args":' + item.args + '}'
    const proposal = JSON.parse(json)
    assert.throws(() => contracts.deserializeProposalEnvelope(json), { message: item.refusal })
    assert.throws(() => contracts.serializeProposalCanonical(proposal), { message: item.refusal })
    await assert.rejects(contracts.proposalDigest(proposal), { message: item.refusal })
  }
})

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
  for (const digest of ['secret', 'sha256:' + 'A'.repeat(64), 'sha256:' + '0'.repeat(63), 'sha256:' + '0'.repeat(64) + '\n']) {
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
  assert.equal(contracts.serializeProposalCanonical({ ...proposal, args: [0, -0, 1e-7, 1e-6, -1.25] }),
    '{"args":[0,0,1e-7,0.000001,-1.25],"command":"draft.edit","schema":"pilot.proposal/3","surface":"forms.builder"}')
  for (const args of [undefined, NaN, Infinity, 1n, () => {}, Symbol(), '\ud800']) {
    assert.throws(() => contracts.serializeProposalCanonical({ ...proposal, args }))
  }
})

test('pilot-ck-16: delegation exports add no receipt authority; oracle: runtime-identity-only receipt contract', () => {
  assert.equal(contracts.mintReceipt, undefined)
  assert.equal(contracts.parseDispatchReceipt, undefined)
  assert.equal(contracts.serializeDispatchReceipt, undefined)
})

test('pilot-ck-15 pilot-ck-18: escaping and JSON-only arguments; oracle: RFC 8785 section 3.2.2.2', () => {
  const proposal = cases.envelopes[0].value
  assert.equal(contracts.serializeProposalCanonical({ ...proposal, args: '\u0001\b\t\n\f\r"\\\u2028😀' }),
    '{"args":"\\u0001\\b\\t\\n\\f\\r\\"\\\\\u2028😀","command":"draft.edit","schema":"pilot.proposal/3","surface":"forms.builder"}')
  const accessor = { get value() { throw new Error('must not be read') } }
  const foreign = new Date()
  const extraArray = [1]; extraArray.extra = 2
  const hidden = {}; Object.defineProperty(hidden, 'value', { value: 1 })
  const symbolic = { [Symbol('content')]: 'secret' }
  const cycle = {}; cycle.self = cycle
  for (const args of [accessor, foreign, extraArray, hidden, symbolic, cycle, Array(1)]) {
    assert.throws(() => contracts.serializeProposalCanonical({ ...proposal, args }))
  }
})

test('pilot-ck-21: exact UTC spelling and accessor refusal; oracle: shared UTC wire contract and ADR-0107', () => {
  const approval = cases.standing_approvals[0]
  for (const expires_at of ['2026-12-31T23:59:59+00:00', '2026-12-31T23:59:59.1234567Z']) {
    assert.equal(contracts.parseStandingApproval({ ...approval, expires_at }).expires_at, expires_at)
  }
  for (const expires_at of ['0000-12-31T23:59:59Z', '2026-12-31T23:59:59.Z', '2026-12-31T23:59:59-00:00', '2026-12-31T23:59:59Z\n']) {
    assert.throws(() => contracts.parseStandingApproval({ ...approval, expires_at }))
  }
  const commands = ['draft.edit']
  Object.defineProperty(commands, '0', { get() { throw new Error('must not be read') }, enumerable: true })
  assert.throws(() => contracts.parseStandingApproval({ ...approval, scope: { surface: 'forms.builder', commands } }), /invalid-args/)
  assert.throws(() => contracts.parseStandingApproval({ ...approval, approver: '\ud800' }))
})
