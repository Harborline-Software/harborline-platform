// Recording React review baselines through the MATRIX flow (--shard=i/n --record, then
// --merge=<dir> --record), and ADDITIVE recording (--record-add). The unsharded gallery step can
// exceed its step budget on a host, and --record used to refuse shards, so a recording could not be
// made there at all. These cases hold the safety properties that refusal used to provide: a partial
// capture set is never recorded, a subset run never records, and an additive recording never touches
// an existing baseline.
import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join, resolve} from 'node:path'
import test from 'node:test'

import {
  captureFileName, readBaselineCaptures, recordGalleryBaselines, unionShardBaselineCaptures, writeBaselineCaptures,
} from '../gallery-baseline-store.mjs'
import {argumentProblems, collectShardReports, gateArgumentProblem, recordingProvenanceProblem, requiredGalleryStepIds, screenshotsPerturbLayout, shardProvenance} from '../run-gallery-gate.mjs'
import {expectedChecks} from '../gallery-observations.mjs'

const root = resolve(import.meta.dirname, '../..')
const catalogs = [
  {moduleId: 'hlp.ui.example', scenarios: [{id: 'example.one'}, {id: 'example.two'}, {id: 'example.three'}]},
  {moduleId: 'hlp.ui.other', scenarios: [{id: 'other.one'}]},
]
// Distinct, valid-signature PNG bytes per scenario, so content addressing tells them apart.
const png = label => Buffer.concat([Buffer.from('89504e470d0a1a0a', 'hex'), Buffer.from(label)])
const capture = (moduleId, scenarioId, bytes = png(scenarioId)) => ({
  fileName: captureFileName(moduleId, scenarioId),
  sha256: createHash('sha256').update(bytes).digest('hex'),
  base64: bytes.toString('base64'),
})
const shard = (index, captures) => ({shard: index, shards: 2, baselineCaptures: captures})
const everyCapture = catalogs.flatMap(catalog => catalog.scenarios.map(scenario => capture(catalog.moduleId, scenario.id)))

function scratch() {
  const directory = mkdtempSync(join(tmpdir(), 'hlp-record-matrix-'))
  return {directory, repositoryRoot: join(directory, 'repository'), captureRoot: join(directory, 'captures')}
}

test('--record is allowed on both halves of a matrix run, and a subset still never records', () => {
  const env = {HARBORLINE_GALLERY_RUN_ID: 'run-1'}
  assert.equal(gateArgumentProblem({record: true, shard: {index: 1, total: 3}, env}), undefined)
  assert.match(gateArgumentProblem({record: true, shard: {index: 1, total: 3}, env: {}}), /requires HARBORLINE_GALLERY_RUN_ID/)
  assert.equal(gateArgumentProblem({record: true, merge: 'dir'}), undefined)
  assert.equal(gateArgumentProblem({record: true, merge: 'dir', recordAdditions: ['a.b']}), undefined)
  assert.match(gateArgumentProblem({record: true, focusedPattern: 'popover'}), /GALLERY_GREP is not allowed/)
  assert.match(gateArgumentProblem({shard: {index: 1, total: 3}, focusedPattern: 'popover'}), /GALLERY_GREP is not allowed/)
  assert.match(gateArgumentProblem({record: true, shard: {index: 1, total: 3}, focusedPattern: 'x'}), /GALLERY_GREP is not allowed/)
  assert.match(gateArgumentProblem({shard: {index: 1, total: 3}, merge: 'dir'}), /two halves of a matrix run/)
  assert.match(gateArgumentProblem({recordAdditions: ['a.b']}), /--record-add requires --record/)
})

test('the union of every shard is the complete capture set, and records like an unsharded run', () => {
  const {directory, repositoryRoot, captureRoot} = scratch()
  try {
    const union = unionShardBaselineCaptures([shard(1, everyCapture.slice(0, 2)), shard(2, everyCapture.slice(2))])
    assert.equal(union.size, everyCapture.length)
    writeBaselineCaptures(captureRoot, union)
    assert.deepEqual(readBaselineCaptures(captureRoot), [...everyCapture].sort((a, b) => a.fileName.localeCompare(b.fileName)))
    const recorded = recordGalleryBaselines({repositoryRoot, captureRoot, catalogs})
    assert.equal(recorded.scenarios, 4)
    assert.equal(recorded.manifests, 2)
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('a shard that did not record, or lost a capture, is refused and nothing is recorded', () => {
  assert.throws(() => unionShardBaselineCaptures([shard(1, everyCapture.slice(0, 2)), {shard: 2, shards: 2}]),
    /gallery shard 2 carries no baseline captures; every shard of a recording matrix must run with --record/)
  const {directory, repositoryRoot, captureRoot} = scratch()
  try {
    // Shard 2 recorded, but shipped one capture short: the union is incomplete, and the store refuses
    // it exactly as it refuses an incomplete unsharded capture.
    writeBaselineCaptures(captureRoot, unionShardBaselineCaptures([shard(1, everyCapture.slice(0, 2)), shard(2, everyCapture.slice(3))]))
    assert.throws(() => recordGalleryBaselines({repositoryRoot, captureRoot, catalogs}),
      /capture set mismatch; missing=\["hlp\.ui\.example--example\.three\.png"\]/)
    assert.equal(existsSync(join(repositoryRoot, 'specs')), false, 'nothing was written')
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('a capture that does not match its digest, or that two shards disagree on, is refused', () => {
  const forged = {...capture('hlp.ui.example', 'example.one'), sha256: '0'.repeat(64)}
  assert.throws(() => unionShardBaselineCaptures([shard(1, [forged])]), /does not match its digest/)
  const other = capture('hlp.ui.example', 'example.one', png('different pixels'))
  assert.throws(() => unionShardBaselineCaptures([shard(1, [capture('hlp.ui.example', 'example.one')]), shard(2, [other])]),
    /shards disagree on hlp\.ui\.example--example\.one\.png/)
  assert.throws(() => unionShardBaselineCaptures([shard(1, [{...capture('hlp.ui.example', 'example.one'), fileName: '../x.png'}])]),
    /unsafe capture name/)
})

test('additive recording adds only the named scenarios and leaves every existing baseline byte-for-byte', () => {
  const {directory, repositoryRoot, captureRoot} = scratch()
  try {
    // An existing store recorded on some OTHER host: different bytes for example.one and example.three,
    // plus an unreferenced PNG that a full recording would delete.
    const existing = [capture('hlp.ui.example', 'example.one', png('other host one')), capture('hlp.ui.example', 'example.three', png('other host three'))]
    const storeRoot = join(repositoryRoot, 'specs/modules/ui/baselines/sha256')
    mkdirSync(storeRoot, {recursive: true})
    for (const item of existing) writeFileSync(join(storeRoot, `${item.sha256}.png`), Buffer.from(item.base64, 'base64'))
    writeFileSync(join(storeRoot, 'stray.png'), png('stray'))
    const manifestPath = join(repositoryRoot, 'specs/modules/ui/hlp.ui.example/react-baselines.json')
    mkdirSync(join(repositoryRoot, 'specs/modules/ui/hlp.ui.example'), {recursive: true})
    const entry = (scenarioId, item) => ({scenarioId, sha256: item.sha256, bytes: Buffer.from(item.base64, 'base64').length, artifact: `../baselines/sha256/${item.sha256}.png`})
    writeFileSync(manifestPath, `${JSON.stringify({schemaVersion: 1, projection: 'react', moduleId: 'hlp.ui.example',
      scenarios: [entry('example.one', existing[0]), entry('example.three', existing[1])]}, null, 2)}\n`)
    const before = readFileSync(manifestPath, 'utf8')

    writeBaselineCaptures(captureRoot, unionShardBaselineCaptures([shard(1, everyCapture)]))
    const recorded = recordGalleryBaselines({repositoryRoot, captureRoot, catalogs, additions: ['example.two', 'other.one']})
    assert.equal(recorded.mode, 'additive')
    assert.deepEqual(recorded.added.map(item => item.scenarioId), ['example.two', 'other.one'])

    const after = JSON.parse(readFileSync(manifestPath, 'utf8'))
    // Inserted in catalog order, existing entries untouched.
    assert.deepEqual(after.scenarios.map(item => item.scenarioId), ['example.one', 'example.two', 'example.three'])
    assert.deepEqual([after.scenarios[0], after.scenarios[2]], JSON.parse(before).scenarios)
    assert.equal(after.scenarios[1].sha256, everyCapture[1].sha256)
    // A module with no manifest gets one holding only its addition.
    const created = JSON.parse(readFileSync(join(repositoryRoot, 'specs/modules/ui/hlp.ui.other/react-baselines.json'), 'utf8'))
    assert.deepEqual(created.scenarios.map(item => item.scenarioId), ['other.one'])
    // Nothing deleted: the existing PNGs and the unreferenced one are all still there.
    const stored = new Set(readdirSync(storeRoot))
    for (const item of existing) assert.ok(stored.has(`${item.sha256}.png`))
    assert.ok(stored.has('stray.png'))
    assert.equal(stored.has(`${everyCapture[0].sha256}.png`), false, 'this host\'s example.one capture was not stored')

    assert.throws(() => recordGalleryBaselines({repositoryRoot, captureRoot, catalogs, additions: ['example.one']}),
      /refuses to overwrite the existing example\.one baseline/)
    assert.throws(() => recordGalleryBaselines({repositoryRoot, captureRoot, catalogs, additions: ['example.nine']}),
      /outside the catalog/)
    // b19 review: a refusal anywhere in the list writes nothing, not even the PNGs of the ids before it.
    const storedBefore = readdirSync(storeRoot).sort()
    const manifestBefore = readFileSync(manifestPath, 'utf8')
    rmSync(join(repositoryRoot, 'specs/modules/ui/hlp.ui.other/react-baselines.json'))
    writeFileSync(join(captureRoot, captureFileName('hlp.ui.other', 'other.one')), png('fresh other'))
    assert.throws(() => recordGalleryBaselines({repositoryRoot, captureRoot, catalogs, additions: ['other.one', 'example.one']}),
      /refuses to overwrite the existing example\.one baseline/)
    assert.deepEqual(readdirSync(storeRoot).sort(), storedBefore, 'no orphan PNG from the refused batch')
    assert.equal(readFileSync(manifestPath, 'utf8'), manifestBefore)
    assert.equal(existsSync(join(repositoryRoot, 'specs/modules/ui/hlp.ui.other/react-baselines.json')), false)
    rmSync(join(captureRoot, captureFileName('hlp.ui.other', 'other.one')))
    assert.throws(() => recordGalleryBaselines({repositoryRoot, captureRoot, catalogs, additions: ['example.two']}),
      /capture set mismatch/, 'an addition is only ever recorded from a complete capture set')
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('the recording collector refuses, by name, a matrix whose shard did not record', () => {
  // The real entry point, no browsers: --merge only reads shard reports.
  const directory = mkdtempSync(join(tmpdir(), 'hlp-record-merge-'))
  try {
    const passing = index => ({
      schemaVersion: 1, kind: 'gallery-shard', shard: index, shards: 2, passed: true,
      results: requiredGalleryStepIds.map(id => ({id, passed: true, durationMs: 1})),
      playwrightReport: null,
      provenance: {galleryHostKey: 'macos-x64-intel', sourceHead: 'a'.repeat(40), runId: 'run-1'},
      ...(index === 1 ? {baselineCaptures: []} : {}),
    })
    for (const index of [1, 2]) writeFileSync(join(directory, `gallery-shard-${index}.json`), JSON.stringify(passing(index)))
    const run = spawnSync(process.execPath, ['tooling/run-gallery-gate.mjs', `--merge=${directory}`, '--record'], {cwd: root, encoding: 'utf8'})
    assert.equal(run.status, 1)
    const report = JSON.parse(run.stdout)
    assert.equal(report.mode, 'record')
    assert.equal(report.status, 'FAIL')
    assert.match(report.failure, /gallery shard 2 carries no baseline captures/)
    assert.equal(report.baselineRecording, undefined)
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})

test('a recording run, sharded or not, declares no per-scenario reflow, as a capture run does', () => {
  assert.equal(screenshotsPerturbLayout({captureGallery: false, recordBaselines: false}), false)
  assert.equal(screenshotsPerturbLayout({captureGallery: true, recordBaselines: false}), true)
  assert.equal(screenshotsPerturbLayout({captureGallery: false, recordBaselines: true}), true)
  const modules = [{moduleId: 'hlp.ui.button', locales: 1, themes: 2, scenarios: [{id: 'button.a', sourceQualityCaseIds: ['x.quality.reflow']}]}]
  const plain = expectedChecks(modules, {capture: screenshotsPerturbLayout({recordBaselines: false})}).reflowChecks
  const recording = expectedChecks(modules, {capture: screenshotsPerturbLayout({recordBaselines: true})}).reflowChecks
  assert.equal(plain - recording, 2, 'one per-scenario reflow assertion per lane is not declared while recording')
})

test('an argument the gate does not know, or a malformed --record-add, is refused before anything runs', () => {
  assert.deepEqual(argumentProblems(['--packages-ready', '--record', '--capture', '--shard=1/3', '--merge=dir', '--record-add=a.b,c.d']), [])
  for (const argv of [['--record-add', 'a.b'], ['--record-add='], ['--record-add=,'], ['--record-add= a.b'], ['--record-ad=a.b'], ['--recordadd=a.b'],
    ['--shard', '1/3'], ['--merge='], ['--packages-redy'], ['stray']]) {
    assert.ok(argumentProblems(argv).length > 0, `${argv.join(' ')} must be refused`)
  }
  assert.match(argumentProblems(['--record-add', 'a.b'])[0], /malformed "--record-add"/)
  // The real entry point: a space-separated --record-add stops the gate, and no report is produced.
  const run = spawnSync(process.execPath, ['tooling/run-gallery-gate.mjs', '--merge=/nonexistent', '--record', '--record-add', 'popover.inside-dialog'], {cwd: root, encoding: 'utf8'})
  assert.notEqual(run.status, 0)
  assert.equal(run.stdout, '')
  assert.match(run.stderr, /malformed "--record-add"/)
})

test('shard reports carry host, source HEAD and run id; a recording matrix refuses any disagreement', () => {
  const provenance = shardProvenance({env: {HARBORLINE_GALLERY_RUN_ID: 'run-7'}, head: 'a'.repeat(40)})
  assert.equal(provenance.runId, 'run-7')
  assert.equal(provenance.sourceHead, 'a'.repeat(40))
  assert.match(provenance.galleryHostKey, /^(windows-11-|macos-|linux-)/)
  const report = (shard, overrides = {}) => ({shard, provenance: {...provenance, ...overrides}})
  assert.equal(recordingProvenanceProblem([report(1), report(2), report(3)]), undefined)
  assert.match(recordingProvenanceProblem([report(1), report(2, {galleryHostKey: 'windows-11-x64'})]), /disagree on galleryHostKey/)
  assert.match(recordingProvenanceProblem([report(1), report(2, {sourceHead: 'b'.repeat(40)})]), /disagree on sourceHead/)
  assert.match(recordingProvenanceProblem([report(1), report(2, {runId: 'run-8'})]), /disagree on runId/)
  assert.match(recordingProvenanceProblem([report(1), {shard: 2}]), /needs galleryHostKey on every shard; shard\(s\) 2/)
})

test('a matrix that repeats a shard index is refused, not silently collapsed', () => {
  assert.throws(() => collectShardReports([{shard: 1, shards: 2}, {shard: 2, shards: 2}, {shard: 2, shards: 2}]), /repeat shard 2/)
})

test('the recording collector refuses shards from different runs, by name', () => {
  const directory = mkdtempSync(join(tmpdir(), 'hlp-record-provenance-'))
  try {
    for (const index of [1, 2]) {
      writeFileSync(join(directory, `gallery-shard-${index}.json`), JSON.stringify({
        schemaVersion: 1, kind: 'gallery-shard', shard: index, shards: 2, passed: true,
        provenance: {galleryHostKey: 'macos-x64-intel', sourceHead: 'a'.repeat(40), runId: `run-${index}`},
        results: requiredGalleryStepIds.map(id => ({id, passed: true, durationMs: 1})),
        playwrightReport: null, baselineCaptures: [],
      }))
    }
    const run = spawnSync(process.execPath, ['tooling/run-gallery-gate.mjs', `--merge=${directory}`, '--record'], {cwd: root, encoding: 'utf8'})
    assert.equal(run.status, 1)
    const out = JSON.parse(run.stdout)
    assert.match(out.failure, /recording shards disagree on runId: "run-1" vs "run-2"/)
    assert.equal(out.baselineRecording, undefined)
  } finally {
    rmSync(directory, {recursive: true, force: true})
  }
})
