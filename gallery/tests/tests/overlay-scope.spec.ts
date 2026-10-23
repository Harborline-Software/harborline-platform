import { expect, test } from '@playwright/test'

import { overlayCaptureRegion, overlayLayerSelector } from './overlay-scope.ts'

// DOM fixtures for the scan's overlay scope. No gallery server: each case is set as page content and
// the selector is asked which body children it picks, by the same querySelectorAll axe uses.
const picked = async (page: import('@playwright/test').Page, body: string) => {
  await page.setContent(`<!doctype html><html><body>${body}</body></html>`)
  return page.evaluate(selector => [...document.querySelectorAll(selector)].map(element => element.id), overlayLayerSelector)
}

test('overlay scope: a classless portal wrapper around a marked window is included', async ({ page }) => {
  expect(await picked(page, '<div id="root"><section data-gallery-probe></section></div><div id="portal"><section class="hl-window" data-hl-module="hlp.ui.window"></section></div>')).toEqual(['portal'])
})

test('overlay scope: marked body children are included (class, module and layer markup)', async ({ page }) => {
  expect(await picked(page, '<div id="root"><section data-gallery-probe></section></div>'
    + '<div id="dialog" class="hl-dialog__portal"></div><div id="content" class="x hl-popover__content"></div>'
    + '<div id="spot" data-hl-module="hlp.ui.spotlight"></div><div id="layer" data-hl-layer="window"></div>')).toEqual(['dialog', 'content', 'spot', 'layer'])
})

test('overlay scope: the probe host is excluded, even when it also carries platform markup', async ({ page }) => {
  expect(await picked(page, '<div id="root" class="hl-app"><section data-gallery-probe class="hl-gallery-scene"><button class="hl-button"></button></section></div>')).toEqual([])
  expect(await picked(page, '<section id="probe" data-gallery-probe class="hl-gallery-scene"></section>')).toEqual([])
})

test('overlay scope: harness chrome without platform markup is excluded', async ({ page }) => {
  expect(await picked(page, '<div id="root"><section data-gallery-probe></section></div>'
    + '<div id="storybook-highlights-root"></div><svg id="storybook-a11y-vision-filters"></svg>'
    + '<div id="blazor-error-ui"><a class="reload">Reload</a></div><div id="sb" class="sb-show-main sb-nopreview"></div>')).toEqual([])
})

test('overlay scope: a marked element nested inside the probe is not picked twice', async ({ page }) => {
  expect(await picked(page, '<div id="root"><section data-gallery-probe><div class="hl-dialog__portal"></div></section></div>')).toEqual([])
})

// The baseline capture region: the probe alone when nothing is portaled; the probe plus the layer's
// rendered boxes, clipped to the viewport, when something is.
const probeBox = { x: 20, y: 20, width: 300, height: 100 }
const scene = '<div id="root"><section data-gallery-probe style="position:absolute;left:20px;top:20px;width:300px;height:100px"></section></div>'

test('capture region: no portaled layer keeps the probe-only capture', async ({ page }) => {
  await page.setViewportSize({ width: 920, height: 720 })
  await page.setContent(`<!doctype html><body style="margin:0">${scene}</body>`)
  expect(await overlayCaptureRegion(page, probeBox)).toBeUndefined()
})

test('capture region: a dialog below the probe widens the capture to include it', async ({ page }) => {
  await page.setViewportSize({ width: 920, height: 720 })
  await page.setContent(`<!doctype html><body style="margin:0">${scene}<div><div class="hl-window" style="position:fixed;left:100px;top:200px;width:400px;height:300px"></div></div></body>`)
  expect(await overlayCaptureRegion(page, probeBox)).toEqual({ x: 20, y: 20, width: 480, height: 480 })
})

test('capture region: a full-viewport scrim makes it the viewport, never beyond', async ({ page }) => {
  await page.setViewportSize({ width: 920, height: 720 })
  await page.setContent(`<!doctype html><body style="margin:0">${scene}<div class="hl-dialog__portal" style="position:fixed;inset:-10px"></div></body>`)
  expect(await overlayCaptureRegion(page, probeBox)).toEqual({ x: 0, y: 0, width: 920, height: 720 })
})
