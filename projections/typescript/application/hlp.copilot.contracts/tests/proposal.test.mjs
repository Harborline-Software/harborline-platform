import test from 'node:test'; import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { assertDispatched, buildManifest, classifyProposal, parseProposal, proposalDigest, ProposalTextSplitter } from '../dist/index.js'
const schema = raw => raw && typeof raw === 'object' && !Array.isArray(raw) ? { ok: true, args: raw } : { ok: false, code: 'object' }
const specs = [
  { id:'draft.edit', summary:'edit', argsHint:'{}', argsSchema:schema, classification:{tier:'ap'}, undoable:true },
  { id:'record.publish', summary:'publish', argsHint:'{}', argsSchema:schema, classification:{tier:'cp'}, undoable:false },
  { id:'authority.confirm', summary:'confirm', argsHint:'{}', argsSchema:schema, classification:{tier:'never',archetype:'human-authority-gate',justification:'A card would be self-approval.'}, undoable:false }
]
test('schema safety maps one-to-one to pinned proposal/schema tests', () => {
  assert.equal(parseProposal(null,'forms.builder',specs).ok,false)
  assert.equal(parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:'draft.edit',args:{}},'forms.builder',specs).ok,true)
})
test('AP/CP/NEVER maps one-to-one to pinned proposal/classify tests', () => {
  const parse=id=>parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:id,args:{}},'forms.builder',specs)
  assert.equal(classifyProposal(parse('draft.edit'),'form#1').kind,'auto-apply')
  assert.equal(classifyProposal(parse('record.publish'),'form#1').kind,'card')
  assert.equal(classifyProposal(parse('authority.confirm'),'form#1').kind,'reject')
})
test('manifest excludes NEVER one-to-one with pinned manifest safety rows', () => {
  const manifest=buildManifest('forms.builder',specs); assert.match(manifest,/draft\.edit/); assert.doesNotMatch(manifest,/authority\.confirm/)
})
test('stream fence maps one-to-one to pinned proposal/extract tests', () => {
  const s=new ProposalTextSplitter(); assert.equal(s.push('hello ```js'),'hello '); assert.equal(s.push('{"x":1}```'),''); assert.deepEqual(s.end().json,{x:1})
})

test('args that cannot be frozen are invalid-args, never an exception out of parse', () => {
  let deep = {}; for (let i = 0; i < 200000; i++) deep = { nested: deep }
  assert.deepEqual(parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:'draft.edit',args:{deep}},'forms.builder',specs), { ok:false, code:'invalid-args' })
  const accessor = {}; Object.defineProperty(accessor, 'x', { get() { return 1 }, enumerable: true })
  assert.deepEqual(parseProposal({schema:'pilot.proposal/3',surface:'forms.builder',command:'draft.edit',args:{accessor}},'forms.builder',specs), { ok:false, code:'invalid-args' })
})

test('pilot-ck-15 pilot-ck-16 pilot-ck-18: alias digest covers received envelope, receipt resolves catalogue id; oracle: M11 control 1373 and independent fixture shasum', async () => {
  const cases = JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.copilot/delegation-cases.json', import.meta.url), 'utf8'))
  const envelope = cases.envelopes.find(item => item.name === 'M11 alias as received')
  const raw = structuredClone(envelope.value)
  const result = parseProposal(raw, 'forms.builder', [{ ...specs[0], aliases: ['draft.alias'] }])
  assert.equal(result.ok, true)
  assert.deepEqual(result.received, envelope.value)
  assert.equal(Object.isFrozen(result.received), true)
  assert.equal(Object.isFrozen(result.received.args), true)
  raw.command = 'rewritten'
  raw.args.changed = true
  assert.equal(await proposalDigest(result), envelope.digest)
  assert.equal(await proposalDigest(result.received), envelope.digest)
  const disposition = classifyProposal(result, 'form#1')
  assert.equal(disposition.kind, 'auto-apply')
  assert.equal(assertDispatched(disposition.receipt, 'form#1').command, 'draft.edit')
  assert.deepEqual(disposition.receipt.args, {})
})
