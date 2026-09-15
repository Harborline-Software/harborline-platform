import { expect, test, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../../../', import.meta.url))
const sheets = {
  react: ['projections/react/ui/hlp.ui.app-shell/src/style.css', 'projections/react/ui/hlp.ui.data-grid/src/style.css'],
  blazor: ['projections/blazor/ui/hlp.ui.app-shell/wwwroot/app-shell.css', 'projections/blazor/ui/hlp.ui.data-grid/wwwroot/data-grid.css'],
}

// Public CSS/markup seam: exercise the actual shipped lane styles in Chromium, not jsdom.
// No gallery theme tokens are injected: the default-dark shell is itself the theme provider.
async function render(page: Page, lane: keyof typeof sheets, container = 'class="hl-app-shell"') {
  await page.setContent(`<style>${sheets[lane].map(path => readFileSync(resolve(root, path), 'utf8')).join('\n')}</style>
    <main ${container}>
      <div class="hl-data-grid" role="grid" aria-label="Theme probe" data-zebra="true">
        <div class="hl-data-grid__row hl-data-grid__row--header" role="row"><div class="hl-data-grid__header-cell" role="columnheader">Name</div></div>
        <div class="hl-data-grid__row hl-data-grid__row--group" role="row"><div class="hl-data-grid__group-cell" role="gridcell">Group <span class="hl-data-grid__group-count">2</span></div></div>
        <div class="hl-data-grid__row hl-data-grid__row--leaf" role="row" aria-selected="true" data-zebra-stripe="alternate"><div class="hl-data-grid__cell" role="gridcell" tabindex="0">Selected</div></div>
      </div>
    </main>`)
}

async function colors(page: Page) {
  return page.getByRole('grid').evaluate(grid => {
    const style = getComputedStyle(grid)
    const child = (selector: string) => getComputedStyle(grid.querySelector(selector)!)
    return {
      background: style.backgroundColor,
      text: style.color,
      border: style.borderTopColor,
      header: child('.hl-data-grid__row--header').backgroundColor,
      group: child('.hl-data-grid__row--group').backgroundColor,
      selected: child('[aria-selected="true"]').backgroundColor,
      selectedText: child('[aria-selected="true"]').color,
      mutedText: child('.hl-data-grid__group-count').color,
    }
  })
}

for (const lane of ['react', 'blazor'] as const) {
  test(`${lane} grid inherits the default-dark AppShell semantic colors`, async ({ page }) => {
    await render(page, lane)
    await expect(page.locator('main')).toHaveCSS('background-color', 'rgb(18, 18, 18)')
    expect(await colors(page)).toEqual({
      background: 'rgb(26, 26, 26)', text: 'rgb(245, 245, 245)', border: 'rgb(58, 58, 58)',
      header: 'rgb(34, 34, 34)', group: 'rgb(29, 45, 68)', selected: 'rgb(29, 45, 68)',
      selectedText: 'rgb(245, 245, 245)', mutedText: 'rgb(208, 208, 208)',
    })
  })

  for (const theme of ['light', 'dark'] as const) {
    test(`${lane} grid follows an explicit ${theme} AppShell`, async ({ page }) => {
      await render(page, lane, `class="hl-app-shell" data-theme="${theme}"`)
      expect(await colors(page)).toEqual(theme === 'light' ? {
        background: 'rgb(255, 255, 255)', text: 'rgb(17, 17, 17)', border: 'rgb(208, 208, 208)',
        header: 'rgb(243, 243, 243)', group: 'rgb(232, 240, 255)', selected: 'rgb(232, 240, 255)',
        selectedText: 'rgb(17, 17, 17)', mutedText: 'rgb(68, 68, 68)',
      } : {
        background: 'rgb(26, 26, 26)', text: 'rgb(245, 245, 245)', border: 'rgb(58, 58, 58)',
        header: 'rgb(34, 34, 34)', group: 'rgb(29, 45, 68)', selected: 'rgb(29, 45, 68)',
        selectedText: 'rgb(245, 245, 245)', mutedText: 'rgb(208, 208, 208)',
      })
    })
  }

  for (const theme of ['default', 'light', 'dark'] as const) {
    test(`${lane} standalone grid preserves its ${theme} palette`, async ({ page }) => {
      await render(page, lane, theme === 'default' ? '' : `data-theme="${theme}"`)
      expect(await colors(page)).toEqual(theme === 'dark' ? {
        background: 'rgb(21, 27, 38)', text: 'rgb(241, 244, 248)', border: 'rgb(68, 80, 100)',
        header: 'rgb(33, 42, 56)', group: 'rgb(36, 54, 83)', selected: 'rgb(36, 54, 83)',
        selectedText: 'rgb(241, 244, 248)', mutedText: 'rgb(89, 101, 121)',
      } : {
        background: 'rgb(255, 255, 255)', text: 'rgb(23, 32, 51)', border: 'rgb(203, 211, 223)',
        header: 'rgb(238, 242, 247)', group: 'rgb(232, 240, 255)', selected: 'rgb(219, 234, 254)',
        selectedText: 'rgb(23, 32, 51)', mutedText: 'rgb(89, 101, 121)',
      })
    })
  }

  for (const theme of ['default', 'light', 'dark'] as const) {
    test(`${lane} legacy color overrides win over the ${theme} shell tokens`, async ({ page }) => {
      const attribute = theme === 'default' ? '' : `data-theme="${theme}"`
      await render(page, lane, `class="hl-app-shell" ${attribute} style="--hl-color-surface:#112233;--hl-color-text:#ddeeff;--hl-color-border:#445566;--hl-color-surface-subtle:#223344;--hl-color-accent-subtle:#334455;--hl-color-text-muted:#aabbcc;--hl-color-focus:#abcdef"`)
      expect(await colors(page)).toEqual({
        background: 'rgb(17, 34, 51)', text: 'rgb(221, 238, 255)', border: 'rgb(68, 85, 102)',
        header: 'rgb(34, 51, 68)', group: 'rgb(51, 68, 85)', selected: 'rgb(51, 68, 85)',
        selectedText: 'rgb(221, 238, 255)', mutedText: 'rgb(170, 187, 204)',
      })
      await page.keyboard.press('Tab')
      await expect(page.locator('.hl-data-grid__cell')).toHaveCSS('outline-color', 'rgb(171, 205, 239)')
      await expect(page.locator('.hl-data-grid__cell')).toHaveCSS('outline-width', '3px')
    })
  }

  test(`${lane} forced-colors retains the system selection, border and focus colors`, async ({ page }) => {
    await page.emulateMedia({ forcedColors: 'active' })
    await render(page, lane)
    // Resolve the OS palette independently; its exact RGB values vary by platform.
    await page.locator('body').evaluate(body => {
      const reference = document.createElement('div')
      reference.id = 'system-palette'
      reference.style.cssText = 'background:Highlight;color:HighlightText;border:1px solid CanvasText;forced-color-adjust:none'
      body.append(reference)
    })
    const system = await page.locator('#system-palette').evaluate(node => {
      const style = getComputedStyle(node)
      return { highlight: style.backgroundColor, highlightText: style.color, canvasText: style.borderTopColor }
    })
    await expect(page.locator('[aria-selected="true"]')).toHaveCSS('background-color', system.highlight)
    await expect(page.locator('[aria-selected="true"]')).toHaveCSS('color', system.highlightText)
    await expect(page.locator('[aria-selected="true"]')).toHaveCSS('forced-color-adjust', 'none')
    await expect(page.getByRole('grid')).toHaveCSS('border-top-color', system.canvasText)
    await page.keyboard.press('Tab')
    await expect(page.locator('.hl-data-grid__cell')).toHaveCSS('outline-color', system.highlight)
    await expect(page.locator('.hl-data-grid__cell')).toHaveCSS('outline-width', '3px')
  })
}
