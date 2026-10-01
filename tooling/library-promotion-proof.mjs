// Same-run exact-byte consumption evidence; existing release attestations remain separate.
import assert from 'node:assert/strict'
import { execFileSync } from 'node:child_process'
import { createHash } from 'node:crypto'
import { readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { producerIds } from './package-producers.mjs'
import { computePackageVersion } from './package-version.mjs'

const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex')

export function sourceIdentity(root, environment = process.env) {
  const source = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim()
  assert.equal(environment.GITHUB_SHA ?? source, source, 'checkout/source mismatch')
  const repository = environment.GITHUB_REPOSITORY ?? 'Harborline-Software/harborline-platform'
  assert.equal(repository, 'Harborline-Software/harborline-platform', 'repository mismatch')
  return { source, repository, run: environment.GITHUB_RUN_ID ?? 'local',
    attempt: environment.GITHUB_RUN_ATTEMPT ?? 'local', workflow: environment.GITHUB_WORKFLOW_REF ?? 'local' }
}

export function writeLibraryProof(feed, cache, manifest, identity) {
  for (const { id, version, sha256 } of manifest) {
    assert.equal(hash(resolve(feed, `${id}.${version}.nupkg`)), sha256, `${id}: staged bytes changed`)
    const name = id.toLowerCase()
    const normalized = version.toLowerCase()
    assert.equal(hash(resolve(cache, name, normalized, `${name}.${normalized}.nupkg`)), sha256,
      `${id}: restored bytes differ from staged bytes`)
  }
  writeFileSync(resolve(feed, 'manifest.json'), `${JSON.stringify(manifest, null, 2)}\n`)
  writeFileSync(resolve(feed, 'consumer-proof.json'), `${JSON.stringify({ identity, packages: manifest,
    manifestSha256: hash(resolve(feed, 'manifest.json')) }, null, 2)}\n`)
}

export function verifyLibraryProof(feed, ids, version, identity) {
  const manifest = JSON.parse(readFileSync(resolve(feed, 'manifest.json'), 'utf8'))
  const proof = JSON.parse(readFileSync(resolve(feed, 'consumer-proof.json'), 'utf8'))
  assert.deepEqual(manifest.map(row => row.id).sort(), [...ids].sort(), 'package inventory mismatch')
  assert.deepEqual(readdirSync(feed).filter(name => name.endsWith('.nupkg')).sort(),
    manifest.map(row => `${row.id}.${version}.nupkg`).sort(), 'staged files mismatch')
  assert.deepEqual(proof.identity, identity, 'consumer proof source/run mismatch')
  assert.deepEqual(proof.packages, manifest, 'consumer proof package mismatch')
  assert.equal(proof.manifestSha256, hash(resolve(feed, 'manifest.json')), 'manifest changed')
  for (const row of manifest) {
    assert.equal(row.version, version, `${row.id}: version mismatch`)
    assert.equal(hash(resolve(feed, `${row.id}.${version}.nupkg`)), row.sha256, `${row.id}: package changed`)
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = resolve(import.meta.dirname, '..')
  verifyLibraryProof(resolve(root, 'artifacts/packages/nuget'), producerIds(), computePackageVersion(root), sourceIdentity(root))
  console.log('Exact library promotion proof: PASS')
}
