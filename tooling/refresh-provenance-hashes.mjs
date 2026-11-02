#!/usr/bin/env node
// Recomputes the `contentHashes` of provenance records from the files they name (HLP-0009).
//
// The hashes are a pure function of files in this repository, and `validate-repository.mjs` checks
// every one on every run, so nobody should compute one by hand. This rewrites the hash values of the
// records asked for and nothing else: it never adds or drops a hashed path, because which files a
// record vouches for is a decision, not a derivation. Each record is its own file, so refreshing one
// module touches one file.
//
// Usage:
//   node tooling/refresh-provenance-hashes.mjs                      every record
//   node tooling/refresh-provenance-hashes.mjs --module <id> [...]  the named records
//   node tooling/refresh-provenance-hashes.mjs --changed-from <ref> records hashing a path changed since <ref>
//   ... --check                                                     report stale hashes, write nothing, exit 1 if any
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {existsSync, readFileSync} from 'node:fs'
import {resolve} from 'node:path'

import {loadProvenance, writeProvenanceRecord} from './module-catalog.mjs'

export function staleHashes(root, record) {
  const stale = []
  for (const [path, expected] of Object.entries(record.contentHashes ?? {})) {
    if (!existsSync(resolve(root, path))) { stale.push({path, expected, actual: null}); continue }
    const actual = createHash('sha256').update(readFileSync(resolve(root, path))).digest('hex')
    if (actual !== expected) stale.push({path, expected, actual})
  }
  return stale
}

export function selectRecords(records, {modules = [], changedPaths = null} = {}) {
  if (modules.length) {
    const known = new Set(records.map(record => record.moduleId))
    const unknown = modules.filter(moduleId => !known.has(moduleId))
    if (unknown.length) throw new Error(`no provenance record for ${unknown.join(', ')}`)
    return records.filter(record => modules.includes(record.moduleId))
  }
  if (changedPaths) {
    const changed = new Set(changedPaths)
    return records.filter(record => Object.keys(record.contentHashes ?? {}).some(path => changed.has(path)))
  }
  return records
}

if (import.meta.main) {
  const root = process.cwd()
  const argv = process.argv.slice(2)
  const values = name => argv.flatMap((argument, index) => argument === name && argv[index + 1] ? [argv[index + 1]] : [])
  const check = argv.includes('--check')
  const from = values('--changed-from')[0]
  const changedPaths = from
    ? execFileSync('git', ['diff', '--name-only', from], {cwd: root, encoding: 'utf8'}).split('\n').filter(Boolean)
    : null
  const records = selectRecords(loadProvenance(root).records, {modules: values('--module'), changedPaths})
  let staleCount = 0
  let missing = 0
  for (const record of records) {
    const stale = staleHashes(root, record)
    if (!stale.length) continue
    staleCount += stale.length
    for (const {path, actual} of stale) {
      if (actual === null) missing += 1
      process.stdout.write(`${record.moduleId}: ${path} ${actual === null ? 'is missing' : check ? 'is stale' : 'refreshed'}\n`)
      if (!check && actual !== null) record.contentHashes[path] = actual
    }
    if (!check) writeProvenanceRecord(root, record)
  }
  process.stdout.write(`${records.length} record(s) read, ${staleCount} stale hash(es)${check ? '' : ' refreshed'}\n`)
  if (missing) {
    process.stderr.write(`${missing} hashed path(s) no longer exist; drop or rename them in the record by hand\n`)
    process.exit(1)
  }
  if (check && staleCount) process.exit(1)
}
