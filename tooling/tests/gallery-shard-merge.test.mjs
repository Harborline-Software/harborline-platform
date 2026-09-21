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
import {
  aggregateShardResults, collectShardReports, observeCompletedGalleryRun, parseShardAssignment, requiredGalleryStepIds,
} from '../run-gallery-gate.mjs'

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

// T-349, the MATRIX half. The shards now run on separate hosted runners and reach the collector as
// uploaded artifacts, so there is a new way for one to go missing that the in-process merge above
// could not have: its artifact is simply not there. The property the ticket asks to be PROVED
// rather than assumed is that this still fails the gate, so each of the three ways a matrix shard
// can vanish gets its own case below.

const producer = (index, shards, scenarioIds, overrides = {}) => ({
  schemaVersion: 1,
  kind: 'gallery-shard',
  shard: index,
  shards,
  passed: true,
  results: requiredGalleryStepIds.map(id => ({id, passed: true, durationMs: 1})),
  failure: undefined,
  playwrightReport: shard(scenarioIds),
  ...overrides,
})

test('--shard rejects a subset run, the way --record already rejects GALLERY_GREP', () => {
  assert.deepEqual(parseShardAssignment('3/8'), {index: 3, total: 8})
  assert.equal(parseShardAssignment(undefined), undefined)
  assert.throws(() => parseShardAssignment('8'), /expects <index>\/<total>/)
  assert.throws(() => parseShardAssignment('9/8'), /out of range/)
  assert.throws(() => parseShardAssignment('0/8'), /out of range/)
})

test('an absent shard ARTIFACT is refused by name, not merged around', () => {
  // Shard 2 of 3 never uploaded. This is the failure the matrix introduces and the in-process
  // merge never had: nothing on disk to merge, and no job left to notice.
  const present = [
    producer(1, 3, ['a.one']),
    producer(3, 3, ['c.three']),
  ]
  assert.throws(() => collectShardReports(present), /incomplete: 1 of 3 missing \(shards 2\)/)
})

test('no shard artifacts at all is refused, not read as an empty pass', () => {
  assert.throws(() => collectShardReports([]), /matrix produced nothing/)
})

test('shards that disagree on the total are refused', () => {
  // A re-run that changed the matrix size, or a stale artifact from an earlier run.
  assert.throws(
    () => collectShardReports([producer(1, 2, ['a.one']), producer(2, 3, ['b.two'])]),
    /disagree on the shard total/,
  )
})

test('a shard that RAN and failed a step fails the collected gate, and is named', () => {
  const broken = producer(2, 2, ['b.two'], {
    passed: false,
    results: requiredGalleryStepIds.map(id => ({
      id,
      passed: id !== 'gallery-accessibility-and-parity',
      durationMs: 1,
      ...(id === 'gallery-accessibility-and-parity' ? {failureOutput: 'budget exceeded'} : {}),
    })),
  })
  const aggregated = aggregateShardResults(
    collectShardReports([producer(1, 2, ['a.one']), broken]),
    requiredGalleryStepIds,
  )
  assert.equal(aggregated.length, requiredGalleryStepIds.length)
  assert.deepEqual(aggregated.map(entry => entry.id), requiredGalleryStepIds)
  const suite = aggregated.find(entry => entry.id === 'gallery-accessibility-and-parity')
  assert.equal(suite.passed, false)
  assert.match(suite.failureOutput, /budget exceeded/)
  // Every other step passed on both shards, so only the real failure is red.
  assert.equal(aggregated.filter(entry => !entry.passed).length, 1)
})

test('a shard whose reporter wrote nothing leaves the merge short, and reconciliation catches it', () => {
  // The step "passed" but playwrightReport is null -- the shape a killed-after-exit-0 run would
  // take. mergeShardReports contributes nothing for it, so the specs are declared-but-not-run.
  const declared = ['a.one', 'b.two']
  const complete = collectShardReports([
    producer(1, 2, ['a.one']),
    producer(2, 2, ['b.two'], {playwrightReport: null}),
  ])
  const merged = mergeShardReports(complete.map(report => report.playwrightReport))
  const observed = observeGalleryRun(merged, declared)
  assert.match(observed.reconciliation, /declared-but-not-run/)
  assert.match(observed.reconciliation, /b\.two/)
})

test('a complete matrix aggregates to nine passed steps and reconciles exactly', () => {
  const declared = ['a.one', 'b.two', 'c.three']
  const complete = collectShardReports([
    producer(1, 3, ['a.one']),
    producer(3, 3, ['c.three']),
    producer(2, 3, ['b.two']),
  ])
  // Restored to shard order regardless of the order the artifacts were read in.
  assert.deepEqual(complete.map(report => report.shard), [1, 2, 3])
  const aggregated = aggregateShardResults(complete, requiredGalleryStepIds)
  assert.ok(aggregated.every(entry => entry.passed))
  const observed = observeGalleryRun(mergeShardReports(complete.map(report => report.playwrightReport)), declared)
  assert.equal(observed.reconciliation, 'exact')
  assert.equal(observed.scenarioBrowserTests, 3)
})
