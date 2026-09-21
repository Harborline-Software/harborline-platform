export type NavigationPage = {
  goto(url: string): Promise<unknown>
  waitForTimeout(milliseconds: number): Promise<void>
}

export async function gotoWithTransientNetworkRetry(page: NavigationPage, url: string): Promise<unknown> {
  const retryDelays = [250, 750]
  let lastError: unknown
  for (let attempt = 0; attempt <= retryDelays.length; attempt += 1) {
    try {
      return await page.goto(url)
    }
    catch (error) {
      if (!String(error).includes('net::ERR_NO_BUFFER_SPACE') || attempt === retryDelays.length) throw error
      lastError = error
      await page.waitForTimeout(retryDelays[attempt])
    }
  }
  throw lastError
}
