import { createRequire } from 'node:module'
import { test } from 'node:test'
import assert from 'node:assert/strict'
const require = createRequire(new URL('../../gallery/tests/package.json', import.meta.url))
const { chromium } = require('@playwright/test')
for (const lane of ['react', 'blazor']) test(`rules-auth-18 ${lane} keyboard promotion announces and retains focus`, async () => {
  const browser = await chromium.launch({ executablePath: process.env.PROMOTION_BROWSER })
  try {
    const page = await browser.newPage()
    const base = lane === 'react' ? 'http://127.0.0.1:6106' : 'http://127.0.0.1:6107'
    let index
    if (lane === 'react') index = await (await page.request.get(`${base}/index.json`)).json()
    else { await page.goto(base); await page.waitForFunction(() => typeof BlazingStory !== 'undefined'); index = await page.evaluate(() => BlazingStory.getStoryIndex()) }
    const entry = Object.values(index.entries).find(item => item.type === 'story' && item.title === 'Platform/Rule authoring' && item.name === 'Field check promotion')
    assert.ok(entry)
    await page.goto(`${base}/iframe.html?id=${encodeURIComponent(entry.id)}&viewMode=story`)
    const button = page.getByRole('button', { name: 'Promote Amount check' })
    await button.waitFor(); await button.focus(); await page.keyboard.press('Enter')
    await page.getByRole('status', { name: 'Promotion status' }).getByText('Promoted Amount check.').waitFor()
    assert.equal(await page.getByLabel('Rule name').evaluate(element => element === document.activeElement), true)
  } finally { await browser.close() }
})
