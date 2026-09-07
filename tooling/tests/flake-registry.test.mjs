#!/usr/bin/env node
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {test} from 'node:test'
import {fileURLToPath} from 'node:url'

import {
  REGISTERED_FLAKE_COUNT,
  registeredFlakeRecords,
  unregisteredFlakes,
  validateFlakeRegistry,
} from '../flake-registry.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const TODAY = '2026-09-07'

// The real row's shape, so the fixtures are the thing the gate actually validates.
const row = (overrides = {}) => ({
  test: 'schema-form.content is accessible and visually conformant',
  owner: '284',
  firstSeen: '2026-09-07',
  expires: '2026-10-06',
  ...overrides,
})

test('a well-formed registry has no problems', () => {
  assert.deepEqual(validateFlakeRegistry([row()], TODAY, 1), [])
})

test('a row without an owner ticket is refused', () => {
  // A flake with no owner is a permanent exemption wearing a date.
  assert.match(validateFlakeRegistry([row({owner: undefined})], TODAY, 1).join('\n'), /no owner ticket/)
  assert.match(validateFlakeRegistry([row({owner: '   '})], TODAY, 1).join('\n'), /no owner ticket/)
})

test('a row past its expiry is refused, and the expiry day itself is still valid', () => {
  assert.deepEqual(validateFlakeRegistry([row()], '2026-10-06', 1), [])
  assert.match(validateFlakeRegistry([row()], '2026-10-07', 1).join('\n'), /registration expired 2026-10-06 \(owner 284\)/)
})

test('undated rows and an expiry that is not after firstSeen are refused', () => {
  assert.match(validateFlakeRegistry([row({firstSeen: undefined})], TODAY, 1).join('\n'), /firstSeen must be YYYY-MM-DD/)
  assert.match(validateFlakeRegistry([row({expires: '06/10/2026'})], TODAY, 1).join('\n'), /expires must be YYYY-MM-DD/)
  assert.match(validateFlakeRegistry([row({expires: '2026-09-07'})], TODAY, 1).join('\n'), /is not after firstSeen/)
})

test('a nameless row and a duplicate registration are refused', () => {
  assert.match(validateFlakeRegistry([row({test: '  '})], TODAY, 1).join('\n'), /no exact spec title/)
  assert.match(validateFlakeRegistry([row(), row()], TODAY, 2).join('\n'), /duplicate registration of "schema-form\.content/)
})

test('the ratchet is an equality: too many rows AND a literal above the real count are both refused', () => {
  // The api half's ceiling only caught growth, so removing a row left the literal drifting above the
  // real count and the next registrations free (284 slice 1 review, MINOR 2).
  assert.match(validateFlakeRegistry([row(), row({test: 'b'})], TODAY, 1).join('\n'), /has 2 rows but REGISTERED_FLAKE_COUNT is 1/)
  assert.match(validateFlakeRegistry([row()], TODAY, 3).join('\n'), /has 1 rows but REGISTERED_FLAKE_COUNT is 3/)
})

test('a retryLimit other than one identical retry is refused, and the default is accepted', () => {
  assert.deepEqual(validateFlakeRegistry([row({retryLimit: 1})], TODAY, 1), [])
  assert.match(validateFlakeRegistry([row({retryLimit: 3})], TODAY, 1).join('\n'), /retryLimit must be 1 \(one identical retry\), got 3/)
})

test('a non-array registry and a bad today fail closed', () => {
  assert.deepEqual(validateFlakeRegistry(undefined, TODAY), ['tooling/flake-registry.json: rows is not an array'])
  assert.match(validateFlakeRegistry([], 'yesterday').join('\n'), /today must be YYYY-MM-DD/)
})

test('a flake nobody registered is named; a registered one is not', () => {
  // The rule that gives the registry teeth: this list is what reddens the gate.
  assert.deepEqual(
    unregisteredFlakes(['toaster.default is accessible and visually conformant', row().test], [row()]),
    ['toaster.default is accessible and visually conformant'],
  )
  assert.deepEqual(unregisteredFlakes([row().test], [row()]), [])
  assert.deepEqual(unregisteredFlakes([], [row()]), [])
})

test('registration is by exact title, never a prefix', () => {
  assert.deepEqual(unregisteredFlakes(['schema-form.content'], [row()]), ['schema-form.content'])
})

test('a registered flake is recorded with its owner, its expiry and BOTH outcomes of the retry', () => {
  assert.deepEqual(
    registeredFlakeRecords([row().test, 'stranger'], [row()], {[row().test]: ['failed', 'passed']}),
    [{test: row().test, owner: '284', firstSeen: '2026-09-07', expires: '2026-10-06', outcomes: ['failed', 'passed']}],
  )
})

test('the real registry is valid today, and its literal equals its row count', () => {
  const registry = JSON.parse(readFileSync(resolve(root, 'tooling/flake-registry.json'), 'utf8'))
  assert.deepEqual(validateFlakeRegistry(registry.rows, new Date().toISOString().slice(0, 10)), [])
  assert.equal(registry.rows.length, REGISTERED_FLAKE_COUNT)
  for (const entry of registry.rows) {
    assert.ok(entry.reason?.trim(), `${entry.test} must say why it is registered`)
    // <= 60 days, the same bound the api half holds its rows to.
    const days = (Date.parse(entry.expires) - Date.parse(entry.firstSeen)) / 86_400_000
    assert.ok(days > 0 && days <= 60, `${entry.test} is registered for ${days} days`)
  }
})
