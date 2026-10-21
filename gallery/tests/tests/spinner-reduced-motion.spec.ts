import { expect, test, type Locator, type Page } from '@playwright/test'

import { openWithSubresourceRetry, readWithReload } from './navigation-resilience.ts'

type Projection = 'react' | 'blazor'
type StoryEntry = { id: string; title: string; name: string; type: 'story' | 'docs' }
type StoryIndex = { entries: Record<string, StoryEntry> }

const bases = {
  react: process.env.REACT_GALLERY_URL ?? 'http://127.0.0.1:6106',
  blazor: process.env.BLAZOR_GALLERY_URL ?? 'http://127.0.0.1:6107',
}

async function openSpinnerTypes(page: Page, projection: Projection) {
  const base = bases[projection]
  let index: StoryIndex | undefined
  if (projection === 'react') {
    await expect(async () => {
      const response = await page.request.get(`${base}/index.json`)
      expect(response.ok()).toBe(true)
      index = await response.json() as StoryIndex
    }).toPass({ intervals: [250, 500, 1_000], timeout: 5_000 })
  }
  else {
    index = await readWithReload(page, base, 'BlazingStory.getStoryIndex()', async () => {
      await page.waitForFunction(() => {
        const host = globalThis as typeof globalThis & {
          BlazingStory?: { getStoryIndex?: unknown }
        }
        return typeof host.BlazingStory?.getStoryIndex === 'function'
      })
      // Reissue only the existing gallery's known page-promise collection failure.
      for (let attempt = 0; ; attempt += 1) {
        try {
          return await page.evaluate(() => {
            const host = globalThis as typeof globalThis & {
              BlazingStory: { getStoryIndex(): Promise<StoryIndex> }
            }
            return host.BlazingStory.getStoryIndex()
          })
        }
        catch (error) {
          if (attempt === 2 || !String(error).includes('Resulting promise was garbage collected')) throw error
        }
      }
    }, 15_000)
  }

  expect(index).toBeDefined()
  const entries = Object.values(index!.entries)
    .filter(entry => entry.type === 'story' && entry.title === 'Platform/Spinner' && entry.name === 'Types')
  expect(entries, 'the actual Spinner Types story').toHaveLength(1)
  const probe = page.locator('[data-gallery-probe][data-gallery-scenario="spinner.types"]')
  await openWithSubresourceRetry(
    page,
    `${base}/iframe.html?id=${encodeURIComponent(entries[0]!.id)}&viewMode=story`,
    base,
    timeout => expect(probe).toBeVisible({ timeout }),
    40_000,
  )
  return probe
}

async function expectVisibleStroke(shape: Locator) {
  await expect(shape).toBeVisible()
  await expect(shape).toHaveCSS('visibility', 'visible')
  await expect(shape).not.toHaveCSS('display', 'none')
  const actual = await shape.evaluate(element => {
    const style = getComputedStyle(element)
    const bounds = element.getBoundingClientRect()
    return {
      width: bounds.width,
      height: bounds.height,
      opacity: Number.parseFloat(style.opacity),
      stroke: style.stroke,
      strokeWidth: Number.parseFloat(style.strokeWidth),
    }
  })
  expect(actual.width).toBeGreaterThan(0)
  expect(actual.height).toBeGreaterThan(0)
  expect(actual.opacity).toBeGreaterThan(0)
  expect(actual.strokeWidth).toBeGreaterThan(0)
  expect(actual.stroke).not.toBe('none')
  expect(actual.stroke).not.toBe('transparent')
  expect(actual.stroke).not.toMatch(/^rgba\([^)]*,\s*0(?:\.0+)?\)$/)
}

async function expectStaticFeedback(root: Locator, name: string, arcCount: number) {
  await expect(root).toBeVisible()
  await expect(root).toHaveAttribute('role', 'status')
  await expect(root).toHaveAccessibleName(name)
  // Playwright visibility alone permits opacity:0, so observe the root directly too.
  expect(await root.evaluate(element => Number.parseFloat(getComputedStyle(element).opacity)))
    .toBeGreaterThan(0)
  await expect(root.locator('.hl-spinner__track')).toHaveCount(1)
  await expect(root.locator('.hl-spinner__arc')).toHaveCount(arcCount)
  await expectVisibleStroke(root.locator('.hl-spinner__track'))
  for (let index = 0; index < arcCount; index += 1) {
    await expectVisibleStroke(root.locator('.hl-spinner__arc').nth(index))
  }
}

async function expectOrdinaryAnimation(ring: Locator, converging: Locator) {
  await expect(ring).toHaveCSS('animation-name', 'hl-spinner-spin')
  await expect(ring.locator('.hl-spinner__arc')).toHaveCSS('animation-name', 'none')
  await expect(converging).toHaveCSS('animation-name', 'none')
  await expect(converging.locator('.hl-spinner__arc').nth(0)).toHaveCSS('animation-name', 'hl-spinner-spin')
  await expect(converging.locator('.hl-spinner__arc').nth(1)).toHaveCSS('animation-name', 'hl-spinner-reverse')
}

for (const projection of ['react', 'blazor'] as const) {
  test(`${projection} Spinner stops every arc for reduced motion and retains visible named feedback`, async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'no-preference' })
    const probe = await openSpinnerTypes(page, projection)
    const ring = probe.getByRole('status', { name: 'Loading', exact: true })
    const converging = probe.getByRole('status', { name: 'Synchronizing', exact: true })
    await expect(probe.locator('.hl-spinner')).toHaveCount(2)
    await expect(ring).toHaveAttribute('data-hl-type', 'ring')
    await expect(converging).toHaveAttribute('data-hl-type', 'converging')

    expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(false)
    await expectStaticFeedback(ring, 'Loading', 1)
    await expectStaticFeedback(converging, 'Synchronizing', 2)
    await expectOrdinaryAnimation(ring, converging)

    await page.emulateMedia({ reducedMotion: 'reduce' })
    expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(true)
    await expectStaticFeedback(ring, 'Loading', 1)
    await expectStaticFeedback(converging, 'Synchronizing', 2)
    for (const [root, count] of [[ring, 1], [converging, 2]] as const) {
      await expect(root).toHaveCSS('animation-name', 'none')
      await expect(root.locator('.hl-spinner__track')).toHaveCSS('animation-name', 'none')
      for (let index = 0; index < count; index += 1) {
        await expect(root.locator('.hl-spinner__arc').nth(index)).toHaveCSS('animation-name', 'none')
      }
    }

    await page.emulateMedia({ reducedMotion: 'no-preference' })
    expect(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches)).toBe(false)
    await expectStaticFeedback(ring, 'Loading', 1)
    await expectStaticFeedback(converging, 'Synchronizing', 2)
    await expectOrdinaryAnimation(ring, converging)
  })
}
