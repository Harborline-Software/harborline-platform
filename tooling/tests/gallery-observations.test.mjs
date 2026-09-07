#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import {countChecks, expectedChecks, observeGalleryRun, reconcileChecks} from '../gallery-observations.mjs'

const scenarioSpec = id => ({title: `${id} is accessible and visually conformant`})
const report = (specs, stats) => ({
  suites: [{title: 'gallery.spec.ts', specs: specs.slice(0, 1), suites: [{title: 'nested', specs: specs.slice(1)}]}],
  stats,
})

test('scenario tests are separated from the rest, however the suites nest', () => {
  const observed = observeGalleryRun(
    report([scenarioSpec('hlp.ui.button.default'), scenarioSpec('hlp.ui.badge.default'), {title: 'react preserves Button affordances in forced colors'}],
      {expected: 3, unexpected: 0, flaky: 0, skipped: 0}),
    ['hlp.ui.button.default', 'hlp.ui.badge.default'],
  )
  assert.equal(observed.browserTests, 3)
  assert.equal(observed.scenarioBrowserTests, 2)
  assert.equal(observed.nonScenarioBrowserTests, 1)
  assert.equal(observed.reconciliation, 'exact')
})

test('a declared scenario that did not run is named, not silently absorbed', () => {
  // The whole point. `scenarioCount + 45` could not see this, which is how the count reached 436
  // against an actual 437 and stayed there through a PR, a merge and a full CI run.
  const observed = observeGalleryRun(
    report([scenarioSpec('hlp.ui.button.default')], {expected: 1}),
    ['hlp.ui.button.default', 'hlp.ui.badge.default'],
  )
  assert.match(observed.reconciliation, /declared-but-not-run=\["hlp\.ui\.badge\.default"\]/)
})

test('a test naming an unknown scenario is named too', () => {
  const observed = observeGalleryRun(report([scenarioSpec('hlp.ui.ghost.default')], {expected: 1}), [])
  assert.match(observed.reconciliation, /run-but-not-declared=\["hlp\.ui\.ghost\.default"\]/)
})

test('an empty report fails closed', () => {
  // A reporter that wrote nothing must not read as a run with zero drift.
  assert.throws(() => observeGalleryRun({suites: [], stats: {}}, []), /contains no tests/)
})

test('outcomes carry flaky separately from expected', () => {
  // retries: 1 means a test can pass on retry. Folding that into the total would hide it.
  const observed = observeGalleryRun(report([{title: 'a'}], {expected: 0, unexpected: 0, flaky: 1, skipped: 0}), [])
  assert.deepEqual(observed.outcomes, {expected: 0, unexpected: 0, flaky: 1, skipped: 0})
})

// Ticket 284 slice 1: the flaky NAMES, not only the count. `schema-form.content ...` is the real
// title of the scenario that went flaky once on macpro on 2026-09-06, which is the occurrence this
// recording exists to make ownable.
const flakySpec = title => ({title, tests: [{status: 'flaky', results: [{}, {}]}]})

test('a flaky test is recorded by name, however the suites nest', () => {
  const observed = observeGalleryRun({
    suites: [{
      title: 'gallery.spec.ts',
      specs: [scenarioSpec('hlp.ui.button.default')],
      suites: [{title: 'nested', specs: [flakySpec('schema-form.content is accessible and visually conformant')]}],
    }],
    stats: {expected: 1, unexpected: 0, flaky: 1, skipped: 0},
  }, ['hlp.ui.button.default', 'schema-form.content'])
  assert.deepEqual(observed.flakyTests, ['schema-form.content is accessible and visually conformant'])
  assert.equal(observed.outcomes.flaky, 1)
})

test('a clean run names no flakes, and the names are sorted so two receipts diff on content', () => {
  const clean = observeGalleryRun(report([scenarioSpec('hlp.ui.button.default')], {expected: 1, flaky: 0}), ['hlp.ui.button.default'])
  assert.deepEqual(clean.flakyTests, [])
  const many = observeGalleryRun(
    report([flakySpec('z second'), flakySpec('a first'), {title: 'passed', tests: [{status: 'expected'}]}], {expected: 1, flaky: 2}), [])
  assert.deepEqual(many.flakyTests, ['a first', 'z second'])
})

const checked = (kind, times, results = 1) => ({
  title: `check ${kind}`,
  tests: [{results: Array.from({length: results}, () => ({annotations: Array.from({length: times}, () => ({type: 'hlp-check', description: kind}))}))}],
})

test('a declared count larger than the measured one fails, naming both numbers', () => {
  // The defect ticket 100 records: `browserTests` said 436 while 437 ran, for fifteen days, because
  // nothing ever held the declaration against the measurement.
  const checks = countChecks(report([checked('reflowChecks', 8)], {expected: 1}))
  assert.deepEqual(checks, {reflowChecks: 8})
  const message = reconcileChecks(checks, {reflowChecks: 16})
  assert.match(message, /reflowChecks declared 16, measured 8/)
})

test('a measured count with no declaration is reported as measured, not as a divergence', () => {
  // contrastRatioAssertions has no derivable construct behind it, so it is measured and published
  // without an expectation rather than compared against a literal somebody once hand-counted.
  const checks = countChecks(report([checked('contrastRatioAssertions', 44)], {expected: 1}))
  assert.equal(checks.contrastRatioAssertions, 44)
  assert.equal(reconcileChecks(checks, {}), 'exact')
})

test('a declared family that nothing recorded is a divergence, not a silent zero', () => {
  assert.match(reconcileChecks({}, {keyboardFocusChecks: 2}), /keyboardFocusChecks declared 2, measured 0/)
})

test('a retried test counts its checks once, from the attempt the suite reports', () => {
  // retries: 1 replays the whole test body, annotations included. Summing attempts would let a flaky
  // test inflate the amount of work the gate claims was done.
  assert.deepEqual(countChecks(report([checked('accessibilityScans', 2, 2)], {flaky: 1})), {accessibilityScans: 2})
})

// A scenario set small enough to check by hand: three scenarios, all three declaring visual parity
// and reflow, one of them wall-clock exempt and one pixel exempt under CI.
const exemptions = {pixel: ['button.pixel'], wallClock: ['button.slow']}
const modules = [{
  moduleId: 'hlp.ui.button',
  locales: 3,
  themes: 2,
  scenarios: ['button.default', 'button.pixel', 'button.slow'].map(id => ({
    id, sourceQualityCaseIds: ['hlp.ui.button.quality.visual-parity', 'hlp.ui.button.quality.reflow'],
  })),
}]

test('a CI run expects only the work CI runs, per exemption class', () => {
  // validate.yml sets HARBORLINE_CI_GALLERY=1. A wall-clock exemption skips the whole test (its
  // accessibility, parity and reflow checks all vanish); a pixel exemption skips only the comparison.
  // Deriving from the full catalog instead reddened gate:phase4 on Actions by construction: 928/924.
  const local = expectedChecks(modules, {exemptions})
  const ci = expectedChecks(modules, {ciGallery: true, exemptions})
  assert.deepEqual([local.accessibilityScans, local.visualParityComparisons, local.reflowChecks], [6, 3 + 6, 3 * 2 + 2 * 2 + 6 + 3 * 2])
  assert.deepEqual([ci.accessibilityScans, ci.visualParityComparisons, ci.reflowChecks], [4, 1 + 6, 3 * 2 + 2 * 2 + 6 + 2 * 2])
})

test('a capture run expects no per-scenario reflow, because it runs none', () => {
  // gallery.spec.ts declines to judge layout on a page the screenshotter just scrolled. Declaring it
  // anyway failed `npm run capture:gallery` with reflowChecks declared 114, measured 16.
  const capture = expectedChecks(modules, {capture: true, exemptions})
  assert.equal(capture.reflowChecks, 3 * 2 + 2 * 2 + 6)
  assert.equal(capture.accessibilityScans, expectedChecks(modules, {exemptions}).accessibilityScans)
})

// Per-scenario element-parity coverage (ticket 147 slice 2). A registered absence exempts a whole
// subtree from comparison, so the run records how much it hid and the gate reports it; read from the
// LAST attempt for the same reason check counts are.
const covered = (description, results = 1) => ({
  title: `coverage ${description}`,
  tests: [{results: Array.from({length: results}, (unused, attempt) => ({
    annotations: [{type: 'hlp-element-parity', description: `${description} attempt ${attempt + 1}`}],
  }))}],
})

test('element-parity coverage is reported per scenario, from the attempt the suite reports', () => {
  const observed = observeGalleryRun(report([
    covered('toaster.theme-dark react 7/49 compared, 42 hidden'),
    covered('side-nav.theme-dark react 10/43 compared, 33 hidden', 2),
  ], {expected: 2}), [])
  assert.deepEqual(observed.elementParityCoverage, [
    'side-nav.theme-dark react 10/43 compared, 33 hidden attempt 2',
    'toaster.theme-dark react 7/49 compared, 42 hidden attempt 1',
  ])
})

// Ticket 284 slice 2: a rescue has to stay visible AS a rescue. Recording only the final verdict
// makes a flaky test indistinguishable from one that never wobbled, which is what a registry that
// permits a retry has to keep the reader from believing.
test('a flaky spec records both outcomes of its retry, in order', () => {
  const observed = observeGalleryRun({
    suites: [{title: 'gallery.spec.ts', specs: [], suites: [{title: 'nested', specs: [{
      title: 'schema-form.content is accessible and visually conformant',
      tests: [{status: 'flaky', results: [{status: 'failed'}, {status: 'passed'}]}],
    }]}]}],
    stats: {expected: 0, unexpected: 0, flaky: 1, skipped: 0},
  }, [])
  assert.deepEqual(observed.flakyAttempts, {
    'schema-form.content is accessible and visually conformant': ['failed', 'passed'],
  })
})

test('a run with no flakes records no attempts', () => {
  const observed = observeGalleryRun(report([scenarioSpec('hlp.ui.button.default')], {expected: 1, flaky: 0}), ['hlp.ui.button.default'])
  assert.deepEqual(observed.flakyAttempts, {})
})
