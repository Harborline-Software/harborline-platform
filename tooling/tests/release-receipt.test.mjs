// T-671. Every receipt under test here is emitted by the release path, except the ones that are
// deliberately authored to prove they are refused. Nothing reads a checked-in receipt document.
import assert from 'node:assert/strict'
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'

import {
  ARTIFACT_PATH, EVIDENCE_PATH, checkReleaseReceipt, emitReleaseReceipt, exportReceipt,
} from '../release-receipt.mjs'

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')

/** The canonical export idiom PlatformPackageExporter uses: digest over the digest-free bytes. */
function artifact(revision = '1.0.0', payload = {id: 'harborline.platform'}) {
  const unsigned = `${JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision, items: [payload]})}\n`
  const digest = sha256(Buffer.from(unsigned, 'utf8'))
  const body = JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision, items: [payload]})
  return `${body.slice(0, -1)},"digest":{"algorithm":"sha256","value":"${digest}"}}\n`
}

function release(options = {}) {
  const root = mkdtempSync(resolve(tmpdir(), 'harborline-release-receipt-'))
  const git = (...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8'}).trim()
  git('init', '--quiet')
  git('config', 'user.email', 'lane@harborline.test')
  git('config', 'user.name', 'lane')
  write(root, ARTIFACT_PATH, options.artifact ?? artifact())
  git('add', '.')
  git('commit', '--quiet', '-m', 'artifact')
  // The recorded run transcript names the commit that produced it, so it can only be written once
  // that commit exists -- which is why it is a second commit rather than part of the first.
  const recordedRun = git('rev-parse', 'HEAD')
  write(root, EVIDENCE_PATH, `${JSON.stringify({
    schemaVersion: 3,
    phase: 4,
    status: options.evidenceStatus ?? 'PASS',
    subject: {
      repository: 'harborline-platform',
      baseHead: options.recordedRun ?? recordedRun,
      testedTree: git('rev-parse', 'HEAD^{tree}'),
      mode: 'current-index',
    },
  }, null, 2)}\n`)
  git('add', '.')
  git('commit', '--quiet', '-m', 'evidence')
  return {root, git, dispose: () => { try { rmSync(root, {recursive: true, force: true}) } catch {} }}
}

function write(root, filePath, content) {
  const target = resolve(root, filePath)
  mkdirSync(resolve(target, '..'), {recursive: true})
  writeFileSync(target, content)
}

test('a release emits a receipt carrying every eng-7 field and the checker accepts it', () => {
  const {root, dispose} = release()
  try {
    const receipt = JSON.parse(emitReleaseReceipt(root).toString('utf8'))

    assert.equal(receipt.repository, 'harborline-platform')
    assert.match(receipt.commit, /^[0-9a-f]{40}$/)
    assert.match(receipt.tree, /^[0-9a-f]{40}$/)
    assert.equal(receipt.artifact.path, ARTIFACT_PATH)
    assert.match(receipt.artifact.digest, /^[0-9a-f]{64}$/)
    assert.equal(receipt.pack.version, '1.0.0')
    assert.match(receipt.pack.seedDigest, /^[0-9a-f]{64}$/)
    assert.equal(receipt.evidence.path, EVIDENCE_PATH)
    assert.match(receipt.evidence.run.baseHead, /^[0-9a-f]{40}$/)
    assert.equal(receipt.transcript.baseHead, receipt.commit)
    assert.equal(checkReleaseReceipt(root, emitReleaseReceipt(root)), null)
  } finally { dispose() }
})

test('a receipt naming a commit the repository does not hold is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, commit: '0'.repeat(40)})

    assert.deepEqual(checkReleaseReceipt(root, forged),
      {code: 'release-receipt-commit-not-in-repository', field: 'commit'})
  } finally { dispose() }
})

test('a receipt whose digest does not match the artefact is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, artifact: {...emitted.artifact, digest: 'f'.repeat(64)}})

    assert.deepEqual(checkReleaseReceipt(root, forged),
      {code: 'release-receipt-artifact-digest-mismatch', field: 'artifact.digest'})
  } finally { dispose() }
})

test('a receipt naming a pack version the artefact does not carry is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, pack: {...emitted.pack, version: '9.9.9'}})

    assert.deepEqual(checkReleaseReceipt(root, forged),
      {code: 'release-receipt-pack-version-mismatch', field: 'pack.version'})
  } finally { dispose() }
})

test('an artefact whose stamped digest is not true of its own bytes is refused naming the field', () => {
  // This is the drift that would let the release receipt and T-586's install receipt name
  // different digests. It is refused here; the other direction -- a self-consistent artefact that
  // is not what the seed code produces -- is refused by VerifyCheckedInExport under native-tests.
  const tampered = artifact().replace('"revision":"1.0.0"', '"revision":"1.0.1"')
  const {root, dispose} = release({artifact: tampered})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root)),
      {code: 'release-receipt-seed-digest-not-self-consistent', field: 'pack.seedDigest'})
  } finally { dispose() }
})

test('a receipt whose recorded run is not in the repository is refused naming the field', () => {
  const {root, dispose} = release({recordedRun: '1'.repeat(40)})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root)),
      {code: 'release-receipt-evidence-run-not-in-repository', field: 'evidence.run.baseHead'})
  } finally { dispose() }
})

test('a receipt whose evidence is not a pass is refused naming the field', () => {
  const {root, dispose} = release({evidenceStatus: 'FAIL'})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root)),
      {code: 'release-receipt-evidence-not-a-pass', field: 'evidence.run'})
  } finally { dispose() }
})

test('an edited receipt is refused on its own digest', () => {
  const {root, dispose} = release()
  try {
    const edited = Buffer.from(emitReleaseReceipt(root).toString('utf8').replace('"1.0.0"', '"1.0.1"'), 'utf8')

    assert.deepEqual(checkReleaseReceipt(root, edited),
      {code: 'release-receipt-digest-mismatch', field: 'digest'})
  } finally { dispose() }
})

test('a hand-written receipt is refused where a receipt is presented rather than emitted', () => {
  const {root, dispose} = release()
  try {
    // Authored from true values: every field below matches the repository, so nothing that checks
    // a field against its referent can refuse it. What it cannot produce is a phase-4 receipt
    // corroborating a run, because only the receipt runner writes one.
    const authored = exportReceipt(JSON.parse(emitReleaseReceipt(root).toString('utf8')))

    assert.equal(checkReleaseReceipt(root, authored), null)
    assert.deepEqual(checkReleaseReceipt(root, authored, {requireAttestation: true}),
      {code: 'release-receipt-transcript-unattested', field: 'transcript'})
  } finally { dispose() }
})

test('an attested receipt whose run is not the recorded one is refused naming the field', () => {
  const {root, git, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const gate = {schemaVersion: 3, phase: 4, status: 'PASS', subject: {mode: 'exact-staged-tree'}}
    writeFileSync(resolve(root, git('rev-parse', '--git-dir'), 'harborline-phase4-receipt.json'), `${JSON.stringify({
      schemaVersion: 3,
      repository: 'harborline-platform',
      baseHead: '2'.repeat(40),
      testedTree: emitted.tree,
      reportSha256: sha256(JSON.stringify(gate)),
      gate,
    }, null, 2)}\n`)

    assert.deepEqual(checkReleaseReceipt(root, exportReceipt(emitted), {requireAttestation: true}),
      {code: 'release-receipt-transcript-not-the-recorded-run', field: 'transcript.baseHead'})
  } finally { dispose() }
})

test('an attested receipt matching the recorded run is accepted', () => {
  const {root, git, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const gate = {schemaVersion: 3, phase: 4, status: 'PASS', subject: {mode: emitted.transcript.mode}}
    writeFileSync(resolve(root, git('rev-parse', '--git-dir'), 'harborline-phase4-receipt.json'), `${JSON.stringify({
      schemaVersion: 3,
      repository: 'harborline-platform',
      baseHead: emitted.commit,
      testedTree: emitted.tree,
      reportSha256: sha256(JSON.stringify(gate)),
      gate,
    }, null, 2)}\n`)

    assert.equal(checkReleaseReceipt(root, exportReceipt(emitted), {requireAttestation: true}), null)
  } finally { dispose() }
})

test('an attested receipt whose gate report was altered after the run is refused', () => {
  const {root, git, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const gate = {schemaVersion: 3, phase: 4, status: 'PASS', subject: {mode: emitted.transcript.mode}}
    writeFileSync(resolve(root, git('rev-parse', '--git-dir'), 'harborline-phase4-receipt.json'), `${JSON.stringify({
      schemaVersion: 3,
      repository: 'harborline-platform',
      baseHead: emitted.commit,
      testedTree: emitted.tree,
      reportSha256: sha256(JSON.stringify({...gate, status: 'FAIL'})),
      gate,
    }, null, 2)}\n`)

    assert.deepEqual(checkReleaseReceipt(root, exportReceipt(emitted), {requireAttestation: true}),
      {code: 'release-receipt-transcript-report-altered', field: 'transcript'})
  } finally { dispose() }
})
