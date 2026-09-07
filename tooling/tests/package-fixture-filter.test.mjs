#!/usr/bin/env node

import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {PACKAGE_FIXTURE_IDS, parsePackageFixtureArguments} from '../package-fixture-selection.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

assert.deepEqual(parsePackageFixtureArguments([]), {only: null, record: false, phase4Gate: false})
for (const id of PACKAGE_FIXTURE_IDS) {
  assert.equal(parsePackageFixtureArguments(['--only', id]).only, id)
}
assert.throws(() => parsePackageFixtureArguments(['--only', 'missing']), /unknown fixture ID/)
assert.throws(() => parsePackageFixtureArguments(['--only']), /requires a fixture ID/)
assert.throws(() => parsePackageFixtureArguments(['--only', PACKAGE_FIXTURE_IDS[0], '--record']), /refused under --record/)
assert.throws(() => parsePackageFixtureArguments(['--phase-4-gate', '--only', PACKAGE_FIXTURE_IDS[0]]), /refused by the phase-4 gate/)

const gate = spawnSync(process.execPath, ['tooling/run-phase-4-gate.mjs', '--only', PACKAGE_FIXTURE_IDS[0]], {
  cwd: root,
  encoding: 'utf8',
})
assert.notEqual(gate.status, 0)
assert.match(`${gate.stdout}\n${gate.stderr}`, /phase-4 gate refuses --only/)

process.stdout.write(`${JSON.stringify({schemaVersion: 1, status: 'PASS', fixtureIds: PACKAGE_FIXTURE_IDS.length, refusals: 3})}\n`)
