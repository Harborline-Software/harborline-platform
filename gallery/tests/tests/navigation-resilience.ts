export type NavigationPage = {
  goto(url: string): Promise<unknown>
  waitForTimeout(milliseconds: number): Promise<void>
}

// T-717: a page-side promise that only a booted app settles (BlazingStory.getStoryIndex()) never
// settles on a page whose boot stalled, and nothing bounded the wait but the 45 s test timeout. The
// Blazor index is cached per worker only on success, so on mac16 one stalled boot timed out every
// later Blazor story in that worker, about eighty tests on both attempts. Each attempt is bounded;
// a stalled page is reloaded, a bounded number of times, and the last failure names `label`.
export async function readWithReload<T>(page: NavigationPage, url: string, label: string, read: () => Promise<T>, attemptMs: number, attempts = 2): Promise<T> {
  let lastError: unknown
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    await gotoWithTransientNetworkRetry(page, url)
    let timer: ReturnType<typeof setTimeout> | undefined
    const stalled = new Promise<never>((_, reject) => {
      timer = setTimeout(() => reject(new Error(`no answer within ${attemptMs} ms`)), attemptMs)
    })
    const reading = read()
    // A read abandoned to a reload rejects when its page goes away; that is not this attempt's result.
    reading.catch(() => {})
    try {
      return await Promise.race([reading, stalled])
    }
    catch (error) {
      lastError = error
    }
    finally {
      clearTimeout(timer)
    }
  }
  throw new Error(`${label}: no answer after ${attempts} bounded attempts of ${attemptMs} ms at ${url}: ${String(lastError)}`)
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

type FailedRequest = { url(): string; failure(): { errorText: string } | null; isNavigationRequest(): boolean }

export type StoryPage = NavigationPage & {
  on(event: 'requestfailed', listener: (request: FailedRequest) => void): unknown
  off(event: 'requestfailed', listener: (request: FailedRequest) => void): unknown
}

// T-709: the same transient error that fails a navigation can fail one of the page's own requests
// instead (captured: a module import, net::ERR_NO_BUFFER_SPACE, 18 ms after the document). The page
// then loads but never boots, and `ready` would wait out its whole timeout. So a network-level failure
// of a request to `origin` before the page is ready reloads it, bounded like the navigation retry. A
// missing asset is an HTTP 404, not a network failure, and still fails; ERR_ABORTED is what leaving
// the previous page does to its requests; a failed document is gotoWithTransientNetworkRetry's to
// recover. Every attempt's `ready` shares one deadline, `budgetMs` from the first, so a reload cannot
// push the wait past the test's own timeout. The last attempt waits on `ready` alone.
export async function openWithSubresourceRetry(page: StoryPage, url: string, origin: string, ready: (timeoutMs: number) => Promise<unknown>, budgetMs: number): Promise<void> {
  const retryDelays = [250, 750]
  const expectedOrigin = new URL(origin).origin
  const sameOrigin = (requestUrl: string) => URL.canParse(requestUrl) && new URL(requestUrl).origin === expectedOrigin
  const deadline = Date.now() + budgetMs
  // Playwright reads a 0 timeout as "no limit", so a spent budget still passes 1 ms.
  const remaining = () => Math.max(1, deadline - Date.now())
  for (let attempt = 0; ; attempt += 1) {
    let onFailed: (request: FailedRequest) => void = () => {}
    const failed = new Promise<string>(resolve => {
      onFailed = request => {
        const errorText = request.failure()?.errorText ?? ''
        if (!request.isNavigationRequest() && sameOrigin(request.url()) && errorText !== 'net::ERR_ABORTED') resolve(`${request.url()} ${errorText}`)
      }
    })
    page.on('requestfailed', onFailed)
    try {
      await gotoWithTransientNetworkRetry(page, url)
      if (attempt === retryDelays.length) {
        await ready(remaining())
        return
      }
      const readiness = ready(remaining()).then(() => undefined)
      readiness.catch(() => {})
      if (await Promise.race([readiness, failed]) === undefined) return
    }
    finally {
      page.off('requestfailed', onFailed)
    }
    await page.waitForTimeout(retryDelays[attempt])
  }
}
