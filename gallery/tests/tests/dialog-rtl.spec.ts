import { expect, test, type Locator, type Page } from '@playwright/test'

import { openWithSubresourceRetry, readWithReload } from './navigation-resilience.ts'

// T-1122: the Dialog surface must be centred, with its close and action buttons inside the
// viewport when focused, in left-to-right and right-to-left content, in both lanes, during the
// opening animation as well as after it, and with reduced motion. ConfirmDialog composes the
// Dialog surface, so its Arabic story is checked too.

type Projection = 'react' | 'blazor'
type StoryEntry = { id: string; title: string; name: string; type: 'story' | 'docs' }
type StoryIndex = { entries: Record<string, StoryEntry> }
type Box = { left: number; top: number; right: number; bottom: number }

const bases = {
  react: process.env.REACT_GALLERY_URL ?? 'http://127.0.0.1:6106',
  blazor: process.env.BLAZOR_GALLERY_URL ?? 'http://127.0.0.1:6107',
}

const stories = [
  { scenario: 'dialog.locale-ar', title: 'Platform/Dialog', name: 'Arabic right-to-left modal', direction: 'rtl' },
  { scenario: 'dialog.lifecycle-content', title: 'Platform/Dialog', name: 'Controlled lifecycle and content', direction: 'ltr' },
  { scenario: 'confirm-dialog.reflow-rtl', title: 'UI/ConfirmDialog', name: 'Long content reflow and RTL mirroring', direction: 'rtl' },
  { scenario: 'confirm-dialog.action-pair', title: 'UI/ConfirmDialog', name: 'Action pair and confirm ordering', direction: 'ltr' },
] as const

async function readIndex(page: Page, projection: Projection): Promise<StoryIndex> {
  const base = bases[projection]
  if (projection === 'react') {
    let index: StoryIndex | undefined
    await expect(async () => {
      const response = await page.request.get(`${base}/index.json`)
      expect(response.ok()).toBe(true)
      index = await response.json() as StoryIndex
    }).toPass({ intervals: [250, 500, 1_000], timeout: 5_000 })
    return index!
  }
  return readWithReload(page, base, 'BlazingStory.getStoryIndex()', async () => {
    await page.waitForFunction(() => {
      const host = globalThis as typeof globalThis & { BlazingStory?: { getStoryIndex?: unknown } }
      return typeof host.BlazingStory?.getStoryIndex === 'function'
    })
    // Reissue only the existing gallery's known page-promise collection failure.
    for (let attempt = 0; ; attempt += 1) {
      try {
        return await page.evaluate(() => {
          const host = globalThis as typeof globalThis & { BlazingStory: { getStoryIndex(): Promise<StoryIndex> } }
          return host.BlazingStory.getStoryIndex()
        })
      }
      catch (error) {
        if (attempt === 2 || !String(error).includes('Resulting promise was garbage collected')) throw error
      }
    }
  }, 15_000)
}

async function openStory(page: Page, projection: Projection, story: typeof stories[number]) {
  const base = bases[projection]
  const index = await readIndex(page, projection)
  const entries = Object.values(index.entries)
    .filter(entry => entry.type === 'story' && entry.title === story.title && entry.name === story.name)
  expect(entries, `the actual ${story.title} "${story.name}" story`).toHaveLength(1)
  const probe = page.locator(`[data-gallery-probe][data-gallery-scenario="${story.scenario}"]`)
  const surface = page.locator('.hl-dialog__surface')
  await openWithSubresourceRetry(
    page,
    `${base}/iframe.html?id=${encodeURIComponent(entries[0]!.id)}&viewMode=story`,
    base,
    timeout => expect(surface).toBeVisible({ timeout }),
    40_000,
  )
  await expect(probe).toBeAttached()
  await expect(surface).toHaveCount(1)
  await expect(surface).toHaveAttribute('dir', story.direction)
  return surface
}

// Restart the surface's own CSS opening animation and sample it paused at its start, middle and
// end, so the check covers the frames a user sees while the dialog opens. Returns null when no
// animation runs (reduced motion).
async function sampleOpening(surface: Locator) {
  return surface.evaluate(element => {
    const box = (target: Element) => {
      const rect = target.getBoundingClientRect()
      return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }
    }
    const surfaceElement = element as HTMLElement
    surfaceElement.style.setProperty('animation', 'none')
    void surfaceElement.offsetWidth
    surfaceElement.style.removeProperty('animation')
    const animation = surfaceElement.getAnimations()
      .find(candidate => (candidate as CSSAnimation).animationName === 'hl-dialog-enter')
    if (!animation) return null
    animation.pause()
    const duration = Number(animation.effect?.getComputedTiming().duration ?? 0)
    const frames = [0, duration / 2, Math.max(0, duration - 1)].map(time => {
      animation.currentTime = time
      return {
        time,
        surface: box(surfaceElement),
        buttons: [...surfaceElement.querySelectorAll('button')].map(button => ({
          name: button.getAttribute('aria-label') ?? button.textContent?.trim() ?? '',
          box: box(button),
        })),
      }
    })
    animation.finish()
    return { duration, frames }
  })
}

function expectInside(box: Box, viewport: { width: number; height: number }, label: string) {
  expect.soft(box.left, `${label}: left edge`).toBeGreaterThanOrEqual(0)
  expect.soft(box.top, `${label}: top edge`).toBeGreaterThanOrEqual(0)
  expect.soft(box.right, `${label}: right edge`).toBeLessThanOrEqual(viewport.width)
  expect.soft(box.bottom, `${label}: bottom edge`).toBeLessThanOrEqual(viewport.height)
}

async function viewportOf(page: Page) {
  return page.evaluate(() => ({ width: document.documentElement.clientWidth, height: document.documentElement.clientHeight }))
}

// Tab through the open dialog. Every element that takes focus inside the surface must be inside
// the viewport, focus must stay inside the modal, and the page must not scroll.
async function expectFocusWalkVisible(page: Page, surface: Locator, label: string, browserName: string) {
  // The config defines no browser projects, so the gate runs Chromium; WebKit and Firefox run when
  // the spec is given --browser. WebKit follows Safari's default, where Tab skips buttons and
  // Option+Tab reaches every control.
  const tab = browserName === 'webkit' ? 'Alt+Tab' : 'Tab'
  const viewport = await viewportOf(page)
  const before = await page.evaluate(() => ({ x: scrollX, y: scrollY }))
  const buttonCount = await surface.locator('button').count()
  expect(buttonCount, `${label}: dialog buttons`).toBeGreaterThan(0)
  const seen = new Set<string>()
  for (let step = 0; step < buttonCount + 6; step += 1) {
    await page.keyboard.press(tab)
    const focused = await page.evaluate(() => {
      const active = document.activeElement
      const surfaceElement = document.querySelector('.hl-dialog__surface')
      if (!active || !surfaceElement) return null
      const rect = active.getBoundingClientRect()
      return {
        inside: surfaceElement.contains(active),
        tag: active.tagName.toLowerCase(),
        name: active.getAttribute('aria-label') ?? active.textContent?.trim() ?? '',
        box: { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom },
      }
    })
    expect(focused, `${label}: focus after Tab ${step + 1}`).not.toBeNull()
    expect(focused!.inside, `${label}: focus stays in the modal after Tab ${step + 1}`).toBe(true)
    if (focused!.tag === 'button') seen.add(focused!.name)
    expectInside(focused!.box, viewport, `${label}: focused ${focused!.tag} "${focused!.name}"`)
  }
  expect(seen.size, `${label}: every dialog button took focus`).toBe(buttonCount)
  expect(await page.evaluate(() => ({ x: scrollX, y: scrollY })), `${label}: page scroll`).toEqual(before)
}

for (const projection of ['react', 'blazor'] as const) {
  for (const story of stories) {
    test(`${projection} ${story.scenario} keeps the Dialog surface centred and its focused buttons visible`, async ({ page, browserName }) => {
      await page.emulateMedia({ reducedMotion: 'no-preference' })
      const surface = await openStory(page, projection, story)
      const label = `${projection} ${story.scenario}`
      const viewport = await viewportOf(page)

      const opening = await sampleOpening(surface)
      expect(opening, `${label}: the opening animation runs`).not.toBeNull()
      expect(opening!.duration).toBeGreaterThan(0)
      for (const frame of opening!.frames) {
        expectInside(frame.surface, viewport, `${label}: surface at ${frame.time}ms of opening`)
        for (const button of frame.buttons) expectInside(button.box, viewport, `${label}: "${button.name}" at ${frame.time}ms of opening`)
      }

      const settled = await surface.evaluate(element => {
        const rect = element.getBoundingClientRect()
        return { centre: (rect.left + rect.right) / 2, width: rect.width }
      })
      expect(settled.width).toBeGreaterThan(0)
      expect(Math.abs(settled.centre - viewport.width / 2), `${label}: horizontal centre`).toBeLessThanOrEqual(1)
      await expectFocusWalkVisible(page, surface, label, browserName)
    })

    test(`${projection} ${story.scenario} keeps its focused buttons visible with reduced motion`, async ({ page, browserName }) => {
      await page.emulateMedia({ reducedMotion: 'reduce' })
      const surface = await openStory(page, projection, story)
      const label = `${projection} ${story.scenario} reduced motion`
      expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true)
      expect(await sampleOpening(surface), `${label}: no opening animation`).toBeNull()
      const viewport = await viewportOf(page)
      const centre = await surface.evaluate(element => {
        const rect = element.getBoundingClientRect()
        return (rect.left + rect.right) / 2
      })
      expect(Math.abs(centre - viewport.width / 2), `${label}: horizontal centre`).toBeLessThanOrEqual(1)
      await expectFocusWalkVisible(page, surface, label, browserName)
    })

    // At a phone width the surface takes the narrow-screen size, calc(100dvw - 2rem), so its
    // gutters are what keep it onscreen.
    if (story.direction === 'rtl') {
      test(`${projection} ${story.scenario} keeps its focused buttons visible at a 320px viewport`, async ({ page, browserName }) => {
        await page.setViewportSize({ width: 320, height: 640 })
        await page.emulateMedia({ reducedMotion: 'reduce' })
        const surface = await openStory(page, projection, story)
        const label = `${projection} ${story.scenario} at 320px`
        const viewport = await viewportOf(page)
        expect(viewport.width).toBe(320)
        const box = await surface.evaluate(element => {
          const rect = element.getBoundingClientRect()
          return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }
        })
        expectInside(box, viewport, `${label}: surface`)
        expect(Math.abs((box.left + box.right) / 2 - viewport.width / 2), `${label}: horizontal centre`).toBeLessThanOrEqual(1)
        await expectFocusWalkVisible(page, surface, label, browserName)
      })
    }
  }
}
