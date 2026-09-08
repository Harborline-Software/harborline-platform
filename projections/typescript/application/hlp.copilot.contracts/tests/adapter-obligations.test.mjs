import test from 'node:test'
import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'
import { classifyProposal, parseProposal } from '../dist/index.js'
import { contractHost } from './fixtures/contract-host.mjs'

// These exercise the shipped parse/classify/assert boundary with S2's host specimen.
// App imports, real human event wiring, authorization and transactions need destination proofs.
const surface = 'forms.builder'
const specs = ['ap', 'cp', 'never'].map(tier => ({
  id: `${tier}.edit`, aliases: [`${tier}.alias`], summary: tier, argsHint: '{value: string}', undoable: false,
  classification: tier === 'never'
    ? { tier, archetype: 'human-authority-gate', justification: 'Human authority only.' } : { tier },
  argsSchema: args => typeof args?.value === 'string'
    ? { ok: true, args: { value: args.value } } : { ok: false },
}))
const refused = code => ({ ok: false, code })
const raw = (tier, value) => ({ schema: 'pilot.proposal/3', surface, command: `${tier}.alias`, args: { value } })
function classify(host, tier = 'ap', value = 'draft') {
  const parsed = parseProposal(raw(tier, value), surface, specs)
  assert.equal(parsed.ok, true)
  assert.equal(parsed.proposal.command, `${tier}.edit`)
  const result = classifyProposal(parsed, host.currentContextKey(surface))
  assert.equal(result.kind, tier === 'cp' ? 'card' : tier === 'never' ? 'reject' : 'auto-apply')
  return result
}
const confirm = (host, receipt) => receipt.tier === 'cp' ? host.apply(receipt) : undefined

test('obligation 1 (adapter-contract.md:38): Stale-key negative test', async () => {
  for (const tier of ['ap', 'cp']) {
    for (const liveKey of ['form#2', null]) {
      const host = contractHost()
      const { receipt } = classify(host, tier)
      const gesture = confirm(host, receipt)
      host.liveKey = liveKey
      assert.deepEqual(await host.execute(receipt, gesture), refused('stale-target'))
      assert.equal(host.attempts, 0)
      assert.deepEqual(host.state, [])
      // Re-observe the target and classify anew; the valid path must still commit.
      host.liveKey = 'form#2'
      const fresh = classify(host, tier, 'fresh').receipt
      assert.deepEqual(await host.execute(fresh, confirm(host, fresh)), { ok: true })
      assert.deepEqual(host.state, [{ value: 'fresh' }])
    }
  }
})

test('obligation 2 (adapter-contract.md:39): Adapter cannot be imported from provider-reachable modules', async () => {
  // Reuse S2's symbol inventory, including its non-empty inventory assertion, without
  // inventing S4's app dependency fence or copying a name-based import scan here.
  const pin = fileURLToPath(new URL('../../../../../tooling/tests/pilot-provider-imports.test.mjs', import.meta.url))
  const env = { ...process.env }
  delete env.NODE_TEST_CONTEXT // The child owns its TAP runner, not the parent's test IPC.
  const scan = spawnSync(process.execPath, ['--test', '--test-reporter=tap', pin], { encoding: 'utf8', env })
  assert.equal(scan.status, 0, `${scan.stdout}\n${scan.stderr}`)
  assert.match(scan.stdout, /^# tests 1$/m)
  assert.match(scan.stdout, /^# pass 1$/m)
  const host = contractHost()
  const proposal = raw('ap', 'provider')
  assert.deepEqual(await host.execute(proposal), refused('undispatched-receipt'))
  assert.equal(host.attempts, 0)
  const { receipt } = classify(host, 'ap', 'provider')
  assert.deepEqual(await host.execute(receipt), { ok: true })
  assert.deepEqual(host.state, [{ value: 'provider' }])
})

test('obligation 3 (adapter-contract.md:40): CP cannot execute without a human gesture', async () => {
  const host = contractHost()
  const { receipt } = classify(host, 'cp')
  assert.deepEqual(await host.execute(receipt), refused('human-gesture-required'))
  const gesture = host.apply(receipt)
  assert.deepEqual(await host.execute(receipt, { ...gesture }), refused('human-gesture-required'))
  const other = classify(host, 'cp').receipt
  assert.deepEqual(await host.execute(other, gesture), refused('human-gesture-required'))
  host.liveKey = 'form#2'
  assert.throws(() => host.apply(receipt), /stale-target/)
  assert.deepEqual(await host.execute(receipt, gesture), refused('stale-target'))
  assert.equal(host.attempts, 0)
  host.liveKey = 'form#1'
  assert.deepEqual(await host.execute(receipt, gesture), { ok: true })
  assert.deepEqual(await host.execute(receipt, gesture), refused('human-gesture-required'))
  assert.equal(host.attempts, 1)
  assert.deepEqual(host.state, [{ value: 'draft' }])
})

test('obligation 4 (adapter-contract.md:41): NEVER cannot be dispatched even through a forged request', async () => {
  const host = contractHost()
  assert.deepEqual(classify(host, 'never'), { kind: 'reject', reason: 'never-exposed' })
  const { receipt } = classify(host)
  for (const tier of ['ap', 'cp', 'never']) {
    const forged = { ...receipt, command: 'never.edit', tier }
    assert.deepEqual(await host.execute(forged), refused('undispatched-receipt'))
    assert.throws(() => host.apply(forged), /undispatched-receipt/)
  }
  assert.equal(host.attempts, 0)
  assert.deepEqual(host.state, [])
  assert.deepEqual(await host.execute(receipt), { ok: true })
  assert.deepEqual(host.state, [{ value: 'draft' }])
})

test('obligation 5 (adapter-contract.md:42): Authorization failure is fail-closed', async () => {
  for (const tier of ['ap', 'cp']) {
    const host = contractHost()
    const seed = classify(host, tier, 'existing').receipt
    assert.deepEqual(await host.execute(seed, confirm(host, seed)), { ok: true })
    const { receipt } = classify(host, tier, 'denied')
    const gesture = confirm(host, receipt)
    // Permission is revoked after parse, classification and any Apply gesture.
    host.authorized = false
    assert.deepEqual(await host.execute(receipt, gesture), refused('forbidden'))
    assert.equal(host.attempts, 1)
    assert.deepEqual(host.state, [{ value: 'existing' }])
    host.authorized = true
    const retry = classify(host, tier, 'allowed').receipt
    assert.deepEqual(await host.execute(retry, confirm(host, retry)), { ok: true })
    assert.deepEqual(host.state, [{ value: 'existing' }, { value: 'allowed' }])
  }
})

test('obligation 6 (adapter-contract.md:43): Effects are atomic or report failure honestly', async () => {
  for (const tier of ['ap', 'cp']) {
    const host = contractHost()
    const seed = classify(host, tier, 'existing').receipt
    assert.deepEqual(await host.execute(seed, confirm(host, seed)), { ok: true })
    for (const code of ['effect-failed', 'storage-unavailable']) {
      const { receipt } = classify(host, tier, 'must-not-commit')
      host.failure = code
      assert.deepEqual(await host.execute(receipt, confirm(host, receipt)), refused(code))
      assert.deepEqual(host.state, [{ value: 'existing' }])
    }
    assert.equal(host.attempts, 3)
    host.failure = null
    const retry = classify(host, tier, 'recovered').receipt
    assert.deepEqual(await host.execute(retry, confirm(host, retry)), { ok: true })
    assert.deepEqual(host.state, [{ value: 'existing' }, { value: 'recovered' }])
    assert.equal(host.attempts, 4)
  }
})
