import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {resolve} from 'node:path'
import test from 'node:test'
import {pathToFileURL} from 'node:url'

const root = resolve(import.meta.dirname, '../..')
const validator = resolve(root, 'tooling/validate-repository.mjs')
const preload = resolve(import.meta.dirname, 'fixtures/validate-repository-preload.mjs')
const moduleId = 'hlp.kernel.work-items'
const fixturePath = 'conformance/hlp.kernel.work-items/fixtures.yaml'
const prefix = `${moduleId}: ${fixturePath}: `

function runFixture(text) {
  const run = spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, validator, '--allow-stale-gate'], {
    cwd: root,
    encoding: 'utf8',
    timeout: 60_000,
    maxBuffer: 4 * 1024 * 1024,
    env: {
      ...process.env,
      HARBORLINE_VALIDATE_REPOSITORY_FILES: '',
      HARBORLINE_VALIDATE_REPOSITORY_FIXTURE: JSON.stringify({path: fixturePath, text}),
    },
  })
  assert.ifError(run.error)
  assert.equal(run.signal, null, run.stderr)
  const report = JSON.parse(run.stdout)
  assert.equal(report.repository, 'harborline-platform')
  assert.ok(Array.isArray(report.errors))
  return {run, report, fixtureErrors: report.errors.filter(error => error.startsWith(prefix))}
}

function specimen(encoding, revision, declared = true) {
  if (encoding === 'JSON') {
    return JSON.stringify({schemaVersion: 1, moduleId, ...(declared ? {interfaceRevision: revision} : {}), cases: [{id: 'live-case'}]})
  }
  return `---\nschemaVersion: 1\nmoduleId: ${moduleId}\n${declared ? `interfaceRevision: ${revision}\n` : '# interfaceRevision: 3 is absent, not declared\n'}cases:\n  - id: live-case\n    interfaceRevision: nested-case-data\n`
}

for (const encoding of ['JSON', 'YAML']) {
  test(`${encoding}: a declared fixture revision matching the literal interface revision is admitted`, () => {
    // The work-items interface declares 4; this literal does not read the validator's result.
    const {fixtureErrors} = runFixture(specimen(encoding, 4))
    assert.deepEqual(fixtureErrors, [])
  })

  test(`${encoding}: a mismatch reports the module, fixture path and literal expected/actual revisions`, () => {
    const {run, report, fixtureErrors} = runFixture(specimen(encoding, 3))
    assert.equal(run.status, 1)
    assert.equal(report.status, 'FAIL')
    assert.deepEqual(fixtureErrors, [
      'hlp.kernel.work-items: conformance/hlp.kernel.work-items/fixtures.yaml: fixture interfaceRevision mismatch (expected 4, actual 3)',
    ])
  })

  test(`${encoding}: omitted fixture revision retains the explicit legacy optional disposition`, () => {
    const {fixtureErrors} = runFixture(specimen(encoding, undefined, false))
    assert.deepEqual(fixtureErrors, [])
  })

  test(`${encoding}: a malformed declared revision cannot pass as omission`, () => {
    const {run, report, fixtureErrors} = runFixture(specimen(encoding, 'not-a-revision'))
    assert.equal(run.status, 1)
    assert.equal(report.status, 'FAIL')
    assert.deepEqual(fixtureErrors, [
      'hlp.kernel.work-items: conformance/hlp.kernel.work-items/fixtures.yaml: fixture interfaceRevision must be a positive safe integer (actual "not-a-revision")',
    ])
  })
}

test('broken JSON is a named fixture metadata error, never an omitted YAML revision', () => {
  const {run, report, fixtureErrors} = runFixture('{"moduleId":"hlp.kernel.work-items","interfaceRevision":')
  assert.equal(run.status, 1)
  assert.equal(report.status, 'FAIL')
  assert.equal(fixtureErrors.length, 1)
  assert.ok(fixtureErrors[0].startsWith(`${prefix}invalid fixture metadata (`), fixtureErrors[0])
})

test('duplicate YAML revision metadata refuses instead of selecting one declaration', () => {
  const {run, report, fixtureErrors} = runFixture('moduleId: hlp.kernel.work-items\ninterfaceRevision: 3\ninterfaceRevision: 4\ncases: []\n')
  assert.equal(run.status, 1)
  assert.equal(report.status, 'FAIL')
  assert.deepEqual(fixtureErrors, [
    'hlp.kernel.work-items: conformance/hlp.kernel.work-items/fixtures.yaml: invalid fixture metadata (duplicate top-level interfaceRevision declarations)',
  ])
})

for (const text of ['null', 'true', 'false', '4', '"text"', '[]', '[{"interfaceRevision":4}]']) {
  test(`JSON root ${text} is refused as a non-mapping rather than optional YAML omission`, () => {
    const {run, report, fixtureErrors} = runFixture(text)
    assert.equal(run.status, 1)
    assert.equal(report.status, 'FAIL')
    assert.deepEqual(fixtureErrors, [
      'hlp.kernel.work-items: conformance/hlp.kernel.work-items/fixtures.yaml: invalid fixture metadata (expected a fixture mapping)',
    ])
  })
}

for (const [name, text] of [
  ['stale then current', '{"interfaceRevision":3,"interfaceRevision":4}'],
  ['two current declarations', '{"interfaceRevision":4,"interfaceRevision":4}'],
  ['escaped second key', String.raw`{"interfaceRevision":3,"\u0069nterfaceRevision":4}`],
  ['escaped first key', String.raw`{"\u0069nterfaceRevision":3,"interfaceRevision":4}`],
]) {
  test(`JSON duplicate root revision is refused: ${name}`, () => {
    const {run, report, fixtureErrors} = runFixture(text)
    assert.equal(run.status, 1)
    assert.equal(report.status, 'FAIL')
    assert.deepEqual(fixtureErrors, [
      'hlp.kernel.work-items: conformance/hlp.kernel.work-items/fixtures.yaml: invalid fixture metadata (duplicate top-level interfaceRevision declarations)',
    ])
  })
}

for (const [name, text] of [
  ['empty mapping retains optional omission', '{}'],
  ['one escaped revision key is one declaration', String.raw`{"\u0069nterfaceRevision":4}`],
  ['nested declarations are not root duplicates', '{"interfaceRevision":4,"nested":{"interfaceRevision":3,"interfaceRevision":4},"cases":[{"interfaceRevision":2}]}'],
  ['a nested key alone is not a root declaration', '{"cases":[{"interfaceRevision":3}]}'],
  ['a similar property name is not a revision key', '{"interfaceRevision":4,"interfaceRevisionNote":3}'],
  ['escaped quotes and braces in values are not property tokens', String.raw`{"note":"literal \"interfaceRevision\":3 { [ ] }","interfaceRevision":4}`],
]) {
  test(`JSON mapping control is admitted: ${name}`, () => {
    const {fixtureErrors} = runFixture(text)
    assert.deepEqual(fixtureErrors, [])
  })
}
