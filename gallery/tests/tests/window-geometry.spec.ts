import { expect, test, type Locator, type Page } from '@playwright/test'

import { openWithSubresourceRetry, readWithReload } from './navigation-resilience.ts'

// T-1123: moving and resizing a Window from the keyboard must change its real geometry the same
// way in both lanes. Every step here is an actual key press on the focused header or resize
// handle, and every check reads the rendered box, in English left-to-right and in Arabic
// right-to-left with the caller's own labels. The display-state transitions (maximize, Escape,
// minimize, restore) are driven from the keyboard too, because they decide when the header and
// handles may act at all.

type Projection = 'react' | 'blazor'
type StoryEntry = { id: string; title: string; name: string; type: 'story' | 'docs' }
type StoryIndex = { entries: Record<string, StoryEntry> }
type Geometry = { left: number; top: number; width: number; height: number }

const bases = {
  react: process.env.REACT_GALLERY_URL ?? 'http://127.0.0.1:6106',
  blazor: process.env.BLAZOR_GALLERY_URL ?? 'http://127.0.0.1:6107',
}

const stories = [
  {
    scenario: 'window.geometry', name: 'Movement and resize', direction: 'ltr',
    labels: {
      move: 'Move window', moveDescription: 'Press arrow keys to move window',
      resizeHeight: 'Resize height from bottom edge', resizeWidth: 'Resize width from right edge',
      maximize: 'Maximize', minimize: 'Minimize', restore: 'Restore',
    },
  },
  {
    scenario: 'window.locale-theme', name: 'Locale and theme', direction: 'rtl',
    labels: {
      move: 'نقل النافذة', moveDescription: 'استخدم مفاتيح الأسهم',
      resizeHeight: 'تغيير الارتفاع', resizeWidth: 'تغيير العرض',
      maximize: 'تكبير', minimize: 'تصغير', restore: 'استعادة',
    },
  },
] as const

// The gallery sets no drag bounds, so the window is unbounded and only the frozen minimum
// size of 200 by 100 limits it.
const minimum = { width: 200, height: 100 }

async function readIndex(page: Page, projection: Projection, base: string): Promise<StoryIndex> {
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
  const index = await readIndex(page, projection, base)
  const entries = Object.values(index.entries)
    .filter(entry => entry.type === 'story' && entry.title === 'Platform/Window' && entry.name === story.name)
  expect(entries, `the actual Platform/Window "${story.name}" story`).toHaveLength(1)
  const dialog = page.getByRole('dialog')
  await openWithSubresourceRetry(
    page,
    `${base}/iframe.html?id=${encodeURIComponent(entries[0]!.id)}&viewMode=story`,
    base,
    timeout => expect(dialog).toBeVisible({ timeout }),
    40_000,
  )
  await expect(page.locator(`[data-gallery-probe][data-gallery-scenario="${story.scenario}"]`)).toHaveCSS('direction', story.direction)
  await expect(dialog).toHaveAttribute('data-window-state', 'default')
  await settled(dialog)
  return dialog
}

// Wait out the window's opening animation, so every box read is the resting geometry.
async function settled(dialog: Locator) {
  await dialog.evaluate(element => Promise.all(element.getAnimations({ subtree: true }).map(animation => animation.finished)))
}

async function geometry(dialog: Locator): Promise<Geometry> {
  const box = await dialog.boundingBox()
  expect(box, 'the window has a rendered box').not.toBeNull()
  return { left: box!.x, top: box!.y, width: box!.width, height: box!.height }
}

// Blazor applies a key press over its circuit, so wait for the box to settle on the expected value.
async function expectGeometry(dialog: Locator, expected: Geometry, label: string) {
  await expect(async () => {
    const actual = await geometry(dialog)
    for (const key of ['left', 'top', 'width', 'height'] as const)
      expect(Math.abs(actual[key] - expected[key]), `${label}: ${key} ${actual[key]} against ${expected[key]}`).toBeLessThanOrEqual(0.5)
  }).toPass({ intervals: [50, 100, 250], timeout: 5_000 })
}

async function press(target: Locator, key: string) {
  await expect(target).toBeFocused()
  await target.page().keyboard.press(key)
}

for (const projection of ['react', 'blazor'] as const) {
  for (const story of stories) {
    test.describe(`${projection} Window keyboard geometry, ${story.direction}`, () => {
      test.use({ locale: story.direction === 'rtl' ? 'ar' : 'en-US' })

      test('arrow keys on the named header move the window by 10, and by 50 with Shift', async ({ page }) => {
        const dialog = await openStory(page, projection, story)
        const header = dialog.getByRole('group', { name: story.labels.move })
        await expect(header).toHaveAttribute('aria-description', story.labels.moveDescription)
        await expect(header).toHaveAttribute('tabindex', '0')
        await header.focus()
        const start = await geometry(dialog)

        // Right-to-left content keeps numeric geometry: ArrowRight still moves the window right.
        await press(header, 'ArrowRight')
        await expectGeometry(dialog, { ...start, left: start.left + 10 }, 'ArrowRight')
        await press(header, 'ArrowDown')
        await expectGeometry(dialog, { ...start, left: start.left + 10, top: start.top + 10 }, 'ArrowDown')
        await press(header, 'Shift+ArrowLeft')
        await expectGeometry(dialog, { ...start, left: start.left - 40, top: start.top + 10 }, 'Shift+ArrowLeft')
        await press(header, 'Shift+ArrowUp')
        await expectGeometry(dialog, { ...start, left: start.left - 40, top: start.top - 40 }, 'Shift+ArrowUp')
        await press(header, 'ArrowLeft')
        await press(header, 'ArrowUp')
        await expectGeometry(dialog, { ...start, left: start.left - 50, top: start.top - 50 }, 'ArrowLeft, ArrowUp')
        await expect(header).toBeFocused()
      })

      test('each axis has one keyboard handle that resizes it and stops at the minimum', async ({ page }) => {
        const dialog = await openStory(page, projection, story)
        await expect(dialog.locator('[role="separator"]')).toHaveCount(2)
        const corner = dialog.locator('.hl-window__resize--corner')
        await expect(corner).toHaveAttribute('aria-hidden', 'true')
        await expect(corner).not.toHaveAttribute('tabindex', /.*/)
        const start = await geometry(dialog)

        const height = dialog.getByRole('separator', { name: story.labels.resizeHeight })
        await expect(height).toHaveAttribute('aria-orientation', 'horizontal')
        await height.focus()
        await press(height, 'ArrowDown')
        await expectGeometry(dialog, { ...start, height: start.height + 10 }, 'height ArrowDown')
        await expect(height).toHaveAttribute('aria-valuenow', String(start.height + 10))
        await press(height, 'Shift+ArrowUp')
        await expectGeometry(dialog, { ...start, height: start.height - 40 }, 'height Shift+ArrowUp')
        // A key for the other axis leaves the box alone.
        await press(height, 'ArrowRight')
        await expectGeometry(dialog, { ...start, height: start.height - 40 }, 'height ArrowRight')
        for (let step = 0; step < 6; step += 1) await press(height, 'Shift+ArrowUp')
        await expectGeometry(dialog, { ...start, height: minimum.height }, 'height at minimum')
        await expect(height).toHaveAttribute('aria-valuenow', String(minimum.height))

        const width = dialog.getByRole('separator', { name: story.labels.resizeWidth })
        await expect(width).toHaveAttribute('aria-orientation', 'vertical')
        await width.focus()
        await press(width, 'ArrowRight')
        await expectGeometry(dialog, { ...start, height: minimum.height, width: start.width + 10 }, 'width ArrowRight')
        await expect(width).toHaveAttribute('aria-valuenow', String(start.width + 10))
        await press(width, 'ArrowDown')
        await expectGeometry(dialog, { ...start, height: minimum.height, width: start.width + 10 }, 'width ArrowDown')
        for (let step = 0; step < 6; step += 1) await press(width, 'Shift+ArrowLeft')
        await expectGeometry(dialog, { ...start, height: minimum.height, width: minimum.width }, 'width at minimum')
        await expect(width).toHaveAttribute('aria-valuenow', String(minimum.width))
        // Resizing never moves the window.
        const end = await geometry(dialog)
        expect(Math.abs(end.left - start.left)).toBeLessThanOrEqual(0.5)
        expect(Math.abs(end.top - start.top)).toBeLessThanOrEqual(0.5)
      })

      test('maximized and minimized windows offer no move target or handles, and Escape restores before closing', async ({ page }) => {
        const dialog = await openStory(page, projection, story)
        const header = dialog.locator('.hl-window__title-bar')
        await header.focus()
        await press(header, 'ArrowRight')
        const moved = await geometry(dialog)

        const maximize = dialog.getByRole('button', { name: story.labels.maximize })
        await maximize.focus()
        await press(maximize, 'Enter')
        await expect(dialog).toHaveAttribute('data-window-state', 'maximized')
        await expect(header).not.toHaveAttribute('role', /.*/)
        await expect(header).not.toHaveAttribute('tabindex', /.*/)
        await expect(dialog.locator('[role="separator"]')).toHaveCount(0)
        const viewport = page.viewportSize()!
        await expectGeometry(dialog, { left: 0, top: 0, width: viewport.width, height: viewport.height }, 'maximized')
        // An arrow key in the maximized window does not move it.
        await page.keyboard.press('ArrowRight')
        await expectGeometry(dialog, { left: 0, top: 0, width: viewport.width, height: viewport.height }, 'maximized ArrowRight')

        // One Escape restores the earlier geometry and the window stays open.
        await page.keyboard.press('Escape')
        await expect(dialog).toHaveAttribute('data-window-state', 'default')
        await expectGeometry(dialog, moved, 'restored by Escape')
        await expect(dialog.getByRole('group', { name: story.labels.move })).toHaveAttribute('tabindex', '0')
        await expect(dialog.locator('[role="separator"]')).toHaveCount(2)

        const minimize = dialog.getByRole('button', { name: story.labels.minimize })
        await minimize.focus()
        await press(minimize, 'Enter')
        await expect(dialog).toHaveAttribute('data-window-state', 'minimized')
        await expect(header).not.toHaveAttribute('role', /.*/)
        await expect(dialog.locator('[role="separator"]')).toHaveCount(0)
        await expect(dialog.locator('.hl-window__body')).toHaveCount(0)

        const restore = dialog.getByRole('button', { name: story.labels.restore })
        await restore.focus()
        await press(restore, 'Enter')
        await expect(dialog).toHaveAttribute('data-window-state', 'default')
        await expect(dialog.getByRole('group', { name: story.labels.move })).toBeVisible()
        await expect(dialog.locator('[role="separator"]')).toHaveCount(2)
      })
    })
  }
}
