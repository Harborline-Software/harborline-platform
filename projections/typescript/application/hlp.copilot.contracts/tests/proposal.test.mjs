import test from 'node:test'; import assert from 'node:assert/strict'
import { buildManifest, classifyProposal, parseProposal, ProposalTextSplitter } from '../dist/index.js'
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
