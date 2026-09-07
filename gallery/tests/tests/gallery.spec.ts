import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Page, type TestInfo } from '@playwright/test'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'
import { appendFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { arch, platform } from 'node:os'

// Control ticket 100: the gallery gate reported sixteen counts and measured none of them, and one
// drifted for fifteen days on the very commit that added the check meant to stop drift. Every count
// the gate now reports is recorded HERE, by the assertion that does the work, and read back out of
// the Playwright json report. Where the gate still derives an expectation from the catalog it is
// compared against the measurement and a divergence fails the step naming both numbers.
const CHECK_ANNOTATION = 'hlp-check'
// Per-scenario element-parity coverage: elements compared versus elements hidden behind a
// registered absence, read back out of the same json report by tooling/gallery-observations.mjs.
const ELEMENT_PARITY_ANNOTATION = 'hlp-element-parity'
function recordCheck(kind: string) {
  test.info().annotations.push({ type: CHECK_ANNOTATION, description: kind })
}

type Scenario = {
  id: string
  name: string
  interaction?: 'pointer' | 'keyboard' | 'keyboard-pointer'
  sourceQualityCaseIds?: string[]
}
type LocaleFixture = {
  tag: string
  role: 'source' | 'pseudo' | 'verification'
  direction: 'ltr' | 'rtl'
  buttonLabel: string
  loadingStatus: string
  minimumExpansionRatio?: number
  mixedDirectionToken?: string
}
type ThemeFixture = {
  id: 'light' | 'dark'
  colorScheme: 'light' | 'dark'
  scenarioId: string
  tokens: Record<string, string>
}
type Projection = 'react' | 'blazor'
type StoryEntry = { id: string; title: string; name: string; type: 'story' | 'docs' }
type StoryIndex = { entries: Record<string, StoryEntry> }
type Catalog = {
  moduleId: string
  galleryInterface: { projectionTitles: Record<Projection, string> }
  scenarios: Scenario[]
}
type ThemeRegistry = {
  visualParity: {
    pixelmatchThreshold: number
    maximumChangedPixelRatio: number
    includeAntialiasing: boolean
    tileSize: number
    maximumChangedTilePixelRatio: number
  }
}

const testsRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const repositoryRoot = resolve(testsRoot, '../..')
const baselineCaptureRoot = process.env.GALLERY_BASELINE_CAPTURE_DIR
const galleryReviewCaptureRoot = process.env.HARBORLINE_GALLERY_CAPTURE_DIR
const scenarioRoot = resolve(repositoryRoot, 'gallery/scenarios')
const catalogs = readdirSync(scenarioRoot)
  .filter(name => /^hlp\.ui\..+\.json$/.test(name))
  .sort()
  .map(name => JSON.parse(readFileSync(resolve(scenarioRoot, name), 'utf8')) as Catalog)
// ── CI leniency: named, scoped, and self-tightening (control ticket 083) ──
//
// The gate reached a full gallery run on a GitHub 2-core runner and finished 426/436. The failures
// were runner-conditional rather than platform defects: visual-parity comparisons that hold under
// the binding local gate and not on the runner, and two wall-clock-budget scenarios on a contended
// machine. Excluding a whole scenario would take its ACCESSIBILITY assertions down with its pixels,
// so the pixel half alone is exempted and the a11y half stays binding everywhere.
//
// Every exemption is listed BY ID rather than expressed as a pattern, and the ids are checked to
// exist below, so a renamed or deleted scenario reddens the run instead of quietly widening the
// leniency. Nothing here changes a local run: the flag is set only by CI.
const ciGallery = process.env.HARBORLINE_CI_GALLERY === '1'

// Visual comparison only; their a11y assertions still run.
//
// CORRECTED 2026-08-23. These were first justified as "compare against baselines recorded on
// Windows, and Linux font rendering moves enough pixels to exceed the threshold." This file's own
// code contradicts that. assertVisualParity compares the REACT probe against the BLAZOR probe,
// both rendered live in the same browser on the same machine — see assertLocatorVisualParity
// below. No stored baseline participates in the assertion at all: recordReactBaseline only WRITES
// a png, and only when GALLERY_BASELINE_CAPTURE_DIR is set. A glyph that rasterizes differently on
// Linux moves BOTH lanes identically and cancels out of a lane-versus-lane diff.
//
// So the cause of these four is UNKNOWN. They stay exempt on the weaker, honest ground that they
// are runner-conditional — passing on the 8-core Windows host that ADR-0004 makes binding, failing
// on a 2-core Linux runner. That is a description, not an explanation, and it is deliberately
// recorded as one. Whatever makes cross-lane parity hold on one machine and break on another is a
// real difference between the projections; control ticket 097 owns finding it.
// Both sets live in gallery/ci-exemptions.json, which the gate ALSO reads
// (tooling/gallery-observations.mjs) to subtract skipped work from its declared check expectations.
// Two copies would let the expectation describe a run that did not happen; before this file read
// the shared declaration, a CI run measured 924 accessibility scans against a declared 928.
const ciExemptions = JSON.parse(readFileSync(resolve(repositoryRoot, 'gallery/ci-exemptions.json'), 'utf8')) as { pixel: string[]; wallClock: string[] }
const ciPixelExempt = new Set(ciExemptions.pixel)

// Whole scenario. These two assert a wall-clock budget, which a shared runner cannot honour — the
// same reason HARBORLINE_SHARED_CONFORMANCE already excludes the vitest performance case. A budget
// measured on a contended machine measures the machine.
const ciWallClockExempt = new Set(ciExemptions.wallClock)

const catalogById = new Map(catalogs.map(catalog => [catalog.moduleId, catalog]))
function requiredCatalog(moduleId: string): Catalog {
  const catalog = catalogById.get(moduleId)
  if (!catalog) throw new Error(`missing gallery catalog ${moduleId}`)
  return catalog
}
const buttonCatalog = requiredCatalog('hlp.ui.button')
const appShellCatalog = requiredCatalog('hlp.ui.app-shell')
const contextMenuCatalog = requiredCatalog('hlp.ui.context-menu')
const errorCardCatalog = requiredCatalog('hlp.ui.error-card')
const loadingStateCatalog = requiredCatalog('hlp.ui.loading-state')
const themeRegistry = JSON.parse(readFileSync(resolve(repositoryRoot, 'catalog/ui-theme-registry.json'), 'utf8')) as ThemeRegistry
// Divergences the tile rule already sees, each with the ticket that owns it and the ratio measured
// ON EACH GATE HOST. Registered BY SCENARIO ID and BY VALUE: a scenario that is not listed cannot
// exceed the tile threshold at all, and a listed one cannot get worse than its recorded band, nor
// quietly get better — a row whose divergence is gone fails until it is removed.
//
// The value is per host because the rasterisation is: side-nav.theme-dark reads 0.3984 on Windows
// and 0.2891 on macOS, and scheduler.theme-dark reads 0.5 on Windows and 0.5625 on macOS — so a
// single recorded number asserted everywhere fails three rows on macOS for a branch that is
// correct. Each run is therefore held to THIS host's own measurement, plus or minus tolerance. A
// union band over every host would be 0.08-0.17 wide, wide enough to swallow the 0.02-0.06 caret
// and stroke regressions the register exists to catch; and a host with no row would pass inside
// somebody else's numbers, unmeasured. A host with no row fails instead, naming the measure flag.
// A `resolved` entry carrying a `host` says the divergence is gone on THAT host while the row still
// stands on the others (ticket 282: scheduler.theme-light fell to 0.1875 on macos-x64-intel and is
// still 0.4023 on windows-11-x64). That host is then held to the frozen tile threshold like any
// unregistered scenario, instead of failing for having no measurement.
const knownDivergences = JSON.parse(readFileSync(resolve(repositoryRoot, 'gallery/visual-parity-known-divergences.json'), 'utf8')) as {
  tolerance: number
  resolved?: Array<{ scenarioId: string; host?: string; removedBy: string; reason: string }>
  divergences: Array<{
    scenarioId: string
    measurements: Array<{ host: string; worstTilePixelRatio: number }>
    ticket: string
    note: string
  }>
}
const resolvedHere = new Set((knownDivergences.resolved ?? [])
  .filter(entry => entry.host)
  .map(entry => `${entry.scenarioId} ${entry.host}`))
const knownDivergenceById = new Map(knownDivergences.divergences.map(entry => [entry.scenarioId, entry]))
// The naming the register already uses for its rows, derived once, so a run can only ever be judged
// against the host it is actually running on.
const elementRegister = JSON.parse(readFileSync(resolve(repositoryRoot, 'gallery/element-parity-known-divergences.json'), 'utf8')) as {
  divergences: Array<{
    scenarioId: string
    path: string
    property: string
    ticket: string
    note: string
    // The host the row was measured on, when the divergence exists on that host only. Same three
    // keys and the same reason as the tile register: layout and font metrics differ per host, so a
    // row measured on Windows is not evidence about a mac. A row WITHOUT a host applies everywhere.
    // On a host the row does not apply to it is ignored — neither asserted nor stale — so the
    // unregistered-clean rule holds that host to its own census.
    host?: string
    // Only on `element` rows. A registered absence exempts the whole subtree under it from
    // comparison, so the SIZE of that exemption is registered too, measured per lane, and held to
    // the same both-ways rule as every other register value: a change in either count is red.
    hiddenDescendants?: { react: number; blazor: number }
  }>
}
type ElementRegisterRow = (typeof elementRegister.divergences)[number]
const elementRegisterByScenario = new Map<string, typeof elementRegister.divergences>()
for (const row of elementRegister.divergences) {
  const rows = elementRegisterByScenario.get(row.scenarioId) ?? []
  rows.push(row)
  elementRegisterByScenario.set(row.scenarioId, rows)
}
const measureFlag = 'HARBORLINE_GALLERY_PARITY_MEASURE'
const galleryHostKey = platform() === 'win32' ? `windows-11-${arch()}`
  : platform() === 'darwin' ? (arch() === 'x64' ? 'macos-x64-intel' : `macos-${arch()}`)
  : `${platform()}-${arch()}`
const quality = JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.button/quality-fixtures.yaml'), 'utf8')) as {
  locales: LocaleFixture[]
  themes: ThemeFixture[]
  accessibilityCases: Array<{ id: string; expected: Record<string, unknown> }>
  themingCases: Array<{ id: string; expected: Record<string, unknown> }>
}
const errorCardQuality = JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.error-card/quality-fixtures.yaml'), 'utf8')) as {
  locales: Record<string, { direction: 'ltr' | 'rtl'; retry: string }>
  themes: Array<{ id: 'light' | 'dark'; scenarioId: string }>
}
const loadingStateQuality = JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.loading-state/quality-fixtures.yaml'), 'utf8')) as {
  locales: Record<string, { direction: 'ltr' | 'rtl'; label: string }>
  themes: Array<{ id: 'light' | 'dark'; scenarioId: string }>
}
const reactBase = process.env.REACT_GALLERY_URL ?? 'http://127.0.0.1:6106'
const blazorBase = process.env.BLAZOR_GALLERY_URL ?? 'http://127.0.0.1:6107'

async function reactIndex(page: Page): Promise<StoryIndex> {
  let index: StoryIndex | undefined
  await expect(async () => {
    const response = await page.request.get(`${reactBase}/index.json`)
    expect(response.ok()).toBeTruthy()
    index = await response.json() as StoryIndex
  }).toPass({ intervals: [250, 500, 1_000], timeout: 5_000 })
  return index!
}

// Under four-worker load, an evaluate that awaits a PAGE-SIDE promise (the BlazingStory
// index/ready calls) can lose that promise to garbage collection before it settles —
// Playwright surfaces "Resulting promise was garbage collected" although the page is
// healthy. Re-issue the evaluate a bounded number of times on exactly that error; every
// other failure propagates unchanged. (Migration ticket 075 — observed on unrelated
// scenarios at ~per-run frequency, so retry-at-test level did not clear it.)
async function evaluateSettled<T>(issue: () => Promise<T>): Promise<T> {
  let lastError: unknown
  for (let attempt = 0; attempt < 3; attempt++) {
    try {
      return await issue()
    }
    catch (error) {
      if (!String(error).includes('Resulting promise was garbage collected')) throw error
      lastError = error
    }
  }
  throw lastError
}

async function blazorIndex(page: Page): Promise<StoryIndex> {
  await page.goto(blazorBase)
  await page.waitForFunction(() => typeof BlazingStory !== 'undefined')
  return evaluateSettled(() => page.evaluate(() => BlazingStory.getStoryIndex()))
}

function storyId(index: StoryIndex, title: string, name: string): string {
  const entry = Object.values(index.entries).find(item => item.type === 'story' && item.title === title && item.name === name)
  expect(entry, `missing ${title} / ${name}`).toBeDefined()
  return entry!.id
}

async function openReactStory(page: Page, id: string) {
  await page.goto(`${reactBase}/iframe.html?id=${encodeURIComponent(id)}&viewMode=story`)
  await expect(page.locator('[data-gallery-probe]')).toBeVisible()
}

async function openBlazorStory(page: Page, id: string) {
  await page.goto(`${blazorBase}/iframe.html?id=${encodeURIComponent(id)}&viewMode=story`)
  await page.waitForFunction(() => typeof BlazingStory !== 'undefined')
  await evaluateSettled(() => page.evaluate(() => BlazingStory.readyView()))
  await expect(page.locator('[data-gallery-probe]')).toBeVisible()
}

async function openProjectionStory(page: Page, projection: Projection, catalog: Catalog, scenarioName: string) {
  const index = projection === 'react' ? await reactIndex(page) : await blazorIndex(page)
  const title = catalog.galleryInterface.projectionTitles[projection]
  const id = storyId(index, title, scenarioName)
  if (projection === 'react') await openReactStory(page, id)
  else await openBlazorStory(page, id)
}

// The story canvas is narrower than the browser viewport (gallery chrome and padding), and the App Shell
// decides dock PLACEMENT from the class its own MEASURED inline size falls in (dock-model.ts createDockLayout),
// never from the viewport. So a conformance row keyed on a measured shell width has to be replayed at a
// viewport DERIVED from the page - viewport = fixture width + the chrome measured right here - and never at a
// viewport constant, or a 1344 viewport seats a sub-1200 shell that docks nothing at all.
async function sizeViewportToShell(page: Page, shellWidth: number, height: number): Promise<number> {
  const shell = page.locator('.hl-app-shell').first()
  const measure = async () => (await shell.boundingBox())!.width
  // The chrome can itself depend on the width (scrollbars, clamped canvases), so converge on it rather than
  // assuming one subtraction lands: each pass re-measures and re-derives, and equality is the fixed point.
  for (let pass = 0; pass < 6; pass += 1) {
    const viewport = page.viewportSize()!.width
    const next = Math.round(shellWidth + viewport - await measure())
    if (next === viewport) break
    await page.setViewportSize({width: next, height})
  }
  const measured = await measure()
  expect(measured, `shell measured ${measured} at viewport ${page.viewportSize()!.width} for conformance width ${shellWidth}`).toBeCloseTo(shellWidth, 0)
  return measured
}

function scenarioName(catalog: Catalog, id: string) {
  const scenario = catalog.scenarios.find(item => item.id === id)
  expect(scenario, `missing neutral gallery scenario ${id}`).toBeDefined()
  return scenario!.name
}

async function prepareScenario(page: Page, scenario: Scenario) {
  if (!scenario.interaction) return
  if (scenario.id.startsWith('action-menu.')) {
    const trigger = page.locator('.hl-action-menu__trigger').first()
    await trigger.focus()
    if (scenario.id === 'action-menu.empty') {
      await trigger.press('ArrowDown')
      await expect(page.getByRole('menu')).toBeVisible()
      const message = page.locator('.hl-action-menu__empty')
      await expect(message).toHaveText('No actions available')
      const box = await message.boundingBox()
      expect(box, 'empty message has a rendered box').not.toBeNull()
      expect(box!.width, 'empty message width').toBeGreaterThan(0)
      expect(box!.height, 'empty message height meets the declared 2.5rem floor').toBeGreaterThanOrEqual(40)
      await expect(trigger, 'focus stays on the trigger when there is nothing to focus').toBeFocused()
      return
    }
    if (scenario.id === 'action-menu.pointer') await trigger.click()
    else await trigger.press('ArrowDown')
    await expect(page.getByRole('menu')).toBeVisible()
    if (scenario.id !== 'action-menu.pointer') await expect(page.getByRole('menuitem').first()).toBeFocused()
    if (scenario.id === 'action-menu.dismissal') {
      await page.keyboard.press('Escape')
      await expect(page.getByRole('menu')).toBeHidden()
      await expect(trigger).toBeFocused()
    }
    return
  }
  if (scenario.id === 'activity-log.disclosure') {
    const more = page.getByRole('button', { name: /^Show more/ })
    await more.focus()
    await more.press('Enter')
    return
  }
  if (scenario.id.startsWith('chip.')) {
    const chip = page.locator('.hl-chip__body').first()
    await chip.focus()
    if (scenario.id === 'chip.interactions') await chip.press('Space')
    return
  }
  if (scenario.id.startsWith('conversation-list.')) {
    if (scenario.id === 'conversation-list.rename') {
      await page.getByRole('button', { name: /^Rename:/ }).first().click()
      await expect(page.getByRole('textbox')).toBeFocused()
      return
    }
    if (scenario.id === 'conversation-list.delete') {
      await page.getByRole('button', { name: /^Delete:/ }).first().click()
      await expect(page.locator('button.hl-conversation-list__confirm, .hl-conversation-list__confirm button').first()).toBeFocused()
      return
    }
    await page.locator('.hl-conversation-list__select').first().focus()
    return
  }
  if (scenario.id.startsWith('guarded-control.')) {
    const action = page.locator('[data-guard-state]').first()
    await action.focus()
    await action.press('Enter')
    if (scenario.id === 'guarded-control.keyboard-recovery') await page.keyboard.press('Escape')
    return
  }
  if (scenario.id === 'layers-rail.lenses') {
    await page.locator('.hl-layers-rail__lenses button').nth(2).click()
    return
  }
  if (scenario.id === 'layers-rail.outline') {
    const node = page.locator('[role="treeitem"] button[tabindex="0"]').first()
    await node.focus()
    await node.press('ArrowDown')
    return
  }
  if (scenario.id === 'layers-rail.provenance') {
    await page.locator('.hl-layers-rail__outline-header button').click()
    return
  }
  if (scenario.id.startsWith('notification-center.')) {
    const trigger = page.locator('.hl-notification-center__trigger').first()
    await trigger.focus()
    await trigger.press('Enter')
    await expect(page.getByRole('dialog')).toBeVisible()
    return
  }
  if (scenario.id === 'page.keyboard-responsive') {
    await page.locator('.hl-page__body').focus()
    return
  }
  if (scenario.id === 'search-input.debounce') {
    await page.getByRole('searchbox').fill('pier')
    return
  }
  if (scenario.id === 'search-input.sync-clear') {
    await page.locator('.hl-search-input__clear').click()
    return
  }
  if (scenario.id.startsWith('segmented-control.')) {
    const option = page.getByRole('radio').first()
    await option.focus()
    if (scenario.interaction === 'pointer') await option.click()
    else await option.press('ArrowRight')
    return
  }
  if (scenario.id.startsWith('select-field.')) {
    const trigger = page.getByRole('combobox').first()
    await trigger.click()
    await expect(page.getByRole('listbox').first()).toBeVisible()
    if (scenario.id === 'select-field.selection') await page.getByRole('option').nth(1).click()
    else await trigger.press('Escape')
    return
  }
  if (scenario.id.startsWith('switch.')) {
    const control = page.getByRole('switch').first()
    await control.focus()
    await control.press('Space')
    return
  }
  if (scenario.id === 'toaster.queue-lifecycle') {
    await page.locator('.hl-toaster__close,.hl-toast__close').first().click()
    return
  }
  if (scenario.id === 'toaster.action-promise') {
    await page.locator('.hl-toaster__action,.hl-toast__action').first().click()
    return
  }
  if (scenario.id.startsWith('user-menu.')) {
    const trigger = page.locator('.hl-user-menu__trigger').first()
    await trigger.focus()
    await trigger.press('Enter')
    await expect(page.locator('[role="menu"],[role="dialog"]').first()).toBeVisible()
    return
  }
  if (scenario.id.startsWith('app-layout.')) {
    if (scenario.id === 'app-layout.dismissal-scroll') {
      await page.keyboard.press('Tab')
      return
    }
    const drawer = page.getByRole('dialog').first()
    if (!await drawer.isVisible().catch(() => false)) await page.locator('.hl-app-layout__nav-trigger').first().click()
    return
  }
  if (scenario.id === 'chart.replacement') {
    await page.getByRole('button', { name: 'Replace definition' }).click()
    return
  }
  if (scenario.id === 'chart.accessible-data') {
    const summary = page.locator('.hl-chart__data > summary')
    await summary.focus()
    await summary.press('Enter')
    return
  }
  if (scenario.id.startsWith('chat.')) {
    if (scenario.id === 'chat.empty-suggestions') {
      await page.getByRole('button', { name: 'Explain the conflict' }).click()
      return
    }
    if (scenario.id === 'chat.controlled-composer') {
      const composer = page.getByRole('textbox').first()
      await composer.focus()
      await composer.press('Enter')
      return
    }
    const message = page.locator('.hl-chat__message').first()
    await message.focus()
    await message.press('ArrowDown')
    return
  }
  if (scenario.id.startsWith('data-grid.')) {
    if (scenario.id === 'data-grid.replacement-state') {
      await page.getByRole('button', { name: 'Replace rows and columns' }).click()
      return
    }
    const cell = page.locator('.hl-data-grid [role="gridcell"]').first()
    await cell.focus()
    return
  }
  if (scenario.id === 'gantt.zoom-navigation') {
    await page.getByRole('combobox').selectOption('week')
    return
  }
  if (scenario.id.startsWith('numeric-text-box.')) {
    const input = page.getByRole('textbox').first()
    if (scenario.id === 'numeric-text-box.stepping-bounds') {
      await input.focus()
      await input.press('ArrowUp')
      return
    }
    if (scenario.id === 'numeric-text-box.controlled-replacement') {
      await page.getByRole('button', { name: 'Replace caller value' }).click()
      return
    }
    await input.fill('1250.75')
    await input.press('Enter')
    return
  }
  if (scenario.id.startsWith('context-menu.')) {
    const trigger = page.locator('[data-context-trigger]').first()
    if (scenario.interaction === 'pointer') await trigger.click({button: 'right', position: {x: 36, y: 36}})
    else {
      await trigger.focus()
      await trigger.dispatchEvent('keydown', {key: 'F10', code: 'F10', shiftKey: true, bubbles: true})
    }
    await expect(page.getByRole('menu')).toBeVisible()
    return
  }
  if (scenario.id === 'app-shell.switchers') {
    await page.locator('[data-switcher="tenant"] .hl-app-shell__switcher-trigger').click()
    await expect(page.locator('.hl-app-shell__switcher-menu')).toBeVisible()
    return
  }
  if (scenario.id === 'app-shell.pins-threads') {
    await page.locator('.hl-app-shell__kebab').first().click()
    await expect(page.locator('.hl-app-shell__row-menu')).toBeVisible()
    return
  }
  if (scenario.id === 'app-shell.actions-endpanel' || scenario.id === 'detail-panel.sheet') {
    await page.keyboard.press('Tab')
    return
  }
  throw new Error(`missing interaction preparation for ${scenario.id}`)
}

// assertResponsiveQuality's evidence half. WCAG 1.4.10 reflow: at a 320 CSS-pixel viewport with text
// scaled to 200%, content must not require horizontal scrolling.
//
// This rides the per-scenario test rather than adding a test of its own, because both pages are
// already open at that point -- a separate loop would have paid 98 fresh page loads for a viewport
// resize and one measurement. It runs LAST in the scenario, after visual parity, because it mutates
// the viewport and the root font size and a pixel comparison taken afterwards would be comparing
// something nobody designed.
//
// Before this, sixteen reflow checks ran in the whole suite -- theme and locale fixtures plus three
// bespoke module tests -- while forty-six modules DECLARED a reflow quality case. Declared and
// unrendered, the same shape the empty/error, content and state gates each turned out to have.
// Four scenarios overflow at 320px and 200% text, measured 2026-08-25 when this check was first
// written. They are RECORDED rather than silenced: each carries the overflow it produced, and the
// assertion below fails in BOTH directions -- a scenario not listed must not overflow, and a listed
// one must still overflow. A fixed component whose entry stays here would otherwise keep a WCAG
// 1.4.10 failure permanently excused for a defect that no longer exists, which is the
// exemption-expiry rule ticket 083 already paid to learn.
//
// These are real defects, not tuning. The viewport is 320 CSS pixels wide and app-shell overflows it
// by 552 -- that is a layout that has never been asked to be narrow, not a rounding error.
// Keyed by scenario AND LANE. hlp.ui.notification-center overflows in Blazor by 6px while React sits
// at 0, so a scenario-only key would have demanded React keep overflowing too and turned a passing
// lane red. A lane-specific defect is also a PARITY defect, and flattening the two lanes into one
// entry would have hidden which one is broken.
const reflowKnownOverflow = new Map<string, number>([
  ['app-shell.actions-endpanel|react', 552],
  ['app-shell.actions-endpanel|blazor', 552],
  ['app-layout.dismissal-scroll|react', 454],
  ['app-layout.dismissal-scroll|blazor', 454],
  ['select-field.states|react', 52],
  ['select-field.states|blazor', 52],
  ['user-menu.placement-dismissal|react', 37],
  ['user-menu.placement-dismissal|blazor', 37],
])

async function assertReflow(page: Page, scenario: Scenario, projection: string) {
  await page.setViewportSize({ width: 320, height: 720 })
  await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
  const probe = page.locator('[data-gallery-probe]')
  await probe.waitFor({ state: 'attached' })
  // Wait for the layout to SETTLE. Changing the viewport and the root font size are two reflow
  // triggers, and measuring immediately reads a value mid-reflow: hlp.ui.notification-center
  // reported 6px of overflow in a capture run and 0 in a sharded gate run minutes apart, which is
  // a measurement that has not settled rather than a component that sometimes overflows. Requiring
  // two consecutive agreeing readings across animation frames removes the flake without hiding a
  // real overflow, which stays stable across any number of frames.
  const overflow = await probe.evaluate(element => new Promise<number>(resolve => {
    const measure = () => Math.max(0, element.scrollWidth - element.clientWidth)
    let previous = measure()
    let stableFrames = 0
    const tick = () => {
      const current = measure()
      stableFrames = current === previous ? stableFrames + 1 : 0
      previous = current
      if (stableFrames >= 2) resolve(current)
      else requestAnimationFrame(tick)
    }
    requestAnimationFrame(tick)
  }))
  recordCheck('reflowChecks')
  const knownKey = `${scenario.id}|${projection}`
  if (reflowKnownOverflow.has(knownKey)) {
    expect(overflow,
      `${knownKey} is recorded as overflowing at 320px and 200% text `
      + `(${reflowKnownOverflow.get(knownKey)}px when measured). It no longer does — remove it from `
      + 'reflowKnownOverflow so the check protects it again.').toBeGreaterThan(0)
    return
  }
  expect(overflow, `${projection} ${scenario.id} horizontal overflow at 320px and 200% text`).toBe(0)
}

async function assertAccessible(page: Page, scenario: Scenario, projection: string) {
  // Storybook's a11y addon runs its own axe pass on story load; on heavy stories it can still be
  // in flight when this scan starts, and axe-core rejects concurrent runs in one frame.
  let result: Awaited<ReturnType<AxeBuilder['analyze']>> | undefined
  for (let attempt = 0; ; attempt++) {
    try {
      result = await new AxeBuilder({ page }).include('[data-gallery-probe]').analyze()
      break
    } catch (error) {
      if (attempt >= 4 || !String(error).includes('Axe is already running')) throw error
      await page.waitForTimeout(500)
    }
  }
  expect(result.violations, `${projection} ${scenario.id} accessibility violations`).toEqual([])
  recordCheck('accessibilityScans')
}

async function recordReactBaseline(page: Page, catalog: Catalog, scenario: Scenario) {
  if (!baselineCaptureRoot) return
  const png = await captureLocatorPng(page.locator('[data-gallery-probe]'))
  writeFileSync(resolve(baselineCaptureRoot, `${catalog.moduleId}--${scenario.id}.png`), png)
}

async function captureGalleryReviewPair(reactPage: Page, blazorPage: Page, catalog: Catalog, scenario: Scenario) {
  if (!galleryReviewCaptureRoot) return
  const reactLocator = reactPage.locator('[data-gallery-probe]')
  const blazorLocator = blazorPage.locator('[data-gallery-probe]')
  await Promise.all([reactLocator.waitFor(), blazorLocator.waitFor()])
  await Promise.all([
    reactPage.waitForFunction(() => {
      const probe = document.querySelector('[data-gallery-probe]')
      return Boolean(probe && (probe.childElementCount > 0 || probe.textContent?.trim()))
    }),
    blazorPage.waitForFunction(() => {
      const probe = document.querySelector('[data-gallery-probe]')
      return Boolean(probe && (probe.childElementCount > 0 || probe.textContent?.trim()))
    }),
  ])
  const [reactBox, blazorBox] = await Promise.all([stabilizedBox(reactLocator), stabilizedBox(blazorLocator)])
  const [reactBytes, blazorBytes] = await Promise.all([
    captureLocatorPng(reactLocator, reactBox),
    captureLocatorPng(blazorLocator, blazorBox),
  ])
  const baseName = `${catalog.moduleId}--${scenario.id}`
  const reactFileName = `${baseName}--react.png`
  const blazorFileName = `${baseName}--blazor.png`
  const diffFileName = `${baseName}--diff.png`
  const metadataName = `${catalog.moduleId}--${scenario.id}.json`
  const directories = ['react', 'blazor', 'diff', 'metadata']
  for (const directory of directories) mkdirSync(resolve(galleryReviewCaptureRoot, directory), { recursive: true })
  writeFileSync(resolve(galleryReviewCaptureRoot, 'react', reactFileName), reactBytes)
  writeFileSync(resolve(galleryReviewCaptureRoot, 'blazor', blazorFileName), blazorBytes)

  const reactPng = PNG.sync.read(reactBytes)
  const blazorPng = PNG.sync.read(blazorBytes)
  let changedPixelRatio: number | null = null
  let diffFile: string | null = null
  if (reactPng.width === blazorPng.width && reactPng.height === blazorPng.height) {
    const diff = new PNG({ width: reactPng.width, height: reactPng.height })
    const changed = pixelmatch(reactPng.data, blazorPng.data, diff.data, reactPng.width, reactPng.height, {
      threshold: themeRegistry.visualParity.pixelmatchThreshold,
      includeAA: themeRegistry.visualParity.includeAntialiasing,
    })
    changedPixelRatio = changed / (reactPng.width * reactPng.height)
    diffFile = `diff/${diffFileName}`
    writeFileSync(resolve(galleryReviewCaptureRoot, diffFile), PNG.sync.write(diff))
  }
  writeFileSync(resolve(galleryReviewCaptureRoot, 'metadata', metadataName), `${JSON.stringify({
    moduleId: catalog.moduleId,
    scenarioId: scenario.id,
    scenarioName: scenario.name,
    react: { file: `react/${reactFileName}`, width: reactPng.width, height: reactPng.height },
    blazor: { file: `blazor/${blazorFileName}`, width: blazorPng.width, height: blazorPng.height },
    diff: diffFile,
    changedPixelRatio,
  }, null, 2)}\n`)
}

// ── Per-element parity (ticket 147 slice 2) ──
//
// The whole-scene probe reads a picture. A picture cannot tell "one lane never gave this element a
// size" from "the same glyphs rasterised a shade differently": a 16px chevron rendered at zero size
// moved select-field.theme-light from 0.000000 to 0.000145 of the capture and 0.1094 of its own
// worst tile, under both floors (slice 1's measurement). So the same two scenes are also read as
// structure: every element under the probe, in both lanes, by its position in the tree.
//
// The pairing key is the DOM path itself — no new attribute scheme, because there is no per-element
// identity both lanes already share: `data-gallery-probe` marks only the scene root, `data-hl-*` is
// per module and partial, and the CLASS is exactly what drifts (React spells the current crumb
// `hl-breadcrumb__text--current`, Blazor `hl-breadcrumb__current`, ticket 283). Tag plus ordinal is
// what the two lanes are built to agree on.
const elementParityStyles = ['font-size', 'font-weight', 'color', 'display', 'visibility', 'opacity'] as const
// Sub-pixel layout differs legitimately between the engines; a whole missing or unsized element does
// not. One CSS pixel is above the observed rounding noise and an order of magnitude below the
// smallest affordance the catalog declares.
const elementParityBoxTolerance = 1
type ElementRecord = { path: string; label: string; box: [number, number, number, number]; styles: Record<string, string> }

async function elementCensus(locator: ReturnType<Page['locator']>): Promise<ElementRecord[]> {
  return locator.evaluate((root, styleNames: readonly string[]) => {
    const rootRect = root.getBoundingClientRect()
    const records: Array<{ path: string; label: string; box: [number, number, number, number]; styles: Record<string, string> }> = []
    const walk = (parent: Element, parentPath: string) => {
      const ordinals = new Map<string, number>()
      for (const child of Array.from(parent.children)) {
        const tag = child.tagName.toLowerCase()
        const ordinal = (ordinals.get(tag) ?? 0) + 1
        ordinals.set(tag, ordinal)
        const path = `${parentPath}>${tag}[${ordinal}]`
        const rect = child.getBoundingClientRect()
        const computed = getComputedStyle(child)
        const styles: Record<string, string> = {}
        for (const name of styleNames) styles[name] = computed.getPropertyValue(name)
        const className = typeof child.className === 'string' ? child.className : child.getAttribute('class') ?? ''
        records.push({
          path,
          label: className ? `${tag}.${className.trim().split(/\s+/).join('.')}` : tag,
          box: [rect.x - rootRect.x, rect.y - rootRect.y, rect.width, rect.height],
          styles,
        })
        walk(child, path)
      }
    }
    walk(root, '')
    return records
  }, elementParityStyles as unknown as string[])
}

// Every difference this pair of censuses shows, one line each, in the register's own row shape:
// scenario, element path, property. `element` means one lane has no element at that path at all.
function elementParityDeltas(react: ElementRecord[], blazor: ElementRecord[]) {
  const byPath = (records: ElementRecord[]) => new Map(records.map(record => [record.path, record]))
  const reactByPath = byPath(react)
  const blazorByPath = byPath(blazor)
  const paths = [...new Set([...reactByPath.keys(), ...blazorByPath.keys()])].sort()
  const deltas: Array<{ path: string; property: string; detail: string; hidden?: { react: number; blazor: number } }> = []
  const descendantsOf = (records: ElementRecord[], path: string) => records.filter(record => record.path.startsWith(`${path}>`)).length
  // An element one lane never rendered takes its whole subtree with it, and every descendant would
  // otherwise be reported again as its own row. The topmost absence is the finding; the 812 raw
  // `element` rows measured on main are 349 once the subtrees are folded into their root.
  const absentRoots: string[] = []
  for (const path of paths) {
    if (absentRoots.some(root => path.startsWith(`${root}>`))) continue
    const inReact = reactByPath.get(path)
    const inBlazor = blazorByPath.get(path)
    if (!inReact || !inBlazor) {
      const present = (inReact ?? inBlazor)!
      absentRoots.push(path)
      deltas.push({
        path,
        property: 'element',
        detail: `${present.label} is present in ${inReact ? 'react' : 'blazor'} and absent in ${inReact ? 'blazor' : 'react'}`,
        hidden: { react: descendantsOf(react, path), blazor: descendantsOf(blazor, path) },
      })
      continue
    }
    const box = (record: ElementRecord) => `${record.box.map(value => value.toFixed(2)).join(',')}`
    const zeroed = (record: ElementRecord) => record.box[2] === 0 || record.box[3] === 0
    if (zeroed(inReact) !== zeroed(inBlazor)) {
      deltas.push({ path, property: 'box', detail: `${inReact.label} renders at zero size in ${zeroed(inReact) ? 'react' : 'blazor'} (react ${box(inReact)} vs blazor ${box(inBlazor)})` })
    }
    else if (inReact.box.some((value, index) => Math.abs(value - inBlazor.box[index]) > elementParityBoxTolerance)) {
      deltas.push({ path, property: 'box', detail: `${inReact.label} box differs beyond ${elementParityBoxTolerance}px (react ${box(inReact)} vs blazor ${box(inBlazor)})` })
    }
    for (const name of elementParityStyles) {
      if (inReact.styles[name] !== inBlazor.styles[name]) {
        deltas.push({ path, property: name, detail: `${inReact.label} ${name} is ${inReact.styles[name]} in react and ${inBlazor.styles[name]} in blazor` })
      }
    }
  }
  return deltas
}

// The whole register comparison, as a pure function of (measured deltas, register rows, host), so
// the semantics can be asserted directly instead of only through a browser run.
function reconcileElementParity(
  id: string,
  deltas: Array<{ path: string; property: string; detail: string; hidden?: { react: number; blazor: number } }>,
  rows: ElementRegisterRow[],
  hostKey: string,
) {
  // A row scoped to another host is not this host's evidence: it is neither applicable nor stale.
  const registered = rows.filter(row => !row.host || row.host === hostKey)
  const registeredKeys = new Set(registered.map(row => `${row.path}\t${row.property}`))
  const seen = new Set<string>()
  const unregistered: string[] = []
  // A registered absence stops its subtree being read element by element, so the exemption's size is
  // measured every run and compared to the registered count. Silence about how much a row hides is
  // exactly how an absence grows from one wrapper into most of a component (review 1, finding 1).
  const hiddenDrift: string[] = []
  const absenceRoots: Array<{ path: string; hidden: { react: number; blazor: number } }> = []
  for (const delta of deltas) {
    const key = `${delta.path}\t${delta.property}`
    if (!registeredKeys.has(key)) {
      unregistered.push(`${id} ${delta.path} ${delta.property}: ${delta.detail}`)
      continue
    }
    seen.add(key)
    if (!delta.hidden) continue
    absenceRoots.push({ path: delta.path, hidden: delta.hidden })
    const row = registered.find(candidate => candidate.path === delta.path && candidate.property === delta.property)!
    const declared = row.hiddenDescendants
    if (!declared || declared.react !== delta.hidden.react || declared.blazor !== delta.hidden.blazor) {
      hiddenDrift.push(`${id} ${delta.path} registered absence hides ${declared ? `${declared.react} react / ${declared.blazor} blazor` : 'an unrecorded number of'} descendants but now hides ${delta.hidden.react} react / ${delta.hidden.blazor} blazor; re-measure the row`)
    }
  }
  // Exactly the rule the pending set uses: a registered row that no longer differs is red, so a
  // divergence cannot be fixed and left claimed. Per host: only rows that apply here can go stale.
  const stale = registered
    .filter(row => !seen.has(`${row.path}\t${row.property}`))
    .map(row => `${id} ${row.path} ${row.property} is registered to ticket ${row.ticket}${row.host ? ` on ${row.host}` : ''} but no longer differs on ${hostKey}; delete the row`)
  return { unregistered, hiddenDrift, stale, absenceRoots }
}

async function assertElementParity(reactPage: Page, blazorPage: Page, id: string) {
  const [react, blazor] = await Promise.all([
    elementCensus(reactPage.locator('[data-gallery-probe]')),
    elementCensus(blazorPage.locator('[data-gallery-probe]')),
  ])
  const deltas = elementParityDeltas(react, blazor)
  const hiddenColumn = (delta: { hidden?: { react: number; blazor: number } }) =>
    delta.hidden ? `${delta.hidden.react},${delta.hidden.blazor}` : ''
  if (process.env.HARBORLINE_GALLERY_ELEMENT_MEASURE) {
    appendFileSync(process.env.HARBORLINE_GALLERY_ELEMENT_MEASURE,
      deltas.map(delta => `${id}	${delta.path}	${delta.property}	${delta.detail}	${hiddenColumn(delta)}
`).join('') || `${id}	-	-	clean	
`)
  }
  const { unregistered, hiddenDrift, stale, absenceRoots } =
    reconcileElementParity(id, deltas, elementRegisterByScenario.get(id) ?? [], galleryHostKey)
  expect(unregistered, `${id} per-element parity`).toEqual([])
  expect(hiddenDrift, `${id} per-element parity registered absence size`).toEqual([])
  expect(stale, `${id} per-element parity register`).toEqual([])
  // What the run actually read versus what the register hid, per lane, so the exemption's size is in
  // the gate's own output and not only in the register file.
  const hiddenIn = (records: ElementRecord[], lane: 'react' | 'blazor') =>
    absenceRoots.reduce((total, root) => total + root.hidden[lane], 0)
  const reactHidden = hiddenIn(react, 'react')
  const blazorHidden = hiddenIn(blazor, 'blazor')
  test.info().annotations.push({
    type: ELEMENT_PARITY_ANNOTATION,
    description: `${id} react ${react.length - reactHidden}/${react.length} compared, ${reactHidden} hidden; blazor ${blazor.length - blazorHidden}/${blazor.length} compared, ${blazorHidden} hidden`,
  })
  recordCheck('elementParityComparisons')
}

async function assertVisualParity(reactPage: Page, blazorPage: Page, scenario: Scenario, testInfo: TestInfo) {
  await assertLocatorVisualParity(
    reactPage.locator('[data-gallery-probe]'),
    blazorPage.locator('[data-gallery-probe]'),
    scenario.id,
    testInfo,
  )
}

async function stabilizedBox(locator: ReturnType<Page['locator']>) {
  // Measure from the untransformed position so repeated snaps (record capture, then
  // parity) are idempotent — re-measuring an already-snapped rect would compute a
  // zero translate and overwrite the compensation, un-snapping the probe.
  await locator.evaluate(element => {
    ;(element as HTMLElement).style.transform = 'none'
    const rect = element.getBoundingClientRect()
    ;(element as HTMLElement).style.transform =
      `translate(${Math.round(rect.x) - rect.x}px, ${Math.round(rect.y) - rect.y}px)`
  })
  const box = await locator.boundingBox()
  if (!box) throw new Error('gallery probe has no bounding box')
  return box
}

async function captureLocatorPng(
  locator: ReturnType<Page['locator']>,
  box?: { x: number, y: number, width: number, height: number },
  dimensions?: { width: number, height: number },
) {
  const clipBox = box ?? await stabilizedBox(locator)
  const clipDimensions = dimensions ?? clipBox
  return locator.page().screenshot({
    animations: 'disabled',
    clip: {
      x: Math.floor(clipBox.x),
      y: Math.floor(clipBox.y),
      width: Math.ceil(clipDimensions.width),
      height: Math.ceil(clipDimensions.height),
    },
  })
}

async function assertLocatorVisualParity(reactLocator: ReturnType<Page['locator']>, blazorLocator: ReturnType<Page['locator']>, id: string, testInfo: TestInfo) {
  // Compare the lanes in CSS-pixel space, not raster space. Each host page positions its
  // probe at a different fractional offset, and an element whose CSS box is identical in
  // both lanes can rasterize to different row counts (ceil(y+h)-floor(y) differs with y),
  // which failed this assertion by exactly one pixel on hosts with fractional offsets.
  // Asserting the CSS box and clipping both screenshots to the same integral rectangle
  // keeps the dimension check exact to the CSS pixel and the pixel diff aligned.
  const [reactBox, blazorBox] = await Promise.all([stabilizedBox(reactLocator), stabilizedBox(blazorLocator)])
  expect({ width: Math.ceil(reactBox.width), height: Math.ceil(reactBox.height) }, `${id} canvas dimensions`).toEqual({
    width: Math.ceil(blazorBox.width),
    height: Math.ceil(blazorBox.height),
  })
  const [reactBytes, blazorBytes] = await Promise.all([
    captureLocatorPng(reactLocator, reactBox),
    captureLocatorPng(blazorLocator, blazorBox, reactBox),
  ])
  const reactPng = PNG.sync.read(reactBytes)
  const blazorPng = PNG.sync.read(blazorBytes)

  const diff = new PNG({ width: reactPng.width, height: reactPng.height })
  const changed = pixelmatch(reactPng.data, blazorPng.data, diff.data, reactPng.width, reactPng.height, {
    threshold: themeRegistry.visualParity.pixelmatchThreshold,
    includeAA: themeRegistry.visualParity.includeAntialiasing,
  })
  const ratio = changed / (reactPng.width * reactPng.height)
  // A whole-capture ratio cannot tell "one lane dropped an element" from "the same glyphs rasterised
  // a shade differently": 288 changed pixels in a 760x270 capture is 0.0014, an order of magnitude
  // under the 0.015 floor, while rasterisation spreads a few changed pixels over every glyph and
  // never concentrates. So the same diff is also read locally, as the worst tileSize-square tile.
  // Measured across all 91 compared scenarios on the binding host, cross-engine noise tops out at
  // 0.27 of a 16px tile and the real lane divergences it separated sat at 0.29-0.63 — the threshold
  // sits in that gap. After 282's stylesheet reconciles the two that remain sit at 0.40-0.63, and
  // each is registered below with the ticket that owns it.
  // (Ticket 282's stylesheet convergence dropped both app-shell rows under 0.34; they were
  // removed from the register and are now held to the frozen threshold like any other scenario.
  // Train B moved scheduler.theme-dark to 0.625 on macOS and dropped scheduler.theme-light to
  // 0.1875 there — that ONE host is resolved out of the row while Windows keeps it at 0.4023.
  // macos-arm64 (Apple silicon) reads exactly what macos-x64-intel reads on every scenario:
  // scheduler.theme-dark 0.625 is a row on both, while scheduler.theme-light 0.1875 sits under the
  // frozen 0.34 and is resolved out for each of them. Train C's side-nav reconcile then took BOTH
  // side-nav scenarios off the register on every host, re-measured on train C's own tree:
  // theme-dark 0.3984 -> 0.1172 on Windows and 0.2891 -> 0.1172 on both macs, theme-light
  // 0.3867 -> 0 on Windows and 0.3516 -> 0 on both macs. The 0.3516 mac rows the register carried
  // were measured on train B, before the stylesheets converged; all six hosts are now under the
  // frozen threshold, which holds them.)
  const worstTile = worstChangedTile(diff.data, reactPng.width, reactPng.height, themeRegistry.visualParity.tileSize)
  if (process.env.HARBORLINE_GALLERY_PARITY_MEASURE) {
    appendFileSync(process.env.HARBORLINE_GALLERY_PARITY_MEASURE,
      `${id}	ratio=${ratio.toFixed(6)}	worstTile=${worstTile.ratio.toFixed(4)}	at=${worstTile.x},${worstTile.y}
`)
  }
  if (ratio > themeRegistry.visualParity.maximumChangedPixelRatio
    || worstTile.ratio > themeRegistry.visualParity.maximumChangedTilePixelRatio) {
    await testInfo.attach(`${id}-react.png`, { body: PNG.sync.write(reactPng), contentType: 'image/png' })
    await testInfo.attach(`${id}-blazor.png`, { body: PNG.sync.write(blazorPng), contentType: 'image/png' })
    await testInfo.attach(`${id}-diff.png`, { body: PNG.sync.write(diff), contentType: 'image/png' })
  }
  expect(ratio, `${id} changed-pixel ratio`).toBeLessThanOrEqual(themeRegistry.visualParity.maximumChangedPixelRatio)
  // A whole-capture ratio cannot tell "one lane dropped a 16px caret" from "the same glyphs
  // rasterised a shade differently": 288 changed pixels in a 760x270 capture is 0.0014, an order of
  // magnitude under the 0.015 floor, while text rasterisation spreads a few changed pixels over
  // every glyph and never concentrates. So the same diff is also read locally: the worst
  // tileSize-square tile. A missing element fills its tile; rasterisation does not.
  recordCheck('visualParityComparisons')
  const known = knownDivergenceById.get(id)
  const mine = known?.measurements.find(entry => entry.host === galleryHostKey)
  if (known && !mine && !resolvedHere.has(`${id} ${galleryHostKey}`)) {
    throw new Error(`${id} is registered as a known divergence (ticket ${known.ticket}) but this host`
      + ` (${galleryHostKey}) has no measurement; measured ${known.measurements.map(entry => `${entry.host}=${entry.worstTilePixelRatio}`).join(', ')}.`
      + ` Re-run with ${measureFlag} set and add this host's row to gallery/visual-parity-known-divergences.json`
      + ' — never judge one host against a measurement from a different host.')
  }
  const detail = mine ? ` (known divergence, ticket ${known!.ticket}: ${known!.note}; ${galleryHostKey} measured ${mine.worstTilePixelRatio})` : ''
  expect(worstTile.ratio, `${id} worst ${themeRegistry.visualParity.tileSize}px tile at ${worstTile.x},${worstTile.y}${detail}`)
    .toBeLessThanOrEqual(mine ? mine.worstTilePixelRatio + knownDivergences.tolerance : themeRegistry.visualParity.maximumChangedTilePixelRatio)
  if (mine) {
    expect(worstTile.ratio, `${id} is registered as a known divergence (ticket ${known!.ticket}) but now compares below what`
      + ` ${galleryHostKey} measured (${mine.worstTilePixelRatio}); if the divergence is gone remove the row,`
      + ` if it merely changed re-measure with ${measureFlag} set`)
      .toBeGreaterThanOrEqual(mine.worstTilePixelRatio - knownDivergences.tolerance)
  }
}

// pixelmatch paints a differing pixel in diffColor (default pure red, fully opaque), an ignored
// anti-aliased pixel yellow, and an unchanged pixel as a faded grayscale of the original — where
// r === g === b. Pure red is therefore exactly the set of pixels the ratio above counted.
function worstChangedTile(diffData: Buffer | Uint8Array, width: number, height: number, tileSize: number) {
  const size = Math.min(tileSize, width, height)
  let worst = { ratio: 0, x: 0, y: 0 }
  if (size <= 0) return worst
  // Tiles at the right and bottom edges are pulled back to sit fully inside the capture, so every
  // judged tile has the same area and a remainder strip cannot manufacture a high ratio.
  for (let top = 0; top < height; top += size) {
    const y0 = Math.min(top, height - size)
    for (let left = 0; left < width; left += size) {
      const x0 = Math.min(left, width - size)
      let changedInTile = 0
      for (let y = y0; y < y0 + size; y += 1) {
        for (let x = x0; x < x0 + size; x += 1) {
          const offset = (y * width + x) * 4
          if (diffData[offset] === 255 && diffData[offset + 1] === 0 && diffData[offset + 2] === 0) changedInTile += 1
        }
      }
      const tileRatio = changedInTile / (size * size)
      if (tileRatio > worst.ratio) worst = { ratio: tileRatio, x: x0, y: y0 }
    }
  }
  return worst
}

function expectedRgb(hex: string) {
  const value = hex.slice(1)
  return `rgb(${Number.parseInt(value.slice(0, 2), 16)}, ${Number.parseInt(value.slice(2, 4), 16)}, ${Number.parseInt(value.slice(4, 6), 16)})`
}

function rgbaChannels(cssColor: string) {
  const channels = cssColor.match(/[\d.]+/g)?.slice(0, 4).map(Number)
  expect(channels?.length, `unsupported computed color ${cssColor}`).toBeGreaterThanOrEqual(3)
  return [channels![0], channels![1], channels![2], channels![3] ?? 1] as const
}

function colorChannels(cssColor: string) {
  return rgbaChannels(cssColor).slice(0, 3)
}

function compositeColor(foreground: string, background: string) {
  const [fr, fg, fb, fa] = rgbaChannels(foreground)
  const [br, bg, bb, ba] = rgbaChannels(background)
  const alpha = fa + ba * (1 - fa)
  const channel = (front: number, back: number) => alpha === 0 ? 0 : Math.round((front * fa + back * ba * (1 - fa)) / alpha)
  return `rgba(${channel(fr, br)}, ${channel(fg, bg)}, ${channel(fb, bb)}, ${alpha})`
}

function contrastRatio(first: string, second: string) {
  recordCheck('contrastRatioAssertions')
  const luminance = (color: string) => colorChannels(color)
    .map(channel => channel / 255)
    .map(channel => channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4)
    .reduce((total, channel, index) => total + channel * [0.2126, 0.7152, 0.0722][index], 0)
  const [lighter, darker] = [luminance(first), luminance(second)].sort((left, right) => right - left)
  return (lighter + 0.05) / (darker + 0.05)
}

async function visualStyle(page: Page, selector: string) {
  return page.locator(selector).first().evaluate(element => {
    const style = getComputedStyle(element)
    return {
      background: style.backgroundColor,
      border: style.borderColor,
      color: style.color,
      opacity: Number.parseFloat(style.opacity),
      outlineColor: style.outlineColor,
      outlineStyle: style.outlineStyle,
      outlineWidth: Number.parseFloat(style.outlineWidth),
    }
  })
}

declare global {
  const BlazingStory: { getStoryIndex(): Promise<StoryIndex>; readyView(): Promise<void> }
}

test('every CI exemption names a scenario that still exists', () => {
  // The exemptions above are the only leniency in this gate, so they must not be able to rot into
  // a wider allowance than they were written for. A listed id that no longer matches a scenario is
  // a failure here, in every environment — including the local run, where the flag is off.
  const known = new Set(catalogs.flatMap(catalog => catalog.scenarios.map(scenario => scenario.id)))
  const orphans = [...ciPixelExempt, ...ciWallClockExempt].filter(id => !known.has(id)).sort()
  expect(orphans, 'CI gallery exemptions naming scenarios that no longer exist').toEqual([])
  // Same rot, same rule, for the per-element register: a row for a scenario that no longer exists is
  // never asserted, so it would sit there claiming a divergence nothing checks.
  const comparedIds = new Set(catalogs.flatMap(catalog => catalog.scenarios
    .filter(scenario => scenario.sourceQualityCaseIds?.some(caseId => caseId.endsWith('.quality.visual-parity')))
    .map(scenario => scenario.id)))
  const elementOrphans = [...new Set(elementRegister.divergences.map(row => row.scenarioId))]
    .filter(id => !comparedIds.has(id)).sort()
  expect(elementOrphans, 'per-element parity register naming scenarios that are not compared').toEqual([])
  // A host key nobody runs on would silently disable its rows on every host, which is a register
  // that claims a divergence no run can ever judge. Only the three keys the registers use.
  const knownHosts = new Set(['windows-11-x64', 'macos-x64-intel', 'macos-arm64'])
  const badHosts = [...new Set(elementRegister.divergences.map(row => row.host).filter(Boolean))]
    .filter(host => !knownHosts.has(host!)).sort()
  expect(badHosts, 'per-element parity register rows naming an unknown host').toEqual([])
})

test('the per-element register is read per host', () => {
  // The three semantics of the host key, asserted directly on the reconciliation rather than only
  // through a browser run: a row that applies here and no longer differs is red; the same row on
  // another host is ignored, not red; a difference with no applicable row is red anyway.
  const row = (host?: string) => ({ scenarioId: 'x.theme-light', path: '>div[1]', property: 'box', ticket: '146', note: 'n', host })
  const delta = { path: '>div[2]', property: 'box', detail: 'differs' }
  const here = 'windows-11-x64'
  const other = 'macos-arm64'
  expect(reconcileElementParity('x.theme-light', [], [row(here)], here).stale)
    .toEqual(['x.theme-light >div[1] box is registered to ticket 146 on windows-11-x64 but no longer differs on windows-11-x64; delete the row'])
  expect(reconcileElementParity('x.theme-light', [], [row()], here).stale).toHaveLength(1)
  expect(reconcileElementParity('x.theme-light', [], [row(other)], here).stale).toEqual([])
  expect(reconcileElementParity('x.theme-light', [delta], [row(other)], here).unregistered)
    .toEqual(['x.theme-light >div[2] box: differs'])
  // And a row scoped to this host still exempts its own difference.
  expect(reconcileElementParity('x.theme-light', [{ ...delta, path: '>div[1]' }], [row(here)], here))
    .toMatchObject({ unregistered: [], stale: [] })
})

test('both galleries expose every exact neutral scenario set', async ({ browser }) => {
  const page = await browser.newPage()
  const [react, blazor] = await Promise.all([reactIndex(page), blazorIndex(page)])
  const names = (index: StoryIndex, title: string) => Object.values(index.entries)
    .filter(entry => entry.type === 'story' && entry.title === title)
    .map(entry => entry.name)
    .sort()
  for (const catalog of catalogs) {
    const expected = catalog.scenarios.map(scenario => scenario.name).sort()
    expect(names(react, catalog.galleryInterface.projectionTitles.react)).toEqual(expected)
    expect(names(blazor, catalog.galleryInterface.projectionTitles.blazor)).toEqual(expected)
  }
  await page.close()
})

for (const catalog of catalogs) for (const scenario of catalog.scenarios) {
  test(`${scenario.id} is accessible and visually conformant`, async ({ browser }, testInfo) => {
    test.skip(ciGallery && ciWallClockExempt.has(scenario.id),
      'wall-clock budget scenario: a shared CI runner measures the runner, not the component')
    const colorScheme = scenario.id.endsWith('.theme-dark') ? 'dark' : 'light'
    const [reactContext, blazorContext] = await Promise.all([
      browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme }),
      browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme }),
    ])
    const [reactPage, blazorPage] = await Promise.all([reactContext.newPage(), blazorContext.newPage()])
    await Promise.all([
      reactPage.emulateMedia({ reducedMotion: 'reduce' }),
      blazorPage.emulateMedia({ reducedMotion: 'reduce' }),
    ])
    const [react, blazor] = await Promise.all([reactIndex(reactPage), blazorIndex(blazorPage)])
    await Promise.all([
      openReactStory(reactPage, storyId(react, catalog.galleryInterface.projectionTitles.react, scenario.name)),
      openBlazorStory(blazorPage, storyId(blazor, catalog.galleryInterface.projectionTitles.blazor, scenario.name)),
    ])
    await Promise.all([prepareScenario(reactPage, scenario), prepareScenario(blazorPage, scenario)])
    await captureGalleryReviewPair(reactPage, blazorPage, catalog, scenario)
    await recordReactBaseline(reactPage, catalog, scenario)
    await assertAccessible(reactPage, scenario, 'react')
    await assertAccessible(blazorPage, scenario, 'blazor')
    if (scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.visual-parity'))
      && !(ciGallery && ciPixelExempt.has(scenario.id))) {
      await assertVisualParity(reactPage, blazorPage, scenario, testInfo)
      await assertElementParity(reactPage, blazorPage, scenario.id)
    }
    // Gated on the DECLARED case, exactly as visual parity above is, and last because it mutates the
    // viewport and root font size.
    //
    // NOT measured during a capture run. captureGalleryReviewPair screenshots each locator, and
    // Playwright scrolls an element into view to do that, which leaves the page in a state this
    // measurement is sensitive to: hlp.ui.notification-center reads 0px without capture and 6px with
    // it, back to back on the same machine. The component is fine; the harness moved the page. A
    // capture run exists to produce review screenshots, and the gate measures reflow on every
    // scenario regardless, so nothing is lost by declining to judge layout on a page something else
    // just scrolled.
    if (!galleryReviewCaptureRoot && scenario.sourceQualityCaseIds?.some(id => id.endsWith('.quality.reflow'))) {
      await assertReflow(reactPage, scenario, 'react')
      await assertReflow(blazorPage, scenario, 'blazor')
    }
    await Promise.all([reactContext.close(), blazorContext.close()])
  })
}

for (const projection of ['react', 'blazor'] as const) {
  test(`${projection} proves Error Card semantics, retry, locale, theme, and reflow`, async ({ browser }) => {
    // Five Blazor circuit starts in one test: 32 s on an idle 8-core Mac, past 45 s under the gate's eight
    // browsers. Same allowance as the six-open Loading State test below.
    test.setTimeout(90_000)
    const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light', reducedMotion: 'reduce' })
    const page = await context.newPage()

    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.default'))
    const defaultAlert = page.getByRole('alert')
    await expect(defaultAlert).toHaveCount(1)
    await expect(defaultAlert.locator(':scope > p').first()).toHaveText('Unable to load')
    await expect(defaultAlert.getByRole('button')).toHaveCount(0)

    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.variants'))
    await expect(page.getByRole('alert')).toHaveCount(3)
    await expect(page.getByRole('alert').locator(':scope > h2')).toHaveCount(1)
    await expect(page.getByRole('alert').locator(':scope > p').filter({ hasText: /Unable to load|Failed/ })).toHaveCount(2)

    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.retry'))
    const retryAlert = page.getByRole('alert')
    const retry = retryAlert.getByRole('button', { name: 'Retry' })
    await retry.focus()
    await expect(retry).toBeFocused()
    const focus = await retry.evaluate(element => {
      const style = getComputedStyle(element)
      return { style: style.outlineStyle, width: Number.parseFloat(style.outlineWidth), duration: Number.parseFloat(style.transitionDuration) * 1000 }
    })
    expect(focus.style).not.toBe('none')
    expect(focus.width).toBeGreaterThanOrEqual(2)
    expect(focus.duration).toBeLessThanOrEqual(1)
    recordCheck('reducedMotionChecks')
    await page.keyboard.press('Enter')
    await page.keyboard.press('Space')
    await expect(retryAlert).toHaveAttribute('data-retry-count', '2')
    await expect(page.getByText('Retry activations: 2')).toBeVisible()

    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.theme-light'))
    const probe = page.locator('[data-gallery-probe]')
    const themedAlert = page.getByRole('alert')
    const lightCard = await visualStyle(page, '[role="alert"]')
    const lightTitle = await visualStyle(page, '[role="alert"] > h2')
    const lightMessage = await visualStyle(page, '[role="alert"] > p')
    expect(contrastRatio(lightTitle.color, lightCard.background)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(lightMessage.color, lightCard.background)).toBeGreaterThanOrEqual(4.5)
    await themedAlert.evaluate(element => { (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity = 'preserved' })
    await probe.evaluate(element => element.setAttribute('data-theme', 'dark'))
    await expect.poll(async () => (await visualStyle(page, '[role="alert"]')).background).not.toBe(lightCard.background)
    expect(await themedAlert.evaluate(element => (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity)).toBe('preserved')
    const darkCard = await visualStyle(page, '[role="alert"]')
    const darkTitle = await visualStyle(page, '[role="alert"] > h2')
    const darkMessage = await visualStyle(page, '[role="alert"] > p')
    expect(contrastRatio(darkTitle.color, darkCard.background)).toBeGreaterThanOrEqual(4.5)
    expect(contrastRatio(darkMessage.color, darkCard.background)).toBeGreaterThanOrEqual(4.5)

    await page.setViewportSize({ width: 320, height: 720 })
    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.locale-pseudo'))
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
    await expect(page.getByRole('alert')).toHaveAttribute('lang', 'en-XA')
    await expect(page.getByRole('alert')).toHaveAttribute('dir', errorCardQuality.locales['en-XA'].direction)
    await expect(page.getByRole('button')).toHaveText(errorCardQuality.locales['en-XA'].retry)
    const overflow = await page.locator('[data-gallery-probe]').evaluate(element => Math.max(0, element.scrollWidth - element.clientWidth))
    expect(overflow, `${projection} Error Card horizontal overflow`).toBe(0)
    recordCheck('reflowChecks')
    await context.close()
  })

  test(`${projection} preserves Error Card affordances in forced colors`, async ({ browser }) => {
    const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light', forcedColors: 'active' })
    const page = await context.newPage()
    await openProjectionStory(page, projection, errorCardCatalog, scenarioName(errorCardCatalog, 'error-card.retry'))
    const alertStyle = await page.getByRole('alert').evaluate(element => getComputedStyle(element).borderStyle)
    expect(alertStyle).not.toBe('none')
    const retry = page.getByRole('button', { name: 'Retry' })
    await retry.focus()
    const focus = await retry.evaluate(element => {
      const style = getComputedStyle(element)
      return { style: style.outlineStyle, width: Number.parseFloat(style.outlineWidth) }
    })
    expect(focus.style).not.toBe('none')
    expect(focus.width).toBeGreaterThanOrEqual(2)
    recordCheck('forcedColorsChecks')
    await context.close()
  })

  test(`${projection} proves Loading State is a persistent non-interactive polite status`, async ({ browser }) => {
    test.setTimeout(90_000)
    const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light', reducedMotion: 'reduce' })
    const page = await context.newPage()

    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.default'))
    const pageStatus = page.getByRole('status')
    await expect(pageStatus).toHaveText(loadingStateQuality.locales['en-US'].label)
    await expect(pageStatus).toHaveAttribute('aria-live', 'polite')
    await expect(pageStatus.locator('button, [role="progressbar"], svg')).toHaveCount(0)
    expect(await pageStatus.evaluate(element => element.tagName)).toBe('DIV')

    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.inline'))
    expect(await page.getByRole('status').evaluate(element => element.tagName)).toBe('P')

    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.label-update'))
    const updatingStatus = page.getByRole('status')
    await updatingStatus.evaluate(element => { (element as HTMLElement & { __statusIdentity?: string }).__statusIdentity = 'preserved' })
    await page.locator('[data-update-control]').click()
    await expect(updatingStatus).toHaveText('Loading attachments')
    expect(await updatingStatus.evaluate(element => (element as HTMLElement & { __statusIdentity?: string }).__statusIdentity)).toBe('preserved')
    await expect(updatingStatus.locator('button, [role="progressbar"], svg')).toHaveCount(0)

    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.host-attributes'))
    await expect(page.getByRole('status')).toHaveAttribute('aria-atomic', 'true')
    await expect(page.getByRole('status')).toHaveAttribute('data-case', 'shared')

    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.theme-light'))
    const probe = page.locator('[data-gallery-probe]')
    const status = page.getByRole('status').first()
    const lightStatus = await visualStyle(page, '[role="status"]')
    const lightScene = await visualStyle(page, '[data-gallery-probe]')
    expect(contrastRatio(lightStatus.color, lightScene.background)).toBeGreaterThanOrEqual(4.5)
    expect(await status.evaluate(element => Number.parseFloat(getComputedStyle(element).transitionDuration) * 1000)).toBeLessThanOrEqual(1)
    recordCheck('reducedMotionChecks')
    await status.evaluate(element => { (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity = 'preserved' })
    await probe.evaluate(element => element.setAttribute('data-theme', 'dark'))
    await expect.poll(async () => (await visualStyle(page, '[role="status"]')).color).not.toBe(lightStatus.color)
    expect(await status.evaluate(element => (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity)).toBe('preserved')
    const darkStatus = await visualStyle(page, '[role="status"]')
    const darkScene = await visualStyle(page, '[data-gallery-probe]')
    expect(contrastRatio(darkStatus.color, darkScene.background)).toBeGreaterThanOrEqual(4.5)

    await page.setViewportSize({ width: 320, height: 720 })
    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.locale-pseudo'))
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
    const localeStatus = page.getByRole('status')
    await expect(localeStatus).toHaveAttribute('lang', 'en-XA')
    await expect(localeStatus).toHaveAttribute('dir', loadingStateQuality.locales['en-XA'].direction)
    await expect(localeStatus).toHaveText(loadingStateQuality.locales['en-XA'].label)
    const overflow = await page.locator('[data-gallery-probe]').evaluate(element => Math.max(0, element.scrollWidth - element.clientWidth))
    expect(overflow, `${projection} Loading State horizontal overflow`).toBe(0)
    recordCheck('reflowChecks')
    await context.close()
  })

  test(`${projection} keeps Loading State visible in forced colors without inventing controls`, async ({ browser }) => {
    const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light', forcedColors: 'active' })
    const page = await context.newPage()
    await openProjectionStory(page, projection, loadingStateCatalog, scenarioName(loadingStateCatalog, 'loading-state.default'))
    const status = page.getByRole('status')
    const color = await status.evaluate(element => getComputedStyle(element).color)
    expect(color).not.toBe('rgba(0, 0, 0, 0)')
    await expect(status.locator('button, [role="progressbar"], svg')).toHaveCount(0)
    recordCheck('forcedColorsChecks')
    await context.close()
  })
}

for (const projection of ['react', 'blazor'] as const) {
  test(`${projection} resizes App Shell dividers with measured geometry and resets the whole tree`, async ({browser}) => {
    const context = await browser.newContext({viewport: {width: 1800, height: 1000}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    // 1800 is the shell's MEASURED inline size, which is what places the dock - so derive the viewport from it.
    await sizeViewportToShell(page, 1800, 1000)
    const dividers = page.locator('.hl-app-shell__dock-divider[role="separator"]')
    await expect(dividers.first()).toBeVisible()
    // A draggable separator that occupies zero pixels is not an affordance, so assert the real box:
    // every separator measures at least its declared hit size across the axis it resizes. The box is
    // pulled back out of pane arithmetic by an equal negative margin, so the two nodes it sits between
    // still fill their parent exactly -- the splitter tree stays the single source of pane sizes.
    const declared = Number((await page.locator('.hl-app-shell').first().evaluate(node => getComputedStyle(node).getPropertyValue('--hl-app-shell-divider-size'))).replace('px', ''))
    expect(declared).toBeGreaterThanOrEqual(24)
    for (const separator of await dividers.all()) {
      const geometry = await separator.evaluate(node => {
        const box = node.getBoundingClientRect(), horizontal = node.getAttribute('aria-orientation') === 'horizontal'
        const parent = node.parentElement!.getBoundingClientRect()
        const before = node.previousElementSibling!.getBoundingClientRect(), after = node.nextElementSibling!.getBoundingClientRect()
        return {across: horizontal ? box.height : box.width, along: horizontal ? box.width : box.height,
          panes: horizontal ? before.height + after.height : before.width + after.width, parent: horizontal ? parent.height : parent.width}
      })
      expect(geometry.across).toBeGreaterThanOrEqual(declared)
      expect(geometry.along).toBeGreaterThan(0)
      expect(geometry.panes).toBeCloseTo(geometry.parent, 0)
    }
    const panels = page.locator('[data-shell-panel-id]')
    const order = await panels.evaluateAll(nodes => nodes.map(n => [n.getAttribute('data-shell-panel-id'), n.getAttribute('data-shell-container-kind')]))
    const divider = page.locator('.hl-app-shell__dock-divider[aria-orientation="horizontal"]').first()
    await expect.poll(async () => Number(await divider.getAttribute('aria-valuemax')) - Number(await divider.getAttribute('aria-valuemin'))).toBeGreaterThan(64)
    const original = Number(await divider.getAttribute('aria-valuenow'))
    const height = () => divider.evaluate(node => node.previousElementSibling!.getBoundingClientRect().height)
    const initialHeight = await height()
    await divider.focus(); await divider.press('ArrowDown')
    await expect.poll(async () => Number(await divider.getAttribute('aria-valuenow'))).toBeCloseTo(original + 8, 0)
    expect(await height()).toBeCloseTo(initialHeight + 8, 0)
    await divider.press('Escape')
    await expect.poll(height).toBeCloseTo(initialHeight, 0)
    const box = await divider.boundingBox()
    // Grab the middle of the divider's real box, not its top edge: the box is 24px across and centred
    // on the boundary, so the edge pixel belongs as much to the pane above it.
    const grab = box!.y + box!.height / 2
    await divider.hover()
    await page.mouse.down(); await page.mouse.move(box!.x + box!.width / 2, grab + 48, {steps: 8}); await page.mouse.up()
    await expect.poll(height).toBeCloseTo(initialHeight + 48, 0)
    for (const separator of await dividers.all()) { await separator.focus(); await separator.press('End'); await separator.press('Enter') }
    await page.getByRole('button', {name: 'Reset panels', exact: true}).click()
    await expect.poll(height).toBeCloseTo(initialHeight, 0)
    expect(await panels.evaluateAll(nodes => nodes.map(n => [n.getAttribute('data-shell-panel-id'), n.getAttribute('data-shell-container-kind')]))).toEqual(order)
    expect((await page.locator('.hl-app-shell__page').boundingBox())!.width).toBeGreaterThanOrEqual(420)
    await context.close()
  })

  test(`${projection} renders the App Shell story panels the shared fixture declares`, async ({browser}) => {
    // Both stories are READERS of conformance/hlp.ui.app-shell/chrome-v1.json galleryPanels: the React story
    // imports it, the Blazor story deserialises an embedded copy. This replaces the regex mirror test that
    // compared the two story SOURCES - what matters is what each shell rendered, and a story that stopped
    // reading the fixture (or dropped a row on the way to the shell) fails here in that projection alone.
    const declared = (JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.app-shell/chrome-v1.json'), 'utf8')) as {
      galleryPanels?: {id: string; traits?: string[]}[]
    }).galleryPanels
    expect(declared, 'chrome-v1.json is missing "galleryPanels"').toBeDefined()
    expect(declared!.length).toBeGreaterThan(0)
    for (const panel of declared!) expect(panel.traits?.length ?? 0, `galleryPanels ${panel.id} declares no traits`).toBeGreaterThan(0)
    const context = await browser.newContext({viewport: {width: 1800, height: 1000}})
    const page = await context.newPage()
    // actions-endpanel opens every declared panel, so the dock IS the panel table this story read.
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    await sizeViewportToShell(page, 1800, 1000)
    await expect.poll(async () => page.locator('[data-shell-panel-id]').evaluateAll(nodes => nodes.map(node => node.getAttribute('data-shell-panel-id'))))
      .toEqual(declared!.map(panel => panel.id))
    await context.close()
  })

  test(`${projection} keeps resizing the App Shell rail after the drag leaves the handle`, async ({browser}) => {
    // Parity with the dock separator's capture (slice 4 gate fix): the rail handle is a 24px box and a real
    // drag leaves it within the first few pixels, so the element must own the pointer for the rest of the
    // gesture. Without setPointerCapture (React) / dock-divider.js capturePointer (Blazor) pointermove stops
    // reaching the handle at its edge and the rail freezes part way. The drag therefore ends far outside the
    // handle's own box, and the assertion is the width the rail actually reached.
    const context = await browser.newContext({viewport: {width: 1800, height: 1000}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.structure'))
    const handle = page.locator('.hl-app-shell__rail-resize')
    await expect(handle).toHaveCount(1)
    const box = (await handle.boundingBox())!
    const width = async () => (await page.locator('[data-shell-region="rail"]').first().boundingBox())!.width
    const before = await width()
    const travel = Math.round(box.width * 4)
    expect(travel, 'the drag must end outside the handle box or capture is not what is measured').toBeGreaterThan(box.width)
    // hover() presses the handle's own hit point, which the rail's clip can shift up to a handle-width from
    // the box centre; a bare mouse.move to the centre misses the element entirely and no drag starts at all.
    await handle.hover()
    await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 + travel, box.y + box.height / 2, {steps: 8}); await page.mouse.up()
    // Uncaptured, the rail stops growing as the pointer crosses the handle's own edge, so it can never gain
    // more than the handle is wide; captured, it follows the whole travel. The tolerance is one handle width
    // -- the hit-point shift above -- derived from the measured box, not a flat constant.
    const growth = async () => (await width()) - before
    await expect.poll(growth).toBeGreaterThan(box.width)
    expect(Math.abs(await growth() - travel), `${projection} rail growth vs pointer travel`).toBeLessThanOrEqual(box.width)
    await context.close()
  })

  test(`${projection} resizes the App Shell dock through its outer separator with a real hit box`, async ({browser}) => {
    const context = await browser.newContext({viewport: {width: 1800, height: 1000}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    await sizeViewportToShell(page, 1800, 1000)
    // The dock's outer width belongs to the panel that opened it, so the affordance that sets it must be a
    // real box, not a hairline: assert the declared hit size, then drag it and read the aside's own geometry.
    const handle = page.locator('.hl-app-shell__dock-resize[role="separator"]')
    await expect(handle).toHaveCount(1)
    const declared = Number((await page.locator('.hl-app-shell').first().evaluate(node => getComputedStyle(node).getPropertyValue('--hl-app-shell-divider-size'))).replace('px', ''))
    const box = (await handle.boundingBox())!
    expect(box.width).toBeGreaterThanOrEqual(declared)
    expect(box.height).toBeGreaterThan(0)
    expect(await handle.getAttribute('data-opening-panel-id')).toBe(await page.locator('[data-shell-panel-id]').first().getAttribute('data-shell-panel-id'))
    const width = async () => (await page.locator('.hl-app-shell__dock').boundingBox())!.width
    const before = await width()
    await handle.hover()
    await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 - 80, box.y + box.height / 2, {steps: 8}); await page.mouse.up()
    await expect.poll(width).toBeGreaterThan(before + 40)
    expect((await page.locator('.hl-app-shell__page').boundingBox())!.width).toBeGreaterThanOrEqual(420)
    await context.close()
  })

  test(`${projection} shows both earned App Shell header forms with real affordance boxes`, async ({browser}) => {
    // conformance/hlp.ui.app-shell/chrome-v1.json headerForms: form 2 is earned by OpensOne, and the affordance
    // group is the shell's in both forms. Every affordance is a real hit box, not a bare visibility check.
    const chrome = JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.app-shell/chrome-v1.json'), 'utf8')) as {
      slotOrder: string[]
      headerForms: {panelId: string; expectedForm: string; expectedAffordances: string[]}[]
    }
    const context = await browser.newContext({viewport: {width: 1800, height: 1000}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    const declared = Number((await page.locator('.hl-app-shell').first().evaluate(node => getComputedStyle(node).getPropertyValue('--hl-app-shell-panel-affordance'))).replace('px', ''))
    for (const row of chrome.headerForms) {
      const panel = page.locator(`[data-shell-panel-id="${row.panelId}"]`)
      if (await panel.count() === 0) continue
      await expect(panel).toHaveAttribute('data-panel-header-form', row.expectedForm)
      for (const affordance of row.expectedAffordances) {
        const control = panel.locator(affordance === 'close' ? '[data-panel-close]' : `[data-panel-${affordance}]`)
        await expect(control).toHaveCount(1)
        const box = (await control.boundingBox())!
        expect(box.width).toBeGreaterThanOrEqual(declared)
        expect(box.height).toBeGreaterThanOrEqual(declared)
      }
      const slots = await panel.evaluateAll(nodes => [...nodes[0].children].map(child => child.className))
      expect(slots.length).toBeGreaterThanOrEqual(chrome.slotOrder.length - 1)
    }
    await context.close()
  })

  test(`${projection} keeps the App Shell dock inside the content row when the drag passes the floor`, async ({browser}) => {
    // conformance/hlp.ui.app-shell/persistence-v1.json dockWidthProperty, row w1344-r0-d1200-*: the clamp lives
    // in the model, so a drag that asks for far more than the row can spare stops at the floor instead of
    // hanging the aside (and its Spread control) outside the row.
    // The width comes from the cited row itself; a missing row fails loudly instead of falling back.
    const clampRow = (JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.app-shell/persistence-v1.json'), 'utf8')) as {
      dockWidthProperty: {cases: {id: string; shellWidth: number; requestedWidth: number; expectedWidth: number}[]}
    }).dockWidthProperty.cases.find(row => row.id === 'w1344-r0-d1200-p1')
    expect(clampRow, 'persistence-v1.json dockWidthProperty is missing row w1344-r0-d1200-p1').toBeDefined()
    const context = await browser.newContext({viewport: {width: clampRow!.shellWidth, height: 1000}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    // w1344 is the row's MEASURED shell width, so the viewport is derived from it, not set to it.
    await sizeViewportToShell(page, clampRow!.shellWidth, 1000)
    const handle = page.locator('.hl-app-shell__dock-resize[role="separator"]')
    const box = (await handle.boundingBox())!
    await handle.hover()
    await page.mouse.down(); await page.mouse.move(0, box.y + box.height / 2, {steps: 8}); await page.mouse.up()
    const edges = async () => {
      const dock = (await page.locator('.hl-app-shell__dock').boundingBox())!
      const row = (await page.locator('.hl-app-shell__content-row').boundingBox())!
      return {dockRight: dock.x + dock.width, rowRight: row.x + row.width}
    }
    await expect.poll(async () => (await edges()).dockRight).toBeLessThanOrEqual((await edges()).rowRight)
    await context.close()
  })

  test(`${projection} measures every App Shell sheet close target across adaptation`, async ({browser}) => {
    const adaptation = JSON.parse(readFileSync(resolve(repositoryRoot, 'conformance/hlp.ui.app-shell/adaptation-v1.json'), 'utf8')) as {
      sheetCloseTarget: number
      classPlacementCases: { widths: number[]; expectedBreakpoint: string; expectedContainerKinds: Record<string, string> }[]
    }
    const context = await browser.newContext({viewport: {width: 600, height: 900}})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    for (const row of adaptation.classPlacementCases) {
      for (const width of row.widths) {
        await page.setViewportSize({width, height: 900})
        await expect(page.locator('[data-shell-id]')).toHaveAttribute('data-shell-breakpoint', row.expectedBreakpoint)
        // The breakpoint ATTRIBUTE follows the viewport, but placement follows the shell's MEASURED box, which
        // arrives a frame later through ResizeObserver (React) / the measurement interop (Blazor). Resolving
        // element handles and then measuring them one at a time straddled that frame: a panel that had already
        // re-placed to docked measured its 28px docked control, and a shrinking sheet set left `nth(n)` waiting
        // out the whole 45s budget (ticket 266, after the full-bleed scene in #64 made the measured box track
        // the viewport at all). So read the WHOLE placement in one page evaluation and poll THAT: each retry
        // re-reads the live DOM, no read straddles a frame, and only a settled placement can satisfy it.
        // Settled placement is correct in both shells at every row width -- what was measured was the frame.
        const minimumSheets = row.expectedBreakpoint === 'compact' || row.expectedBreakpoint === 'medium' ? 1 : 0
        await expect.poll(async () => page.evaluate(([target, minimum]) => {
          const sheets = [...document.querySelectorAll('[data-shell-container-kind]:not([data-shell-container-kind="docked"])')]
          const closeTargets = sheets.map(sheet => sheet.querySelector('[data-sheet-close]'))
          return {
            atLeastMinimumSheets: sheets.length >= minimum,
            sheetsWithoutACloseTarget: closeTargets.filter(close => close === null).length,
            offTarget: closeTargets.flatMap(close => {
              if (close === null) return []
              const box = close.getBoundingClientRect()
              return box.width === target && box.height === target
                ? [] : [`${close.getAttribute('aria-label')}:${box.width}x${box.height}`]
            }),
          }
        }, [adaptation.sheetCloseTarget, minimumSheets] as const), {message: `${projection}:${width}`})
          .toEqual({atLeastMinimumSheets: true, sheetsWithoutACloseTarget: 0, offTarget: []})
      }
    }
    await context.close()
  })

  test(`${projection} measures App Shell separator, scrollbar contrast, content floor, and 1200px toggle boundary`, async ({browser}) => {
    const context = await browser.newContext({viewport: {width: 1199, height: 720}, colorScheme: 'light'})
    const page = await context.newPage()
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.performance'))
    await expect(page.locator('[data-shell-bar-slot="cluster"] > [data-action-id="documents"]')).toHaveCount(0)
    await expect(page.getByRole('button', {name: 'Panels'})).toHaveCount(1)

    const separator = page.locator('.hl-app-shell__rail-resize')
    const separatorBox = await separator.boundingBox()
    expect(separatorBox?.width ?? 0).toBeGreaterThanOrEqual(24)
    const before = Number(await separator.getAttribute('aria-valuenow'))
    await separator.focus()
    const focus = await separator.evaluate(element => { const style=getComputedStyle(element); return {style:style.outlineStyle,width:Number.parseFloat(style.outlineWidth)} })
    expect(focus.style).not.toBe('none'); expect(focus.width).toBeGreaterThanOrEqual(2)
    await separator.press('ArrowRight')
    expect(Number(await separator.getAttribute('aria-valuenow'))).toBeGreaterThan(before)

    const scrollbar = await page.locator('.hl-app-shell__rail-scroll').evaluate(element => {
      const style=getComputedStyle(element); const pseudo=getComputedStyle(element,'::-webkit-scrollbar')
      const paintedSurfaces: string[]=[]
      for(let candidate: Element|null=element; candidate; candidate=candidate.parentElement){
        const background=getComputedStyle(candidate).backgroundColor
        const channels=background.match(/[\d.]+/g)?.map(Number) ?? []
        const alpha=channels[3] ?? (channels.length >= 3 ? 1 : 0)
        if(alpha>0){paintedSurfaces.push(background);if(alpha>=1)break}
      }
      return {gutter:element.getBoundingClientRect().width-element.clientWidth,pseudoWidth:Number.parseFloat(pseudo.width),scrollbarColor:style.scrollbarColor,paintedSurfaces}
    })
    expect(Math.max(scrollbar.gutter,scrollbar.pseudoWidth)).toBeGreaterThanOrEqual(24)
    const thumb = scrollbar.scrollbarColor.match(/rgba?\([^)]*\)/)?.[0]
    expect(thumb).toBeTruthy()
    expect(scrollbar.paintedSurfaces.length, 'scrollbar must resolve to a painted ancestor').toBeGreaterThan(0)
    const paintedSurface=scrollbar.paintedSurfaces.reduceRight((background, foreground)=>compositeColor(foreground,background),'rgb(255, 255, 255)')
    const paintedThumb=compositeColor(thumb!,paintedSurface)
    const plantedLowContrastThumb=compositeColor('rgba(118, 118, 118, 0.3)',paintedSurface)
    expect(contrastRatio(plantedLowContrastThumb, paintedSurface), 'alpha-composited planted thumb').toBeLessThan(3)
    expect(contrastRatio(paintedThumb, paintedSurface)).toBeGreaterThanOrEqual(3)

    await page.setViewportSize({width: 1200, height: 720})
    await expect(page.locator('[data-shell-bar-slot="cluster"] > [data-action-id="documents"]')).toHaveCount(1)
    await expect(page.getByRole('button', {name: 'Panels'})).toHaveCount(0)
    await openProjectionStory(page, projection, appShellCatalog, scenarioName(appShellCatalog, 'app-shell.actions-endpanel'))
    const contentBox = await page.locator('.hl-app-shell__page').boundingBox()
    expect(contentBox?.width ?? 0).toBeGreaterThanOrEqual(420)
    await context.close()
  })

  test(`${projection} proves Context Menu Tier B interaction, locale, motion, and reflow`, async ({browser}) => {
    // Five Blazor circuit starts in one test: 32 s on an idle 8-core Mac, past 45 s under the gate's eight
    // browsers. Same allowance as the six-open Loading State test below.
    test.setTimeout(90_000)
    const context = await browser.newContext({viewport: {width: 920, height: 720}, reducedMotion: 'reduce'})
    const page = await context.newPage()

    const keyboardScenario = contextMenuCatalog.scenarios.find(scenario => scenario.id === 'context-menu.keyboard')!
    await openProjectionStory(page, projection, contextMenuCatalog, keyboardScenario.name)
    await prepareScenario(page, keyboardScenario)
    const trigger = page.locator('[data-context-trigger]').first()
    const active = page.locator('[role="menuitem"][data-active="true"]')
    await expect(active).toBeFocused()
    await expect(active).toContainText('Copy inspection')
    await page.keyboard.press('End')
    await expect(page.locator('[role="menuitem"][data-active="true"]')).toContainText('Delete inspection')
    await page.keyboard.press('ArrowDown')
    await expect(page.locator('[role="menuitem"][data-active="true"]')).toContainText('Copy inspection')
    await page.keyboard.press('Escape')
    await expect(page.getByRole('menu')).toHaveCount(0)
    await expect(trigger).toBeFocused()

    const groupsScenario = contextMenuCatalog.scenarios.find(scenario => scenario.id === 'context-menu.groups')!
    await openProjectionStory(page, projection, contextMenuCatalog, groupsScenario.name)
    await prepareScenario(page, groupsScenario)
    await expect(page.getByRole('menuitem')).toHaveCount(3)
    await expect(page.getByRole('separator')).toHaveCount(1)
    await expect(page.getByRole('menuitem', {name: 'Paste'})).toBeDisabled()
    await expect(page.getByRole('menuitem', {name: 'Delete inspection'})).toHaveAttribute('data-danger', 'true')

    const arabicScenario = contextMenuCatalog.scenarios.find(scenario => scenario.id === 'context-menu.locale-ar')!
    await openProjectionStory(page, projection, contextMenuCatalog, arabicScenario.name)
    await prepareScenario(page, arabicScenario)
    await expect(page.getByRole('menu')).toHaveAttribute('aria-label', 'قائمة السياق')
    await expect(page.getByRole('menu')).toHaveAttribute('lang', 'ar-SA')
    await expect(page.getByRole('menu')).toHaveAttribute('dir', 'rtl')

    const lightScenario = contextMenuCatalog.scenarios.find(scenario => scenario.id === 'context-menu.theme-light')!
    await openProjectionStory(page, projection, contextMenuCatalog, lightScenario.name)
    await prepareScenario(page, lightScenario)
    const transitionMs = await page.getByRole('menuitem').first().evaluate(element =>
      Math.max(...getComputedStyle(element).transitionDuration.split(',').map(value => Number.parseFloat(value) * 1000)))
    expect(transitionMs).toBeLessThanOrEqual(1)
    recordCheck('reducedMotionChecks')

    await page.setViewportSize({width: 320, height: 720})
    const pseudoScenario = contextMenuCatalog.scenarios.find(scenario => scenario.id === 'context-menu.locale-pseudo')!
    await openProjectionStory(page, projection, contextMenuCatalog, pseudoScenario.name)
    await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
    await prepareScenario(page, pseudoScenario)
    const overflow = await page.locator('[data-gallery-probe]').evaluate(element => Math.max(0, element.scrollWidth - element.clientWidth))
    expect(overflow, `${projection} Context Menu horizontal overflow`).toBe(0)
    recordCheck('reflowChecks')
    recordCheck('contextMenuTierBProjectionChecks')
    await context.close()
  })
}

for (const theme of quality.themes) {
  for (const projection of ['react', 'blazor'] as const) {
    test(`${projection} resolves and validates the ${theme.id} neutral theme fixture`, async ({ browser }) => {
      const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: theme.colorScheme })
      const page = await context.newPage()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId))
      const probe = page.locator('[data-gallery-probe]')
      await expect(probe).toHaveAttribute('data-theme', theme.id)

      const resolvedTokens = await probe.evaluate((element, tokenNames) => {
        const style = getComputedStyle(element)
        return Object.fromEntries(tokenNames.map(token => [token, style.getPropertyValue(token).trim()]))
      }, Object.keys(theme.tokens))
      expect(resolvedTokens).toEqual(theme.tokens)

      const sceneStyle = await visualStyle(page, '[data-gallery-probe]')
      expect(sceneStyle.background).toBe(expectedRgb(theme.tokens['--hl-gallery-surface']))
      expect(sceneStyle.color).toBe(expectedRgb(theme.tokens['--hl-gallery-text']))

      const primarySelector = '[data-theme-state="interactive"]'
      const primary = page.locator(primarySelector)
      let primaryStyle = await visualStyle(page, primarySelector)
      expect(primaryStyle.background).toBe(expectedRgb(theme.tokens['--hl-button-primary']))
      expect(primaryStyle.color).toBe(expectedRgb(theme.tokens['--hl-button-primary-foreground']))
      expect(contrastRatio(primaryStyle.color, primaryStyle.background)).toBeGreaterThanOrEqual(4.5)
      expect(contrastRatio(primaryStyle.background, sceneStyle.background)).toBeGreaterThanOrEqual(3)

      await primary.hover()
      await expect.poll(async () => (await visualStyle(page, primarySelector)).background)
        .toBe(expectedRgb(theme.tokens['--hl-button-primary-hover']))
      primaryStyle = await visualStyle(page, primarySelector)
      expect(contrastRatio(primaryStyle.color, primaryStyle.background)).toBeGreaterThanOrEqual(4.5)

      const box = await primary.boundingBox()
      expect(box).not.toBeNull()
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2)
      await page.mouse.down()
      await expect.poll(async () => (await visualStyle(page, primarySelector)).background)
        .toBe(expectedRgb(theme.tokens['--hl-button-primary-active']))
      primaryStyle = await visualStyle(page, primarySelector)
      expect(contrastRatio(primaryStyle.color, primaryStyle.background)).toBeGreaterThanOrEqual(4.5)
      await page.mouse.up()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId))
      await page.keyboard.press('Tab')
      await expect(primary).toBeFocused()
      primaryStyle = await visualStyle(page, primarySelector)
      expect(primaryStyle.outlineStyle).not.toBe('none')
      expect(primaryStyle.outlineWidth).toBeGreaterThanOrEqual(2)
      expect(primaryStyle.outlineColor).toBe(expectedRgb(theme.tokens['--hl-button-focus']))
      expect(contrastRatio(primaryStyle.outlineColor, sceneStyle.background)).toBeGreaterThanOrEqual(3)

      const secondaryStyle = await visualStyle(page, '[data-theme-state="secondary"]')
      expect(secondaryStyle.background).toBe(expectedRgb(theme.tokens['--hl-button-secondary']))
      expect(secondaryStyle.color).toBe(expectedRgb(theme.tokens['--hl-button-secondary-foreground']))
      expect(secondaryStyle.border).toBe(expectedRgb(theme.tokens['--hl-button-border']))
      expect(contrastRatio(secondaryStyle.color, secondaryStyle.background)).toBeGreaterThanOrEqual(4.5)
      expect(contrastRatio(secondaryStyle.border, sceneStyle.background)).toBeGreaterThanOrEqual(3)

      const disabled = page.locator('[data-theme-state="disabled"]')
      await expect(disabled).toBeDisabled()
      const disabledStyle = await visualStyle(page, '[data-theme-state="disabled"]')
      expect(disabledStyle.background).toBe(expectedRgb(theme.tokens['--hl-button-primary']))
      expect(disabledStyle.color).toBe(expectedRgb(theme.tokens['--hl-button-primary-foreground']))
      expect(disabledStyle.opacity).toBeLessThan(1)
      const loading = page.locator('[data-theme-state="loading"]')
      await expect(loading).toHaveAttribute('aria-busy', 'true')
      await expect(loading).toHaveAttribute('aria-disabled', 'true')
      const loadingStyle = await visualStyle(page, '[data-theme-state="loading"]')
      expect(loadingStyle.background).toBe(expectedRgb(theme.tokens['--hl-button-primary']))
      expect(loadingStyle.color).toBe(expectedRgb(theme.tokens['--hl-button-primary-foreground']))
      expect(loadingStyle.opacity).toBe(1)
      expect(contrastRatio(loadingStyle.color, loadingStyle.background)).toBeGreaterThanOrEqual(4.5)
      const icon = page.locator('[data-theme-state="icon"]')
      await expect(icon).toHaveAccessibleName('Add item')
      const iconStyle = await visualStyle(page, '[data-theme-state="icon"]')
      expect(iconStyle.background).toBe(expectedRgb(theme.tokens['--hl-button-primary']))
      expect(iconStyle.color).toBe(expectedRgb(theme.tokens['--hl-button-primary-foreground']))
      recordCheck('themeFixtureProjectionChecks')
      await context.close()
    })

    if (theme.id === 'light') {
      test(`${projection} switches the public theme at runtime without remounting Button`, async ({ browser }) => {
        const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light' })
        const page = await context.newPage()
        await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, 'button.theme-light'))
        const probe = page.locator('[data-gallery-probe]')
        const button = page.locator('[data-theme-state="interactive"]')
        const light = quality.themes.find(candidate => candidate.id === 'light')!
        const dark = quality.themes.find(candidate => candidate.id === 'dark')!
        await button.evaluate(element => { (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity = 'preserved' })
        const before = await visualStyle(page, '[data-theme-state="interactive"]')
        await probe.evaluate(element => element.setAttribute('data-theme', 'dark'))
        await expect.poll(async () => (await visualStyle(page, '[data-theme-state="interactive"]')).background)
          .toBe(expectedRgb(dark.tokens['--hl-button-primary']))
        const after = await visualStyle(page, '[data-theme-state="interactive"]')
        expect(before.background).toBe(expectedRgb(light.tokens['--hl-button-primary']))
        expect(after.background).toBe(expectedRgb(dark.tokens['--hl-button-primary']))
        expect(await button.evaluate(element => (element as HTMLElement & { __themeIdentity?: string }).__themeIdentity)).toBe('preserved')
        recordCheck('runtimeThemeSwitchChecks')
        await context.close()
      })
    }

    test(`${projection} reflows the ${theme.id} fixture at 200 percent text`, async ({ browser }) => {
      const context = await browser.newContext({ viewport: { width: 320, height: 720 }, colorScheme: theme.colorScheme })
      const page = await context.newPage()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId))
      await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
      const overflow = await page.locator('[data-gallery-probe]').evaluate(element => Math.max(0, element.scrollWidth - element.clientWidth))
      expect(overflow, `${projection} ${theme.id} horizontal overflow`).toBe(0)
      recordCheck('reflowChecks')
      await context.close()
    })

    test(`${projection} removes transitions for reduced motion in the ${theme.id} fixture`, async ({ browser }) => {
      const context = await browser.newContext({
        viewport: { width: 920, height: 720 },
        colorScheme: theme.colorScheme,
        reducedMotion: 'reduce',
      })
      const page = await context.newPage()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId))
      const durationMs = await page.locator('[data-theme-state="interactive"]').evaluate(element => {
        const durations = getComputedStyle(element).transitionDuration.split(',').map(value => Number.parseFloat(value) * 1000)
        return Math.max(...durations)
      })
      expect(durationMs).toBeLessThanOrEqual(1)
      recordCheck('reducedMotionChecks')
      await context.close()
    })
  }

  test(`React and Blazor preserve ${theme.id} hover, active, and focus-visible parity`, async ({ browser }, testInfo) => {
    const [reactContext, blazorContext] = await Promise.all([
      browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: theme.colorScheme, reducedMotion: 'reduce' }),
      browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: theme.colorScheme, reducedMotion: 'reduce' }),
    ])
    const [reactPage, blazorPage] = await Promise.all([reactContext.newPage(), blazorContext.newPage()])
    for (const state of ['hover', 'active', 'focus-visible'] as const) {
      await Promise.all([
        openProjectionStory(reactPage, 'react', buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId)),
        openProjectionStory(blazorPage, 'blazor', buttonCatalog, scenarioName(buttonCatalog, theme.scenarioId)),
      ])
      const reactButton = reactPage.locator('[data-theme-state="interactive"]')
      const blazorButton = blazorPage.locator('[data-theme-state="interactive"]')
      if (state === 'focus-visible') {
        await Promise.all([reactPage.keyboard.press('Tab'), blazorPage.keyboard.press('Tab')])
      } else {
        await Promise.all([reactButton.hover(), blazorButton.hover()])
        if (state === 'active') {
          const [reactBox, blazorBox] = await Promise.all([reactButton.boundingBox(), blazorButton.boundingBox()])
          await Promise.all([
            reactPage.mouse.move(reactBox!.x + reactBox!.width / 2, reactBox!.y + reactBox!.height / 2),
            blazorPage.mouse.move(blazorBox!.x + blazorBox!.width / 2, blazorBox!.y + blazorBox!.height / 2),
          ])
          await Promise.all([reactPage.mouse.down(), blazorPage.mouse.down()])
        }
      }
      await assertLocatorVisualParity(reactButton, blazorButton, `${theme.id}-${state}`, testInfo)
      recordCheck('themeStateParityComparisons')
      if (state === 'active') await Promise.all([reactPage.mouse.up(), blazorPage.mouse.up()])
    }
    await Promise.all([reactContext.close(), blazorContext.close()])
  })
}

const scenarioByLocale: Record<string, string> = {
  'en-US': 'button.locale-en',
  'en-XA': 'button.locale-pseudo',
  'ar-SA': 'button.locale-ar',
}

for (const locale of quality.locales) {
  for (const projection of ['react', 'blazor'] as const) {
    test(`${projection} exposes the ${locale.tag} neutral locale fixture`, async ({ browser }) => {
      const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light' })
      const page = await context.newPage()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, scenarioByLocale[locale.tag]))
      const button = page.locator('[data-gallery-probe] button').first()
      await expect(button).toHaveAttribute('lang', locale.tag)
      await expect(button).toHaveAttribute('dir', locale.direction)
      await expect(button).toContainText(locale.buttonLabel)
      const status = page.getByRole('status', { name: locale.loadingStatus })
      await expect(status).toHaveCount(1)
      await expect(status).toHaveText(locale.loadingStatus)
      if (locale.minimumExpansionRatio) {
        const ratio = locale.buttonLabel.length / quality.locales[0].buttonLabel.length
        expect(ratio).toBeGreaterThanOrEqual(locale.minimumExpansionRatio)
        expect(locale.buttonLabel.startsWith('⟦')).toBeTruthy()
        expect(locale.buttonLabel.endsWith('⟧')).toBeTruthy()
      }
      if (locale.mixedDirectionToken) {
        const isolated = button.locator('bdi')
        await expect(isolated).toHaveText(locale.mixedDirectionToken)
        await expect(isolated).toHaveAttribute('dir', 'ltr')
      }
      recordCheck('localeProjectionChecks')
      await context.close()
    })

    test(`${projection} reflows the ${locale.tag} fixture at 200 percent text`, async ({ browser }) => {
      const context = await browser.newContext({ viewport: { width: 320, height: 720 }, colorScheme: 'light' })
      const page = await context.newPage()
      await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, scenarioByLocale[locale.tag]))
      await page.evaluate(() => { document.documentElement.style.fontSize = '200%' })
      const overflow = await page.locator('[data-gallery-probe]').evaluate(element => Math.max(0, element.scrollWidth - element.clientWidth))
      expect(overflow, `${projection} ${locale.tag} horizontal overflow`).toBe(0)
      recordCheck('reflowChecks')
      const box = await page.locator('[data-gallery-probe] button').first().boundingBox()
      expect(box?.width ?? Number.POSITIVE_INFINITY).toBeLessThanOrEqual(292)
      await context.close()
    })
  }
}

for (const projection of ['react', 'blazor'] as const) {
  test(`${projection} preserves native keyboard activation and a visible focus indicator`, async ({ browser }) => {
    const context = await browser.newContext({ viewport: { width: 920, height: 720 }, colorScheme: 'light' })
    const page = await context.newPage()
    await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, 'button.defaults'))
    const button = page.locator('[data-gallery-probe] button').first()
    await button.evaluate(element => {
      element.setAttribute('data-activation-count', '0')
      element.addEventListener('click', () => {
        const count = Number(element.getAttribute('data-activation-count') ?? '0')
        element.setAttribute('data-activation-count', String(count + 1))
      })
    })
    await page.keyboard.press('Tab')
    await expect(button).toBeFocused()
    const focusStyle = await button.evaluate(element => {
      const style = getComputedStyle(element)
      return { outlineStyle: style.outlineStyle, outlineWidth: Number.parseFloat(style.outlineWidth) }
    })
    expect(focusStyle.outlineStyle).not.toBe('none')
    expect(focusStyle.outlineWidth).toBeGreaterThanOrEqual(2)
    await page.keyboard.press('Enter')
    await page.keyboard.press('Space')
    await expect(button).toHaveAttribute('data-activation-count', '2')
    recordCheck('keyboardFocusChecks')
    await expect(button).toBeFocused()
    await context.close()
  })

  test(`${projection} preserves Button affordances in forced colors`, async ({ browser }) => {
    const context = await browser.newContext({
      viewport: { width: 920, height: 720 },
      colorScheme: 'light',
      forcedColors: 'active',
    })
    const page = await context.newPage()
    await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, 'button.defaults'))
    const button = page.locator('[data-gallery-probe] button').first()
    await page.keyboard.press('Tab')
    const focusStyle = await button.evaluate(element => {
      const style = getComputedStyle(element)
      return { borderStyle: style.borderStyle, outlineStyle: style.outlineStyle, outlineWidth: Number.parseFloat(style.outlineWidth) }
    })
    expect(focusStyle.borderStyle).not.toBe('none')
    expect(focusStyle.outlineStyle).not.toBe('none')
    expect(focusStyle.outlineWidth).toBeGreaterThanOrEqual(2)
    await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, 'button.disabled'))
    const disabledOpacity = await page.locator('[data-gallery-probe] button:disabled').first()
      .evaluate(element => Number.parseFloat(getComputedStyle(element).opacity))
    expect(disabledOpacity).toBe(1)
    recordCheck('forcedColorsChecks')
    await context.close()
  })

  test(`${projection} removes component transitions for reduced motion`, async ({ browser }) => {
    const context = await browser.newContext({
      viewport: { width: 920, height: 720 },
      colorScheme: 'light',
      reducedMotion: 'reduce',
    })
    const page = await context.newPage()
    await openProjectionStory(page, projection, buttonCatalog, scenarioName(buttonCatalog, 'button.defaults'))
    const durationMs = await page.locator('[data-gallery-probe] button').first().evaluate(element => {
      const durations = getComputedStyle(element).transitionDuration.split(',').map(value => Number.parseFloat(value) * 1000)
      return Math.max(...durations)
    })
    expect(durationMs).toBeLessThanOrEqual(1)
    recordCheck('reducedMotionChecks')
    await context.close()
  })
}
