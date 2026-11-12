import assert from 'node:assert/strict'
import { buildManifest, parseProposal, deserializeProposalEnvelope, serializeProposalCanonical, proposalDigest, parseDelegationRecord, parseDelegationDisposition, parseStandingApproval } from '@harborline-software/copilot-contracts'
const specs=[{id:'draft.edit',summary:'Edit a draft',argsHint:'{}',argsSchema:x=>({ok:true,args:x}),classification:{tier:'ap'},undoable:true}]
if (!parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:'draft.edit',args:{}},'forms.builder',specs).ok) throw new Error('packed parser failed')
if (!buildManifest('forms.builder',specs).includes('draft.edit')) throw new Error('packed manifest failed')

const proposal = { schema: 'pilot.proposal/3', surface: 'forms.builder', command: 'draft.edit', args: {} }
assert.equal(serializeProposalCanonical(proposal), '{"args":{},"command":"draft.edit","schema":"pilot.proposal/3","surface":"forms.builder"}')
assert.equal(await proposalDigest(proposal), 'sha256:e9090668397c0ecf3bb91cd8bff3a980aa81b3d5755148a6c4566172d95b0aea')
assert.deepEqual(deserializeProposalEnvelope(JSON.stringify(proposal)), proposal)
assert.equal(await proposalDigest({ ...proposal, args: { integer: 9007199254740991 } }), 'sha256:01f127498747a2b64c334388dd2ac4de6ea870022bfac97e294f5da25d82414e')
assert.throws(() => deserializeProposalEnvelope('{"schema":"pilot.proposal/3","surface":"forms.builder","command":"draft.edit","args":9007199254740992}'), { message: 'args-integer-out-of-range' })
const actor = { binding_key: 'assistance.forms', binding_version: '1.0.0', provider_id: 'provider-a', model_version: 'model-2026-11',
  proposal_digest: 'sha256:e9090668397c0ecf3bb91cd8bff3a980aa81b3d5755148a6c4566172d95b0aea' }
const record = { subject: 'alice', actor, disposition: { kind: 'dismiss' } }
assert.deepEqual(parseDelegationRecord(JSON.parse(JSON.stringify(record))), record)
assert.deepEqual(Object.keys(parseDelegationRecord(record).actor).sort(), ['binding_key', 'binding_version', 'model_version', 'proposal_digest', 'provider_id'])
const approval = { id: 'approval-a', approver: 'alice', binding_key: 'assistance.forms', scope: { surface: 'forms.builder', commands: ['draft.edit'] },
  schedule: 'FREQ=WEEKLY;BYDAY=MO,WE', expires_at: '2026-12-31T23:59:59Z', revoked_at: null }
assert.deepEqual(parseStandingApproval(JSON.parse(JSON.stringify(approval))), approval)
assert.equal(parseStandingApproval({ ...approval, schedule: null }).schedule, null)
const standing = { kind: 'standing', approval_id: 'approval-a', approver: 'alice', scope: approval.scope, expires_at: '2026-12-31T23:59:59Z' }
assert.deepEqual(parseDelegationDisposition(standing), standing)
assert.throws(() => parseDelegationDisposition({ kind: 'hide' }))
assert.throws(() => parseStandingApproval({ ...approval, expires_at: undefined }))
assert.throws(() => parseStandingApproval({ ...approval, approver: undefined }))
assert.throws(() => parseStandingApproval({ ...approval, scope: { surface: 'forms.builder', commands: [] } }))

