#!/usr/bin/env node
// The release receipt DES-0007 `platform-package-eng-7` owes and R1's done-when clause names.
//
// This is NOT T-586's install receipt, and the two must not blur. T-586's is written by an install
// on a node and answers "what did this node actually resolve?". This one is written by the release
// and answers "what exactly was released, from what source?". Where they name the same digest they
// must agree; `ReleaseReceiptAgreementTests` in hlp.blocks.builder-definitions holds that seam.
//
// Every field is checked against something outside the receipt. A field compared only to itself is
// decoration, and this receipt is R1's own completion evidence, so decoration here is worse than
// elsewhere. The referents:
//
//   commit           the repository. The object must exist and be a commit.
//   tree             the repository. The object must exist and be a tree.
//   artifact.digest  sha256 of the blob at <commit>:<artifact.path>, read with `git cat-file`.
//   pack.version     the `revision` inside that blob.
//   pack.seedDigest  the `digest.value` inside that blob, re-verified against the blob's own
//                    digest-free bytes, which is how PlatformPackageExporter computes it.
//   evidence.digest  sha256 of the blob at <commit>:docs/evidence/phase-4/gate.json.
//   evidence.run     that document's own subject; its baseHead and testedTree must be real objects.
//   transcript       the run emitting this receipt, and the phase-4 receipt when one is present.
//
// Nothing here trusts a value the receipt carries: every comparison re-derives from git or from the
// bytes git holds. Reading from git objects at the named commit, rather than from the working tree,
// is what makes these external -- you cannot name a commit that is not in the repository, and once
// you have named one, every blob digest under it is determined.
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {existsSync, readFileSync} from 'node:fs'
import path from 'node:path'

export const ARTIFACT_PATH = '_shared/packs/platform/platform-pack.export.json'
export const EVIDENCE_PATH = 'docs/evidence/phase-4/gate.json'
const REPOSITORY = 'harborline-platform'

const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')

function git(root, args, encoding = 'utf8') {
  return execFileSync('git', args, {cwd: root, encoding, maxBuffer: 256 * 1024 * 1024})
}

/** The object's type, or null when the repository does not hold it. */
function objectType(root, id) {
  if (!/^[0-9a-f]{40}$/.test(id ?? '')) return null
  try { return git(root, ['cat-file', '-t', id]).trim() } catch { return null }
}

/** The raw bytes of one path as the named commit holds it, or null when it holds no such path. */
function blobAt(root, commit, filePath) {
  try { return git(root, ['cat-file', 'blob', `${commit}:${filePath}`], null) } catch { return null }
}

/**
 * The published artifact's own identity, read the same way T-586's PublishedArtifact.FromExport
 * reads it, so the two receipts cannot be reading different rules over the same document.
 */
function artifactIdentity(bytes) {
  const document = JSON.parse(bytes.toString('utf8'))
  return {version: document.revision, seedDigest: document.digest?.value}
}

/**
 * Re-derives the digest PlatformPackageExporter stamped: sha256 over the canonical document with
 * its own `digest` member removed, which is the exact two-pass rule the exporter uses.
 */
function selfDigestOf(bytes) {
  const text = bytes.toString('utf8')
  const marker = text.lastIndexOf(',"digest":{')
  if (marker < 0) return null
  return sha256(Buffer.from(`${text.slice(0, marker)}}\n`, 'utf8'))
}

/** Emits the receipt for the release of the tree this process is running against. */
export function emitReleaseReceipt(root, {baseHead, testedTree, mode} = {}) {
  const commit = baseHead ?? git(root, ['rev-parse', 'HEAD']).trim()
  const tree = testedTree ?? git(root, ['write-tree']).trim()
  const artifact = blobAt(root, commit, ARTIFACT_PATH)
  if (!artifact) throw new Error(`release receipt: the commit holds no ${ARTIFACT_PATH}`)
  const evidence = blobAt(root, commit, EVIDENCE_PATH)
  if (!evidence) throw new Error(`release receipt: the commit holds no ${EVIDENCE_PATH}`)
  const identity = artifactIdentity(artifact)
  const recorded = JSON.parse(evidence.toString('utf8'))
  return exportReceipt({
    repository: REPOSITORY,
    commit,
    tree,
    artifact: {path: ARTIFACT_PATH, digest: sha256(artifact)},
    pack: {version: identity.version, seedDigest: identity.seedDigest},
    evidence: {
      path: EVIDENCE_PATH,
      digest: sha256(evidence),
      run: {baseHead: recorded.subject?.baseHead, testedTree: recorded.subject?.testedTree},
    },
    transcript: {
      baseHead: commit,
      testedTree: tree,
      mode: mode ?? (process.env.HARBORLINE_TESTED_TREE ? 'exact-staged-tree' : 'current-index'),
    },
  })
}

/**
 * A refusal naming the field that refused, or null when every field matched its referent.
 *
 * `requireAttestation` decides whether the phase-4 receipt must corroborate the run. It is the
 * ground on which a hand-written receipt is refused, and it is required where a receipt is
 * PRESENTED -- the release act, which has the phase-4 receipt in hand. The gate step does not
 * require it, because CI deliberately does not run the receipt runner (.github/workflows/verify.yml
 * says so) and because in the gate the receipt is emitted by the step itself, so there is no
 * presented document to distrust. Every other field is checked against git either way.
 */
export function checkReleaseReceipt(root, document, {requireAttestation = false} = {}) {
  const parsed = parseReceipt(document)
  if (parsed.refusal) return parsed.refusal
  const receipt = parsed.receipt

  if (receipt.repository !== REPOSITORY) return refuse('release-receipt-repository-mismatch', 'repository')
  if (objectType(root, receipt.commit) !== 'commit') return refuse('release-receipt-commit-not-in-repository', 'commit')
  if (objectType(root, receipt.tree) !== 'tree') return refuse('release-receipt-tree-not-in-repository', 'tree')

  const artifact = blobAt(root, receipt.commit, receipt.artifact.path)
  if (!artifact) return refuse('release-receipt-artifact-absent', `artifact.path`)
  if (receipt.artifact.digest !== sha256(artifact)) return refuse('release-receipt-artifact-digest-mismatch', 'artifact.digest')

  const identity = artifactIdentity(artifact)
  if (receipt.pack.version !== identity.version) return refuse('release-receipt-pack-version-mismatch', 'pack.version')
  if (receipt.pack.seedDigest !== identity.seedDigest) return refuse('release-receipt-seed-digest-mismatch', 'pack.seedDigest')
  // The artifact's stamped digest must also be true of its own bytes, so a document whose digest
  // member was edited to match a receipt is refused rather than believed.
  if (identity.seedDigest !== selfDigestOf(artifact)) return refuse('release-receipt-seed-digest-not-self-consistent', 'pack.seedDigest')

  const evidence = blobAt(root, receipt.commit, receipt.evidence.path)
  if (!evidence) return refuse('release-receipt-evidence-absent', 'evidence.path')
  if (receipt.evidence.digest !== sha256(evidence)) return refuse('release-receipt-evidence-digest-mismatch', 'evidence.digest')
  const recorded = JSON.parse(evidence.toString('utf8'))
  if (recorded.status !== 'PASS') return refuse('release-receipt-evidence-not-a-pass', 'evidence.run')
  if (receipt.evidence.run.baseHead !== recorded.subject?.baseHead) return refuse('release-receipt-evidence-run-mismatch', 'evidence.run.baseHead')
  if (receipt.evidence.run.testedTree !== recorded.subject?.testedTree) return refuse('release-receipt-evidence-run-mismatch', 'evidence.run.testedTree')
  // The recorded run must name objects this repository actually holds. The step list is
  // deliberately not compared against the current contract: the recorded run predates any step
  // added since, and demanding otherwise would fail the gate for the evidence being older.
  if (objectType(root, recorded.subject?.baseHead) !== 'commit') return refuse('release-receipt-evidence-run-not-in-repository', 'evidence.run.baseHead')
  if (objectType(root, recorded.subject?.testedTree) !== 'tree') return refuse('release-receipt-evidence-run-not-in-repository', 'evidence.run.testedTree')

  if (receipt.transcript.baseHead !== receipt.commit) return refuse('release-receipt-transcript-mismatch', 'transcript.baseHead')
  if (receipt.transcript.testedTree !== receipt.tree) return refuse('release-receipt-transcript-mismatch', 'transcript.testedTree')

  // When the release ran under the receipt runner, the phase-4 receipt is the authoritative record
  // of that run and the transcript must be the same run. run-phase-4-gate.mjs run directly stamps
  // `current-index`, and only the receipt runner's detached staged-tree run stamps
  // `exact-staged-tree`, so this is where a directly-minted release is told apart from a released
  // one. It proves which run; it does not prove freshness, and a signature would be needed for that.
  const phase4 = readPhase4Receipt(root)
  if (!phase4 && requireAttestation) return refuse('release-receipt-transcript-unattested', 'transcript')
  if (phase4) {
    if (receipt.transcript.baseHead !== phase4.baseHead) return refuse('release-receipt-transcript-not-the-recorded-run', 'transcript.baseHead')
    if (receipt.transcript.testedTree !== phase4.testedTree) return refuse('release-receipt-transcript-not-the-recorded-run', 'transcript.testedTree')
    if (receipt.transcript.mode !== phase4.gate?.subject?.mode) return refuse('release-receipt-transcript-mode-mismatch', 'transcript.mode')
    if (sha256(JSON.stringify(phase4.gate)) !== phase4.reportSha256) return refuse('release-receipt-transcript-report-altered', 'transcript')
  }
  return null
}

function readPhase4Receipt(root) {
  const gitDir = git(root, ['rev-parse', '--git-dir']).trim()
  const receiptPath = path.resolve(root, gitDir, 'harborline-phase4-receipt.json')
  if (!existsSync(receiptPath)) return null
  try { return JSON.parse(readFileSync(receiptPath, 'utf8')) } catch { return null }
}

const refuse = (code, field) => ({code, field})

/**
 * Parses and verifies the receipt reproduces its own canonical bytes, the same idiom
 * PlatformPackageExporter and T-586's receipt use. Any edit, including to the digest, refuses.
 */
export function parseReceipt(document) {
  const bytes = Buffer.isBuffer(document) ? document : Buffer.from(document)
  let receipt
  try { receipt = JSON.parse(bytes.toString('utf8')) } catch { return {refusal: refuse('release-receipt-malformed', 'schemaVersion')} }
  if (receipt?.schemaVersion !== 1) return {refusal: refuse('release-receipt-malformed', 'schemaVersion')}
  let canonical
  try { canonical = exportReceipt(receipt) } catch { return {refusal: refuse('release-receipt-malformed', 'schemaVersion')} }
  if (!canonical.equals(bytes)) return {refusal: refuse('release-receipt-digest-mismatch', 'digest')}
  return {receipt}
}

/** The canonical document: fixed property order, self-digest last, one trailing newline. */
export function exportReceipt(fields) {
  const body = {
    schemaVersion: 1,
    repository: fields.repository,
    commit: fields.commit,
    tree: fields.tree,
    artifact: {path: fields.artifact.path, digest: fields.artifact.digest},
    pack: {version: fields.pack.version, seedDigest: fields.pack.seedDigest},
    evidence: {
      path: fields.evidence.path,
      digest: fields.evidence.digest,
      run: {baseHead: fields.evidence.run.baseHead, testedTree: fields.evidence.run.testedTree},
    },
    transcript: {
      baseHead: fields.transcript.baseHead,
      testedTree: fields.transcript.testedTree,
      mode: fields.transcript.mode,
    },
  }
  const unsigned = Buffer.from(`${JSON.stringify(body, null, 2)}\n`, 'utf8')
  const signed = {...body, digest: {algorithm: 'sha256', value: sha256(unsigned)}}
  return Buffer.from(`${JSON.stringify(signed, null, 2)}\n`, 'utf8')
}

if (process.argv[1] && import.meta.url === new URL(`file://${process.argv[1].replaceAll('\\', '/')}`).href) {
  const root = path.resolve(import.meta.dirname, '..')
  // The gate emits from the live repository and checks what the release just produced. There is no
  // checked-in receipt to read, so there is no fixture this can be pointed at by mistake.
  const emitted = emitReleaseReceipt(root)
  const refusal = checkReleaseReceipt(root, emitted)
  if (refusal) {
    process.stdout.write(`${JSON.stringify({status: 'FAIL', ...refusal}, null, 2)}\n`)
    process.exitCode = 1
  } else {
    const receipt = JSON.parse(emitted.toString('utf8'))
    process.stdout.write(`${JSON.stringify({status: 'PASS', commit: receipt.commit, artifactDigest: receipt.artifact.digest, packVersion: receipt.pack.version, mode: receipt.transcript.mode}, null, 2)}\n`)
  }
}
