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

import {mergeShardReports} from './gallery-shard-merge.mjs'
import {observeGalleryRun} from './gallery-observations.mjs'

import {galleryReconciliationFailures} from './gate-contract.mjs'
import {loadModuleSpec} from './generate-blazor-smoke.mjs'
import {loadModuleCatalog} from './module-catalog.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')

// Same module selection and count as run-phase-4-gate.mjs's generation-smoke step, without the build.
export function specScenarioCount(repositoryRoot = root) {
  const catalog = loadModuleCatalog(repositoryRoot)
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

// Signer-side reconciliation: only JSON from artifacts enters this function. Never import the
// candidate's gallery runner (it executes code at import time). The shard protocol is version 1.
export function reconcileReceiptShards(reports, expectedIds, expectedSteps, allowedSkippedIds = []) {
  if (!reports.length || new Set(expectedIds).size !== expectedIds.length) throw new Error('invalid scenario or shard census')
  const total = reports[0].shards
  if (!Number.isSafeInteger(total) || total < 1 || total > 64 || reports.length !== total) throw new Error('missing shard')
  const indices = new Set()
  for (const report of reports) {
    if (report.schemaVersion !== 1 || report.kind !== 'gallery-shard' || report.passed !== true
        || report.shards !== total || !Number.isSafeInteger(report.shard) || report.shard < 1
        || report.shard > total || indices.has(report.shard)) throw new Error('invalid or failed shard')
    indices.add(report.shard)
    if (report.results?.length !== expectedSteps.length || expectedSteps.some((id, i) =>
      report.results[i]?.id !== id || report.results[i]?.passed !== true)) throw new Error('incomplete shard steps')
    if (!report.playwrightReport) throw new Error('missing raw Playwright report')
  }
  const merged = mergeShardReports(reports.map(report => report.playwrightReport))
  const specs = []
  const visit = node => { specs.push(...(node.specs ?? [])); for (const child of node.suites ?? []) visit(child) }
  visit(merged)
  const scenarioTitles = specs.map(spec => spec.title).filter(title => title.endsWith(' is accessible and visually conformant'))
  if (new Set(scenarioTitles).size !== scenarioTitles.length) throw new Error('duplicate raw scenario result')
  const allowed = new Set(allowedSkippedIds.map(id => `${id} is accessible and visually conformant`))
  let declaredSkips = 0
  for (const spec of specs) {
    if (!spec.tests?.length) throw new Error('raw report contains no test result')
    for (const test of spec.tests) {
      if (test.status === 'skipped') {
        if (!allowed.has(spec.title)) throw new Error('raw report skips an undeclared scenario')
        declaredSkips++
      } else if (!['expected', 'flaky'].includes(test.status) || test.results?.at(-1)?.status !== 'passed') {
        throw new Error('raw report contains a failed or absent test result')
      }
    }
  }
  const observation = observeGalleryRun(merged, expectedIds)
  if (observation.reconciliation !== 'exact' || observation.outcomes.unexpected || observation.outcomes.skipped !== declaredSkips) {
    throw new Error('raw shard scenarios or outcomes do not reconcile')
  }
  return {merged, observation}
}
