import assert from 'node:assert/strict'
import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync, cpSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'
import { writeLibraryProof, verifyLibraryProof } from '../library-promotion-proof.mjs'

const identity = { source: 'a'.repeat(40), repository: 'Harborline-Software/harborline-platform',
  run: '123', attempt: '1', workflow: 'validate.yml@refs/heads/main' }
const id = 'Harborline.Foundation'
const version = '0.1.0-preview.probe'
// Published SHA-256 test vector for the literal ASCII bytes "abc".
const sha256 = 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad'
const manifest = [{ id, version, sha256 }]

function assertPublicationProofOrder(workflow) {
  const verifier = workflow.indexOf('run: node tooling/library-promotion-proof.mjs')
  const attestation = workflow.indexOf('subject-path:')
  const push = workflow.indexOf('dotnet nuget push')
  assert.ok(verifier >= 0, 'publication verifier is required')
  assert.ok(attestation >= 0, 'attestation is required')
  assert.ok(push >= 0, 'publication push is required')
  assert.ok(verifier < attestation, 'proof must precede attestation')
  assert.ok(verifier < push, 'proof must precede push')
}

function assertConsumerProofOrder(producer) {
  const consumer = producer.indexOf('NuGet consumer Harborline closure mismatch')
  const proof = producer.indexOf('writeLibraryProof(nugetArtifacts')
  assert.ok(consumer >= 0, 'consumer closure assertion is required')
  assert.ok(proof >= 0, 'consumption proof writer is required')
  assert.ok(proof > consumer, 'proof must follow the consumer closure assertion')
}

function fixture(t) {
  const root = mkdtempSync(resolve(tmpdir(), 'library-proof-test-'))
  t.after(() => rmSync(root, { recursive: true, force: true }))
  const feed = resolve(root, 'feed')
  const cache = resolve(root, 'cache')
  const cached = resolve(cache, id.toLowerCase(), version, `${id.toLowerCase()}.${version}.nupkg`)
  mkdirSync(feed, { recursive: true })
  mkdirSync(resolve(cached, '..'), { recursive: true })
  const staged = resolve(feed, `${id}.${version}.nupkg`)
  writeFileSync(staged, 'abc')
  writeFileSync(cached, 'abc')
  return { root, feed, cache, cached, staged }
}

test('exact staged and restored hashes permit a copied bundle in the same run', t => {
  const { root, feed, cache } = fixture(t)
  writeLibraryProof(feed, cache, manifest, identity)
  const downloaded = resolve(root, 'downloaded')
  cpSync(feed, downloaded, { recursive: true })
  verifyLibraryProof(downloaded, [id], version, identity)
  assert.equal(JSON.parse(readFileSync(resolve(downloaded, 'consumer-proof.json'))).packages[0].sha256, sha256)
})

test('same-ID/version replacement in the isolated cache cannot earn proof', t => {
  const { feed, cache, cached } = fixture(t)
  writeFileSync(cached, 'poison')
  assert.throws(() => writeLibraryProof(feed, cache, manifest, identity), /restored bytes differ/)
  assert.throws(() => readFileSync(resolve(feed, 'consumer-proof.json')), /ENOENT/)
})

test('missing/tampered staged bytes and incorrect inventory block promotion', t => {
  const { feed, cache, staged } = fixture(t)
  writeLibraryProof(feed, cache, manifest, identity)
  assert.throws(() => verifyLibraryProof(feed, ['Harborline.Other'], version, identity), /inventory mismatch/)
  writeFileSync(staged, 'tampered')
  assert.throws(() => verifyLibraryProof(feed, [id], version, identity), /package changed/)
  rmSync(staged)
  assert.throws(() => verifyLibraryProof(feed, [id], version, identity), /staged files mismatch/)
})

test('wrong manifest, source, workflow, run and attempt cannot promote another proof', t => {
  const { feed, cache } = fixture(t)
  writeLibraryProof(feed, cache, manifest, identity)
  for (const key of ['source', 'repository', 'workflow', 'run', 'attempt']) {
    assert.throws(() => verifyLibraryProof(feed, [id], version, { ...identity, [key]: 'wrong' }), /source\/run mismatch/)
  }
  writeFileSync(resolve(feed, 'manifest.json'), JSON.stringify([{ ...manifest[0], sha256: '0'.repeat(64) }]))
  assert.throws(() => verifyLibraryProof(feed, [id], version, identity), /package mismatch/)
})

test('publication proof follows the real consumer and closure assertions, and precedes attest/push', () => {
  const producer = readFileSync(resolve(import.meta.dirname, '../verify-package-fixtures.mjs'), 'utf8')
  const workflow = readFileSync(resolve(import.meta.dirname, '../../.github/workflows/validate.yml'), 'utf8')
  assertConsumerProofOrder(producer)
  assertPublicationProofOrder(workflow)
})

test('deleting any required publication marker is refused before comparing order', () => {
  const workflow = readFileSync(resolve(import.meta.dirname, '../../.github/workflows/validate.yml'), 'utf8')
  const deleted = workflow.replace(
    /      - name: Verify exact consumed bytes before attestation and publication\n        run: node tooling\/library-promotion-proof\.mjs\n/,
    '')
  assert.notEqual(deleted, workflow, 'the planted deletion must remove the verifier step')
  assert.throws(() => assertPublicationProofOrder(deleted), /publication verifier is required/)
  for (const [marker, refusal] of [['subject-path:', /attestation is required/],
    ['dotnet nuget push', /publication push is required/]]) {
    const removed = workflow.replace(marker, '')
    assert.notEqual(removed, workflow, `planted deletion must remove ${marker}`)
    assert.throws(() => assertPublicationProofOrder(removed), refusal)
  }
})

test('deleting either consumer/proof marker is refused before comparing order', () => {
  const producer = readFileSync(resolve(import.meta.dirname, '../verify-package-fixtures.mjs'), 'utf8')
  for (const [marker, refusal] of [
    ['NuGet consumer Harborline closure mismatch', /consumer closure assertion is required/],
    ['writeLibraryProof(nugetArtifacts', /consumption proof writer is required/],
  ]) {
    const removed = producer.replace(marker, '')
    assert.notEqual(removed, producer, `planted deletion must remove ${marker}`)
    assert.throws(() => assertConsumerProofOrder(removed), refusal)
  }
})
