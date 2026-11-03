#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import {countChecks, expectedChecks, observeGalleryRun, reconcileChecks, tabWalkByShard, tabWalkTotals} from '../gallery-observations.mjs'

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

test('ARIA-tree parity is declared once per running scenario, and only a wall-clock exemption removes it', () => {
  // T-943: the comparison rides every scenario test, so a pixel exemption (which skips pixels only)
  // leaves it in place and a capture run still makes it; a wall-clock exemption skips the whole test.
  assert.equal(expectedChecks(modules, {exemptions}).ariaParityComparisons, 3)
  assert.equal(expectedChecks(modules, {ciGallery: true, exemptions}).ariaParityComparisons, 2)
  assert.equal(expectedChecks(modules, {capture: true, exemptions}).ariaParityComparisons, 3)
  assert.match(reconcileChecks({ariaParityComparisons: 2}, {ariaParityComparisons: 3}), /ariaParityComparisons declared 3, measured 2/)
})

test('a capture run expects no per-scenario reflow, because it runs none', () => {
  // gallery.spec.ts declines to judge layout on a page the screenshotter just scrolled. Declaring it
  // anyway failed `npm run capture:gallery` with reflowChecks declared 114, measured 16.
  const capture = expectedChecks(modules, {capture: true, exemptions})
  assert.equal(capture.reflowChecks, 3 * 2 + 2 * 2 + 6)
  assert.equal(capture.accessibilityScans, expectedChecks(modules, {exemptions}).accessibilityScans)
})

test('SelectField open-popup scans are declared per running scenario in both projections', () => {
  const selectionScenarios = ['select-field.selection', 'select-field.search', 'select-field.multiple', 'select-field.multiple-search']
    .map(id => ({id, sourceQualityCaseIds: []}))
  const catalogs = [...modules, {moduleId: 'hlp.ui.select-field', scenarios: selectionScenarios}]
  // Seven baseline scenarios across two projections, plus three open-popup scans per projection.
  assert.equal(expectedChecks(catalogs).accessibilityScans, 20)
  assert.equal(expectedChecks(catalogs, {capture: true}).accessibilityScans, 20)
  // Wall-clock skips the whole scenario (both scans); a pixel exemption skips neither scan.
  const ciExemptions = {wallClock: ['select-field.search'], pixel: ['select-field.multiple']}
  assert.equal(expectedChecks(catalogs, {ciGallery: true, exemptions: ciExemptions}).accessibilityScans, 16)
  const isolated = [modules[0], {...catalogs[1], scenarios: [selectionScenarios[1]]}]
  assert.equal(expectedChecks(isolated).accessibilityScans, 10)
  const declared = expectedChecks(catalogs).accessibilityScans
  assert.equal(reconcileChecks({accessibilityScans: 20}, {accessibilityScans: declared}), 'exact')
  assert.match(reconcileChecks({accessibilityScans: 18}, {accessibilityScans: declared}), /declared 20, measured 18/)
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

// T-962: the keyboard tab walk is the last step of every scenario test and records four families
// once each, after all its expects pass; fourteen seeded detector proofs record one each.
const tabWalkFamilies = ['tabWalkLeaveChecks', 'tabWalkReverseChecks', 'tabWalkObscuredChecks', 'tabWalkOrderComparisons']

test('the four tab-walk families are declared once per running scenario, and only a wall-clock exemption removes them', () => {
  for (const family of tabWalkFamilies) {
    assert.equal(expectedChecks(modules, {exemptions})[family], 3, family)
    // CI skips the wall-clock-exempt scenario's whole test, walk included.
    assert.equal(expectedChecks(modules, {ciGallery: true, exemptions})[family], 2, family)
    // A pixel exemption skips pixels only, and a capture run still walks: the walk restores its own layout.
    assert.equal(expectedChecks(modules, {capture: true, exemptions})[family], 3, family)
  }
  assert.match(reconcileChecks({tabWalkLeaveChecks: 2}, {tabWalkLeaveChecks: 3}), /tabWalkLeaveChecks declared 3, measured 2/)
})

test('the tab-walk detector proofs are a constant fourteen, so deleting one is a divergence', () => {
  for (const options of [{exemptions}, {ciGallery: true, exemptions}, {capture: true, exemptions}]) {
    assert.equal(expectedChecks(modules, options).tabWalkDetectorProofs, 14)
  }
  assert.match(reconcileChecks({tabWalkDetectorProofs: 13}, {tabWalkDetectorProofs: 14}), /tabWalkDetectorProofs declared 14, measured 13/)
})

const walked = (coverage, results = 1) => ({
  title: `${coverage.id} is accessible and visually conformant`,
  tests: [{results: Array.from({length: results}, (unused, attempt) => ({
    annotations: [{type: 'hlp-tab-walk', description: JSON.stringify({...coverage, attempt: attempt + 1})}],
  }))}],
})
const lane = overrides => ({stops: 4, reverseStops: 4, walks: 2, stepPresses: 0, releases: 0, scrollers: 0, partial: 0,
  exemptUserOpened: 0, unmeasured: 0, returned: 0, census: 4, zeroStop: false, noisy: false, walkMs: 300, ...overrides})

test('tab-walk coverage is reported per scenario, from the attempt the suite reports', () => {
  const observed = observeGalleryRun(report([
    walked({id: 'dialog.dismissal-focus', wallMs: 900, attributed: false, react: lane({releases: 1}), blazor: lane({releases: 1})}),
    walked({id: 'button.variants', wallMs: 400, attributed: false, react: lane(), blazor: lane()}, 2),
  ], {expected: 2}), ['dialog.dismissal-focus', 'button.variants'])
  assert.deepEqual(observed.tabWalkCoverage.map(line => [line.id, line.attempt]), [['button.variants', 2], ['dialog.dismissal-focus', 1]])
  assert.equal(observed.tabWalkTotals.scenarios, 2)
  assert.deepEqual(observed.tabWalkTotals.releases, {react: 1, blazor: 1})
})

test('tab-walk totals sum what the walk covered and cost, and name the five slowest scenarios', () => {
  const lines = ['a', 'b', 'c', 'd', 'e', 'f'].map((id, index) => ({
    id, wallMs: (index + 1) * 100, attributed: id === 'b',
    react: lane({stops: index, stepPresses: 2, partial: id === 'c' ? 1 : 0, zeroStop: index === 0}),
    blazor: lane({stops: index + 1, exemptUserOpened: id === 'd' ? 2 : 0, noisy: id === 'e'}),
  }))
  const totals = tabWalkTotals(lines)
  assert.equal(totals.scenarios, 6)
  assert.deepEqual(totals.stops, {react: 15, blazor: 21})
  assert.deepEqual(totals.stepPresses, {react: 12, blazor: 0})
  assert.equal(totals.wallMs, 2100)
  assert.deepEqual(totals.partial, {react: 1, blazor: 0})
  assert.deepEqual(totals.exemptUserOpened, {react: 0, blazor: 2})
  assert.equal(totals.zeroStopLanes, 1)
  assert.equal(totals.noisyLanes, 1)
  assert.equal(totals.attributed, 1)
  assert.deepEqual(totals.slowest.map(line => line.id), ['f', 'e', 'd', 'c', 'b'])
  assert.deepEqual(tabWalkTotals([]).stops, {react: 0, blazor: 0})
})

test('each matrix shard records its step and suite wall clock, its workers, and the walk share, before the merge drops them', () => {
  const shard = (index, stepMs, duration, walls) => ({
    shard: index,
    results: [{id: 'gallery-structure', durationMs: 1}, {id: 'gallery-accessibility-and-parity', durationMs: stepMs}],
    playwrightReport: {
      config: {workers: 4},
      stats: {duration},
      suites: [{title: 'gallery.spec.ts', specs: walls.map((wallMs, at) => walked({id: `s${index}.${at}`, wallMs, react: lane(), blazor: lane()}))}],
    },
  })
  assert.deepEqual(tabWalkByShard([shard(1, 450_000, 440_000, [300, 700]), shard(2, 500_000, 495_000, [1_000])]), [
    {shard: 1, stepMs: 450_000, suiteMs: 440_000, workers: 4, tabWalkWallMs: 1_000},
    {shard: 2, stepMs: 500_000, suiteMs: 495_000, workers: 4, tabWalkWallMs: 1_000},
  ])
  // A shard that died before its reporter wrote anything still has a row, with nothing measured.
  assert.deepEqual(tabWalkByShard([{shard: 3, results: [], playwrightReport: null}]),
    [{shard: 3, stepMs: null, suiteMs: null, workers: null, tabWalkWallMs: null}])
})
