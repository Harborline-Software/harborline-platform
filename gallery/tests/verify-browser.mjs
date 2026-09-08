// The launch probe. A missing package is as much "browser unavailable" as a missing executable,
// so the import lives inside the try and its reason is reported the same way (330 s3).
try {
  const { chromium } = await import('@playwright/test')
  const browser = await chromium.launch({ headless: true })
  await browser.close()
} catch (error) {
  const reason = error instanceof Error ? error.message : String(error)
  process.stderr.write(`Playwright Chromium unavailable: ${reason}\n`)
  process.exitCode = 1
}
