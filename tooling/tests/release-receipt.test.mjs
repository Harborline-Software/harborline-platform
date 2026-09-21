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

// The gate step's setting. Attestation and current design reviews both default ON, so every check
// that is about neither opts out explicitly here, exactly as the gate step does.
const asGateStep = {requireAttestation: false, requireCurrentDesignReviews: false}

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
    ...('designReview' in options ? {designReview: options.designReview} : {designReview: {status: 'PASS', expired: [], failingExpired: []}}),
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
    assert.equal(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep), null)
  } finally { dispose() }
})

test('a receipt naming a commit the repository does not hold is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, commit: '0'.repeat(40)})

    assert.deepEqual(checkReleaseReceipt(root, forged, asGateStep),
      {code: 'release-receipt-commit-not-in-repository', field: 'commit'})
  } finally { dispose() }
})

test('a receipt whose digest does not match the artefact is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, artifact: {...emitted.artifact, digest: 'f'.repeat(64)}})

    assert.deepEqual(checkReleaseReceipt(root, forged, asGateStep),
      {code: 'release-receipt-artifact-digest-mismatch', field: 'artifact.digest'})
  } finally { dispose() }
})

test('a receipt naming a pack version the artefact does not carry is refused naming the field', () => {
  const {root, dispose} = release()
  try {
    const emitted = JSON.parse(emitReleaseReceipt(root).toString('utf8'))
    const forged = exportReceipt({...emitted, pack: {...emitted.pack, version: '9.9.9'}})

    assert.deepEqual(checkReleaseReceipt(root, forged, asGateStep),
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
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep),
      {code: 'release-receipt-seed-digest-not-self-consistent', field: 'pack.seedDigest'})
  } finally { dispose() }
})

test('a receipt whose recorded run is not in the repository is refused naming the field', () => {
  const {root, dispose} = release({recordedRun: '1'.repeat(40)})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep),
      {code: 'release-receipt-evidence-run-not-in-repository', field: 'evidence.run.baseHead'})
  } finally { dispose() }
})

test('a receipt whose evidence is not a pass is refused naming the field', () => {
  const {root, dispose} = release({evidenceStatus: 'FAIL'})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep),
      {code: 'release-receipt-evidence-not-a-pass', field: 'evidence.run'})
  } finally { dispose() }
})

test('an edited receipt is refused on its own digest', () => {
  const {root, dispose} = release()
  try {
    const edited = Buffer.from(emitReleaseReceipt(root).toString('utf8').replace('"1.0.0"', '"1.0.1"'), 'utf8')

    assert.deepEqual(checkReleaseReceipt(root, edited, asGateStep),
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

    assert.equal(checkReleaseReceipt(root, authored, asGateStep), null)
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

// The three below are regressions for defects found while writing this module's own explanation.
// Each is a way the checker could return, or appear to return, a pass on something it never read.

test('attestation is required by default, so a caller that forgets the flag fails closed', () => {
  const {root, dispose} = release()
  try {
    // No options at all. A future consumer reading a stored receipt back as evidence gets the
    // strict behaviour by omission rather than the weak one.
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root)),
      {code: 'release-receipt-transcript-unattested', field: 'transcript'})
  } finally { dispose() }
})

test('a phase-4 receipt that is present but unreadable is refused under both settings', () => {
  const {root, git, dispose} = release()
  try {
    writeFileSync(resolve(root, git('rev-parse', '--git-dir'), 'harborline-phase4-receipt.json'), '{ this is not json')
    const expected = {code: 'release-receipt-transcript-unreadable', field: 'transcript'}

    // Collapsing "present but corrupt" into "absent" is what made this pass before: under the
    // gate step's setting the attestation block was skipped entirely and the checker returned a
    // pass on a file it had failed to read.
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep), expected)
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), {requireAttestation: true}), expected)
  } finally { dispose() }
})

test('an unreadable artefact is refused naming the field rather than thrown', () => {
  const {root, dispose} = release({artifact: 'not a document at all\n'})
  try {
    const emitted = exportReceipt({
      repository: 'harborline-platform',
      commit: '0'.repeat(40),
      tree: '0'.repeat(40),
      artifact: {path: ARTIFACT_PATH, digest: '0'.repeat(64)},
      pack: {version: '1.0.0', seedDigest: '0'.repeat(64)},
      evidence: {path: EVIDENCE_PATH, digest: '0'.repeat(64), run: {baseHead: '0'.repeat(40), testedTree: '0'.repeat(40)}},
      transcript: {baseHead: '0'.repeat(40), testedTree: '0'.repeat(40), mode: 'current-index'},
    })

    // The commit refuses first, which is the point: every refusal names a field, and none of them
    // is an uncaught JSON parse escaping as a stack trace.
    assert.deepEqual(checkReleaseReceipt(root, emitted, asGateStep),
      {code: 'release-receipt-commit-not-in-repository', field: 'commit'})
    assert.throws(() => emitReleaseReceipt(root), /not a readable document/)
  } finally { dispose() }
})

// T-631, the owner's ruling of 2026-09-20. The 51 design reviews are owed before R1 is released
// publicly, not by a date, and this receipt is where R1's done-when clause binds the obligation.
test('a release whose tree carries an expired catalogued design review is refused naming the field', () => {
  const {root, dispose} = release({designReview: {status: 'PASS', expired: ['hlp.ui.window'], failingExpired: []}})
  try {
    // status is PASS: the gate is green, because the module is amnestied for MERGING. That is
    // exactly the tree this refusal exists for -- a red check can be waited out, this cannot.
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), {requireAttestation: false}),
      {code: 'release-receipt-design-review-expired', field: 'evidence.run.designReview'})
  } finally { dispose() }
})

test('current design reviews are required by default, so a caller that forgets the flag fails closed', () => {
  const {root, dispose} = release({designReview: {status: 'PASS', expired: ['hlp.ui.window'], failingExpired: []}})
  try {
    // Refused before attestation is even reached: a release is not licensed by a missing flag.
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root)),
      {code: 'release-receipt-design-review-expired', field: 'evidence.run.designReview'})
  } finally { dispose() }
})

test('the gate step tolerates the amnestied set, and accepts a tree with none expired', () => {
  const {root, dispose} = release({designReview: {status: 'PASS', expired: ['hlp.ui.window'], failingExpired: []}})
  try {
    assert.equal(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep), null)
  } finally { dispose() }

  const clean = release()
  try {
    assert.equal(checkReleaseReceipt(clean.root, emitReleaseReceipt(clean.root), {requireAttestation: false}), null)
  } finally { clean.dispose() }
})

test('a recorded run carrying no design-review finding at all is refused, not read as none expired', () => {
  // Absent is not empty. run-phase-4-gate.mjs writes the block from the ui-gate-model step, so a
  // recording without that step has no block -- and a checker that returns a pass on an input it
  // never read is the defect this file's own header is about.
  const {root, dispose} = release({designReview: undefined})
  try {
    assert.deepEqual(checkReleaseReceipt(root, emitReleaseReceipt(root), {requireAttestation: false}),
      {code: 'release-receipt-design-review-unrecorded', field: 'evidence.run.designReview'})
    assert.equal(checkReleaseReceipt(root, emitReleaseReceipt(root), asGateStep), null)
  } finally { dispose() }
})
