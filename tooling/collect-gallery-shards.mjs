#!/usr/bin/env node
// CI's gallery collector (2026-09-29 owner ruling: one gate per event, on ubuntu). The headless
// phase-4 gate runs beside the gallery shard matrix instead of after it, so the gallery half of the
// gate's pass predicate runs here: merge the shard reports (run-gallery-gate.mjs --merge, which
// refuses a missing or failed shard and reconciles scenarios and checks), then apply the same
// galleryReconciliationFailures the full gate applies, against the UI spec's scenario count that
// generation-smoke would have supplied. Prints the merged gallery report; exits 1 on any failure.
//
//   node tooling/collect-gallery-shards.mjs <shard-report-dir>

import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

import {galleryReconciliationFailures} from './gate-contract.mjs'
import {loadModuleSpec} from './generate-blazor-smoke.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')

// Same module selection and count as run-phase-4-gate.mjs's generation-smoke step, without the build.
export function specScenarioCount(repositoryRoot = root) {
  const catalog = JSON.parse(readFileSync(resolve(repositoryRoot, 'catalog/modules.yaml'), 'utf8'))
  return Object.entries(catalog.modules)
    .filter(([moduleId, module]) => moduleId.startsWith('hlp.ui.') && module.presentation?.disposition === 'visual')
    .reduce((total, [moduleId]) => total + loadModuleSpec(resolve(repositoryRoot, 'specs/modules/ui'), moduleId).scenarios.scenarios.length, 0)
}

export function collectorFailures(gallery, specScenarios) {
  return [
    ...(gallery?.status === 'PASS' ? [] : [`gallery merge status ${gallery?.status}: ${gallery?.failure ?? 'no failure text'}`]),
    ...galleryReconciliationFailures(gallery, specScenarios),
  ]
}

if (import.meta.main) {
  const directory = process.argv[2]
  if (!directory) throw new Error('usage: collect-gallery-shards.mjs <shard-report-dir>')
  const merge = spawnSync(process.execPath, ['tooling/run-gallery-gate.mjs', `--merge=${directory}`], {
    cwd: root, encoding: 'utf8', maxBuffer: 256 * 1024 * 1024, stdio: ['ignore', 'pipe', 'inherit'],
  })
  process.stdout.write(merge.stdout)
  let gallery
  try { gallery = JSON.parse(merge.stdout) } catch { gallery = undefined }
  const failures = collectorFailures(gallery, specScenarioCount())
  if (merge.status !== 0 && failures.length === 0) failures.push(`run-gallery-gate.mjs --merge exited ${merge.status}`)
  for (const failure of failures) process.stderr.write(`gallery collector: ${failure}\n`)
  process.exitCode = failures.length === 0 ? 0 : 1
}
