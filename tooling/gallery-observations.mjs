// Observations of a Playwright run, as distinct from expectations derived from the catalog.
//
// Control ticket 100 measured that sixteen gallery counts were arithmetic over `gallery/scenarios/`
// rather than observations of anything, and that `browserTests` had drifted to 436 against an actual
// 437 -- on the very commit that added the test meant to stop drift. This module reads what ran.
//
// Two kinds of measurement, and NEITHER is reconstructed from test titles -- a title mapping would
// be a fabricated observation, which is the defect wearing better clothes. Test-level counts come
// from the reporter's own spec tree. Assertion-level counts (`accessibilityScans` is 782 assertions
// made by 391 tests, which no reporter can see) come from annotations the assertions themselves
// record while running, read back out of the same file.

const SCENARIO_TITLE_SUFFIX = ' is accessible and visually conformant'
const CHECK_ANNOTATION = 'hlp-check'
const ELEMENT_PARITY_ANNOTATION = 'hlp-element-parity'

function collectSpecTitles(node, titles = []) {
  for (const spec of node.specs ?? []) titles.push(spec.title)
  for (const child of node.suites ?? []) collectSpecTitles(child, titles)
  return titles
}

// Ticket 284: a flaky test is one that failed and then passed on its retry. The gate has always
// counted them (`outcomes.flaky`) and a count cannot be acted on — nobody can own, date or fix a
// number. These are the NAMES, read from the same reporter tree, so a flake occurrence is a thing
// with an identity. (The registry that owns and expires those identities is slice 2.)
function collectFlakyTitles(node, titles = []) {
  for (const spec of node.specs ?? []) {
    if ((spec.tests ?? []).some(entry => entry.status === 'flaky')) titles.push(spec.title)
  }
  for (const child of node.suites ?? []) collectFlakyTitles(child, titles)
  return titles
}

// Ticket 284 slice 2: both outcomes of the retry, per flaky spec, in the order the runner produced
// them (`['failed', 'passed']` for the ordinary rescue). A registered flake stays visible as a
// failure followed by a pass; a receipt that recorded only the final verdict would make a rescue
// indistinguishable from a test that never wobbled.
export function collectFlakyAttempts(node, attempts = {}) {
  for (const spec of node.specs ?? []) {
    for (const entry of spec.tests ?? []) {
      if (entry.status !== 'flaky') continue
      attempts[spec.title] = (entry.results ?? []).map(result => result.status)
    }
  }
  for (const child of node.suites ?? []) collectFlakyAttempts(child, attempts)
  return attempts
}

function collectTests(node, tests = []) {
  for (const spec of node.specs ?? []) for (const entry of spec.tests ?? []) tests.push(entry)
  for (const child of node.suites ?? []) collectTests(child, tests)
  return tests
}

// One test may run twice under `retries: 1`, and a retried attempt records its checks again. Only the
// LAST attempt is counted, because that is the attempt whose result the suite reports; summing every
// attempt would make a flaky test inflate the count of work the gate claims was done.
export function countChecks(report) {
  const checks = {}
  for (const entry of collectTests(report)) {
    const results = entry.results ?? []
    const annotations = results.length > 0
      ? (results[results.length - 1].annotations ?? [])
      : (entry.annotations ?? [])
    for (const annotation of annotations) {
      if (annotation.type !== CHECK_ANNOTATION) continue
      checks[annotation.description] = (checks[annotation.description] ?? 0) + 1
    }
  }
  return Object.fromEntries(Object.entries(checks).sort(([left], [right]) => left.localeCompare(right)))
}

// A declared expectation is the thing ticket 100 exists to distrust, so it is never REPORTED as a
// count -- it is only ever compared to one. A count with no declaration is reported as measured;
// a declaration with no measurement is a divergence, because a family nothing recorded is exactly
// the dead instrument the ticket names.
export function reconcileChecks(checks, expected) {
  const divergences = []
  for (const [kind, declared] of Object.entries(expected ?? {})) {
    const measured = checks[kind] ?? 0
    if (measured !== declared) divergences.push(`${kind} declared ${declared}, measured ${measured}`)
  }
  return divergences.length === 0 ? 'exact' : `gallery check counts diverge: ${divergences.join('; ')}`
}

// Per-scenario element-parity coverage, recorded by the assertion itself: how many elements it
// compared and how many a registered absence hid from it. A registered absence exempts a whole
// subtree, so the gate reports the size of that exemption rather than only the number of rows.
export function collectElementParityCoverage(report) {
  const lines = []
  for (const entry of collectTests(report)) {
    const results = entry.results ?? []
    const annotations = results.length > 0
      ? (results[results.length - 1].annotations ?? [])
      : (entry.annotations ?? [])
    for (const annotation of annotations) {
      if (annotation.type === ELEMENT_PARITY_ANNOTATION) lines.push(annotation.description)
    }
  }
  return lines.sort()
}

export function observeGalleryRun(report, scenarioIds) {
  const titles = collectSpecTitles(report)
  if (titles.length === 0) throw new Error('Playwright report contains no tests; the reporter wrote nothing to observe')

  const observedScenarios = new Set()
  let nonScenarioTests = 0
  for (const title of titles) {
    if (title.endsWith(SCENARIO_TITLE_SUFFIX)) observedScenarios.add(title.slice(0, -SCENARIO_TITLE_SUFFIX.length))
    else nonScenarioTests += 1
  }

  const declared = new Set(scenarioIds)
  const notRun = [...declared].filter(id => !observedScenarios.has(id)).sort()
  const notDeclared = [...observedScenarios].filter(id => !declared.has(id)).sort()

  const stats = report.stats ?? {}
  return {
    checks: countChecks(report),
    elementParityCoverage: collectElementParityCoverage(report),
    browserTests: titles.length,
    scenarioBrowserTests: observedScenarios.size,
    nonScenarioBrowserTests: nonScenarioTests,
    outcomes: {
      expected: stats.expected ?? 0,
      unexpected: stats.unexpected ?? 0,
      flaky: stats.flaky ?? 0,
      skipped: stats.skipped ?? 0,
    },
    // Sorted so two runs with the same flakes produce the same list, and a diff of two receipts
    // shows a NEW flake rather than a reordering.
    flakyTests: collectFlakyTitles(report).sort(),
    flakyAttempts: collectFlakyAttempts(report),
    reconciliation: notRun.length === 0 && notDeclared.length === 0
      ? 'exact'
      : `scenario tests do not match the catalog; declared-but-not-run=${JSON.stringify(notRun)} run-but-not-declared=${JSON.stringify(notDeclared)}`,
  }
}

// The declared expectation, derived from the catalog AND from the conditions the run will actually
// execute under. Deriving it from the full catalog while the suite skips work is the same defect
// ticket 100 exists to remove, one level up: under HARBORLINE_CI_GALLERY=1 (validate.yml) two
// scenarios and four parity comparisons do not run, and under a capture run per-scenario reflow does
// not run. `exemptions` is gallery/ci-exemptions.json, the same file gallery.spec.ts skips from.
export function expectedChecks(modules, { ciGallery = false, capture = false, exemptions } = {}) {
  const pixelExempt = new Set(ciGallery ? exemptions.pixel : [])
  const wallClockExempt = new Set(ciGallery ? exemptions.wallClock : [])
  const button = modules.find(module => module.moduleId === 'hlp.ui.button')
  if (!button) throw new Error('gallery expectations require the hlp.ui.button catalog')
  const declares = (scenario, quality) => scenario.sourceQualityCaseIds?.some(id => id.endsWith(`.quality.${quality}`)) ?? false
  // A wall-clock-exempt scenario skips its whole test, so it contributes nothing at all.
  const running = modules.flatMap(module => module.scenarios).filter(scenario => !wallClockExempt.has(scenario.id))
  return {
    accessibilityScans: running.length * 2,
    visualParityComparisons: running.filter(scenario => declares(scenario, 'visual-parity') && !pixelExempt.has(scenario.id)).length
      + button.themes * 3,
    // Per-scenario reflow (one assertion per lane, gated on the declared reflow case id) plus the six
    // bespoke module reflow tests and the button theme and locale fixtures. A capture run declines to
    // judge layout on a page the screenshotter just scrolled, so its per-scenario half is zero.
    reflowChecks: button.locales * 2 + button.themes * 2 + 6
      + (capture ? 0 : running.filter(scenario => declares(scenario, 'reflow')).length * 2),
    // These four were wrong by 3x to 10x while nothing could see them: the theme and locale fixture
    // tests loop over the BUTTON quality fixture, not over every module's themes and locales, and
    // there is one keyboard and one runtime-theme-switch test per projection, not six.
    localeProjectionChecks: button.locales * 2,
    themeFixtureProjectionChecks: button.themes * 2,
    themeStateParityComparisons: button.themes * 3,
    runtimeThemeSwitchChecks: 2,
    keyboardFocusChecks: 2,
    forcedColorsChecks: 6,
    reducedMotionChecks: 8 + button.themes * 2,
    contextMenuTierBProjectionChecks: 2,
  }
}
