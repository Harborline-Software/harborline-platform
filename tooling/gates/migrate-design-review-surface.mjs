#!/usr/bin/env node
// Ticket 138 slice 6: move every legacy design-review record onto a bound surface.
//
// One rule, applied to all of them (design-review-pins.mjs resolves the pin, this walks it):
//
//   1. resolve the pin -- the newest commit at or before `recordedAt` whose two reviewed files hash
//      to the `reference.revision` the record carries;
//   2. check that pin out into a scratch worktree and compute `referenceSurface` THERE;
//   3. write it into the record as `reference.surface`, with `reference.pin` naming the commit.
//
// Step 2 is the whole slice. The surface has to be computed from the tree the reviewer saw, so that
// reviewing the migrated record today expires exactly the modules whose surface has moved since --
// no more (a green wall) and no fewer (mass churn). Computing it from the working tree would make
// every record read PASS the moment it was written, including the five this ticket exists for.
//
// Idempotent: run it twice and the second run writes the same bytes. It refuses rather than guesses
// -- an unresolvable pin, or a pin whose tree does not carry the module, stops the run.

import {execFileSync} from 'node:child_process'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {isDeepStrictEqual} from 'node:util'

import {referenceRevision, referenceSurface, recordsRoot} from './design-review.mjs'
import {resolvePin} from './design-review-pins.mjs'

const git = (cwd, ...args) => execFileSync('git', args, {cwd, maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'pipe']})

export function legacyRecords(root = recordsRoot) {
  if (!existsSync(root)) return []
  return readdirSync(root).filter(name => name.endsWith('.json')).sort()
    .map(name => JSON.parse(readFileSync(resolve(root, name), 'utf8')))
    .filter(record => !record.reference?.surface)
}

// Groups by pin so a tree is checked out once however many verdicts share it (59 records, 7 pins).
export function migrate({platformRoot, ref = 'origin/main', root = recordsRoot, log = () => {}}) {
  const records = legacyRecords(root)
  const byPin = new Map()
  for (const record of records) {
    const {commit, committedAt} = resolvePin(platformRoot, record, ref)
    if (!byPin.has(commit)) byPin.set(commit, {committedAt, records: []})
    byPin.get(commit).records.push(record)
  }
  const migrated = []
  const scratchRoot = mkdtempSync(resolve(tmpdir(), 'design-review-pin-'))
  try {
    for (const [commit, {committedAt, records: group}] of byPin) {
      const scratch = resolve(scratchRoot, commit.slice(0, 12))
      git(platformRoot, 'worktree', 'add', '--detach', '--quiet', scratch, commit)
      try {
        log(`${commit.slice(0, 12)} (${committedAt}): ${group.length} record(s)`)
        for (const record of group) {
          const surface = referenceSurface(scratch, record.moduleId)
          if (!surface) throw new Error(`${record.moduleId}: pin ${commit.slice(0, 12)} carries no gallery scenario catalog for it`)
          const next = {...record, reference: {...record.reference, pin: commit, surface}}
          writeFileSync(resolve(root, `${record.moduleId}.json`), `${JSON.stringify(next, null, 2)}\n`)
          migrated.push({moduleId: record.moduleId, pin: commit, files: Object.keys(surface).length})
        }
      } finally {
        git(platformRoot, 'worktree', 'remove', '--force', scratch)
      }
    }
  } finally {
    rmSync(scratchRoot, {recursive: true, force: true})
  }
  return migrated
}

// Ticket 330: transport an existing approval through an explicitly named family rename only.
// Read committed snapshots without registering worktrees. Both the old binding and the destination
// must be proved; replacing a record with today's hashes without this comparison is re-approval.
export function migrateRenames({platformRoot, historyRoot = platformRoot, ref = 'HEAD', root = recordsRoot, from, to}) {
  if (!/^[A-Z][A-Za-z0-9]+$/.test(from ?? '') || !/^[A-Z][A-Za-z0-9]+$/.test(to ?? '')
    || from.toLowerCase() === to.toLowerCase()) throw new Error('rename requires distinct family identifiers --from and --to')
  const pin = git(platformRoot, 'rev-parse', `${ref}^{commit}`).toString('utf8').trim()
  const scratchRoot = mkdtempSync(resolve(tmpdir(), 'design-review-rename-'))
  const snapshots = new Map()
  const results = []
  const renamed = text => text.replaceAll(from, to)
  const snapshot = (repository, commit) => {
    const key = `${repository}:${commit}`
    if (!snapshots.has(key)) {
      const directory = resolve(scratchRoot, `snapshot-${snapshots.size}`)
      mkdirSync(directory)
      const archive = git(repository, 'archive', commit, '--', 'specs/modules/ui', 'gallery/scenarios',
        'gallery/projections', 'projections/react', 'projections/blazor', 'conformance')
      execFileSync('tar', ['-xf', '-'], {cwd: directory, input: archive, maxBuffer: 1 << 28})
      snapshots.set(key, directory)
    }
    return snapshots.get(key)
  }
  try {
    const target = snapshot(platformRoot, pin)
    for (const name of readdirSync(root).filter(name => name.endsWith('.json')).sort()) {
      const record = JSON.parse(readFileSync(resolve(root, name), 'utf8'))
      const refuse = reason => results.push({moduleId: record.moduleId, status: 'refused', reason})
      if (record.verdict !== 'approved') {
        results.push({moduleId: record.moduleId, status: 'unchanged', reason: 'not an approval'})
        continue
      }
      const current = referenceSurface(target, record.moduleId)
      if (!current || !isDeepStrictEqual(current, referenceSurface(platformRoot, record.moduleId))) {
        refuse('working surface does not match the destination commit')
        continue
      }
      if (isDeepStrictEqual(record.reference?.surface, current)) {
        results.push({moduleId: record.moduleId, status: 'unchanged', reason: 'binding already matches destination'})
        continue
      }
      if (!record.reference?.pin || !record.reference?.surface) {
        refuse('record requires the legacy pin migration first')
        continue
      }
      const old = snapshot(historyRoot, record.reference.pin)
      if (!isDeepStrictEqual(referenceSurface(old, record.moduleId), record.reference.surface)
        || referenceRevision(old, record.moduleId) !== record.reference.revision) {
        refuse('recorded binding does not match its historical pin')
        continue
      }
      const candidate = resolve(scratchRoot, 'candidate', record.moduleId)
      for (const file of Object.keys(record.reference.surface)) {
        const source = resolve(old, file)
        if (!existsSync(source)) continue
        const destination = resolve(candidate, renamed(file))
        mkdirSync(dirname(destination), {recursive: true})
        if (existsSync(destination)) throw new Error(`${record.moduleId}: rename collides at ${file}`)
        writeFileSync(destination, renamed(readFileSync(source, 'utf8')))
      }
      const transported = referenceSurface(candidate, record.moduleId)
      if (!isDeepStrictEqual(transported, current)) {
        const files = [...new Set([...Object.keys(transported ?? {}), ...Object.keys(current)])]
          .filter(file => transported?.[file] !== current[file]).sort()
        refuse(`content changed beyond the rename: ${files.join(', ')}`)
        continue
      }
      const next = {...record, reference: {...record.reference, pin, surface: current,
        revision: referenceRevision(target, record.moduleId),
        migration: {kind: 'family-rename', from, to, judgedAgainst: record.reference.pin, reviewedReference: record.reference}}}
      writeFileSync(resolve(root, name), `${JSON.stringify(next, null, 2)}\n`)
      results.push({moduleId: record.moduleId, status: 'migrated', pin, judgedAgainst: record.reference.pin})
    }
  } finally {
    rmSync(scratchRoot, {recursive: true, force: true})
  }
  return {schemaVersion: 1, migrated: results.filter(row => row.status === 'migrated').length,
    refused: results.filter(row => row.status === 'refused').length,
    unchanged: results.filter(row => row.status === 'unchanged').length, records: results}
}

if (import.meta.main) {
  const platformRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
  const args = process.argv.slice(2)
  const option = name => args[args.indexOf(name) + 1]
  const ref = args.includes('--rename') ? (args.includes('--ref') ? option('--ref') : 'HEAD') : (args[0] ?? 'origin/main')
  mkdirSync(recordsRoot, {recursive: true})
  const migrated = args.includes('--rename')
    ? migrateRenames({platformRoot, ref, from: option('--from'), to: option('--to'),
      historyRoot: args.includes('--history-root') ? resolve(option('--history-root')) : platformRoot})
    : migrate({platformRoot, ref, log: line => process.stderr.write(`${line}\n`)})
  process.stdout.write(`${JSON.stringify(Array.isArray(migrated)
    ? {schemaVersion: 1, migrated: migrated.length, records: migrated} : migrated, null, 2)}\n`)
}
