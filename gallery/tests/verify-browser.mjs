import { chromium } from '@playwright/test'

try {
  const browser = await chromium.launch({ headless: true })
  await browser.close()
} catch (error) {
  const reason = error instanceof Error ? error.message : String(error)
  process.stderr.write(`Playwright Chromium unavailable: ${reason}\n`)
  process.exitCode = 1
}
