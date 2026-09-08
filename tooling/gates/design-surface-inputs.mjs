#!/usr/bin/env node
// Trace the gate itself, including private discovery/hash helpers, without changing its rule.
// Builtin instrumentation is synchronous, scoped to this diagnostic, and restored in finally.
import assert from 'node:assert/strict'
import crypto from 'node:crypto'
import fs from 'node:fs'
import {syncBuiltinESMExports} from 'node:module'
import path from 'node:path'
import {fileURLToPath} from 'node:url'

import {loadRecord, referenceRevision, referenceSurface, reviewVerdict} from './design-review.mjs'

export function designSurfaceInputs(platformRoot, moduleId) {
  const original = {
    resolve: path.resolve, existsSync: fs.existsSync, readdirSync: fs.readdirSync,
    readFileSync: fs.readFileSync, createHash: crypto.createHash,
  }
  const events = []
  let phase
  let nextHash = 0
  const emit = event => events.push({order: events.length, phase, ...event})
  const bytesOf = (data, encoding) => typeof data === 'string' ? Buffer.from(data, encoding) : Buffer.from(data)
  const describe = bytes => ({byteLength: bytes.length, sha256: original.createHash('sha256').update(bytes).digest('hex')})
  let revision, surface
  try {
    path.resolve = (...inputs) => {
      const output = original.resolve(...inputs)
      emit({operation: 'resolve', inputs, output})
      return output
    }
    fs.existsSync = (...args) => {
      const exists = original.existsSync(...args)
      emit({operation: 'existsSync', path: String(args[0]), exists})
      return exists
    }
    fs.readdirSync = (...args) => {
      const entries = original.readdirSync(...args)
      emit({operation: 'readdirSync', source: 'readdir (working tree, not git index)', path: String(args[0]),
        returnedOrder: entries.map(entry => ({name: entry.name, directory: entry.isDirectory(), file: entry.isFile()}))})
      return entries
    }
    fs.readFileSync = (...args) => {
      const data = original.readFileSync(...args)
      // Read raw bytes separately when the gate asks for UTF-8, preserving the gate's return value.
      const bytes = Buffer.isBuffer(data) ? data : original.readFileSync(args[0])
      emit({operation: 'readFileSync', path: String(args[0]), encoding: args[1] ?? null, ...describe(bytes)})
      return data
    }
    crypto.createHash = (...args) => {
      const hash = original.createHash(...args)
      const id = nextHash++
      const update = hash.update.bind(hash)
      const digest = hash.digest.bind(hash)
      emit({operation: 'createHash', id, algorithm: args[0]})
      hash.update = (data, encoding) => {
        const bytes = bytesOf(data, encoding)
        emit({operation: 'hash.update', id, input: typeof data === 'string' ? 'normalised UTF-8 text' : 'raw bytes',
          ...describe(bytes)})
        return update(data, encoding)
      }
      hash.digest = (...options) => {
        const result = digest(...options)
        emit({operation: 'hash.digest', id, digest: Buffer.isBuffer(result) ? result.toString('hex') : result})
        return result
      }
      return hash
    }
    syncBuiltinESMExports()
    // Same call order as gate-rows.mjs contextFor; no discovery or normalisation copied here.
    phase = 'referenceRevision'
    revision = referenceRevision(platformRoot, moduleId)
    phase = 'referenceSurface'
    surface = referenceSurface(platformRoot, moduleId)
  } finally {
    path.resolve = original.resolve
    fs.existsSync = original.existsSync
    fs.readdirSync = original.readdirSync
    fs.readFileSync = original.readFileSync
    crypto.createHash = original.createHash
    syncBuiltinESMExports()
  }
  // Fail loudly if observing the computation changed either result.
  assert.equal(revision, referenceRevision(platformRoot, moduleId))
  assert.deepEqual(surface, referenceSurface(platformRoot, moduleId))
  const record = loadRecord(moduleId, path.resolve(platformRoot, 'docs/evidence/design-review'))
  const reads = events.filter(event => event.phase === 'referenceSurface' && event.operation === 'readFileSync')
  return {
    schemaVersion: 1, moduleId, platformRoot, node: process.version, platform: process.platform,
    identityRule: 'Ordered path-to-digest map; no aggregate surface hash. Paths use resolve without case folding.',
    listingRule: 'readdir; render entries localeCompare-sorted before recursion; final deduplicated paths use sort().',
    events,
    files: Object.entries(surface ?? {}).map(([relative, contentHash]) => {
      const absolute = original.resolve(platformRoot, relative)
      const read = reads.find(event => event.path === absolute)
      return {pathBeforeResolve: relative, pathAfterResolve: absolute, exists: Boolean(read),
        rawByteLength: read?.byteLength ?? 0, rawSha256: read?.sha256 ?? describe(Buffer.alloc(0)).sha256, contentHash}
    }),
    revision, surface, recordedReference: record?.reference ?? null,
    verdict: reviewVerdict({record, revision, surface}),
    instrumentationPreservesIdentity: true,
  }
}

if (import.meta.main) {
  const moduleId = process.argv[2]
  if (!moduleId || process.argv.length !== 3 || !/^hlp\.[a-z0-9.-]+$/.test(moduleId)) {
    process.stderr.write('Usage: node tooling/gates/design-surface-inputs.mjs <moduleId>\n')
    process.exitCode = 1
  } else {
    const platformRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..')
    process.stdout.write(`${JSON.stringify(designSurfaceInputs(platformRoot, moduleId), null, 2)}\n`)
  }
}
