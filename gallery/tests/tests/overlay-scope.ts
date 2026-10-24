import type { Page } from '@playwright/test'

// The accessibility scan's scope beyond the probe: every overlay layer the scenario portaled to
// document.body (gallery.spec.ts assertAccessible, recordReactBaseline).
//
// Dialog, Sheet, Spotlight, Window and Popover move their open content to a direct child of
// document.body in both projections (React createPortal; Blazor appends the portal element in
// dialog.js / sheet.js / spotlight.js / window.js / popover.js). A layer is identified by the
// platform's own markup -- an `hl-` class or a `data-hl-*` module/layer attribute -- on the body
// child ITSELF or on anything inside it, because a portal may be a bare wrapper around the marked
// element. Never by role or position: the story iframe's body also holds harness chrome
// (Storybook's highlights root and a11y vision filters, Blazing Story's boot elements), and none of
// it carries that markup. Each story is its own iframe document, so every such layer belongs to the
// scenario on screen and no other. A body child that holds the probe is the probe's own host,
// already in scope, and is excluded whatever else it contains.
const platformMarkup = '[class^="hl-"], [class*=" hl-"], [data-hl-module], [data-hl-layer]'

export const overlayLayerSelector = `body > :is(${platformMarkup}, :has(${platformMarkup})):not([data-gallery-probe]):not(:has([data-gallery-probe]))`

type Box = { x: number, y: number, width: number, height: number }

// The React baseline's capture region (gallery.spec.ts recordReactBaseline). With an open portaled
// layer: the union of the probe box and every rendered box inside those layers, clipped to the
// viewport (the probe alone shows the scrim over the scene and none of the overlay). A full-viewport
// scrim makes it the whole viewport, fixed per test, so the capture stays deterministic. Without
// such a layer: undefined, and the capture is the probe exactly as before.
export async function overlayCaptureRegion(page: Page, probe: Box): Promise<Box | undefined> {
  return page.evaluate(({ selector, box }) => {
    const layers = [...document.querySelectorAll(selector)]
    if (layers.length === 0) return undefined
    let [left, top, right, bottom] = [box.x, box.y, box.x + box.width, box.y + box.height]
    for (const element of layers.flatMap(layer => [layer, ...layer.querySelectorAll('*')])) {
      const rect = element.getBoundingClientRect()
      if (rect.width === 0 || rect.height === 0) continue
      left = Math.min(left, rect.left)
      top = Math.min(top, rect.top)
      right = Math.max(right, rect.right)
      bottom = Math.max(bottom, rect.bottom)
    }
    left = Math.max(0, left)
    top = Math.max(0, top)
    right = Math.min(window.innerWidth, right)
    bottom = Math.min(window.innerHeight, bottom)
    return { x: left, y: top, width: right - left, height: bottom - top }
  }, { selector: overlayLayerSelector, box: probe })
}
