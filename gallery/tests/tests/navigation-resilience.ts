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

type FailedRequest = { url(): string; failure(): { errorText: string } | null }

export type StoryPage = NavigationPage & {
  on(event: 'requestfailed', listener: (request: FailedRequest) => void): unknown
  off(event: 'requestfailed', listener: (request: FailedRequest) => void): unknown
}

// T-709: the same transient error that fails a navigation can fail one of the page's own requests
// instead (captured: a module import, net::ERR_NO_BUFFER_SPACE, 18 ms after the document). The page
// then loads but never boots, and `ready` would wait out its whole timeout. So a network-level failure
// of a request to `origin` before the page is ready reloads it, bounded like the navigation retry. A
// missing asset is an HTTP 404, not a network failure, and still fails; ERR_ABORTED is what leaving
// the previous page does to its requests. The last attempt waits on `ready` alone.
export async function openWithSubresourceRetry(page: StoryPage, url: string, origin: string, ready: () => Promise<unknown>): Promise<void> {
  const retryDelays = [250, 750]
  for (let attempt = 0; ; attempt += 1) {
    let onFailed: (request: FailedRequest) => void = () => {}
    const failed = new Promise<string>(resolve => {
      onFailed = request => {
        const errorText = request.failure()?.errorText ?? ''
        if (request.url().startsWith(origin) && errorText !== 'net::ERR_ABORTED') resolve(`${request.url()} ${errorText}`)
      }
    })
    page.on('requestfailed', onFailed)
    try {
      await gotoWithTransientNetworkRetry(page, url)
      if (attempt === retryDelays.length) {
        await ready()
        return
      }
      const readiness = ready().then(() => undefined)
      readiness.catch(() => {})
      if (await Promise.race([readiness, failed]) === undefined) return
    }
    finally {
      page.off('requestfailed', onFailed)
    }
    await page.waitForTimeout(retryDelays[attempt])
  }
}
