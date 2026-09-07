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

import {referenceSurface, recordsRoot} from './design-review.mjs'
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

if (import.meta.main) {
  const platformRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
  const ref = process.argv[2] ?? 'origin/main'
  mkdirSync(recordsRoot, {recursive: true})
  const migrated = migrate({platformRoot, ref, log: line => process.stderr.write(`${line}\n`)})
  process.stdout.write(`${JSON.stringify({schemaVersion: 1, migrated: migrated.length, records: migrated}, null, 2)}\n`)
}
