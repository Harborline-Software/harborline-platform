// Ticket 098 phase 3, the sharding half. Playwright --shard splits the suite across independent
// invocations, and each writes its own json report; the gate merges them before observation.
//
// The property under test is not "does it concatenate". It is that a shard which SILENTLY DID NOT
// RUN cannot shrink the suite into a smaller, quietly passing one. observeGalleryRun reconciles the
// observed scenarios against the catalog, so a missing shard must leave the merged report short —
// which surfaces as declared-but-not-run rather than as green.
//
// This is the failure the first sharded run actually had: four shards ran and all passed, every
// shard wrote to the same results.json because an explicit `outputFile` overrides
// PLAYWRIGHT_JSON_OUTPUT_NAME, and the merged report observed ZERO tests.

import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'

import { mergeShardReports } from '../gallery-shard-merge.mjs'
import { observeGalleryRun } from '../gallery-observations.mjs'
import { observeCompletedGalleryRun } from '../run-gallery-gate.mjs'

const SUFFIX = ' is accessible and visually conformant'
const shard = (scenarioIds, stats = {}) => ({
  suites: [{specs: scenarioIds.map(id => ({title: `${id}${SUFFIX}`}))}],
  stats: {expected: scenarioIds.length, unexpected: 0, flaky: 0, skipped: 0, ...stats},
})

test('every shard contributes its specs and its stats', () => {
  const merged = mergeShardReports([shard(['a.one']), shard(['b.two']), shard(['c.three'])])
  assert.equal(merged.suites.length, 3)
  assert.equal(merged.stats.expected, 3)
})

test('stats sum rather than overwrite', () => {
  const merged = mergeShardReports([
    shard(['a.one'], {flaky: 2}),
    shard(['b.two'], {flaky: 3, unexpected: 1}),
  ])
  assert.equal(merged.stats.flaky, 5)
  assert.equal(merged.stats.unexpected, 1)
  assert.equal(merged.stats.expected, 2)
})

test('a missing shard leaves the merge SHORT, and reconciliation catches it', () => {
  // The whole point. null stands for a shard whose report was never written.
  const declared = ['a.one', 'b.two', 'c.three']
  const merged = mergeShardReports([shard(['a.one']), null, shard(['c.three'])])
  const observed = observeGalleryRun(merged, declared)
  assert.notEqual(observed.reconciliation, 'exact')
  assert.match(observed.reconciliation, /declared-but-not-run/)
  assert.match(observed.reconciliation, /b\.two/)
})

test('a complete merge reconciles exactly', () => {
  const declared = ['a.one', 'b.two', 'c.three']
  const merged = mergeShardReports([shard(['a.one']), shard(['b.two']), shard(['c.three'])])
  const observed = observeGalleryRun(merged, declared)
  assert.equal(observed.reconciliation, 'exact')
  assert.equal(observed.browserTests, 3)
  assert.equal(observed.scenarioBrowserTests, 3)
})

test('every shard missing is refused loudly, not reported as an empty pass', () => {
  // observeGalleryRun throws on an empty report rather than returning zero counts. That is what
  // turned the first broken sharded run red instead of letting it report a suite of no tests.
  assert.throws(() => observeGalleryRun(mergeShardReports([null, null]), ['a.one']),
    /report contains no tests/)
})

test('a failed browser subprocess still contributes its written observations', () => {
  const scratch = mkdtempSync(resolve(tmpdir(), 'hlp-failed-gallery-report-'))
  const reportPath = resolve(scratch, 'results.json')
  try {
    writeFileSync(reportPath, JSON.stringify(shard(['a.one'], {expected: 0, unexpected: 1})))
    const observed = observeCompletedGalleryRun({
      results: [{id: 'gallery-accessibility-and-parity', passed: false}],
      reportPath,
      scenarioIds: ['a.one'],
    })
    assert.equal(observed.browserTests, 1)
    assert.equal(observed.scenarioBrowserTests, 1)
    assert.equal(observed.outcomes.unexpected, 1)
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
})
