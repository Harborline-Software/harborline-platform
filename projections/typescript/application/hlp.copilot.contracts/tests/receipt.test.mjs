import test from 'node:test'; import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { assertDispatched, classifyProposal, parseProposal } from '../dist/index.js'
import * as surface from '../dist/index.js'

const schema = raw => raw && typeof raw === 'object' && !Array.isArray(raw) ? { ok: true, args: raw } : { ok: false, code: 'object' }
const ap = { id:'draft.edit', summary:'edit', argsHint:'{}', argsSchema:schema, classification:{tier:'ap'}, undoable:true }
const cp = { id:'record.publish', summary:'publish', argsHint:'{}', argsSchema:schema, classification:{tier:'cp'}, undoable:false }
const never = { id:'authority.confirm', summary:'confirm', argsHint:'{}', argsSchema:schema, classification:{tier:'never',archetype:'human-authority-gate',justification:'A card would be self-approval.'}, undoable:false }

const specs = [ap, cp, never]
// classify now takes its spec, surface and args from a parseProposal result, by identity.
const parse = (spec, args = {}, surfaceName = 'forms.builder') =>
  parseProposal({ schema:'pilot.proposal/3', surface:surfaceName, command:spec.id, args }, surfaceName, specs)

// An adapter that honours the contract: refuses anything it was not handed by classify.
const adapter = { execute: receipt => { assertDispatched(receipt); return { ok: true } } }

test('a receipt minted by classify is the only thing the adapter accepts', () => {
  const d = classifyProposal(parse(ap, { a: 1 }), 'form#1')
  assert.equal(d.kind, 'auto-apply')
  assert.equal(d.expectedContextKey, 'form#1')
  assert.deepEqual(adapter.execute(d.receipt), { ok: true })
  assert.equal(Object.isFrozen(d.receipt), true)
})

test('a forged receipt is refused: literal, JSON round-trip, prototype clone', () => {
  const real = classifyProposal(parse(cp), 'form#1').receipt
  const forgeries = [
    { surface:'forms.builder', command:'record.publish', args:{}, tier:'cp', expectedContextKey:'form#1' },
    JSON.parse(JSON.stringify(real)),
    Object.assign(Object.create(Object.getPrototypeOf(real)), real),
    { ...real },
  ]
  for (const forged of forgeries) assert.throws(() => adapter.execute(forged), /undispatched-receipt/)
})

test('a dispatch with no receipt is refused', () => {
  for (const nothing of [undefined, null, 'form#1', 0]) assert.throws(() => adapter.execute(nothing), /undispatched-receipt/)
})

test('a NEVER classification can produce no receipt at all', () => {
  const d = classifyProposal(parse(never), 'form#1')
  assert.equal(d.kind, 'reject')
  assert.equal('receipt' in d, false)
})

test('no live target yields a clarify with no receipt, for every tier', () => {
  for (const spec of [ap, cp, never]) {
    const d = classifyProposal(parse(spec), null)
    assert.equal('receipt' in d, false)
    assert.equal(d.kind, spec === never ? 'reject' : 'clarify')
  }
})

test('the minting function is not on the package public surface', () => {
  assert.equal(typeof surface.assertDispatched, 'function')
  assert.equal(Object.keys(surface).includes('mintReceipt'), false)
  assert.equal(surface.mintReceipt, undefined)
  const index = readFileSync(new URL('../src/index.ts', import.meta.url), 'utf8')
  assert.doesNotMatch(index, /mintReceipt/)
  assert.doesNotMatch(index, /export \* from '\.\/receipt\.js'/)
  const packed = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf8'))
  assert.deepEqual(Object.keys(packed.exports), ['.'])
  assert.equal(packed.exports['.'].import, './dist/index.js')
})

test('a caller-authored spec naming a NEVER command cannot mint a receipt', () => {
  // The review-1 exploit: declare the NEVER command id at a permissive tier and classify it.
  const forgedSpec = { ...never, classification: { tier: 'ap' } }
  assert.throws(() => classifyProposal(forgedSpec, 'form#1', 'forms.builder', { approve: true }), /unparsed-proposal/)
  // A look-alike parse result carrying the same forged spec is refused too: identity, not shape.
  const lookAlike = { ok: true, proposal: { schema:'pilot.proposal/3', surface:'forms.builder', command:'authority.confirm', args:{} }, spec: forgedSpec }
  assert.throws(() => classifyProposal(lookAlike, 'form#1'), /unparsed-proposal/)
  // And the genuine route rejects it, so no receipt exists for that id by any path.
  assert.equal(classifyProposal(parse(never), 'form#1').kind, 'reject')
})

test('mutating a genuine parse result after the parse cannot move the mint', () => {
  // The review-2 exploit: a genuine parse of a harmless AP command, then rewrite the result.
  const r = parse(ap, { z: 1 })
  assert.equal(Object.isFrozen(r), true)
  try { r.spec = { ...never, classification: { tier: 'ap' } } } catch { /* frozen: assignment throws in strict mode */ }
  try { r.proposal = { schema:'pilot.proposal/3', surface:'admin', command:'view.zoom', args:{ all: true } } } catch { /* frozen */ }
  const d = classifyProposal(r, 'ctx#1')
  assert.equal(d.kind, 'auto-apply')
  assert.deepEqual({ ...d.receipt }, { surface:'forms.builder', command:'draft.edit', args:{ z: 1 }, tier:'ap', expectedContextKey:'ctx#1' })
})

test('a parse result replayed with another proposal on it mints only what was parsed', () => {
  const rNever = parse(never)
  const rAp = parse(ap, { a: 1 })
  // Carry the AP result's own fields onto the NEVER result, then classify the NEVER one.
  try { rNever.spec = rAp.spec } catch { /* frozen */ }
  try { rNever.proposal = rAp.proposal } catch { /* frozen */ }
  assert.equal(classifyProposal(rNever, 'ctx#1').kind, 'reject')
  // And the AP result cannot be made to speak for the NEVER command.
  try { rAp.proposal = { ...rAp.proposal, command: never.id } } catch { /* frozen */ }
  assert.equal(classifyProposal(rAp, 'ctx#1').receipt.command, 'draft.edit')
})
