import { AxeBuilder } from '@axe-core/playwright'
import { expect, test, type Browser, type Locator, type Page, type TestInfo } from '@playwright/test'
import pixelmatch from 'pixelmatch'
import { PNG } from 'pngjs'
import { appendFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { arch, platform } from 'node:os'

import { evaluateSettled, gotoWithTransientNetworkRetry, openWithSubresourceRetry, readWithReload } from './navigation-resilience.ts'
import {
  attributeOrderDiffToAria, canonicalCycle, laneCoverage, laneFindingLines, laneHostStops, laneOrderLines, lanePortalKeys,
  partitionByClause, prepareLane, reverseDifference, stopKeyFromSnapshotLine, TAB_WALK_ANNOTATION, teardownLane, walkLane,
  type LaneWalk, type Stop,
} from './tab-walk.ts'

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
  content?: { environment?: { locale?: string } }
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
let cachedBlazorIndex: StoryIndex | undefined

async function reactIndex(page: Page): Promise<StoryIndex> {
  let index: StoryIndex | undefined
  await expect(async () => {
    const response = await page.request.get(`${reactBase}/index.json`)
    expect(response.ok()).toBeTruthy()
    index = await response.json() as StoryIndex
  }).toPass({ intervals: [250, 500, 1_000], timeout: 5_000 })
  return index!
}

// T-717: two bounded attempts of 15 s, reloading a stalled boot, instead of one wait bounded only by the test.
const blazorIndexAttemptMs = 15_000

async function blazorIndex(page: Page): Promise<StoryIndex> {
  if (cachedBlazorIndex) return cachedBlazorIndex
  cachedBlazorIndex = await readWithReload(page, blazorBase, 'BlazingStory.getStoryIndex()', async () => {
    await page.waitForFunction(() => typeof BlazingStory !== 'undefined')
    return evaluateSettled(() => page.evaluate(() => BlazingStory.getStoryIndex()))
  }, blazorIndexAttemptMs)
  return cachedBlazorIndex
}

function storyId(index: StoryIndex, title: string, name: string): string {
  const entry = Object.values(index.entries).find(item => item.type === 'story' && item.title === title && item.name === name)
  expect(entry, `missing ${title} / ${name}`).toBeDefined()
  return entry!.id
}

// T-643 / T-709: a story is ready when its per-story probe is visible, in both projections. The wait
// is bounded by the test, not by the 5 s default expect timeout, which a loaded host exceeds on a Vite
// transform or a WASM boot; the test timeout itself is unchanged. BlazingStory.readyView() is not
// used: it is one module-lifetime promise, not a readiness predicate for the story on screen.
const storyReadyTimeoutMs = 40_000

async function openStory(page: Page, base: string, id: string) {
  await openWithSubresourceRetry(page, `${base}/iframe.html?id=${encodeURIComponent(id)}&viewMode=story`, base,
    timeout => expect(page.locator('[data-gallery-probe]')).toBeVisible({ timeout }), storyReadyTimeoutMs)
}

async function openReactStory(page: Page, id: string) {
  await openStory(page, reactBase, id)
}

async function openBlazorStory(page: Page, id: string) {
  await openStory(page, blazorBase, id)
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
    if (scenario.id === 'select-field.multiple' || scenario.id === 'select-field.multiple-search') {
      const trigger = page.getByRole('button', { name: 'Structure status' })
      await expect(page.getByRole('combobox')).toHaveCount(0)
      await trigger.click()
      const list = page.getByRole('listbox', { name: 'Structure status' })
      await expect(list).toHaveAttribute('aria-multiselectable', 'true')
      await assertAccessible(page, scenario, 'open multiple popup')
      if (scenario.id === 'select-field.multiple-search') {
        const search = page.getByRole('searchbox', { name: 'Structure status' })
        await expect(list.getByRole('searchbox')).toHaveCount(0)
        await expect(search).toBeFocused()
        await search.fill('pending')
        await search.press('Enter')
        await expect(list.getByRole('option', { name: 'Pending review' })).toHaveAttribute('aria-selected', 'false')
        await search.press('ArrowDown')
      } else {
        await expect(list).toBeFocused()
        await list.press('ArrowDown')
      }
      await expect(list).toBeFocused()
      await list.press('Space')
      await expect(trigger).toContainText('Pending review')
      await expect(list).toBeVisible()
      await list.press('Space')
      await expect(trigger).not.toContainText('Pending review')
      await expect(list.getByRole('option', { name: 'Pending review' })).toHaveAttribute('aria-selected', 'false')
      await list.press('Escape')
      await expect(trigger).toBeFocused()
      await expect(list).toBeHidden()
      return
    }
    if (scenario.id === 'select-field.search') {
      const search = page.getByRole('combobox', { name: 'Structure status' })
      await search.fill('pending')
      await expect(page.getByRole('option')).toHaveCount(1)
      await assertAccessible(page, scenario, 'open editable popup')
      await search.press('ArrowDown')
      await search.press('Enter')
      await expect(search).toHaveValue('Pending review')
      await search.fill('unknown')
      await expect(page.getByRole('option')).toHaveCount(0)
      await search.press('Enter')
      await search.press('Escape')
      await expect(search).toHaveValue('Pending review')
      return
    }
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

async function assertRuleAuthoringKeyboard(page: Page) {
  const ruleName = page.getByRole('textbox', { name: 'Rule name', exact: true })
  const target = page.getByRole('textbox', { name: 'Target', exact: true })
  const action = page.getByRole('combobox', { name: 'Rule action', exact: true })
  const scope = page.getByRole('combobox', { name: 'Rule scope', exact: true })
  const version = page.getByRole('combobox', { name: 'Version selection', exact: true })
  const formula = page.getByRole('radio', { name: 'Formula', exact: true })
  const table = page.getByRole('radio', { name: 'Decision table', exact: true })

  await ruleName.focus()
  await page.keyboard.press('Tab')
  await expect(target).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(action).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(scope).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(version).toBeFocused()
  await page.keyboard.press('Tab')
  await expect(formula).toBeFocused()

  await page.keyboard.press('ArrowDown')
  await expect(table).toBeChecked()
  await page.keyboard.press('ArrowUp')
  await expect(formula).toBeChecked()

  const addInput = page.getByRole('button', { name: 'Add declared input', exact: true })
  await addInput.focus()
  await page.keyboard.press('Enter')
  await expect(page.getByRole('combobox', { name: 'Input 1 reference', exact: true })).toBeVisible()
}

async function assertSchemaFormChoicePopup(page: Page, projection: Projection, testInfo: TestInfo) {
  const trigger = page.getByRole('combobox', { name: 'Literal choices', exact: true })
  const listbox = page.locator('.hl-schema-form .hl-select-field__listbox')
  const labels = ['Draft', 'Review', 'Approved', 'Rejected', 'Paused', 'Closed']
  const measurements = []
  await page.evaluate(() => document.fonts.ready)
  for (const width of [920, 320]) for (const textSize of ['100%', '200%']) {
    await page.setViewportSize({ width, height: 720 })
    await page.evaluate(size => { document.documentElement.style.fontSize = size }, textSize)
    for (const selected of ['Closed', 'Review']) {
      await trigger.click()
      await listbox.getByRole('option', { name: selected, exact: true }).click()
      await expect(trigger).toHaveText(new RegExp(selected))
      await expect(listbox).toBeHidden()
      const closedProbeOverflow = await page.locator('[data-gallery-probe]').evaluate(element =>
        Math.max(0, element.scrollWidth - element.clientWidth))
      await trigger.click()
      await expect(listbox.getByRole('option')).toHaveText(labels.map(label => new RegExp(label)))
      for (const label of labels) {
        const option = listbox.getByRole('option', { name: label, exact: true })
        await option.scrollIntoViewIfNeeded()
        await expect(option).toBeInViewport()
      }
      const geometry = await listbox.evaluate(element => {
        const popup = element.closest('.hl-select-field__popup')!.getBoundingClientRect()
        const button = element.closest('.hl-select-field')!.querySelector('[role="combobox"]')!.getBoundingClientRect()
        return {
          popupWidth: popup.width,
          triggerWidth: button.width,
          fieldWidth: element.closest('.hl-form-field__control')!.getBoundingClientRect().width,
          left: popup.left,
          right: popup.right,
          viewportWidth: document.documentElement.clientWidth,
          // Match assertReflow's component boundary; Storybook's outer padded scene is not SchemaForm.
          probeOverflow: (() => {
            const probe = element.closest('[data-gallery-probe]')!
            return Math.max(0, probe.scrollWidth - probe.clientWidth)
          })(),
          popupOverflow: Math.max(0, element.scrollWidth - element.clientWidth),
          labels: [...element.querySelectorAll('[role="option"]')].map(option => {
            const label = option.lastElementChild!
            const box = label.getBoundingClientRect()
            return { text: label.textContent, width: box.width, height: box.height, lineHeight: parseFloat(getComputedStyle(label).lineHeight) }
          }),
        }
      })
      measurements.push({ projection, width, textSize, selected, closedProbeOverflow, ...geometry })
      const context = `${projection} Literal choices at ${width}px / ${textSize}, selected ${selected}`
      // Soft geometry assertions retain evidence from both lanes and every size on failure.
      expect.soft(geometry.popupWidth, `${context}: popup covers trigger`).toBeGreaterThanOrEqual(geometry.triggerWidth - 1)
      expect.soft(geometry.popupWidth, `${context}: popup fits available field width`).toBeLessThanOrEqual(geometry.fieldWidth + 1)
      expect.soft(geometry.left, `${context}: popup left edge`).toBeGreaterThanOrEqual(0)
      expect.soft(geometry.right, `${context}: popup right edge`).toBeLessThanOrEqual(geometry.viewportWidth)
      expect.soft(geometry.probeOverflow, `${context}: horizontal component overflow`).toBe(0)
      expect.soft(closedProbeOverflow, `${context}: closed control horizontal component overflow`).toBe(0)
      expect.soft(geometry.popupOverflow, `${context}: horizontal popup overflow`).toBe(0)
      // At narrow widths wrapping is legitimate; desktop has ample room for all six short labels.
      if (width === 920) for (const label of geometry.labels) {
        expect.soft(label.height, `${context}: ${label.text} should fit on one line (label width ${label.width}px)`)
          .toBeLessThanOrEqual(label.lineHeight + 1)
      }
      await trigger.press('Escape')
      await expect(listbox).toBeHidden()
    }
  }
  await testInfo.attach(`${projection}-schema-form-popup`, {
    body: JSON.stringify(measurements, null, 2), contentType: 'application/json',
  })
}

async function assertSinglePopupScroll(listbox: Locator, context: string) {
  await expect(listbox).toBeVisible()
  const scroll = await listbox.evaluate(element => {
    const popup = element.closest('.hl-select-field__popup')!
    return {
      owners: [popup, ...popup.querySelectorAll('*')]
        .filter(node => /auto|scroll/.test(getComputedStyle(node).overflowY) && node.scrollHeight > node.clientHeight + 1)
        .map(node => node.getAttribute('role')),
      outerOverflow: popup.scrollHeight - popup.clientHeight,
    }
  })
  expect(scroll.owners, `${context}: one scrolling listbox`).toEqual(['listbox'])
  expect(scroll.outerOverflow, `${context}: popup content fits its frame`).toBeLessThanOrEqual(1)
  const last = listbox.getByRole('option').last()
  await last.scrollIntoViewIfNeeded()
  await expect(last).toBeInViewport()
  const position = await last.evaluate(option => {
    const list = option.closest('[role="listbox"]')!
    return {
      top: option.getBoundingClientRect().top - list.getBoundingClientRect().top,
      bottom: option.getBoundingClientRect().bottom - list.getBoundingClientRect().bottom,
      scrollTop: list.scrollTop,
    }
  })
  expect(position.top, `${context}: final option top visible`).toBeGreaterThanOrEqual(-1)
  expect(position.bottom, `${context}: final option bottom visible`).toBeLessThanOrEqual(1)
  expect(position.scrollTop, `${context}: listbox actually scrolls`).toBeGreaterThan(0)
  return last
}

async function assertSchemaFormPickerScroll(page: Page, projection: Projection) {
  const viewport = page.viewportSize()!
  const fontSize = await page.evaluate(() => document.documentElement.style.fontSize)
  for (const [width, height] of [[920, 720], [320, 480]] as const) for (const textSize of ['100%', '200%']) {
    await page.setViewportSize({ width, height })
    await page.evaluate(size => { document.documentElement.style.fontSize = size }, textSize)
    for (const [name, query] of [['Taxonomy', 'Category 0'], ['Record', '3999']] as const) {
      const control = page.getByRole('combobox', { name, exact: true })
      await control.fill(query)
      const listbox = page.getByRole('listbox', { name, exact: true })
      const context = `${projection} ${name} at ${width}x${height} / ${textSize}`
      const last = await assertSinglePopupScroll(listbox, context)
      const value = (await last.innerText()).trim()
      await last.click()
      await expect(control).toHaveValue(value.replace(/^✓\s*/, ''))
      await expect(listbox).toBeHidden()
    }
  }
  await page.setViewportSize(viewport)
  await page.evaluate(size => { document.documentElement.style.fontSize = size }, fontSize)
}

async function assertSearchableMultipleScroll(page: Page, projection: Projection) {
  for (const [height, textSize] of [[200, '100%'], [320, '200%']] as const) {
    await page.setViewportSize({ width: 320, height })
    await page.evaluate(size => { document.documentElement.style.fontSize = size }, textSize)
    const trigger = page.getByRole('button', { name: 'Structure status', exact: true })
    await trigger.click()
    const search = page.getByRole('searchbox', { name: 'Structure status', exact: true })
    await search.fill('')
    const listbox = page.getByRole('listbox', { name: 'Structure status', exact: true })
    await assertSinglePopupScroll(listbox, `${projection} searchable multiple at 320x${height} / ${textSize}`)
    await expect(search).toBeInViewport()
    await search.fill('pending')
    await expect(listbox.getByRole('option')).toHaveCount(1)
    await search.press('Escape')
    await expect(trigger).toBeFocused()
    await expect(listbox).toBeHidden()
  }
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
  if (scenario.id === 'schema-form.host-readonly') {
    await Promise.all([reactPage, blazorPage].map(page => page.setViewportSize({
      width: page.viewportSize()!.width,
      height: 1200,
    })))
  }
  const reactLocator = reactPage.locator('[data-gallery-probe]')
  const blazorLocator = blazorPage.locator('[data-gallery-probe]')
  await Promise.all([reactLocator.waitFor(), blazorLocator.waitFor()])
  if (scenario.id === 'schema-form.host-readonly') {
    const expectedOutputs = [
      'MV North Star',
      'Annual hull, machinery, navigation, and lifesaving equipment inspection.',
      '18425.5',
      '24',
      'Annual safety inspection',
      'Navigation, Fire suppression, Lifesaving equipment',
      'true',
      'true',
      'false',
      '2026-09-16',
      '2026-09-16T09:30:00-04:00',
      '14:45',
      '48750.5',
      '92.5',
      '+1 410 555 0142',
      'port.agent@example.test',
      'https://records.example.test/inspections/HLI-2048',
      'Approved for certificate issuance',
      'Restricted value',
      'Restricted value',
      'Control unavailable: future-object',
    ]
    for (const locator of [reactLocator, blazorLocator]) {
      await expect(locator.locator('output')).toHaveText(expectedOutputs)
      for (const [id, value] of [
        ['vesselName', 'MV North Star'],
        ['inspectionType', 'Annual safety inspection'],
        ['systemsReviewed', 'Navigation, Fire suppression, Lifesaving equipment'],
        ['documentsVerified', 'true'],
        ['masterAttested', 'true'],
        ['followUpRequired', 'false'],
        ['certificateUrl', 'https://records.example.test/inspections/HLI-2048'],
        ['applicationStatus', 'Approved for certificate issuance'],
      ] as const) await expect(locator.locator(`#${id}`)).toHaveText(value)
      await expect(locator.locator('input, select, textarea, button[type="submit"]')).toHaveCount(0)
    }
  }
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
  if (scenario.id === 'schema-form.host-readonly') {
    for (const [projection, png] of [['react', reactPng], ['blazor', blazorPng]] as const) {
      expect(png.height, `${projection} all-kinds capture must include the full form`).toBeGreaterThan(704)
      let bottomInk = 0
      const firstBottomRow = Math.floor(png.height * 0.75)
      for (let y = firstBottomRow; y < png.height - 4; y += 1) {
        for (let x = 4; x < png.width - 4; x += 1) {
          const offset = (y * png.width + x) * 4
          if (png.data[offset]! < 210 || png.data[offset + 1]! < 210 || png.data[offset + 2]! < 210) bottomInk += 1
        }
      }
      expect(bottomInk, `${projection} all-kinds capture bottom quarter is blank or clipped`).toBeGreaterThan(500)
    }
  }
  if (scenario.id === 'schema-form.host-readonly') {
    for (const [lane, png] of [['react', reactPng], ['blazor', blazorPng]] as const) {
      let contentPixels = 0
      // Exclude the frame: an empty bordered probe must not count as rendered content.
      for (let y = 20; y < png.height - 20; y++) for (let x = 20; x < png.width - 20; x++) {
        const offset = (y * png.width + x) * 4
        if (png.data[offset + 3] > 250 && png.data[offset] < 100
          && png.data[offset + 1] < 100 && png.data[offset + 2] < 100) contentPixels++
      }
      expect(contentPixels, `${lane} read-only capture must contain visible text, not an empty frame`).toBeGreaterThan(100)
    }
  }
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

// ── ARIA-tree lane parity (T-943) ──
//
// Pixels and per-element boxes say the two lanes LOOK alike. Neither can say they are the same thing
// to assistive technology: a scene can render pixel-alike and still differ in a role, an accessible
// name or a state, the class of defect behind the 842 accessibility survivors T-921 to T-927 record
// (R-0125 §2, "ARIA-tree parity"). So every scenario also compares the accessibility tree of its
// probe across the two lanes, as Playwright's `locator.ariaSnapshot()` renders it (role, accessible
// name, and the checked, disabled, expanded, invalid, level, pressed and selected states). DES-0034
// open ruling 5 was ruled this way (T-724 ruling 108, G3.6): lane parity includes the accessibility
// tree, ARIA-snapshot equality per scenario, with a named waiver list.
//
// Scope is the probe, the same scope the axe scan (assertAccessible) and the element census read on
// main. An overlay a lane portals out of the probe is outside both scans alike; widening this one
// alone would make the tree comparison judge content the accessibility scan never reads. Where one
// lane portals an open overlay and the other renders it inside the probe, the scenario is registered
// with that reason, so a later, wider scope turns those rows stale instead of silently changing what
// they excuse.
//
// What `ariaSnapshot()` already leaves out, so nothing here has to: elements with no ARIA role (the
// div and span wrappers each lane nests differently) are not nodes at all, their children are hoisted
// into the nearest node with a role; whitespace in names and text is collapsed; ids, classes and
// styles are absent. A `false` state is not rendered, so it is indistinguishable from an absent one
// (`aria-pressed="false"` and no `aria-pressed` both read as a plain button); that is a limit of the
// rendering this check inherits, not something it normalises.
//
// The register is exact both ways, like the element and overflow registers: an unwaived difference
// fails with the diff, a waived scenario whose lanes now match fails until its row is deleted, and a
// waived scenario whose difference CHANGED fails until it is re-measured, so a waiver cannot quietly
// widen to cover a new defect. A row records its difference as position-anchored hunks (laneHunks), so
// a waived line that moves elsewhere in the tree is a changed difference too. A row for an unknown
// scenario, or one owned by T-943 itself, is refused by tooling/validate-gallery.mjs.
//
// A row may name a `state`: a later moment of a scene that runs on a timer (ariaLaterStates), judged
// apart from the prepared scene, which is the row without one.
type AriaWaiver = { scenarioId: string; state?: string; ticket: string; reason: string; hunks: string[] }
const ariaRegister = JSON.parse(readFileSync(resolve(repositoryRoot, 'gallery/aria-parity-known-divergences.json'), 'utf8')) as {
  measureFlag: string
  divergences: AriaWaiver[]
}
const ariaWaiverKey = (id: string, state?: string) => state ? `${id} @${state}` : id
const ariaWaiverByScenario = new Map(ariaRegister.divergences.map(row => [ariaWaiverKey(row.scenarioId, row.state), row]))

// The per-scenario two-lane step: one read of the same prepared scene in each lane, concurrently.
// Every lane-versus-lane probe read goes through here (the ARIA tree now; T-962's tab walk is meant
// to reuse it), so a comparison never has to open a page pair of its own. `probe.page()` gives the
// page to a read that needs the keyboard.
async function readBothLanes<T>(reactPage: Page, blazorPage: Page, read: (probe: Locator, lane: Projection) => Promise<T>) {
  const [react, blazor] = await Promise.all([
    read(reactPage.locator('[data-gallery-probe]'), 'react'),
    read(blazorPage.locator('[data-gallery-probe]'), 'blazor'),
  ])
  return { react, blazor }
}

// The snapshot as the lines the comparison and the register both speak. Each normalisation removes a
// difference that carries no role, name or state; none rewrites a role, a name or a state.
function normaliseAriaSnapshot(snapshot: string): string[] {
  return snapshot
    .split(/\r?\n/)
    // Trailing whitespace and blank lines are YAML layout, not tree content: neither is a node, a
    // name or a state, and both depend on how the renderer joined lines.
    .map(line => line.trimEnd())
    .filter(line => line.length > 0)
}

type LaneDiff = { reactOnly: string[]; blazorOnly: string[]; unified: string[] }

// A line diff of two lanes' snapshots: the lines only React has, the lines only Blazor has, and a
// unified rendering with two lines of context for the failure message. Lines keep their indentation,
// so a node moved to another depth is a difference. The common prefix and suffix are trimmed first,
// so a matching scene costs one pass; the longest-common-subsequence table is built only over the
// differing middle, and above `tableCap` cells the middle is reported whole (every React line
// removed, every Blazor line added) instead of aligned, which is still exact for the register.
function laneLineDiff(react: readonly string[], blazor: readonly string[], context = 2, tableCap = 4_000_000): LaneDiff {
  let head = 0
  while (head < react.length && head < blazor.length && react[head] === blazor[head]) head += 1
  let reactEnd = react.length
  let blazorEnd = blazor.length
  while (reactEnd > head && blazorEnd > head && react[reactEnd - 1] === blazor[blazorEnd - 1]) {
    reactEnd -= 1
    blazorEnd -= 1
  }
  const left = react.slice(head, reactEnd)
  const right = blazor.slice(head, blazorEnd)
  type Op = { kind: ' ' | '-' | '+'; line: string; reactLine: number; blazorLine: number }
  const ops: Op[] = []
  for (let index = 0; index < head; index += 1) ops.push({ kind: ' ', line: react[index], reactLine: index, blazorLine: index })
  const width = right.length + 1
  if ((left.length + 1) * width > tableCap) {
    left.forEach((line, index) => ops.push({ kind: '-', line, reactLine: head + index, blazorLine: head }))
    right.forEach((line, index) => ops.push({ kind: '+', line, reactLine: reactEnd, blazorLine: head + index }))
  } else {
    // suffix[i][j] = length of the longest common subsequence of left[i..] and right[j..].
    const suffix = new Uint32Array((left.length + 1) * width)
    for (let i = left.length - 1; i >= 0; i -= 1) {
      for (let j = right.length - 1; j >= 0; j -= 1) {
        suffix[i * width + j] = left[i] === right[j]
          ? suffix[(i + 1) * width + j + 1] + 1
          : Math.max(suffix[(i + 1) * width + j], suffix[i * width + j + 1])
      }
    }
    let i = 0
    let j = 0
    while (i < left.length || j < right.length) {
      if (i < left.length && j < right.length && left[i] === right[j]) {
        ops.push({ kind: ' ', line: left[i], reactLine: head + i, blazorLine: head + j })
        i += 1
        j += 1
      } else if (j >= right.length || (i < left.length && suffix[(i + 1) * width + j] >= suffix[i * width + j + 1])) {
        ops.push({ kind: '-', line: left[i], reactLine: head + i, blazorLine: head + j })
        i += 1
      } else {
        ops.push({ kind: '+', line: right[j], reactLine: head + i, blazorLine: head + j })
        j += 1
      }
    }
  }
  for (let index = reactEnd; index < react.length; index += 1) {
    ops.push({ kind: ' ', line: react[index], reactLine: index, blazorLine: blazorEnd + index - reactEnd })
  }
  const reactOnly = ops.filter(op => op.kind === '-').map(op => op.line)
  const blazorOnly = ops.filter(op => op.kind === '+').map(op => op.line)
  const unified: string[] = []
  const changed = ops.map((op, index) => op.kind === ' ' ? -1 : index).filter(index => index >= 0)
  let cursor = 0
  while (cursor < changed.length) {
    const start = Math.max(0, changed[cursor] - context)
    let end = changed[cursor]
    while (cursor + 1 < changed.length && changed[cursor + 1] - end <= context * 2 + 1) end = changed[++cursor]
    end = Math.min(ops.length - 1, end + context)
    // The hunk's first line, counted among the identical lines before it in its own lane, anchors the
    // hunk in a tree that repeats itself (a list of identical rows), where context alone cannot.
    const anchor = ops[start]
    const anchorLane = anchor.kind === '+' ? blazor : react
    const anchorAt = anchor.kind === '+' ? anchor.blazorLine : anchor.reactLine
    let occurrence = 0
    for (let index = 0; index <= anchorAt; index += 1) if (anchorLane[index] === anchor.line) occurrence += 1
    unified.push(`@@ react line ${ops[start].reactLine + 1}, blazor line ${ops[start].blazorLine + 1}, first line occurrence ${occurrence} @@`)
    for (let index = start; index <= end; index += 1) unified.push(`${ops[index].kind} ${ops[index].line}`)
    cursor += 1
  }
  return { reactOnly, blazorOnly, unified }
}

// What a register row records: the unified hunks with each header reduced to '@@ #k', k being which
// occurrence of the hunk's first line (in its own lane) the hunk starts at. The context lines pin each
// differing line to its neighbours and k pins the hunk among identical neighbourhoods, so a waived line
// that moves is a changed difference even into a repeated structure. Absolute line numbers are left out:
// an unrelated change elsewhere in the tree shifts them without moving the difference. (A change that
// adds or removes an identical copy of the anchor line before the hunk shifts k and reads as changed,
// which errs towards re-measuring.)
function laneHunks(diff: LaneDiff): string[] {
  return diff.unified.map(line => line.startsWith('@@ ') ? `@@ #${/first line occurrence (\d+) @@$/.exec(line)?.[1] ?? '?'}` : line)
}

const sameLines = (left: readonly string[], right: readonly string[]) =>
  left.length === right.length && left.every((line, index) => line === right[index])

// The register row a measurement describes, ready to paste: the waiver's own ticket and reason are
// carried over when there is one, and left empty (which validate-gallery refuses) when there is not.
function ariaRegisterRow(id: string, diff: LaneDiff, waiver: AriaWaiver | undefined, state?: string): AriaWaiver | null {
  const hunks = laneHunks(diff)
  if (hunks.length === 0) return null
  return { scenarioId: id, ...(state ? { state } : {}), ticket: waiver?.ticket ?? '', reason: waiver?.reason ?? '', hunks }
}

// The whole register judgement, as a pure function of (measured diff, waiver), so its semantics are
// asserted directly below instead of only through a browser run. T-962 judges its tab-walk rows with
// the same semantics, so the judgement is shared; per register are only its wording, what a row
// records (`measured`: the hunks now, or a tab-walk findings list), how a difference is printed and
// the label its messages name (a scenario, or a scenario and its later state).
type LaneSubject = { differs: string; waivedFor: string; cleared: string; registerPath: string; measureFlag: string; row: () => string }
function reconcileLaneDiff(
  label: string,
  measured: readonly string[],
  shown: readonly string[],
  waiver: { ticket: string; recorded: readonly string[] } | undefined,
  subject: LaneSubject,
) {
  const differs = measured.length > 0
  const unwaived: string[] = []
  const drift: string[] = []
  const stale: string[] = []
  if (differs && !waiver) {
    unwaived.push(`${label} ${subject.differs}`, ...shown,
      `If the difference is owned by an open ticket, waive it with ${subject.measureFlag} set. ${subject.row()}`)
  } else if (differs && waiver && !sameLines(measured, waiver.recorded)) {
    // What changed against the row, not the whole measured diff again: the recorded hunks are the
    // left side and the measured ones the right, so a reviewer reads only the lines that moved.
    drift.push(`${label} is waived for ${subject.waivedFor} (ticket ${waiver.ticket}) but the difference changed; re-measure the row with ${subject.measureFlag} set.`,
      'Recorded row versus measured now (- recorded, + measured):', ...laneLineDiff(waiver.recorded, measured).unified, subject.row())
  } else if (!differs && waiver) {
    stale.push(`${label} is waived for ${subject.waivedFor} (ticket ${waiver.ticket}) but ${subject.cleared}; delete the row from ${subject.registerPath}`)
  }
  return { unwaived, drift, stale }
}
function reconcileAriaParity(id: string, diff: LaneDiff, waiver: AriaWaiver | undefined, state?: string) {
  return reconcileLaneDiff(state ? `${id} (${state})` : id, laneHunks(diff), diff.unified, waiver && { ticket: waiver.ticket, recorded: waiver.hunks }, {
    differs: 'ARIA trees differ between lanes (- react only, + blazor only)',
    waivedFor: 'ARIA tree parity',
    cleared: 'its lanes now match',
    registerPath: 'gallery/aria-parity-known-divergences.json',
    measureFlag: ariaRegister.measureFlag,
    row: () => `Row for gallery/aria-parity-known-divergences.json: ${JSON.stringify(ariaRegisterRow(id, diff, waiver, state))}`,
  })
}

// One lane's tree, read until two consecutive reads agree. A lane is judged at a state it settled
// into on its own: neither the other lane nor the register takes part, so a real difference cannot be
// waited out, only a render that is still landing (a debounced search, a Blazor render one round trip
// late). A lane that has not settled when the window closes is a failure of its own, not a verdict.
const ariaParitySettleMs = 5_000
async function settleLane(
  read: () => Promise<string[]>,
  windowMs = ariaParitySettleMs,
  pause = (ms: number) => new Promise<void>(done => setTimeout(done, ms)),
  now = () => Date.now(),
) {
  const deadline = now() + windowMs
  let previous = await read()
  let reads = 1
  for (;;) {
    if (now() >= deadline) return { lines: previous, reads, settled: false }
    await pause([100, 250, 500, 1_000][Math.min(reads - 1, 3)])
    const current = await read()
    reads += 1
    if (sameLines(previous, current)) return { lines: current, reads, settled: true }
    previous = current
  }
}

// Scenes whose prepared state runs on a timer are compared on a frozen clock, listed by exact id: a
// scene with no timer keeps the ordinary settled read after the scans on a running clock, because a
// paused clock also holds back the deferred renders a settled read waits for. two-step's arming starts
// a five-second window that counts down every second, so the armed tree only holds still while time
// does. Each lane's page gets Playwright's fake clock before its story opens (React's timers and Blazor
// WebAssembly's Task.Delay both run on the page's setTimeout, so both follow it). The clock is paused
// once the story is up, the scene is prepared and compared on the paused clock, and the clock then
// resumes, because the axe scans need running timers.
const ariaFrozenClockScenes = new Set(['guarded-control.two-step'])
const guardArmedOrCommitting = '[data-guard-state="armed"], [data-guard-state="committing"]'
// A later moment of a timer-driven scene, read after the scans once each lane has reached it, and
// judged against its own row (`state`). two-step's guard recovers when its arm window runs out.
const ariaLaterStates = new Map<string, Array<{ state: string; settle: (probe: Locator) => Promise<void> }>>([
  ['guarded-control.two-step', [{
    state: 'after-arm-window',
    settle: probe => expect(probe.locator(guardArmedOrCommitting), 'the guard is still armed; this state begins when its arm window ends')
      .toHaveCount(0, { timeout: 15_000 }),
  }]],
])

// Playwright has no "pause now": pauseAt jumps to a time and pauses there, and refuses a time the
// running fake clock has already passed. The page's clock keeps moving while the target is computed, so
// the target sits a margin ahead, retried with a wider margin on a loaded host. Nothing is armed yet,
// so the jump is harmless.
async function pauseClocks(pages: Page[]) {
  await Promise.all(pages.map(async page => {
    for (const margin of [50, 250, 1_000, 5_000]) {
      try {
        await page.clock.pauseAt(await page.evaluate(() => Date.now()) + margin)
        return
      } catch (error) {
        if (margin === 5_000 || !String(error).includes('Cannot fast-forward to the past')) throw error
      }
    }
  }))
}

async function assertAriaParity(reactPage: Page, blazorPage: Page, id: string, state?: string) {
  const waiver = ariaWaiverByScenario.get(ariaWaiverKey(id, state))
  const settleState = state ? ariaLaterStates.get(id)?.find(entry => entry.state === state)?.settle : undefined
  if (state && !settleState) throw new Error(`${id} has no later ARIA state ${state}`)
  const started = performance.now()
  const lanes = await readBothLanes(reactPage, blazorPage, async probe => {
    await settleState?.(probe)
    return settleLane(async () => normaliseAriaSnapshot(await probe.ariaSnapshot()))
  })
  const label = state ? `${id} (${state})` : id
  const unsettled = (['react', 'blazor'] as const).filter(lane => !lanes[lane].settled)
    .map(lane => `${label} ${lane} ARIA tree was still changing after ${ariaParitySettleMs} ms (${lanes[lane].reads} reads)`)
  const diff = laneLineDiff(lanes.react.lines, lanes.blazor.lines)
  const outcome = reconcileAriaParity(id, diff, waiver, state)
  // A row for a state this scenario never reads would never be judged; the prepared read says so.
  const unread = state ? [] : ariaRegister.divergences
    .filter(row => row.scenarioId === id && row.state && !(ariaLaterStates.get(id) ?? []).some(entry => entry.state === row.state))
    .map(row => `${id} has a row for state ${row.state}, which this scenario never reads; delete or rename the row`)
  // One line per comparison: `row` is the register row this measurement describes, pasteable as is.
  // It is null when the lanes match, and also when a lane did not settle, because a tree caught
  // mid-change is not a difference to waive. `measure` holds the diagnostics a row must not carry.
  if (process.env[ariaRegister.measureFlag]) {
    appendFileSync(process.env[ariaRegister.measureFlag]!, `${JSON.stringify({
      scenarioId: id,
      ...(state ? { state } : {}),
      row: unsettled.length ? null : ariaRegisterRow(id, diff, waiver, state),
      measure: {
        reactOnly: diff.reactOnly, blazorOnly: diff.blazorOnly, unified: diff.unified,
        lines: { react: lanes.react.lines.length, blazor: lanes.blazor.lines.length },
        reads: { react: lanes.react.reads, blazor: lanes.blazor.reads },
        settled: { react: lanes.react.settled, blazor: lanes.blazor.settled },
        elapsedMs: Math.round(performance.now() - started),
      },
    })}\n`)
  }
  expect(unsettled, `${label} ARIA tree parity: a lane did not settle`).toEqual([])
  expect(outcome.unwaived, `${label} ARIA tree parity:\n${outcome.unwaived.join('\n')}`).toEqual([])
  expect(outcome.drift, `${label} ARIA tree parity waiver:\n${outcome.drift.join('\n')}`).toEqual([])
  expect(outcome.stale, `${label} ARIA tree parity register`).toEqual([])
  expect(unread, `${id} ARIA tree parity register`).toEqual([])
  // One comparison per scenario: a later state is part of the scenario's comparison, not another.
  if (!state) recordCheck('ariaParityComparisons')
  // The tab walk attributes a cross-lane order difference to this one when T-943 already waived it.
  return diff
}

// ── Keyboard tab walk (T-962) ──
//
// axe cannot see focus order or a keyboard trap; both need a keyboard. So every scenario, in both
// lanes, is also walked: Tab from a sentinel before the whole document to one after it, Shift+Tab
// back, and from the focus the scenario prepared, with a stop cap so a trap fails instead of hanging
// (tab-walk.ts has the engine). It resolves DES-0034 §9's three keyboard clauses with one test shape
// (R-0125 B5 and recommendation 3):
// - 2.1.2 No Keyboard Trap: focus always leaves the scenario, or a modal releases it by Escape or one
//   of its own buttons (the APG modal dialog pattern);
// - 2.4.3 Focus Order: the two lanes produce the same order (DES-0008 §11's parity rule), Shift+Tab
//   retraces it, focus is never lost, never leaks out of an open modal and never follows a positive
//   tabindex (F44). Equality with visual order stays a person's review;
// - 2.4.11 Focus Not Obscured (Minimum): every stop is inside the viewport and not entirely hidden by
//   author content. Content the scenario opened for the user may cover a stop only when Escape,
//   pressed on that stop, reveals it with focus kept there (SC 2.4.11 Note 2: the user can reveal the
//   focused component without advancing focus); anything else is obscured. That is verified once,
//   after both directions have walked and judged the page with the popup open.
//
// It runs LAST, after every other reader: by its nature it changes state (Tab closes a menu, Escape
// releases a modal, blur commits an edit, focus scrolls). It restores the viewport, the root font
// size, the probe transform and the scroll position that earlier checks changed, and nothing else.
//
// Its register is exact both ways, judged by T-943's reconcile: an unwaived difference or finding
// fails under its clause, a waived one whose lines changed fails as drift, and a row with nothing left
// to waive fails as stale. `order` rows hold the cross-lane order diff as T-943's position-anchored
// hunks (the differing lines with two lines of context), so a waived difference that moves fails as
// changed; `findings` rows hold each lane's complete sorted finding lines, optionally for one host
// (occlusion and keyboard-focusable scrollers can depend on font metrics). A `budget` row gives a
// scenario whose own key handling is too slow to walk in full a fixed press budget: each walk stops
// after that many presses and is recorded as the budget, the same on every host. A wall-clock
// `deadline` is a hang and is never waivable. A cross-lane order difference T-943 already waived is
// attributed to it and not registered twice. Every failure prints the row to paste; validate-gallery
// refuses a row too thin to review.
type TabWalkRow = {
  scenarioId: string
  kind: 'order' | 'findings' | 'budget'
  host?: string
  ticket: string
  reason: string
  // An order row: the cross-lane diff of the two lanes' order lines, as laneHunks writes it.
  hunks?: string[]
  // A findings row: each lane's complete sorted finding lines.
  reactOnly?: string[]
  blazorOnly?: string[]
  // A budget row: the presses each walk may take, and the cost of one press the test allowance is
  // sized for (the Shift probe cannot see a lane's Tab handling).
  presses?: number
  pressMs?: number
}
const tabWalkRegisterPath = 'gallery/tab-walk-known-divergences.json'
const tabWalkRegister = JSON.parse(readFileSync(resolve(repositoryRoot, tabWalkRegisterPath), 'utf8')) as {
  rule: string
  measureFlag: string
  divergences: TabWalkRow[]
}
const tabWalkOrderRows = new Map(tabWalkRegister.divergences.filter(row => row.kind === 'order').map(row => [row.scenarioId, row]))
const tabWalkBudgetRows = new Map(tabWalkRegister.divergences.filter(row => row.kind === 'budget').map(row => [row.scenarioId, row]))
const tabWalkFindingsRows = new Map<string, TabWalkRow[]>()
for (const row of tabWalkRegister.divergences.filter(entry => entry.kind === 'findings')) {
  tabWalkFindingsRows.set(row.scenarioId, [...(tabWalkFindingsRows.get(row.scenarioId) ?? []), row])
}
// A host-scoped findings row applies on its host only; on another host it is neither asserted nor
// stale, so that host is held to its own measurement.
function tabWalkFindingsRow(id: string, host: string) {
  return (tabWalkFindingsRows.get(id) ?? []).find(row => !row.host || row.host === host)
}
// The viewport every scenario context opens with, and the one the walk restores.
const scenarioViewport = { width: 920, height: 720 }
// A findings row's lines as one list, React's then Blazor's, which is what the reconcile compares.
const findingsLines = (react: readonly string[], blazor: readonly string[]) =>
  [...react.map(line => `react: ${line}`), ...blazor.map(line => `blazor: ${line}`)]

// The whole tab-walk judgement for one scenario, as a pure function of the two lanes' walks and the
// register, so its semantics are asserted directly below. It lives here, not in tab-walk.ts, because
// it judges with T-943's laneLineDiff, laneHunks and reconcileLaneDiff, which a second copy must not
// duplicate.
function tabWalkVerdict(id: string, react: LaneWalk, blazor: LaneWalk, {
  orderRow, findingsRow, budgetRow, ariaDiff, host,
}: { orderRow?: TabWalkRow; findingsRow?: TabWalkRow; budgetRow?: TabWalkRow; ariaDiff?: LaneDiff; host: string }) {
  const lines = { react: laneOrderLines(react), blazor: laneOrderLines(blazor) }
  const findings = { react: laneFindingLines(react), blazor: laneFindingLines(blazor) }
  const orderDiff = laneLineDiff(lines.react, lines.blazor)
  const attributed = attributeOrderDiffToAria(lines, ariaDiff, { react: lanePortalKeys(react), blazor: lanePortalKeys(blazor) })
  const hunks = attributed ? [] : laneHunks(orderDiff)
  const measuredFindings = findingsLines(findings.react, findings.blazor)
  // The rows this measurement describes, ready to paste: a waiver's ticket and reason are carried
  // over, and left empty (which validate-gallery refuses) when there is none.
  const orderPaste: TabWalkRow | null = hunks.length === 0 ? null
    : { scenarioId: id, kind: 'order', ticket: orderRow?.ticket ?? '', reason: orderRow?.reason ?? '', hunks }
  const findingsPaste: TabWalkRow | null = measuredFindings.length === 0 ? null : {
    scenarioId: id, kind: 'findings', ...(findingsRow?.host ? { host: findingsRow.host } : {}),
    ticket: findingsRow?.ticket ?? '', reason: findingsRow?.reason ?? '', reactOnly: findings.react, blazorOnly: findings.blazor,
  }
  const pasteRow = (row: TabWalkRow | null) => `Row for ${tabWalkRegisterPath} (measured on ${host}): ${JSON.stringify(row)}`
  const order = reconcileLaneDiff(id, hunks, orderDiff.unified, orderRow && { ticket: orderRow.ticket, recorded: orderRow.hunks ?? [] }, {
    differs: 'tab walk order differs between lanes (- react only, + blazor only)',
    waivedFor: 'tab walk order',
    cleared: 'its lanes now walk the same order',
    registerPath: tabWalkRegisterPath,
    measureFlag: tabWalkRegister.measureFlag,
    row: () => pasteRow(orderPaste),
  })
  const found = reconcileLaneDiff(id, measuredFindings, measuredFindings,
    findingsRow && { ticket: findingsRow.ticket, recorded: findingsLines(findingsRow.reactOnly ?? [], findingsRow.blazorOnly ?? []) }, {
      differs: 'tab walk findings',
      waivedFor: 'tab walk findings',
      cleared: 'neither lane has a finding any more',
      registerPath: tabWalkRegisterPath,
      measureFlag: tabWalkRegister.measureFlag,
      row: () => pasteRow(findingsPaste),
    })
  // A budget row whose budget neither lane reached has nothing left to bound.
  const budgeted = [react, blazor].some(lane => laneCoverage(lane).budgeted)
  const budgetStale = budgetRow && !budgeted
    ? [`${id} is given a tab walk press budget (ticket ${budgetRow.ticket}) but neither lane reached it; delete the budget row from ${tabWalkRegisterPath}`]
    : []
  // An unwaived finding fails under its own clause, lane by lane; a waived one is judged whole.
  const clause = (pick: (lanes: ReturnType<typeof partitionByClause>) => string[]) => {
    if (found.unwaived.length === 0) return []
    const failing = [...pick(partitionByClause(findings.react)).map(line => `react: ${line}`), ...pick(partitionByClause(findings.blazor)).map(line => `blazor: ${line}`)]
    return failing.length === 0 ? [] : [...failing, `If the finding is owned by an open ticket, waive it with ${tabWalkRegister.measureFlag} set. ${pasteRow(findingsPaste)}`]
  }
  return {
    lines,
    findings,
    attributed,
    leave: clause(lanes => lanes.leave),
    reverse: clause(lanes => lanes.reverse),
    order: clause(lanes => lanes.order),
    obscured: clause(lanes => lanes.obscured),
    crossLane: order.unwaived,
    drift: [...order.drift, ...found.drift],
    stale: [...order.stale, ...found.stale, ...budgetStale],
    hostStops: [...laneHostStops(react).map(stop => `react: ${stop}`), ...laneHostStops(blazor).map(stop => `blazor: ${stop}`)],
    rows: [orderPaste, findingsPaste].filter((row): row is TabWalkRow => row !== null),
  }
}

async function assertTabWalk(reactPage: Page, blazorPage: Page, scenario: Scenario, testInfo: TestInfo, {
  testStarted, ariaDiffs,
}: { testStarted: number; ariaDiffs: LaneDiff[] }) {
  const id = scenario.id
  // The walk meets the scene after every T-943 comparison of it (on a frozen clock, a guarded scene is
  // compared armed and then after its arm window), so an order difference is attributed against all of
  // their waived differences together.
  const ariaDiff: LaneDiff = {
    reactOnly: ariaDiffs.flatMap(diff => diff.reactOnly),
    blazorOnly: ariaDiffs.flatMap(diff => diff.blazorOnly),
    unified: [],
  }
  const started = performance.now()
  const prepared = await readBothLanes(reactPage, blazorPage, probe => prepareLane(probe.page(), scenarioViewport))
  // The walk's allowance is granted only now, after everything before it has finished inside the
  // ordinary 45 s (90 s on CI). It is sized from the census for every walk the engine can take: E,
  // stepped all the way, when either lane will walk it; F and R each batched and then re-walked in
  // step mode, which any batched candidate forces (stepped only, under a press budget); each press at
  // what a press costs here, never less than 45 ms, and a step press about 40 ms more because it
  // settles the page; then 30% on top. So a 256-link scene or a lane with slow key handling is not a
  // trap, and a hang still fails. The walk stops itself 5 s before the timeout and reports a deadline,
  // so an overrun is a finding rather than a killed test.
  // The census counts a date or time input as the nine presses its segments may take. A budget row
  // sizes the allowance explicitly: its press budget, at the press cost it declares.
  const budgetRow = tabWalkBudgetRows.get(id)
  const census = Math.max(prepared.react.census.presses, prepared.blazor.census.presses)
  const presses = budgetRow?.presses ? Math.min(census, budgetRow.presses) : census
  const perPress = Math.max(45, budgetRow?.pressMs ?? 0, Math.ceil(1.5 * Math.max(prepared.react.pressMs, prepared.blazor.pressMs)) + 20)
  const stepped = (prepared.react.prepared || prepared.blazor.prepared ? 1 : 0) + 2
  const batched = budgetRow ? 0 : 2
  const walkAllowance = Math.ceil((presses + 16) * (batched * perPress + stepped * (perPress + 40)) * 1.3)
  testInfo.setTimeout(Math.max(testInfo.timeout, (performance.now() - testStarted) + 15_000 + walkAllowance))
  const deadline = Date.now() + Math.max(5_000, testInfo.timeout - (performance.now() - testStarted) - 5_000)
  const walking = Date.now()
  const walks = await readBothLanes(reactPage, blazorPage, (probe, lane) =>
    walkLane(probe.page(), { lane, deadline, settled: prepared[lane].settled, pressMs: prepared[lane].pressMs, pressBudget: budgetRow?.presses }))
  // How much of the walk's own allowance (up to the deadline) the slower lane left unused.
  const margin = { allowanceMs: deadline - walking, marginMs: deadline - Date.now() }
  await Promise.all([teardownLane(reactPage), teardownLane(blazorPage)])
  const verdict = tabWalkVerdict(id, walks.react, walks.blazor, {
    orderRow: tabWalkOrderRows.get(id),
    findingsRow: tabWalkFindingsRow(id, galleryHostKey),
    budgetRow,
    ariaDiff,
    host: galleryHostKey,
  })
  const coverage = {
    id,
    wallMs: Math.round(performance.now() - started),
    ...margin,
    attributed: verdict.attributed,
    react: laneCoverage(walks.react),
    blazor: laneCoverage(walks.blazor),
  }
  // The measure line is the register rows a reviewer would add, plus what the walk saw and cost.
  if (process.env[tabWalkRegister.measureFlag]) {
    appendFileSync(process.env[tabWalkRegister.measureFlag]!, `${JSON.stringify({
      scenarioId: id, host: galleryHostKey, lines: verdict.lines, findings: verdict.findings, rows: verdict.rows,
      hostStops: verdict.hostStops, coverage,
    })}\n`)
  }
  expect(verdict.leave, `${id} tab walk focus leaves the scenario (WCAG 2.1.2):\n${verdict.leave.join('\n')}`).toEqual([])
  expect(verdict.reverse, `${id} tab walk Shift+Tab retraces the order (WCAG 2.4.3):\n${verdict.reverse.join('\n')}`).toEqual([])
  expect(verdict.order, `${id} tab walk focus order (WCAG 2.4.3):\n${verdict.order.join('\n')}`).toEqual([])
  expect(verdict.obscured, `${id} tab walk stop is not obscured (WCAG 2.4.11):\n${verdict.obscured.join('\n')}`).toEqual([])
  expect(verdict.crossLane, `${id} tab walk order matches across lanes (WCAG 2.4.3):\n${verdict.crossLane.join('\n')}`).toEqual([])
  expect(verdict.drift, `${id} tab walk register drift:\n${verdict.drift.join('\n')}`).toEqual([])
  expect(verdict.stale, `${id} tab walk register`).toEqual([])
  expect(verdict.hostStops, `${id} tab walk reached a stop outside the scenario and its overlays`).toEqual([])
  recordCheck('tabWalkLeaveChecks')
  recordCheck('tabWalkReverseChecks')
  recordCheck('tabWalkObscuredChecks')
  recordCheck('tabWalkOrderComparisons')
  test.info().annotations.push({ type: TAB_WALK_ANNOTATION, description: JSON.stringify(coverage) })
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
  const BlazingStory: { getStoryIndex(): Promise<StoryIndex> }
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
  // that claims a divergence no run can ever judge. Keep this list aligned with the host keys
  // produced by galleryHostKey and measured in the registers.
  const knownHosts = new Set(['windows-11-x64', 'linux-x64', 'macos-x64-intel', 'macos-arm64'])
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

// ARIA-tree lane parity (T-943): the comparison, its normalisation and the register judgement,
// asserted directly on the pure functions rather than only through a browser run.
const ariaScene = [
  '- heading "Actions" [level=2]',
  '- button "More actions" [expanded]',
  '- menu "More actions":',
  '  - menuitem "Rename"',
  '  - menuitem "Archive" [disabled]',
  '- checkbox "Notify" [checked]',
]

test('ARIA parity normalisation drops only YAML layout, never a role, name or state', () => {
  // Trailing whitespace and blank lines are the only things removed.
  expect(normaliseAriaSnapshot('- heading "Actions" [level=2]   \n\n- button "Save"\r\n'))
    .toEqual(['- heading "Actions" [level=2]', '- button "Save"'])
  // Indentation is structure (depth in the tree), so leading whitespace is kept.
  expect(normaliseAriaSnapshot('- list:\n  - listitem: One')).toEqual(['- list:', '  - listitem: One'])
  // Roles, names and every state Playwright renders come through byte for byte.
  expect(normaliseAriaSnapshot(ariaScene.join('\n'))).toEqual(ariaScene)
  for (const line of ['- switch "Wi-Fi" [checked=mixed]', '- button "Bold" [pressed]', '- tab "One" [selected]',
    '- textbox "Name" [invalid]', '- treeitem "Root" [expanded] [level=1]']) {
    expect(normaliseAriaSnapshot(line), line).toEqual([line])
  }
})

test('ARIA parity fails a role, name or state difference between lanes and shows it as a diff', () => {
  const swap = (index: number, line: string) => ariaScene.map((entry, at) => at === index ? line : entry)
  expect(laneLineDiff(ariaScene, [...ariaScene])).toEqual({ reactOnly: [], blazorOnly: [], unified: [] })
  for (const [label, blazor] of [
    ['role', swap(5, '- switch "Notify" [checked]')],
    ['name', swap(1, '- button "More options" [expanded]')],
    ['state', swap(4, '  - menuitem "Archive"')],
    ['level', swap(0, '- heading "Actions" [level=3]')],
    ['depth', swap(3, '- menuitem "Rename"')],
  ] as const) {
    const diff = laneLineDiff(ariaScene, blazor)
    const index = blazor.findIndex((line, at) => line !== ariaScene[at])
    expect(diff.reactOnly, label).toEqual([ariaScene[index]])
    expect(diff.blazorOnly, label).toEqual([blazor[index]])
    const outcome = reconcileAriaParity('x.default', diff, undefined)
    expect(outcome.unwaived[0], label).toBe('x.default ARIA trees differ between lanes (- react only, + blazor only)')
    expect(outcome.unwaived, label).toContain(`- ${ariaScene[index]}`)
    expect(outcome.unwaived, label).toContain(`+ ${blazor[index]}`)
  }
  // A node one lane lacks is a difference too, aligned so the rest of the tree still matches.
  const missing = laneLineDiff(ariaScene, ariaScene.filter((_, at) => at !== 2 && at !== 3 && at !== 4))
  expect(missing).toMatchObject({ reactOnly: ariaScene.slice(2, 5), blazorOnly: [] })
  expect(missing.unified).toEqual([
    '@@ react line 1, blazor line 1, first line occurrence 1 @@',
    '  - heading "Actions" [level=2]',
    '  - button "More actions" [expanded]',
    `- ${ariaScene[2]}`,
    `- ${ariaScene[3]}`,
    `- ${ariaScene[4]}`,
    '  - checkbox "Notify" [checked]',
  ])
  // The hunks a row records are the same body with the line numbers taken out of the header.
  expect(laneHunks(missing)).toEqual(['@@ #1', ...missing.unified.slice(1)])
  // Above the table cap the differing middle is reported whole rather than aligned.
  expect(laneLineDiff(['- a', '- b', '- c'], ['- a', '- x', '- c'], 2, 1)).toMatchObject({ reactOnly: ['- b'], blazorOnly: ['- x'] })
})

test('the ARIA parity register is exact both ways', () => {
  const blazor = ariaScene.map((line, at) => at === 1 ? '- button "More options" [expanded]' : line)
  const diff = laneLineDiff(ariaScene, blazor)
  const waiver = (hunks: string[]) => ({ scenarioId: 'x.default', ticket: 'T-1104', reason: 'recorded difference', hunks })
  const clean = { unwaived: [], drift: [], stale: [] }
  // A waiver that records exactly the measured hunks covers them.
  expect(reconcileAriaParity('x.default', diff, waiver(laneHunks(diff)))).toEqual(clean)
  // A waived scenario whose lanes now match is stale, like a fixed overflow left in its register.
  expect(reconcileAriaParity('x.default', laneLineDiff(ariaScene, ariaScene), waiver(laneHunks(diff))).stale)
    .toEqual(['x.default is waived for ARIA tree parity (ticket T-1104) but its lanes now match; delete the row from gallery/aria-parity-known-divergences.json'])
  // A waiver cannot cover a difference it does not name: a new or changed line is drift.
  const other = laneLineDiff(ariaScene, ariaScene.map((line, at) => at === 1 ? '- button "Other" [expanded]' : line))
  const drift = reconcileAriaParity('x.default', diff, waiver(laneHunks(other)))
  expect(drift.drift[0]).toContain('x.default is waived for ARIA tree parity (ticket T-1104) but the difference changed')
  expect(drift.unwaived).toEqual([])
  // And no waiver at all leaves a matching scene clean.
  expect(reconcileAriaParity('x.default', laneLineDiff(ariaScene, ariaScene), undefined)).toEqual(clean)
  // A later state of a timer-driven scene is judged on its own row, and its messages and pasteable row
  // name the state.
  const later = reconcileAriaParity('x.default', diff, undefined, 'after-arm-window')
  expect(later.unwaived[0]).toBe('x.default (after-arm-window) ARIA trees differ between lanes (- react only, + blazor only)')
  expect(JSON.parse(later.unwaived.at(-1)!.replace(/^.*Row for gallery\/aria-parity-known-divergences\.json: /, '')))
    .toMatchObject({ scenarioId: 'x.default', state: 'after-arm-window', hunks: laneHunks(diff) })
})

test('an ARIA parity waiver pins each line to its place: a waived line that moves is drift', () => {
  // The numeric-text-box.editing-commit shape: the lanes differ only in Decrement's disabled state.
  const react = ['- textbox "Cost"', '- button "Increment"', '- button "Decrement" [disabled]', '- text: Focus to edit', '- status: "Requests: null"']
  const blazor = react.map(line => line === '- button "Decrement" [disabled]' ? '- button "Decrement"' : line)
  const row = { scenarioId: 'x.default', ticket: 'T-1108', reason: 'recorded difference', hunks: laneHunks(laneLineDiff(react, blazor)) }
  expect(reconcileAriaParity('x.default', laneLineDiff(react, blazor), row).drift).toEqual([])
  // Blazor now reads Decrement before Increment. The lines only each lane has are the very same two,
  // so a row of bare lines would still pass; the hunk's context says the line moved.
  const moved = ['- textbox "Cost"', '- button "Decrement"', '- button "Increment"', '- text: Focus to edit', '- status: "Requests: null"']
  const measured = laneLineDiff(react, moved)
  expect([measured.reactOnly, measured.blazorOnly]).toEqual([['- button "Decrement" [disabled]'], ['- button "Decrement"']])
  expect(reconcileAriaParity('x.default', measured, row).drift[0]).toContain('but the difference changed')
  // An unrelated change outside the context, made identically in both lanes, moves no hunk.
  const longer = ['- heading "Costs" [level=2]', '- paragraph: Intro', '- paragraph: More', ...react]
  const longerBlazor = ['- heading "Costs" [level=2]', '- paragraph: Intro', '- paragraph: More', ...blazor]
  const shifted = laneLineDiff(longer, longerBlazor)
  expect(shifted.unified[0]).not.toBe(laneLineDiff(react, blazor).unified[0])
  expect(reconcileAriaParity('x.default', shifted, row)).toEqual({ unwaived: [], drift: [], stale: [] })
})

test('an ARIA parity waiver tells repeated rows apart: a line that moves into identical context is drift', () => {
  // Four identical rows; Blazor drops the second row's button name. Moving that defect to the third
  // row leaves the hunk body identical, context lines included, so only the anchor can tell (review 2,
  // finding 6): the hunk now starts at the second occurrence of its first line, not the first.
  const rows = (blank: number) => [0, 1, 2, 3].flatMap(at => ['- listitem:', at === blank ? '  - button' : '  - button "Remove"'])
  const react = rows(-1)
  const recorded = laneLineDiff(react, rows(1))
  const moved = laneLineDiff(react, rows(2))
  expect(moved.unified.slice(1)).toEqual(recorded.unified.slice(1))
  expect([laneHunks(recorded)[0], laneHunks(moved)[0]]).toEqual(['@@ #1', '@@ #2'])
  const row = { scenarioId: 'x.default', ticket: 'T-1104', reason: 'recorded difference', hunks: laneHunks(recorded) }
  expect(reconcileAriaParity('x.default', recorded, row).drift).toEqual([])
  expect(reconcileAriaParity('x.default', moved, row).drift[0]).toContain('but the difference changed')
})

test('an ARIA parity drift names what changed against the row and prints a row that can be pasted', () => {
  const react = ['- heading "Alerts" [level=2]', '- alert:', '  - text: Saved']
  const recorded = laneLineDiff(react, ['- heading "Alerts" [level=2]', '- status:', '  - text: Saved'])
  const waiver = { scenarioId: 'alert.dismissal', ticket: 'T-1104', reason: 'story renders the warning variant in React only', hunks: laneHunks(recorded) }
  const measured = laneLineDiff(react, ['- heading "Alerts" [level=2]', '- log:', '  - text: Saved'])
  const { drift } = reconcileAriaParity('alert.dismissal', measured, waiver)
  // Only the changed line, recorded against measured; not the whole measured diff again.
  expect(drift).toContain('- + - status:')
  expect(drift).toContain('+ + - log:')
  expect(drift).not.toContain('- - heading "Alerts" [level=2]')
  // The last line is the row as measured, with the waiver's ticket and reason carried over.
  const pasted = JSON.parse(drift.at(-1)!.replace(/^Row for gallery\/aria-parity-known-divergences\.json: /, ''))
  expect(pasted).toEqual({ ...waiver, hunks: laneHunks(measured) })
  // An unwaived difference prints the row to fill in, and a match prints no row at all.
  expect(reconcileAriaParity('alert.dismissal', measured, undefined).unwaived.at(-1)).toContain('"ticket":"","reason":""')
  expect(ariaRegisterRow('alert.dismissal', laneLineDiff(react, react), undefined)).toBeNull()
})

test('an ARIA parity lane is judged where it settles on its own, and a real difference is not waited out', async () => {
  const reader = (reads: string[][]) => { let at = 0; return async () => reads[Math.min(at++, reads.length - 1)] }
  const instant = async () => {}
  // Settled on the first two agreeing reads.
  expect(await settleLane(reader([['- a'], ['- a']]), 5_000, instant)).toEqual({ lines: ['- a'], reads: 2, settled: true })
  // A render still landing: judged at the state it settles into.
  expect(await settleLane(reader([['- a'], ['- b'], ['- b']]), 5_000, instant)).toEqual({ lines: ['- b'], reads: 3, settled: true })
  // The other lane plays no part: a lane that settles on a tree the other lane does not have is
  // judged there, even if it would have changed later. The pair is compared once, at the settled
  // reads, and the difference fails rather than being re-read until it goes away.
  const react = await settleLane(reader([['- status: Armed'], ['- status: Armed'], ['- status: Restored']]), 5_000, instant)
  const blazor = await settleLane(reader([['- status: Restored']]), 5_000, instant)
  expect(react.lines).toEqual(['- status: Armed'])
  expect(reconcileAriaParity('x.default', laneLineDiff(react.lines, blazor.lines), undefined).unwaived).not.toEqual([])
  // A lane that never settles inside the window is reported as unsettled, never as a verdict.
  let clock = 0
  const ticking = async () => [`- timer: ${clock}`]
  const unsettled = await settleLane(ticking, 1_000, async ms => { clock += ms }, () => clock)
  expect(unsettled.settled).toBe(false)
})

// ── Keyboard tab walk (T-962): pure tests ──
//
// The line shapes, the cycle rotation, the reverse rule, the order lines and the register judgement,
// asserted directly on the pure functions rather than only through a browser run.
const walkStop = (id: number, extra: Partial<Stop> = {}): Stop => ({
  id, tag: 'button', location: 'probe', modal: -1, segmented: false, scroller: false, verdict: { kind: 'visible' }, leak: -1,
  trusted: true, stepped: true, repeat: false, returned: false, ...extra,
})
const walkRecord = (direction: 'forward' | 'reverse' | 'prepared', stops: Stop[], extra: Partial<LaneWalk['forward'][number]> = {}) => ({
  direction, stops, outcome: 'exited' as const, presses: stops.length + 1, stepPresses: 0, unconfirmed: false, stepThrough: 0, cap: 100,
  c0: 'same', c1: 'same', ...extra,
})
const laneWalk = (keys: Record<number, string>, extra: Partial<LaneWalk> = {}): LaneWalk => ({
  lane: 'react', keys: Object.fromEntries(Object.entries(keys)), census: { count: 0, segmented: 0, presses: 0, first: -1, digest: '', openModals: [], openPopups: 0, positiveTabindex: [] },
  forward: [], reverse: [], walkMs: 0, noisy: false, ...extra,
})
const fourButtons = { 1: 'button "A"', 2: 'button "B"', 3: 'button "C"', 4: 'button "D"' }
const straightWalk = (keys: Record<number, string> = fourButtons, ids = [1, 2, 3, 4]) => laneWalk(keys, {
  forward: [walkRecord('forward', ids.map(id => walkStop(id)))], clean: 0,
  reverse: [walkRecord('reverse', [...ids].reverse().map(id => walkStop(id)))], cleanReverse: 0,
})

test('a tab walk stop key is the role and name of its snapshot line, whatever follows them', () => {
  for (const [line, key] of [
    ['  - button "Save" [pressed]', 'button "Save"'],
    ['- textbox "Name": x', 'textbox "Name"'],
    ['- article "A":', 'article "A"'],
    ['- separator', 'separator'],
    ['- heading "Actions" [level=2]', 'heading "Actions"'],
    ['- button "Say \\"hi\\"" [expanded]', 'button "Say \\"hi\\""'],
    ['- link "Docs":', 'link "Docs"'],
    ["- 'button \"a: b\"'", 'button "a: b"'],
    ['- tablist:', 'tablist'],
  ] as const) expect(stopKeyFromSnapshotLine(line), line).toBe(key)
})

test('a tab walk cycle is rotated to the member first in document order', () => {
  expect(canonicalCycle([3, 1, 2], [1, 2, 3])).toEqual([1, 2, 3])
  expect(canonicalCycle([7, 9], [9, 7])).toEqual([9, 7])
  expect(canonicalCycle([], [1])).toEqual([])
})

test('Shift+Tab must retrace the clean Tab order, by radio group, collapsed repeat and re-render', () => {
  const keys = { ...fourButtons, 5: 'radio "North"', 6: 'radio "South"', 7: 'textbox "When"', 8: 'button "B"' }
  const forward = [walkStop(5, { radioGroup: 'g', tag: 'input' }), walkStop(7, { segmented: true, tag: 'input' }), walkStop(7, { segmented: true, tag: 'input', repeat: true }), walkStop(2)]
  const walk = laneWalk(keys)
  // An unchecked native group is entered at its first radio going forward and its last going back.
  expect(reverseDifference(walk, forward, [walkStop(2), walkStop(7, { segmented: true }), walkStop(7, { segmented: true }), walkStop(7, { segmented: true }), walkStop(6, { radioGroup: 'g' })])).toBeUndefined()
  // A different element with the same key at the same position was re-rendered, not reordered.
  expect(reverseDifference(walk, forward, [walkStop(8), walkStop(7), walkStop(5, { radioGroup: 'g' })])).toBeUndefined()
  // A real reordering, and a missing stop, are named by key.
  expect(reverseDifference(walk, forward, [walkStop(7), walkStop(2), walkStop(5, { radioGroup: 'g' })])).toBe('2.4.3 reverse differs: expected button "B" got textbox "When"')
  expect(reverseDifference(walk, forward, [walkStop(2), walkStop(7)])).toBe('2.4.3 reverse differs: expected radio group g got the start')
})

test('the cross-lane order lines are the release walks then the clean walk, without scrollers, transitional or prepared walks', () => {
  const keys = { ...fourButtons, 10: 'dialog "Settings"', 11: 'button "One"', 12: 'button "Two"', 20: '(no role) div' }
  const release = walkRecord('forward', [walkStop(11, { modal: 10 }), walkStop(12, { modal: 10 }), walkStop(11, { modal: 10 }), walkStop(12, { modal: 10 })], {
    outcome: 'modal-cycle', cycle: { start: 0, members: [11, 12], walked: [11, 12] }, release: { released: true, by: 'Escape ×1', reverseOk: true, modal: 10 },
  })
  const transitional = walkRecord('forward', [walkStop(1), walkStop(3)], { c0: 'before', c1: 'after' })
  const clean = walkRecord('forward', [walkStop(1), walkStop(20, { scroller: true, tag: 'div' }), walkStop(2)])
  const walk = laneWalk(keys, { prepared: walkRecord('prepared', [walkStop(4)]), forward: [release, transitional, clean], clean: 2 })
  expect(laneOrderLines(walk)).toEqual(['↻ - button "One"', '↻ - button "Two"', '↺ ok', 'released by Escape ×1', '- button "A"', '- button "B"', 'exited'])
  // A modal the prepared walk released is part of the order (the other lane may reach it from the
  // top instead); the prepared walk's other stops are not.
  const preparedRelease = { ...release, direction: 'prepared' as const }
  expect(laneOrderLines(laneWalk(keys, { prepared: preparedRelease, forward: [clean], clean: 0 })))
    .toEqual(['↻ - button "One"', '↻ - button "Two"', '↺ ok', 'released by Escape ×1', '- button "A"', '- button "B"', 'exited'])
  // A segmented input's repeats are one line with a count; a stop focus came back to is marked.
  const segmented = laneWalk({ 7: 'textbox "When"', 1: 'button "A"' }, { forward: [walkRecord('forward', [walkStop(1, { returned: true }), walkStop(7), walkStop(7, { repeat: true }), walkStop(7, { repeat: true })])], clean: 0 })
  expect(laneOrderLines(segmented)).toEqual(['↩ - button "A"', '- textbox "When" ×3', 'exited'])
  // No clean walk: the reason the phase stopped is the last line.
  expect(laneOrderLines(laneWalk(fourButtons, { forward: [walkRecord('forward', [walkStop(1)], { outcome: 'trap', cycle: { start: 0, members: [2, 3], walked: [2, 3] } })], forwardStopped: 'trap' }))).toEqual(['trap'])
  // A walk stopped at its declared press budget is compared as far as it went, then the budget.
  expect(laneOrderLines(laneWalk(fourButtons, { pressBudget: 24, forward: [walkRecord('forward', [walkStop(1), walkStop(2)], { outcome: 'budget' })], forwardStopped: 'budget' })))
    .toEqual(['- button "A"', '- button "B"', 'budget: 24 presses'])
})

test('the prepared walk and a budgeted walk are judged like any walk, and only a verified reveal exempts a user-opened cover', () => {
  const keys = { ...fourButtons, 10: 'dialog "Settings"', 11: 'button "One"', 12: 'button "Two"', 13: 'menuitem "Open"' }
  const clean = walkRecord('forward', [walkStop(1), walkStop(2)])
  const released = walkRecord('prepared', [walkStop(12, { modal: 10, verdict: { kind: 'obscured', occluder: 'div.cover' } }), walkStop(11, { modal: 10 }), walkStop(12, { modal: 10, verdict: { kind: 'obscured', occluder: 'div.cover' } }), walkStop(4, { leak: 10 })], {
    outcome: 'modal-cycle', cycle: { start: 0, members: [11, 12], walked: [12, 11] }, release: { released: true, by: 'Escape ×1', reverseOk: false, modal: 10 },
  })
  expect(laneFindingLines(laneWalk(keys, { prepared: released, forward: [clean], clean: 0 }))).toEqual([
    '2.4.11 prepared obscured: button "Two" under div.cover',
    '2.4.3 prepared modal reverse differs: dialog "Settings"',
    '2.4.3 prepared modal-leak: button "D" reached while dialog "Settings" is open',
  ])
  // Without a release, E's stops are judged too: focus lost on Tab out of a prepared popup is only
  // visible to E, because F's anchor closes the popup first.
  const lost = walkRecord('prepared', [walkStop(13), walkStop(0, { location: 'body', tag: 'body' }), walkStop(4)])
  expect(laneFindingLines(laneWalk(keys, { prepared: lost, forward: [clean], clean: 0 }))).toEqual(['2.4.3 prepared focus-lost: after menuitem "Open"'])
  const trapped = walkRecord('prepared', [walkStop(2), walkStop(3), walkStop(2), walkStop(3)], { outcome: 'trap', cycle: { start: 0, members: [2, 3], walked: [2, 3] } })
  trapped.stops[0].verdict = { kind: 'offscreen' }
  expect(laneFindingLines(laneWalk(keys, { prepared: trapped, forward: [clean], clean: 0 }))).toEqual(['2.1.2 prepared trap: ↻ button "B" → button "C"', '2.4.11 prepared offscreen: button "B"'])
  // A partial modal cycle is a trap that names the modal stops it never reaches.
  const partial = walkRecord('forward', [walkStop(11, { modal: 10 }), walkStop(12, { modal: 10 })], { outcome: 'trap', cycle: { start: 0, members: [11, 12], walked: [11, 12] }, skipped: [4] })
  expect(laneFindingLines(laneWalk(keys, { forward: [partial], forwardStopped: 'trap' }))).toEqual(['2.1.2 forward trap: ↻ button "One" → button "Two"; never reaches button "D"'])
  // A budgeted walk is stepped and judged as far as it went; only its outcome adds no line.
  const budget = walkRecord('forward', [walkStop(1, { verdict: { kind: 'clipped' } }), walkStop(0, { location: 'body', tag: 'body' })], { outcome: 'budget' })
  expect(laneFindingLines(laneWalk(keys, { pressBudget: 8, forward: [budget], forwardStopped: 'budget' })))
    .toEqual(['2.4.11 forward clipped: button "A"', '2.4.3 forward focus-lost: after button "A"'])
  // A deadline is a hang: it is a finding (and validate-gallery refuses to waive it).
  expect(laneFindingLines(laneWalk(keys, { forward: [walkRecord('forward', [walkStop(1)], { outcome: 'deadline' })], forwardStopped: 'deadline' })))
    .toEqual(['2.1.2 forward deadline: the walk did not finish within the test\'s allowance'])
  // A user-opened cover is obscured unless verifyCovers revealed it (or the same dismissal did).
  const covered = walkRecord('forward', [walkStop(1, { verdict: { kind: 'obscured', occluder: 'div (fixed)', userOpened: true, popups: [9] } }), walkStop(2, { verdict: { kind: 'obscured', occluder: 'div (fixed)', userOpened: true, popups: [9] } })])
  const lines = (outcomes: Array<NonNullable<LaneWalk['covers']>[number]['outcome']>) => laneFindingLines(laneWalk(keys, {
    forward: [covered], clean: 0, covers: outcomes.map((outcome, index) => ({ id: index + 1, popups: [9], outcome })),
  }))
  expect(lines(['revealed', 'same-dismissal'])).toEqual([])
  expect(lines(['covered', 'closed-before'])).toEqual(['2.4.11 forward obscured: button "A" under div (fixed)', '2.4.11 forward obscured: button "B" under div (fixed)'])
  expect(lines(['in-modal', 'gone'])).toHaveLength(2)
  expect(laneCoverage(laneWalk(keys, { forward: [covered], clean: 0, covers: [{ id: 1, popups: [9], outcome: 'revealed' }, { id: 2, popups: [9], outcome: 'covered' }] })))
    .toMatchObject({ exemptUserOpened: 1, coveredUserOpened: 1 })
})

test('tab walk findings are split by the clause message that reports them', () => {
  expect(partitionByClause([
    '2.1.2 forward trap: ↻ button "B" → button "C"', '2.4.3 reverse differs: expected a got b', '2.4.3 forward focus-lost: after button "A"',
    '2.4.3 walk positive-tabindex: button "Go" tabindex=2', '2.4.11 reverse obscured: button "B" under header.bar (sticky)',
  ])).toEqual({
    leave: ['2.1.2 forward trap: ↻ button "B" → button "C"'],
    reverse: ['2.4.3 reverse differs: expected a got b'],
    order: ['2.4.3 forward focus-lost: after button "A"', '2.4.3 walk positive-tabindex: button "Go" tabindex=2'],
    obscured: ['2.4.11 reverse obscured: button "B" under header.bar (sticky)'],
  })
})

test('the tab walk register is exact both ways, per kind and per host', () => {
  const host = 'macos-arm64'
  const clean = straightWalk()
  const reordered = straightWalk(fourButtons, [1, 3, 2, 4])
  const row = (kind: TabWalkRow['kind'], fields: Partial<TabWalkRow>): TabWalkRow =>
    ({ scenarioId: 'x.default', kind, ticket: 'T-962', reason: 'recorded difference for the test', ...fields })
  // Unwaived: a cross-lane order difference fails with the diff and the row to paste.
  const unwaived = tabWalkVerdict('x.default', clean, reordered, { host })
  expect(unwaived.crossLane[0]).toBe('x.default tab walk order differs between lanes (- react only, + blazor only)')
  expect(unwaived.crossLane.at(-1)).toMatch(/^If the difference is owned by an open ticket, waive it with HARBORLINE_GALLERY_TAB_WALK_MEASURE set\. Row for gallery\/tab-walk-known-divergences\.json \(measured on macos-arm64\): \{"scenarioId":"x\.default","kind":"order","ticket":"","reason":"","hunks":\["@@ #1"/)
  // An order row records T-943's hunks: the differing lines anchored by two lines of context.
  const hunks = laneHunks(laneLineDiff(laneOrderLines(clean), laneOrderLines(reordered)))
  expect(hunks).toEqual(['@@ #1', '  - button "A"', '- - button "B"', '  - button "C"', '+ - button "B"', '  - button "D"', '  exited'])
  // Waived exactly: clean. Waived but changed: drift. Waived with nothing left: stale.
  expect(tabWalkVerdict('x.default', clean, reordered, { host, orderRow: row('order', { hunks }) })).toMatchObject({ crossLane: [], drift: [], stale: [] })
  const changed = tabWalkVerdict('x.default', clean, reordered, { host, orderRow: row('order', { hunks: ['@@ #1', '- - button "Z"'] }) })
  expect(changed.drift[0]).toBe('x.default is waived for tab walk order (ticket T-962) but the difference changed; re-measure the row with HARBORLINE_GALLERY_TAB_WALK_MEASURE set.')
  expect(changed.drift.at(-1)).toContain('"ticket":"T-962","reason":"recorded difference for the test"')
  expect(tabWalkVerdict('x.default', clean, clean, { host, orderRow: row('order', { hunks }) }).stale)
    .toEqual(['x.default is waived for tab walk order (ticket T-962) but its lanes now walk the same order; delete the row from gallery/tab-walk-known-divergences.json'])
  // Position-aware: the same two stops swapped next to different neighbours are a changed difference.
  const shifted = { ...fourButtons, 5: 'button "Z"' }
  const moved = tabWalkVerdict('x.default', straightWalk(shifted, [1, 2, 5, 4, 3]), straightWalk(shifted, [1, 2, 5, 3, 4]), { host, orderRow: row('order', { hunks }) })
  expect(moved.drift[0]).toContain('but the difference changed')
  // Findings: unwaived ones fail under their clause, lane by lane, with the row to paste.
  const obscured = straightWalk()
  obscured.forward[0].stops[1].verdict = { kind: 'obscured', occluder: 'header.bar (sticky)' }
  const line = '2.4.11 forward obscured: button "B" under header.bar (sticky)'
  const found = tabWalkVerdict('x.default', obscured, clean, { host })
  expect(found.obscured[0]).toBe(`react: ${line}`)
  expect(found.obscured.at(-1)).toMatch(/"kind":"findings","ticket":"","reason":"","reactOnly":\["2\.4\.11 forward obscured/)
  expect([found.leave, found.reverse, found.order]).toEqual([[], [], []])
  expect(tabWalkVerdict('x.default', obscured, clean, { host, findingsRow: row('findings', { reactOnly: [line], blazorOnly: [] }) })).toMatchObject({ obscured: [], drift: [], stale: [] })
  expect(tabWalkVerdict('x.default', clean, clean, { host, findingsRow: row('findings', { reactOnly: [line], blazorOnly: [] }) }).stale)
    .toEqual(['x.default is waived for tab walk findings (ticket T-962) but neither lane has a finding any more; delete the row from gallery/tab-walk-known-divergences.json'])
  // A finding that moves to the other lane is a changed difference, not a covered one.
  expect(tabWalkVerdict('x.default', clean, obscured, { host, findingsRow: row('findings', { reactOnly: [line], blazorOnly: [] }) }).drift[0])
    .toContain('x.default is waived for tab walk findings (ticket T-962) but the difference changed')
  // A host-scoped findings row applies on its host only: elsewhere it is neither asserted nor stale.
  tabWalkFindingsRows.set('x.scoped', [row('findings', { scenarioId: 'x.scoped', host: 'linux-x64', reactOnly: [line], blazorOnly: [] })])
  expect(tabWalkFindingsRow('x.scoped', 'linux-x64')?.host).toBe('linux-x64')
  expect(tabWalkFindingsRow('x.scoped', host)).toBeUndefined()
  tabWalkFindingsRows.delete('x.scoped')
  // A budget row is exact too: it is stale once neither lane reaches the budget.
  const budgetRow = row('budget', { presses: 24, pressMs: 400 })
  const budgeted = laneWalk(fourButtons, { pressBudget: 24, forward: [walkRecord('forward', [walkStop(1)], { outcome: 'budget' })], forwardStopped: 'budget' })
  expect(tabWalkVerdict('x.default', budgeted, clean, { host, budgetRow, orderRow: row('order', { hunks: laneHunks(laneLineDiff(laneOrderLines(budgeted), laneOrderLines(clean))) }) }).stale).toEqual([])
  expect(tabWalkVerdict('x.default', clean, clean, { host, budgetRow }).stale)
    .toEqual(['x.default is given a tab walk press budget (ticket T-962) but neither lane reached it; delete the budget row from gallery/tab-walk-known-divergences.json'])
})

test('a cross-lane order difference is attributed to T-943 only when its waived nodes explain all of it', () => {
  const none = { react: new Set<string>(), blazor: new Set<string>() }
  const exited = (...stops: string[]) => [...stops.map(stop => `- ${stop}`), 'exited']
  // A name T-943 waived (React spells it one way, Blazor another), in the same place: attributed.
  // The Blazor line is the YAML-quoted form Playwright writes for a name with a colon.
  const guardedAria = { reactOnly: ['- button "Sign and save package"'], blazorOnly: ['- \'button "Guarded action: Sign. Activate to arm."\': Sign'], unified: [] }
  expect(attributeOrderDiffToAria({ react: exited('button "Sign and save package"'), blazor: exited('button "Guarded action: Sign. Activate to arm."') }, guardedAria, none)).toBe(true)
  // The same rename, but the node also moved: not attributed, the move is a defect T-943 cannot see.
  const moved = { react: exited('button "A"', 'button "Sign and save package"'), blazor: exited('button "Guarded action: Sign. Activate to arm."', 'button "A"') }
  expect(attributeOrderDiffToAria(moved, guardedAria, none)).toBe(false)
  // A state difference T-943 waived (disabled in React, so no tab stop there): attributed.
  const stateAria = { reactOnly: ['- button "Decrement" [disabled]'], blazorOnly: ['- button "Decrement"'], unified: [] }
  expect(attributeOrderDiffToAria({ react: exited('textbox "Quantity"'), blazor: exited('textbox "Quantity"', 'button "Decrement"') }, stateAria, none)).toBe(true)
  // Different fields in each lane (several nodes each side) collapse to one placeholder each side.
  const fieldsAria = { reactOnly: ['- textbox "Asset tag"', '- button "Replace"'], blazorOnly: ['- button "Clear"', '- textbox "Password"', '- button "Show password"'], unified: [] }
  expect(attributeOrderDiffToAria({ react: exited('textbox "Name"', 'textbox "Asset tag"', 'button "Replace"'), blazor: exited('textbox "Name"', 'button "Clear"', 'textbox "Password"', 'button "Show password"') }, fieldsAria, none)).toBe(true)
  // A node only one lane has, and nothing on the other side: attributed.
  expect(attributeOrderDiffToAria({ react: exited('link "Overview"', 'button "Replace model"'), blazor: exited('link "Overview"') }, { reactOnly: ['- button "Replace model"'], blazorOnly: [], unified: [] }, none)).toBe(true)
  // A stop T-943 did not list, a portaled stop, a different outcome, no T-943 diff, or no difference: not attributed.
  expect(attributeOrderDiffToAria({ react: exited('button "B"', 'button "C"'), blazor: exited('button "C"', 'button "B"') }, guardedAria, none)).toBe(false)
  expect(attributeOrderDiffToAria({ react: exited('button "Sign and save package"'), blazor: exited('button "Guarded action: Sign. Activate to arm."') }, guardedAria, { react: new Set(['button "Sign and save package"']), blazor: new Set() })).toBe(false)
  expect(attributeOrderDiffToAria({ react: exited('button "Sign and save package"'), blazor: ['trap'] }, guardedAria, none)).toBe(false)
  expect(attributeOrderDiffToAria({ react: exited('button "Sign and save package"'), blazor: exited('button "Guarded action: Sign. Activate to arm."') }, undefined, none)).toBe(false)
  expect(attributeOrderDiffToAria({ react: exited('button "A"'), blazor: exited('button "A"') }, guardedAria, none)).toBe(false)
})

// ── Keyboard tab walk (T-962): seeded detector proofs ──
//
// Each proof drives the real walk over a page built to contain exactly one keyboard defect (or its
// control), with no gallery server, and asserts the exact finding line and the clause message it
// fails under. Each records tabWalkDetectorProofs once; the gate declares fourteen, so deleting one
// is "declared 14, measured 13".
const proofHost = galleryHostKey
const proofProbe = (body: string) => `<main data-gallery-probe>${body}</main>`
// The proof pages open the way the scenario pages do (the scenario viewport, light, reduced motion,
// a UTC clock zone), with an en-US language, since a proof page is no catalog scenario.
const proofContext = (browser: Browser) => browser.newContext({ viewport: scenarioViewport, colorScheme: 'light', reducedMotion: 'reduce', timezoneId: 'UTC', locale: 'en-US' })
async function walkProof(browser: Browser, html: string, lane: Projection = 'react', { pressBudget }: { pressBudget?: number } = {}) {
  const context = await proofContext(browser)
  try {
    const page = await context.newPage()
    await page.setContent(`<!doctype html><html lang="en"><body>${html}</body></html>`)
    await prepareLane(page, scenarioViewport)
    return await walkLane(page, { lane, deadline: Date.now() + 20_000, ...(pressBudget ? { pressBudget } : {}) })
  } finally {
    await context.close()
  }
}
const proofVerdict = (walk: LaneWalk, other: LaneWalk = walk, extra: { ariaDiff?: LaneDiff } = {}) => tabWalkVerdict('proof', walk, other, { host: proofHost, ...extra })
const aplModal = (label: string, buttons: string, { escape = true, wrap = true } = {}) => `<div role="dialog" aria-modal="true" aria-label="${label}" id="modal">${buttons}</div>
  <script>const modal = document.getElementById('modal'); document.addEventListener('keydown', event => {
    if (!modal.isConnected) return
    if (event.key === 'Escape' && ${escape}) { modal.remove(); return }
    if (event.key !== 'Tab' || !${wrap}) return
    const items = [...modal.querySelectorAll('button, input')]; const first = items[0], last = items.at(-1)
    if (event.shiftKey && (document.activeElement === first || !modal.contains(document.activeElement))) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && (document.activeElement === last || !modal.contains(document.activeElement))) { event.preventDefault(); first.focus() }
  }, true)</script>`

test('tab walk proof P1: a seeded focus trap fails 2.1.2, and the same page without it passes', async ({ browser }) => {
  const buttons = '<button>A</button><div id="trap"><button>B</button><button>C</button></div><button>D</button>'
  const trapped = await walkProof(browser, proofProbe(`${buttons}<script>document.getElementById('trap').addEventListener('keydown', event => {
    if (event.key !== 'Tab') return
    event.preventDefault()
    const [b, c] = document.querySelectorAll('#trap button')
    ;(document.activeElement === b ? c : b).focus()
  })</script>`))
  expect(trapped.forward.at(-1)?.outcome, 'the end sentinel is never reached').toBe('trap')
  expect(laneFindingLines(trapped)).toEqual(['2.1.2 forward trap: ↻ button "B" → button "C"', '2.1.2 reverse trap: ↻ button "B" → button "C"'])
  expect(proofVerdict(trapped).leave).toContain('react: 2.1.2 forward trap: ↻ button "B" → button "C"')
  const control = await walkProof(browser, proofProbe(buttons))
  expect(laneOrderLines(control)).toEqual(['- button "A"', '- button "B"', '- button "C"', '- button "D"', 'exited'])
  expect(laneFindingLines(control)).toEqual([])
  expect(proofVerdict(control)).toMatchObject({ leave: [], reverse: [], order: [], obscured: [], crossLane: [] })
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P2: an element that swallows Tab is stuck, and a Shift+Tab-only trap fails in reverse', async ({ browser }) => {
  const stuck = await walkProof(browser, proofProbe(`<button>A</button><button id="b">B</button><button>C</button>
    <script>document.getElementById('b').addEventListener('keydown', event => { if (event.key === 'Tab' && !event.shiftKey) event.preventDefault() })</script>`))
  expect(laneFindingLines(stuck)).toEqual(['2.1.2 forward stuck: button "B"'])
  expect(proofVerdict(stuck).leave[0]).toBe('react: 2.1.2 forward stuck: button "B"')
  const backwards = await walkProof(browser, proofProbe(`<button>A</button><div id="trap"><button>B</button><button>C</button></div><button>D</button>
    <script>document.getElementById('trap').addEventListener('keydown', event => {
      if (event.key !== 'Tab' || !event.shiftKey) return
      event.preventDefault()
      const [b, c] = document.querySelectorAll('#trap button')
      ;(document.activeElement === b ? c : b).focus()
    })</script>`))
  expect(laneOrderLines(backwards)).toEqual(['- button "A"', '- button "B"', '- button "C"', '- button "D"', 'exited'])
  expect(laneFindingLines(backwards)).toEqual(['2.1.2 reverse trap: ↻ button "B" → button "C"'])
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P3: an APG modal releases on Escape, and an Escape-suppressed one on its Cancel button', async ({ browser }) => {
  const escape = await walkProof(browser, proofProbe(`<button>Open</button>${aplModal('Settings', '<button>One</button><button>Two</button>')}`))
  expect(laneOrderLines(escape)).toEqual(['↻ - button "One"', '↻ - button "Two"', '↺ ok', 'released by Escape ×1', '- button "Open"', 'exited'])
  expect(laneFindingLines(escape)).toEqual([])
  const cancel = await walkProof(browser, proofProbe(`<button>Open</button>${aplModal('Delete record', '<input aria-label="Reason"><button id="cancel">Cancel</button>', { escape: false })}
    <script>document.getElementById('cancel').addEventListener('click', () => document.getElementById('modal').remove())</script>`))
  expect(laneOrderLines(cancel)).toEqual(['↻ - textbox "Reason"', '↻ - button "Cancel"', '↺ ok', 'released by Enter on button "Cancel"', '- button "Open"', 'exited'])
  expect(laneFindingLines(cancel)).toEqual([])
  expect(proofVerdict(cancel)).toMatchObject({ leave: [], reverse: [], order: [], obscured: [] })
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P4: a modal that ignores every key is not released, and a non-inert one leaks', async ({ browser }) => {
  // A modal whose only control keeps Tab is a cycle of one, released like any other.
  const single = await walkProof(browser, proofProbe(`<button>Open</button>${aplModal('Search', '<input aria-label="Type to search">')}`))
  expect(laneOrderLines(single)).toEqual(['↻ - textbox "Type to search"', '↺ ok', 'released by Escape ×1', '- button "Open"', 'exited'])
  expect(laneFindingLines(single)).toEqual([])
  const stuck = await walkProof(browser, proofProbe(`<button>Open</button>${aplModal('Stuck', '<button>Keep</button><button>Also keep</button>', { escape: false })}`))
  expect(laneFindingLines(stuck)).toEqual([
    '2.1.2 forward modal not released: dialog "Stuck" (Escape ×2, Enter on 2 buttons)',
    '2.1.2 reverse modal not released: dialog "Stuck" (Escape ×2, Enter on 2 buttons)',
  ])
  expect(laneOrderLines(stuck)).toEqual(['↻ - button "Keep"', '↻ - button "Also keep"', '↺ ok', 'not released'])
  const leaky = await walkProof(browser, proofProbe(`<button>Outside</button><div role="dialog" aria-modal="true" aria-label="Leaky"><button>Inside</button></div>`))
  expect(laneFindingLines(leaky)).toEqual([
    '2.4.3 forward modal-leak: button "Outside" reached while dialog "Leaky" is open',
    '2.4.3 reverse modal-leak: button "Outside" reached while dialog "Leaky" is open',
  ])
  expect(proofVerdict(leaky).order[0]).toBe('react: 2.4.3 forward modal-leak: button "Outside" reached while dialog "Leaky" is open')
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P5: focus returned to a menu trigger is not a trap, and focus lost to a removed element is a finding', async ({ browser }) => {
  const menu = await walkProof(browser, proofProbe(`<button id="trigger">Trigger</button><div role="menu" id="menu" aria-label="Actions"><button role="menuitem">One</button><button role="menuitem">Two</button></div><button>After</button>
    <script>const menu = document.getElementById('menu'); menu.addEventListener('keydown', event => {
      if (event.key === 'Tab' && !event.shiftKey && document.activeElement === menu.lastElementChild) { event.preventDefault(); menu.remove(); document.getElementById('trigger').focus() }
    })</script>`))
  expect(menu.forward[0].stops.find(stop => stop.returned), 'the return to the trigger is recorded').toBeDefined()
  expect(laneFindingLines(menu)).toEqual([])
  expect(laneOrderLines(menu)).toEqual(['- button "Trigger"', '- button "After"', 'exited'])
  const lost = await walkProof(browser, proofProbe(`<button>A</button><button id="x">Vanishes</button><button>C</button>
    <script>document.getElementById('x').addEventListener('focus', event => event.target.remove())</script>`))
  expect(laneFindingLines(lost)).toEqual(['2.4.3 forward focus-lost: after button "A"'])
  expect(proofVerdict(lost).order[0]).toBe('react: 2.4.3 forward focus-lost: after button "A"')
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P6: an opaque sticky header and a filled svg shape hide a stop; partial covers, scrims, stroke-only and pointer-transparent svg do not', async ({ browser }) => {
  const sticky = await walkProof(browser, `<style>body{margin:0}.bar{position:sticky;top:0;height:420px;background:#fff;z-index:3}.gap{height:900px}</style>${proofProbe('<header class="bar">Sticky</header><button>First</button><div class="gap"></div><button>Middle</button><div class="gap"></div><button>Last</button>')}`)
  expect(laneFindingLines(sticky)).toContain('2.4.11 reverse obscured: button "First" under header.bar (sticky)')
  expect(proofVerdict(sticky).obscured).toContain('react: 2.4.11 reverse obscured: button "First" under header.bar (sticky)')
  const half = await walkProof(browser, `<style>.cover{position:absolute;left:0;top:0;width:60px;height:30px;background:#000}</style>${proofProbe('<div style="position:relative"><button style="width:120px;height:30px">Half</button><div class="cover"></div></div>')}`)
  expect(laneFindingLines(half)).toEqual([])
  expect(laneCoverage(half).partial, 'a partly covered stop is counted, not failed (2.4.12 territory)').toBeGreaterThan(0)
  const scrim = await walkProof(browser, `<style>.scrim{position:fixed;inset:0;background:rgba(0,0,0,.4)}</style>${proofProbe('<button>Under scrim</button><div class="scrim"></div>')}`)
  expect(laneFindingLines(scrim)).toEqual([])
  const svgStyle = '<style>svg{position:absolute;left:0;top:0;width:400px;height:200px;z-index:2}</style>'
  const layers = await walkProof(browser, `${svgStyle}${proofProbe('<div style="position:relative"><button>Bar</button><svg style="pointer-events:none"><rect width="400" height="200" fill="#000"/></svg><svg><rect width="400" height="200" fill="none" stroke="#000"/></svg></div>')}`)
  expect(laneFindingLines(layers), 'a pointer-events:none layer (the Gantt dependency layer) and a stroke-only shape cover nothing').toEqual([])
  const filled = await walkProof(browser, `${svgStyle}${proofProbe('<div style="position:relative"><button>Bar</button><svg><rect width="400" height="200" fill="#000"/></svg></div>')}`)
  expect(laneFindingLines(filled), 'a filled, hit-testable svg shape is an opaque cover').toEqual(['2.4.11 forward obscured: button "Bar" under rect', '2.4.11 reverse obscured: button "Bar" under rect'])
  const away = await walkProof(browser, proofProbe('<a href="#x" style="display:inline-block;transform:translateX(-200vw)">Away</a><button>Here</button>'))
  expect(laneFindingLines(away)).toEqual(['2.4.11 forward offscreen: link "Away"', '2.4.11 reverse offscreen: link "Away"'])
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P7: a visually hidden input is judged by its label, and only a user-opened popup that Escape dismisses with focus kept may cover a stop', async ({ browser }) => {
  const proxy = await walkProof(browser, `<style>.sr{position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)}</style>${proofProbe('<label><input type="checkbox" class="sr"> Notify me</label>')}`)
  expect(laneFindingLines(proxy)).toEqual([])
  expect(laneCoverage(proxy).unmeasured).toBe(0)
  const popover = '<style>#pop{position:fixed;inset:auto;left:0;top:0;width:300px;height:120px;margin:0;background:#fff;border:0}</style>'
  const opened = (mode: string) => walkProof(browser, popover + proofProbe(`<button style="position:fixed;left:10px;top:10px">Under</button><div popover="${mode}" id="pop">Opened by the user</div><button style="position:fixed;left:10px;top:300px">Elsewhere</button>
    <script>document.getElementById('pop').showPopover()</script>`))
  // An auto popover closes on Escape without moving focus: the user can reveal the stop in place
  // (SC 2.4.11 Note 2), so it is counted, not failed, and the walk goes on without the popover.
  const dismissable = await opened('auto')
  expect(laneFindingLines(dismissable)).toEqual([])
  expect(laneCoverage(dismissable).exemptUserOpened, 'verified with Escape, then counted').toBe(1)
  // A manual popover ignores Escape: the user would have to move focus back into it to dismiss it,
  // which Note 2 does not exempt.
  const manual = await opened('manual')
  expect(laneFindingLines(manual)).toEqual(['2.4.11 forward obscured: button "Under" under div (fixed)', '2.4.11 reverse obscured: button "Under" under div (fixed)'])
  expect(laneCoverage(manual).exemptUserOpened).toBe(0)
  expect(proofVerdict(manual).obscured[0]).toBe('react: 2.4.11 forward obscured: button "Under" under div (fixed)')
  // A document-level Escape that closes the popup and hands focus back to its trigger (a Radix-style
  // dismissable layer): the stop is not revealed in place, so the cover stands.
  const handsBack = await walkProof(browser, proofProbe(`<button id="trigger" style="position:fixed;left:10px;top:400px">Options</button>
    <div role="menu" id="pop" style="position:fixed;left:0;top:0;width:300px;height:120px;background:#fff;z-index:10">Menu</div>
    <button style="position:fixed;left:10px;top:10px">Under</button><button style="position:fixed;left:10px;top:300px">Elsewhere</button>
    <script>document.addEventListener('keydown', event => { const pop = document.getElementById('pop'); if (event.key === 'Escape' && pop.style.display !== 'none') { pop.style.display = 'none'; document.getElementById('trigger').focus() } })</script>`))
  expect(laneFindingLines(handsBack)).toEqual(['2.4.11 forward obscured: button "Under" under div (fixed)', '2.4.11 reverse obscured: button "Under" under div (fixed)'])
  // A popup Escape dismisses in place is still walked open in both directions, so a defect inside it
  // is found, and the cover it put over "Under" is exempt.
  const inside = await walkProof(browser, popover + proofProbe(`<button style="position:fixed;left:10px;top:10px">Under</button><button style="position:fixed;left:10px;top:300px">Elsewhere</button>
    <div popover="auto" id="pop">Opened <button style="transform:translateX(-200vw)">Lost item</button></div>
    <script>document.getElementById('pop').showPopover()</script>`))
  expect(laneFindingLines(inside)).toEqual(['2.4.11 forward offscreen: button "Lost item"', '2.4.11 reverse offscreen: button "Lost item"'])
  expect(laneCoverage(inside).exemptUserOpened).toBe(1)
  // A cover only Shift+Tab meets: both directions walk the popup open, so the reverse still retraces
  // the forward order, and the reveal is verified afterwards.
  const reverseOnly = await walkProof(browser, `<style>body{margin:0}#pop{position:fixed;inset:auto;left:0;top:0;width:300px;height:420px;margin:0;background:#fff;border:0}.gap{height:900px}.right{margin-left:400px}</style>`
    + proofProbe(`<div style="height:440px"></div><button>First</button><div class="gap"></div><button class="right">Middle</button><div class="gap"></div><button class="right">Last</button>
    <div popover="auto" id="pop">Opened <button>In pop</button></div><script>document.getElementById('pop').showPopover()</script>`))
  expect(laneFindingLines(reverseOnly)).toEqual([])
  expect(laneCoverage(reverseOnly).exemptUserOpened).toBe(1)
  const late = await walkProof(browser, popover + proofProbe(`<button id="info" style="position:fixed;left:10px;top:300px">Info</button><button style="position:fixed;left:10px;top:10px">Under</button><div popover="manual" id="pop">Opened by focus</div>
    <script>document.getElementById('info').addEventListener('focus', () => document.getElementById('pop').showPopover())</script>`))
  expect(laneFindingLines(late)).toEqual(['2.4.11 forward obscured: button "Under" under div (fixed)', '2.4.11 reverse obscured: button "Under" under div (fixed)'])
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P8: a radio group, a roving tablist, a text-only scene and date inputs walk cleanly', async ({ browser }) => {
  const composite = await walkProof(browser, proofProbe('<input type="radio" name="g" aria-label="North"><input type="radio" name="g" aria-label="South"><div role="tablist" aria-label="Views"><button role="tab" aria-selected="true">Map</button><button role="tab" tabindex="-1">List</button></div><input type="date" aria-label="When">'))
  // The unchecked group is entered at North going forward and South going back; the reverse passes.
  expect(laneOrderLines(composite).filter(line => !line.startsWith('- textbox "When"'))).toEqual(['- radio "North"', '- tab "Map"', 'exited'])
  expect(laneOrderLines(composite).find(line => line.startsWith('- textbox "When"')), 'date segments collapse into one stop').toMatch(/^- textbox "When"( ×\d)?$/)
  expect(laneFindingLines(composite)).toEqual([])
  // Four datetime-local fields take 25 presses (six segments each, then out), more than the 2 × 4 + 16
  // a plain census would allow: the cap counts each segmented input as the nine presses it may take.
  const dates = await walkProof(browser, proofProbe(['One', 'Two', 'Three', 'Four'].map(name => `<input type="datetime-local" aria-label="${name}">`).join('')))
  expect(dates.forward.at(-1)?.outcome).toBe('exited')
  expect(laneFindingLines(dates)).toEqual([])
  const words = await walkProof(browser, proofProbe('<p>Only words here.</p>'))
  expect(laneOrderLines(words)).toEqual(['exited'])
  expect(laneCoverage(words).zeroStop).toBe(true)
  expect(laneFindingLines(words)).toEqual([])
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P9: a positive tabindex is a 2.4.3 finding, a stop outside the scenario fails closed, and so does an iframe left within one batch', async ({ browser }) => {
  const positive = await walkProof(browser, proofProbe('<button>A</button><button tabindex="2">Jumps the queue</button>'))
  expect(laneFindingLines(positive)).toEqual(['2.4.3 walk positive-tabindex: button "Jumps the queue" tabindex=2'])
  expect(proofVerdict(positive).order[0]).toBe('react: 2.4.3 walk positive-tabindex: button "Jumps the queue" tabindex=2')
  const host = await walkProof(browser, `<button>Host chrome</button>${proofProbe('<button>Inside</button>')}`)
  expect(laneHostStops(host)).toEqual(['button "Host chrome" <button>'])
  expect(proofVerdict(host).hostStops).toEqual(['react: button "Host chrome" <button>', 'blazor: button "Host chrome" <button>'])
  // An overlay a component portals to the body is the scenario's, not the host's.
  const portal = await walkProof(browser, `${proofProbe('<button>Inside</button>')}<div class="hl-popover"><button>Portaled</button></div>`)
  expect(laneHostStops(portal)).toEqual([])
  // The frame's own document gets the Tab presses inside it, so the walker never sees them; entering
  // it fails closed even when the whole stay falls inside one batch of presses.
  const framed = await walkProof(browser, proofProbe('<button>Before</button><iframe title="Embedded" srcdoc="<button>In one</button><button>In two</button>"></iframe><button>After</button>'))
  expect(laneFindingLines(framed)).toContain('2.1.2 forward unsupported frame')
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P10: a different order across lanes fails with its diff, a name-only difference T-943 waived is attributed, and keys are T-943 lines', async ({ browser }) => {
  const react = await walkProof(browser, proofProbe('<button>A</button><button>B</button><button>C</button>'), 'react')
  const blazor = await walkProof(browser, proofProbe('<button>A</button><button>C</button><button>B</button>'), 'blazor')
  const crossed = proofVerdict(react, blazor)
  expect(crossed.crossLane[0]).toBe('proof tab walk order differs between lanes (- react only, + blazor only)')
  expect(crossed.crossLane).toContain('- - button "B"')
  expect(crossed.crossLane).toContain('+ - button "B"')
  const save = await walkProof(browser, proofProbe('<button>A</button><button>Save</button>'), 'react')
  const store = await walkProof(browser, proofProbe('<button>A</button><button>Store</button>'), 'blazor')
  const ariaDiff = { reactOnly: ['- button "Save"'], blazorOnly: ['- button "Store"'], unified: [] }
  expect(proofVerdict(save, store, { ariaDiff })).toMatchObject({ attributed: true, crossLane: [] })
  expect(proofVerdict(save, store).crossLane[0]).toBe('proof tab walk order differs between lanes (- react only, + blazor only)')
  // Every walker key is the reduced spelling of the corresponding line of the probe's own snapshot.
  const context = await proofContext(browser)
  const page = await context.newPage()
  await page.setContent(`<!doctype html><html lang="en"><body>${proofProbe(`<h2>Form</h2><button aria-pressed="true">Bold</button><a href="#docs">Docs</a>
    <input aria-label="Name" value="Ada"><input type="checkbox" aria-label="Notify" checked><select aria-label="Pick"><option>One</option></select>
    <div role="tablist" aria-label="Views"><button role="tab" aria-selected="true">Map</button></div><article tabindex="0" aria-label="Note">Text</article>
    <button aria-expanded="false">Menu "quoted"</button>`)}</body></html>`)
  const snapshot = normaliseAriaSnapshot(await page.locator('[data-gallery-probe]').ariaSnapshot()).map(stopKeyFromSnapshotLine)
  await prepareLane(page, scenarioViewport)
  const walk = await walkLane(page, { lane: 'react', deadline: Date.now() + 20_000 })
  await context.close()
  const keys = laneOrderLines(walk).filter(line => line.startsWith('- ')).map(line => line.slice(2))
  expect(keys).toEqual(['button "Bold"', 'link "Docs"', 'textbox "Name"', 'checkbox "Notify"', 'combobox "Pick"', 'tab "Map"', 'article "Note"', 'button "Menu \\"quoted\\""'])
  let cursor = 0
  for (const key of keys) {
    cursor = snapshot.indexOf(key, cursor)
    expect(cursor, `${key} is a line of the probe snapshot, in order`).toBeGreaterThanOrEqual(0)
  }
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P11: a modal the prepared walk releases is judged like any modal: an obscured stop and a leak fail', async ({ browser }) => {
  // Prepared focus on the modal's second button: E walks the modal and releases it, and F and R walk
  // a page without it, so only E can see the cover over "Two".
  const covered = await walkProof(browser, proofProbe(`${aplModal('Edit', '<button>One</button><span style="position:relative;display:inline-block"><button id="two">Two</button><span class="cover" style="position:absolute;inset:0;background:#000"></span></span><button>Three</button>')}
    <script>document.getElementById('two').focus()</script>`))
  expect(covered.prepared?.release?.released).toBe(true)
  expect(laneFindingLines(covered)).toEqual(['2.4.11 prepared obscured: button "Two" under span.cover'])
  expect(proofVerdict(covered).obscured[0]).toBe('react: 2.4.11 prepared obscured: button "Two" under span.cover')
  // The same cover as a user-opened tooltip inside the modal: Escape would close the modal, so the
  // walk never presses it there, the modal is walked and released as before, and the cover stands.
  const tooltip = await walkProof(browser, proofProbe(`${aplModal('Edit', '<button id="one">One</button><span style="position:relative;display:inline-block"><button id="two">Two</button><span role="tooltip" class="cover" style="position:absolute;inset:0;background:#000">Tip</span></span><button>Three</button>')}
    <script>document.getElementById('one').focus()</script>`))
  expect(tooltip.prepared?.release?.released).toBe(true)
  expect(laneFindingLines(tooltip)).toEqual(['2.4.11 prepared obscured: button "Two" under span.cover'])
  expect(laneCoverage(tooltip)).toMatchObject({ exemptUserOpened: 0, coveredUserOpened: 1 })
  // Prepared focus outside an open modal is a leak, and E runs even though that focus is the first
  // stop from the top: F's anchor could not reach the same state.
  const leaking = await walkProof(browser, proofProbe(`<button id="behind">Behind</button>${aplModal('Edit', '<button>One</button><button>Two</button>')}
    <script>document.getElementById('behind').focus()</script>`))
  expect(leaking.preparedAtFirstStop).toBe(false)
  expect(laneFindingLines(leaking)).toEqual(['2.4.3 prepared modal-leak: button "Behind" reached while dialog "Edit" is open'])
  expect(proofVerdict(leaking).order[0]).toBe('react: 2.4.3 prepared modal-leak: button "Behind" reached while dialog "Edit" is open')
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P12: a control that swallows Tab inside a modal with other stops is stuck, and a partial modal cycle is a trap', async ({ browser }) => {
  const modal = (body: string, handler: string) => `<div role="dialog" aria-modal="true" aria-label="Edit note" id="modal">${body}</div>
    <script>const modal = document.getElementById('modal'); document.addEventListener('keydown', event => {
      if (!modal.isConnected) return
      if (event.key === 'Escape') { modal.remove(); return }
      if (event.key !== 'Tab') return
      const items = [...modal.querySelectorAll('textarea, button')]; const first = items[0], last = items.at(-1)
      ${handler}
      if (event.shiftKey && (document.activeElement === first || !modal.contains(document.activeElement))) { event.preventDefault(); last.focus() }
      else if (!event.shiftKey && (document.activeElement === last || !modal.contains(document.activeElement))) { event.preventDefault(); first.focus() }
    }, true)</script>`
  // The Blazor Chat pattern inside a dialog: the textarea cancels every key, so Save and Cancel are
  // never reached by Tab. It is not a "cycle of one": the modal has three tab stops.
  const swallowed = await walkProof(browser, proofProbe(modal('<textarea id="note" aria-label="Note"></textarea><button>Save</button><button>Cancel</button>', '')
    + `<script>document.getElementById('note').addEventListener('keydown', event => { if (event.key !== 'Escape') event.preventDefault() })</script>`))
  expect(laneFindingLines(swallowed)).toContain('2.1.2 forward stuck: textbox "Note"')
  // Tab on "Two" wraps back to "One": a cycle inside the modal that never reaches "Three".
  const partial = await walkProof(browser, proofProbe(modal('<button>One</button><button>Two</button><button>Three</button>',
    `if (!event.shiftKey && document.activeElement === items[1]) { event.preventDefault(); first.focus(); return }`)))
  expect(laneFindingLines(partial)).toContain('2.1.2 forward trap: ↻ button "One" → button "Two"; never reaches button "Three"')
  expect(proofVerdict(partial).leave).toContain('react: 2.1.2 forward trap: ↻ button "One" → button "Two"; never reaches button "Three"')
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P13: the prepared walk keeps an outcome reached past its first presses, and a batched candidate counts only if it reproduces in step mode', async ({ browser }) => {
  // A trap that exists only while the prepared group holds focus, reached after 36 stops that each
  // change the page when focused. F's anchor takes focus out of the group, which disarms the trap,
  // so only E can report it, and E is walked in step mode all the way.
  const items = Array.from({ length: 36 }, (_, index) => `<button>Item ${index + 1}</button>`).join('')
  const prepared = await walkProof(browser, proofProbe(`<button>Top</button><div id="group"><button id="start">Start</button>${items}<button id="b">B</button><button id="c">C</button></div><button>After</button>
    <script>const group = document.getElementById('group'); let armed = true
      group.addEventListener('focusin', event => event.target.classList.toggle('seen'))
      group.addEventListener('focusout', event => { if (!group.contains(event.relatedTarget)) armed = false })
      for (const id of ['b', 'c']) document.getElementById(id).addEventListener('keydown', event => {
        if (!armed || event.key !== 'Tab' || event.shiftKey) return
        event.preventDefault(); document.getElementById(id === 'b' ? 'c' : 'b').focus()
      })
      document.getElementById('start').focus()</script>`))
  expect(prepared.prepared?.stepPresses, 'E is stepped throughout').toBe(prepared.prepared?.presses)
  expect(laneFindingLines(prepared)).toEqual(['2.1.2 prepared trap: ↻ button "B" → button "C"'])
  // A cover that shows only the first time "Shy" is focused, through an attribute the walker does
  // not watch: the batched walk sees it, the step-mode walk through that stretch does not, so it is
  // dropped rather than reported.
  const transient = await walkProof(browser, `<style>.cover{display:none;position:absolute;inset:0;background:#000}[data-cover="on"] .cover{display:block}</style>${proofProbe(`<button>A</button><span style="position:relative;display:inline-block"><button id="shy">Shy</button><span class="cover"></span></span><button>C</button>
    <script>const shy = document.getElementById('shy'); let shown = false
      shy.addEventListener('focus', () => { if (!shown) { shown = true; document.body.dataset.cover = 'on' } })
      shy.addEventListener('blur', () => { document.body.dataset.cover = 'off' })</script>`)}`)
  expect(transient.forward[0].unconfirmed, 'the batched candidate made the first walk unconfirmed').toBe(true)
  expect(transient.forward[0].stops.some(stop => stop.verdict?.kind === 'obscured' && !stop.trusted)).toBe(true)
  expect(laneFindingLines(transient)).toEqual([])
  // A prepared open menu whose Tab handler removes it drops focus to the body: only E sees it, and E's
  // stops are judged whether or not it released a modal.
  const dropped = await walkProof(browser, proofProbe(`<button id="trigger" aria-haspopup="menu">Menu</button>
    <div role="menu" id="menu" aria-label="Actions"><button role="menuitem" id="m1">One</button><button role="menuitem" tabindex="-1">Two</button></div><button>After</button>
    <script>const menu = document.getElementById('menu'); menu.addEventListener('keydown', event => { if (event.key === 'Tab') { event.preventDefault(); menu.remove() } }); document.getElementById('m1').focus()</script>`))
  expect(laneFindingLines(dropped)).toEqual(['2.4.3 prepared focus-lost: after menuitem "One"'])
  // A prepared panel whose Escape closes it and moves focus to its trigger: the cover over "Under"
  // stands in every direction, and E reports no trap (no Escape is pressed while a walk runs).
  const panel = (mode: 'refocus' | 'keep') => walkProof(browser, proofProbe(`<button id="trigger" style="position:fixed;left:10px;top:400px">Threads</button>
    <div role="dialog" aria-label="Threads" id="pop" style="position:fixed;left:0;top:0;width:300px;height:120px;background:#fff;z-index:10"><button id="inner" style="margin-left:200px">Close</button></div>
    <button style="position:fixed;left:10px;top:10px">Under</button><button style="position:fixed;left:10px;top:300px">Elsewhere</button>
    <script>document.getElementById('inner').focus(); document.addEventListener('keydown', event => { const pop = document.getElementById('pop')
      if (event.key !== 'Escape' || pop.hidden) return; pop.hidden = true; if ('${mode}' === 'refocus') document.getElementById('trigger').focus() })</script>`))
  expect(laneFindingLines(await panel('refocus'))).toEqual([
    '2.4.11 forward obscured: button "Under" under div (fixed)', '2.4.11 prepared obscured: button "Under" under div (fixed)', '2.4.11 reverse obscured: button "Under" under div (fixed)',
  ])
  const kept = await panel('keep')
  expect(laneFindingLines(kept)).toEqual([])
  expect(laneCoverage(kept).exemptUserOpened).toBe(1)
  // An editing control at the first stop is walked from its prepared state: F's anchor would commit it.
  const editor = await walkProof(browser, proofProbe(`<input id="name" aria-label="Name" value="Ada"><button>After</button><script>document.getElementById('name').focus()</script>`))
  expect(editor.preparedAtFirstStop).toBe(false)
  expect(editor.prepared?.outcome).toBe('exited')
  // The prepared focus is judged where focusing it put it, not where prepareLane's scroll reset left it.
  const below = await walkProof(browser, proofProbe(`<button>Top</button><div style="height:1600px"></div><button id="below">Below</button><script>document.getElementById('below').focus()</script>`))
  expect(below.prepared?.stops[0]?.verdict?.kind).toBe('visible')
  expect(laneFindingLines(below)).toEqual([])
  recordCheck('tabWalkDetectorProofs')
})

test('tab walk proof P14: a press budget stops the walk early but judges what it walked: a trap begun or a cover met inside the budget fails', async ({ browser }) => {
  const tail = Array.from({ length: 30 }, (_, index) => `<button>Item ${index + 1}</button>`).join('')
  // A six-stop cycle after "A": its first stop comes round again on press 9, inside a 12-press budget,
  // and the second traversal that confirms it ends on press 14. The budget never cuts it short.
  const names = ['One', 'Two', 'Three', 'Four', 'Five', 'Six']
  const trapped = await walkProof(browser, proofProbe(`<button>A</button><div id="trap">${names.map(name => `<button>${name}</button>`).join('')}</div>${tail}
    <script>const trap = document.getElementById('trap'); trap.addEventListener('keydown', event => {
      const items = [...trap.querySelectorAll('button')]
      if (event.key === 'Tab' && !event.shiftKey && document.activeElement === items.at(-1)) { event.preventDefault(); items[0].focus() }
    })</script>`), 'react', { pressBudget: 12 })
  const cycle = `2.1.2 forward trap: ↻ ${names.map(name => `button "${name}"`).join(' → ')}`
  expect(laneFindingLines(trapped)).toEqual([cycle])
  expect(proofVerdict(trapped).leave[0]).toBe(`react: ${cycle}`)
  const covered = await walkProof(browser, proofProbe(`<button>A</button><span style="position:relative;display:inline-block"><button>Hidden</button><span class="cover" style="position:absolute;inset:0;background:#000"></span></span>${tail}`), 'react', { pressBudget: 12 })
  expect(laneCoverage(covered).budgeted).toBe(true)
  expect(laneFindingLines(covered)).toEqual(['2.4.11 forward obscured: button "Hidden" under span.cover'])
  const lines = laneOrderLines(covered)
  expect([lines[0], lines[1], lines.at(-1)], 'the budgeted prefix is compared stop for stop').toEqual(['- button "A"', '- button "Hidden"', 'budget: 12 presses'])
  recordCheck('tabWalkDetectorProofs')
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
    const testStarted = performance.now()
    test.skip(ciGallery && ciWallClockExempt.has(scenario.id),
      'wall-clock budget scenario: a shared CI runner measures the runner, not the component')
    const colorScheme = scenario.id.endsWith('.theme-dark') ? 'dark' : 'light'
    // The clock zone and the browser language come from the scenario, never from the host. Both lanes
    // render instants in the browser's zone (React through Intl with no timeZone, Blazor WebAssembly
    // through TimeZoneInfo.Local, which it takes from the browser), so the zone is pinned to UTC (T-943
    // review 1, finding 1). The language is the locale the scenario's catalog declares
    // (content.environment.locale): React formats with it wherever a story names no locale, and Blazor
    // WebAssembly takes its default culture from it (T-943 review 2, finding 4). That covers the scene,
    // not a component that carries its own locale inside it (schema-form.locale-theme's Arabic form in
    // an en-US scene); so that such a component formats from real locale data too, the Blazor gallery
    // loads all ICU data (BlazorWebAssemblyLoadAllGlobalizationData) instead of the shard the browser
    // language would pick (T-943 review 3, finding 1).
    const locale = scenario.content?.environment?.locale
    if (!locale) throw new Error(`${scenario.id} declares no content.environment.locale in its gallery catalog`)
    const scenarioContext = { viewport: scenarioViewport, colorScheme, timezoneId: 'UTC', locale } as const
    const [reactContext, blazorContext] = await Promise.all([
      browser.newContext(scenarioContext),
      browser.newContext(scenarioContext),
    ])
    const [reactPage, blazorPage] = await Promise.all([reactContext.newPage(), blazorContext.newPage()])
    const frozenClock = ariaFrozenClockScenes.has(scenario.id)
    // Every tree difference T-943 compares in this scenario (the prepared scene, and any later state it
    // reads), which the tab walk reads to attribute order differences.
    const ariaDiffs: LaneDiff[] = []
    if (frozenClock) await Promise.all([reactPage.clock.install(), blazorPage.clock.install()])
    await Promise.all([
      reactPage.emulateMedia({ reducedMotion: 'reduce' }),
      blazorPage.emulateMedia({ reducedMotion: 'reduce' }),
    ])
    const [react, blazor] = await Promise.all([reactIndex(reactPage), blazorIndex(blazorPage)])
    await Promise.all([
      openReactStory(reactPage, storyId(react, catalog.galleryInterface.projectionTitles.react, scenario.name)),
      openBlazorStory(blazorPage, storyId(blazor, catalog.galleryInterface.projectionTitles.blazor, scenario.name)),
    ])
    if (frozenClock) await pauseClocks([reactPage, blazorPage])
    await Promise.all([prepareScenario(reactPage, scenario), prepareScenario(blazorPage, scenario)])
    if (frozenClock) {
      // Compared as prepared, on the paused clock (ariaFrozenClockScenes); the scans need running time.
      ariaDiffs.push(await assertAriaParity(reactPage, blazorPage, scenario.id))
      await Promise.all([reactPage.clock.resume(), blazorPage.clock.resume()])
    }
    if (scenario.id === 'data-exchange.authoring' || scenario.id === 'data-exchange.stale-review') {
      for (const page of [reactPage, blazorPage]) {
        await expect(page.getByRole('textbox', { name: 'Reference dataset', exact: true })).toHaveValue('dataset.customers')
        await expect(page.getByRole('textbox', { name: 'Pack distribution', exact: true })).toHaveValue('pack://customers')
        await expect(page.getByRole('textbox', { name: 'Feed distribution', exact: true })).toHaveValue('feed://customers')
        await expect(page.getByRole('combobox', { name: 'Format', exact: true }).locator('option')).toHaveText(['Choose a format', 'CSV'])
      }
    }
    await captureGalleryReviewPair(reactPage, blazorPage, catalog, scenario)
    await recordReactBaseline(reactPage, catalog, scenario)
    await assertAccessible(reactPage, scenario, 'react')
    await assertAccessible(blazorPage, scenario, 'blazor')
    // Every scenario, not only those declaring visual parity: the tree is what the lanes must agree
    // on whatever they look like. Before reflow, which mutates the viewport and root font size.
    if (!frozenClock) ariaDiffs.push(await assertAriaParity(reactPage, blazorPage, scenario.id))
    for (const { state } of ariaLaterStates.get(scenario.id) ?? []) ariaDiffs.push(await assertAriaParity(reactPage, blazorPage, scenario.id, state))
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
    if (scenario.id === 'rule-authoring.from-empty') {
      await assertRuleAuthoringKeyboard(reactPage)
      await assertRuleAuthoringKeyboard(blazorPage)
    }
    if (scenario.id === 'select-field.multiple-search') {
      await assertSearchableMultipleScroll(reactPage, 'react')
      await assertSearchableMultipleScroll(blazorPage, 'blazor')
    }
    if (scenario.id === 'schema-form.controls-structure') {
      for (const page of [reactPage, blazorPage]) {
        const services = page.getByRole('button', { name: 'Services', exact: true })
        await services.click()
        const choices = page.getByRole('listbox', { name: 'Services', exact: true })
        await expect(choices).toHaveAttribute('aria-multiselectable', 'true')
        await choices.press('End')
        await choices.press('Space')
        await expect(services).toContainText('Towage')
        await expect(choices).toBeVisible()
        await choices.press('Escape')
        await expect(services).toBeFocused()
      }
    }
    if (scenario.id === 'schema-form.domain-editors') {
      await assertSchemaFormChoicePopup(reactPage, 'react', testInfo)
      await assertSchemaFormChoicePopup(blazorPage, 'blazor', testInfo)
      await assertSchemaFormPickerScroll(reactPage, 'react')
      await assertSchemaFormPickerScroll(blazorPage, 'blazor')
      for (const page of [reactPage, blazorPage]) {
        const record = page.getByRole('combobox', { name: 'Record', exact: true })
        await record.fill('outside')
        await expect(page.getByRole('option')).toHaveCount(0)
        await record.press('Enter')
        await expect(page.getByText('Saved selected candidate.', { exact: true })).toHaveCount(0)
        await record.fill('3999')
        expect(await page.getByRole('option').count()).toBeLessThanOrEqual(25)
        await record.press('ArrowDown')
        await record.press('Enter')
        await page.getByRole('button', { name: 'Save request', exact: true }).click()
        await expect(page.getByText('Saved selected candidate.', { exact: true })).toBeVisible()
      }
    }
    // Last: the walk changes state by its nature, so it judges the scene every check above left.
    await assertTabWalk(reactPage, blazorPage, scenario, testInfo, { testStarted, ariaDiffs })
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

    for (const theme of ['light', 'dark']) {
      await page.locator('[data-gallery-probe]').evaluate((element, value) => element.setAttribute('data-theme', value), theme)
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
      test.info().annotations.push({type: 'app-shell-scrollbar', description: JSON.stringify({
        projection, theme, thumb: paintedThumb, track: paintedSurface, contrast: contrastRatio(paintedThumb, paintedSurface),
      })})
      expect(contrastRatio(paintedThumb, paintedSurface), `${projection} ${theme} scrollbar thumb`).toBeGreaterThanOrEqual(3)
    }

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

    // T-943 review 2: every close returns focus to the element that had it when the menu opened, by
    // keyboard or by pointer (APG menu pattern), and an outside press on nothing focusable is a close
    // too: in a real browser the press's own default action runs after the dismissal and would
    // otherwise leave focus on <body>. jsdom and bUnit cannot see that ordering; this does.
    const outsidePress = () => page.mouse.click(8, 712)
    await trigger.focus()
    await trigger.dispatchEvent('keydown', {key: 'F10', code: 'F10', shiftKey: true, bubbles: true})
    await expect(page.locator('[role="menuitem"][data-active="true"]')).toBeFocused()
    await outsidePress()
    await expect(page.getByRole('menu')).toHaveCount(0)
    await expect(trigger, `${projection} keyboard-opened menu, outside press`).toBeFocused()
    for (const close of ['Escape', 'selection', 'outside press'] as const) {
      await trigger.click({button: 'right', position: {x: 36, y: 36}})
      await expect(page.locator('[role="menuitem"][data-active="true"]'), `${projection} pointer open focuses the menu`).toBeFocused()
      if (close === 'Escape') await page.keyboard.press('Escape')
      else if (close === 'selection') await page.getByRole('menuitem', {name: 'Copy inspection'}).click()
      else await outsidePress()
      await expect(page.getByRole('menu')).toHaveCount(0)
      await expect(trigger, `${projection} pointer-opened menu, ${close}`).toBeFocused()
    }

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
