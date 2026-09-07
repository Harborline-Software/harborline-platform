#!/usr/bin/env node

import assert from 'node:assert/strict'
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'

import { captureFileName, recordGalleryBaselines } from '../gallery-baseline-store.mjs'

const scratch = mkdtempSync(resolve(tmpdir(), 'hlp-gallery-baselines-'))
const repositoryRoot = resolve(scratch, 'repository')
const captureRoot = resolve(scratch, 'captures')
const catalogs = [{ moduleId: 'hlp.ui.example', scenarios: [{ id: 'example.default' }, { id: 'example.dark' }] }]
const png = Buffer.from('89504e470d0a1a0a0000000049454e44ae426082', 'hex')

try {
  mkdirSync(captureRoot, { recursive: true })
  for (const scenario of catalogs[0].scenarios) writeFileSync(resolve(captureRoot, captureFileName(catalogs[0].moduleId, scenario.id)), png)
  const first = recordGalleryBaselines({ repositoryRoot, captureRoot, catalogs })
  assert.deepEqual(first, {
    schemaVersion: 1, projection: 'react', manifests: 1, scenarios: 2,
    uniqueArtifacts: 1, storedBytes: png.length, store: 'specs/modules/ui/baselines/sha256',
  })
  const manifestPath = resolve(repositoryRoot, 'specs/modules/ui/hlp.ui.example/react-baselines.json')
  const firstManifest = readFileSync(manifestPath, 'utf8')
  assert.equal(recordGalleryBaselines({ repositoryRoot, captureRoot, catalogs }).storedBytes, png.length)
  assert.equal(readFileSync(manifestPath, 'utf8'), firstManifest, 'identical captures must reproduce the manifest byte-for-byte')
  rmSync(resolve(captureRoot, captureFileName('hlp.ui.example', 'example.dark')))
  assert.throws(() => recordGalleryBaselines({ repositoryRoot, captureRoot, catalogs }), /capture set mismatch/)
  process.stdout.write('gallery baseline recording check PASS\n')
} finally {
  rmSync(scratch, { recursive: true, force: true })
}
