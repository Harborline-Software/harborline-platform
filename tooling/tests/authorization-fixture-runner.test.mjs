import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'
import {pathToFileURL} from 'node:url'

const root = resolve(import.meta.dirname, '../..')
const runner = resolve(root, 'conformance/hlp.contracts.authorization/runners/run-shared.mjs')
const preload = resolve(import.meta.dirname, 'fixtures/authorization-fixture-runner-preload.mjs')
const canonical = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.contracts.authorization/fixtures.yaml'), 'utf8'))

function runFixture(fixture) {
  const run = spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, runner], {
    cwd: root,
    encoding: 'utf8',
    timeout: 30_000,
    maxBuffer: 1024 * 1024,
    env: {...process.env, HARBORLINE_AUTHORIZATION_RUNNER_FIXTURE: JSON.stringify(fixture)},
  })
  assert.ifError(run.error)
  assert.equal(run.signal, null, run.stderr)
  return {run, output: `${run.stdout}${run.stderr}`}
}

test('canonical23 rows admit all seven operations and reach the trapped build, without executing the cases', () => {
  assert.equal(canonical.cases.length, 23)
  assert.deepEqual([...new Set(canonical.cases.map(row => row.operation))].sort(), [
    'allows', 'invalid', 'invalid-capabilities', 'require', 'require-capability', 'resolve', 'resolve-capability',
  ])
  const {run, output} = runFixture(canonical)
  assert.equal(run.status, 1, output)
  assert.match(output, /AUTHORIZATION_FIXTURE_BUILD_SENTINEL/)
  assert.doesNotMatch(output, /Unknown authorization fixture operation/)
  assert.doesNotMatch(output, /\bPASS\b/)
})

for (const [originalId, operation, id] of [
  ['resolve-qualified-role', 'resovle', 'unknown-operation-typo'],
  ['capability-resolves-with-version', 'invented-capability', 'unknown-capability-operation'],
]) {
  test(`unknown operation ${operation} refuses the actual CLI before building or claiming PASS`, () => {
    const fixture = structuredClone(canonical)
    const row = fixture.cases.find(candidate => candidate.id === originalId)
    assert.ok(row, `Missing canonical fixture ${originalId}`)
    row.operation = operation
    row.id = id

    const {run, output} = runFixture(fixture)
    assert.notEqual(run.status, 0, output)
    assert.match(output, /Unknown authorization fixture operation/)
    assert.ok(output.includes(operation), output)
    assert.ok(output.includes(id), output)
    assert.doesNotMatch(output, /AUTHORIZATION_FIXTURE_BUILD_SENTINEL/)
    assert.doesNotMatch(output, /\bPASS\b/)
  })
}
