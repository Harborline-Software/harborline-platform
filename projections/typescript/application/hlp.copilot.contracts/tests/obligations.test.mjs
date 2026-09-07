import test from 'node:test'
import assert from 'node:assert/strict'
import ts from 'typescript'
import { fileURLToPath } from 'node:url'
import { classifyProposal, parseProposal } from '../dist/index.js'
import { contractHost } from './fixtures/contract-host.mjs'

const surface = 'forms.builder'
const specs = ['ap', 'cp', 'never'].map(tier => ({
  id: tier, summary: tier, argsHint: '{}', undoable: false,
  classification: tier === 'never' ? { tier, archetype: 'human-authority-gate', justification: 'Human authority only.' } : { tier },
  argsSchema: args => ({ ok: true, args }),
}))
const parse = (tier, args = {}) => parseProposal({ schema: 'pilot.proposal/3', surface, command: tier, args }, surface, specs)
const receipt = (tier = 'ap', key = 'form#1', args = {}) => classifyProposal(parse(tier, args), key).receipt
const refused = (code) => ({ ok: false, code })

// Pin the public TypeScript signature too: these are deliberate compile-negative consumers.
function keyTypeDiagnostics() {
  const path = fileURLToPath(new URL('./fixtures/context-key.ts', import.meta.url))
  const program = ts.createProgram([path], { strict: true, noEmit: true, skipLibCheck: true, target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.NodeNext })
  return ts.getPreEmitDiagnostics(program).map(d => ({ file: d.file?.fileName, code: d.code, text: ts.flattenDiagnosticMessageText(d.messageText, '\n') }))
}

test('obligation: stale key and missing live-target key fail closed', async () => {
  const host = contractHost()
  const r = receipt()
  host.liveKey = 'form#2'
  assert.deepEqual(await host.execute(r), refused('stale-target'))
  assert.equal(host.attempts, 0)
  for (const key of [undefined, null, '', 42]) {
    assert.equal('receipt' in classifyProposal(parse('ap'), key), false)
    assert.deepEqual(await host.execute({ ...r, expectedContextKey: key }), refused('undispatched-receipt'))
  }
  host.liveKey = 'form#1'
  assert.deepEqual(await host.execute(r), { ok: true })
  const diagnostics = keyTypeDiagnostics()
  assert.equal(diagnostics.length, 2, JSON.stringify(diagnostics))
  assert.ok(diagnostics.every(d => d.text.includes('expectedContextKey')), JSON.stringify(diagnostics))
})

test('obligation: CP needs a distinct one-use gesture bound to receipt and target', async () => {
  const host = contractHost()
  const r = receipt('cp')
  assert.deepEqual(await host.execute(r), refused('human-gesture-required'))
  assert.deepEqual(await host.execute(r, { contextKey: 'form#1' }), refused('human-gesture-required'))
  const proof = host.apply(r)
  assert.deepEqual(await host.execute(receipt('cp'), proof), refused('human-gesture-required'))
  host.liveKey = 'form#2'
  assert.throws(() => host.apply(r), /stale-target/)
  assert.deepEqual(await host.execute(r, proof), refused('stale-target'))
  assert.equal(host.attempts, 0)
  host.liveKey = 'form#1'
  assert.deepEqual(await host.execute(r, proof), { ok: true })
  assert.deepEqual(await host.execute(r, proof), refused('human-gesture-required'))
  assert.equal(host.attempts, 1)
})

test('obligation: NEVER cannot dispatch through a forged request', async () => {
  const host = contractHost()
  assert.deepEqual(classifyProposal(parse('never'), 'form#1'), { kind: 'reject', reason: 'never-exposed' })
  const genuine = receipt()
  for (const forged of [{ ...genuine, command: 'never', tier: 'ap' }, { ...genuine, command: 'never', tier: 'never' }]) {
    assert.deepEqual(await host.execute(forged), refused('undispatched-receipt'))
  }
  assert.equal(host.attempts, 0)
  assert.deepEqual(await host.execute(genuine), { ok: true })
})

test('obligation: authorization denial is fail-closed in the host contract', async () => {
  const host = contractHost()
  host.authorized = false
  assert.deepEqual(await host.execute(receipt()), refused('forbidden'))
  const cp = receipt('cp')
  assert.deepEqual(await host.execute(cp, host.apply(cp)), refused('forbidden'))
  assert.equal(host.attempts, 0)
  assert.deepEqual(host.state, [])
  host.authorized = true
  assert.deepEqual(await host.execute(receipt()), { ok: true })
  assert.equal(host.attempts, 1)
})

test('obligation: effects commit atomically or report failure honestly in the host contract', async () => {
  const host = contractHost()
  assert.deepEqual(await host.execute(receipt()), { ok: true })
  const before = structuredClone(host.state)
  host.failure = 'effect-failed'
  assert.deepEqual(await host.execute(receipt()), refused('effect-failed'))
  assert.deepEqual(host.state, before)
  assert.equal(host.attempts, 2)
  host.failure = null
  assert.deepEqual(await host.execute(receipt()), { ok: true })
  assert.equal(host.state.length, 2)
})

test('receipt args: nested mutation after classification cannot reach execute', async () => {
  const host = contractHost()
  const args = { nested: { amount: 1 }, rows: [{ value: 'original' }] }
  const parsed = parse('ap', args)
  const r = classifyProposal(parsed, 'form#1').receipt
  args.nested.amount = 999
  args.rows[0].value = 'rewritten'
  Reflect.set(r.args.nested, 'amount', 777)
  Reflect.set(parsed.proposal.args.rows[0], 'value', 'rewritten-again')
  assert.deepEqual(await host.execute(r), { ok: true })
  assert.deepEqual(host.state, [{ nested: { amount: 1 }, rows: [{ value: 'original' }] }])
  for (const value of [r.args, r.args.nested, r.args.rows, r.args.rows[0]]) assert.ok(Object.isFrozen(value))
})
