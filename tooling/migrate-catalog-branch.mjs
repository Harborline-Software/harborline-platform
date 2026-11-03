#!/usr/bin/env node
// Carries a branch's catalog and provenance edits from the old single documents into the per-module
// files (HLP-0009). For pull requests opened before the split.
//
// A branch made before the split edited `catalog/modules.yaml` and `docs/provenance/source-map.yaml`
// as whole documents. Merging main into it conflicts on both, because main now keeps only a header
// there. This reads the two documents at the branch's merge base and at its tip, works out which
// modules the branch actually changed, and applies just those changes to the per-module files in the
// working tree -- which, after the merge, hold main's current rows. Modules the branch never touched
// keep main's rows, so nothing main changed since is reverted.
//
// A module both sides changed is reported, not guessed: its file keeps main's row and the branch's
// row is printed, and the exit code is 2. Provenance hash values are not merged at all -- the
// branch's hashed paths are kept and every value is recomputed from the merged files afterwards
// (tooling/refresh-provenance-hashes.mjs), because a hash taken on either side is wrong for the merge.
//
// Usage, on the pull request's branch:
//   git merge origin/main                     # conflicts in the two documents (and maybe elsewhere)
//   node tooling/migrate-catalog-branch.mjs --base $(git merge-base HEAD origin/main) --branch HEAD
//   # resolve any other conflicts, then:
//   node tooling/refresh-provenance-hashes.mjs --module <each module it lists>
//   node tooling/validate-repository.mjs --allow-stale-gate
//   git add catalog docs/provenance && git commit
// For a stacked pull request, migrate its base branch first and use that branch instead of
// origin/main. `--onto <ref>` names where the two headers come from (default: MERGE_HEAD if a merge
// is in progress, otherwise origin/main). `--dry-run` prints the plan and writes nothing.
import {execFileSync} from 'node:child_process'
import {existsSync, readFileSync, writeFileSync} from 'node:fs'
import {resolve} from 'node:path'
import {isDeepStrictEqual} from 'node:util'

import {CATALOG_PATH, PROVENANCE_PATH, canonicalJson, moduleFilePath, provenanceFilePath,
  splitCatalog, splitProvenance, writeModuleFile, writeProvenanceRecord} from './module-catalog.mjs'

const git = (root, args) => execFileSync('git', args, {cwd: root, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024})

function documentAt(root, ref, path) {
  return JSON.parse(git(root, ['show', `${ref}:${path}`]))
}

// Per-module rows of a single document at a ref; null when the ref is already split.
function rowsAt(root, ref) {
  const catalog = documentAt(root, ref, CATALOG_PATH)
  const provenance = documentAt(root, ref, PROVENANCE_PATH)
  if (!catalog.modules || !provenance.records) return null
  return {catalog: splitCatalog(catalog).files, provenance: splitProvenance(provenance).files}
}

const readRow = (root, path) => existsSync(resolve(root, path)) ? JSON.parse(readFileSync(resolve(root, path), 'utf8')) : undefined
const withoutHashValues = record => record && {...record, contentHashes: Object.keys(record.contentHashes ?? {}).sort()}

// The three-way decision for one module's row. `same` compares what a merge should compare.
export function decide(base, branch, current, same = isDeepStrictEqual) {
  if (same(base, branch)) return {action: 'keep'}
  if (same(current, branch)) return {action: 'keep'}
  if (same(current, base)) return {action: branch === undefined ? 'delete' : 'write'}
  return {action: 'conflict'}
}

export function plan(rows, readCurrent) {
  const steps = []
  for (const kind of ['catalog', 'provenance']) {
    const {base, branch} = rows[kind]
    const ids = [...new Set([...base.keys(), ...branch.keys()])].sort()
    const same = kind === 'provenance'
      ? (a, b) => isDeepStrictEqual(withoutHashValues(a), withoutHashValues(b))
      : isDeepStrictEqual
    for (const moduleId of ids) {
      const current = readCurrent(kind, moduleId)
      const decision = decide(base.get(moduleId), branch.get(moduleId), current, same)
      if (decision.action !== 'keep') steps.push({kind, moduleId, ...decision, row: branch.get(moduleId), current})
    }
  }
  return steps
}

if (import.meta.main) {
  const root = process.cwd()
  const argv = process.argv.slice(2)
  const value = name => { const index = argv.indexOf(name); return index >= 0 ? argv[index + 1] : undefined }
  const base = value('--base')
  const branch = value('--branch') ?? 'HEAD'
  const dryRun = argv.includes('--dry-run')
  if (!base) {
    process.stderr.write('usage: node tooling/migrate-catalog-branch.mjs --base <merge-base> [--branch HEAD] [--onto <ref>] [--dry-run]\n')
    process.exit(2)
  }
  const mergeInProgress = existsSync(resolve(git(root, ['rev-parse', '--git-dir']).trim(), 'MERGE_HEAD'))
  const onto = value('--onto') ?? (mergeInProgress ? 'MERGE_HEAD' : 'origin/main')

  const baseRows = rowsAt(root, base)
  const branchRows = rowsAt(root, branch)
  if (!baseRows || !branchRows) {
    process.stdout.write(`${!branchRows ? branch : base} already uses per-module files; nothing to migrate\n`)
    process.exit(0)
  }
  const steps = plan(
    {catalog: {base: baseRows.catalog, branch: branchRows.catalog}, provenance: {base: baseRows.provenance, branch: branchRows.provenance}},
    (kind, moduleId) => readRow(root, kind === 'catalog' ? moduleFilePath(moduleId) : provenanceFilePath(moduleId)),
  )

  const conflicts = steps.filter(step => step.action === 'conflict')
  // Any record whose hashes the branch changed needs recomputing, even when only hash values moved.
  const refresh = new Set([...new Set([...baseRows.provenance.keys(), ...branchRows.provenance.keys()])]
    .filter(id => branchRows.provenance.has(id) && !isDeepStrictEqual(baseRows.provenance.get(id), branchRows.provenance.get(id))))
  for (const step of steps) {
    const path = step.kind === 'catalog' ? moduleFilePath(step.moduleId) : provenanceFilePath(step.moduleId)
    process.stdout.write(`${step.action.padEnd(8)} ${path}\n`)
    if (dryRun || step.action === 'conflict') continue
    if (step.action === 'delete') { git(root, ['rm', '--quiet', '--', path]); continue }
    if (step.kind === 'catalog') writeModuleFile(root, step.row)
    else writeProvenanceRecord(root, step.row)
  }
  if (!dryRun) {
    for (const path of [CATALOG_PATH, PROVENANCE_PATH]) writeFileSync(resolve(root, path), canonicalJson(documentAt(root, onto, path)))
    process.stdout.write(`headers ${CATALOG_PATH} and ${PROVENANCE_PATH} taken from ${onto}\n`)
  }
  if (refresh.size) {
    process.stdout.write(`\nAfter the other conflicts are resolved, recompute the hashes:\n  node tooling/refresh-provenance-hashes.mjs ${[...refresh].map(id => `--module ${id}`).join(' ')}\n`)
  }
  if (conflicts.length) {
    process.stdout.write('\nBoth this branch and the target changed these rows. Each file keeps the target\'s row; merge the branch\'s by hand:\n')
    for (const step of conflicts) process.stdout.write(`\n--- branch row for ${step.kind} ${step.moduleId}:\n${canonicalJson(step.row ?? null)}`)
    process.exit(2)
  }
}
