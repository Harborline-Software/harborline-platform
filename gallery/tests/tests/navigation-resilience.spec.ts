import { expect, test } from '@playwright/test'
import { createServer, type Server } from 'node:http'
import type { AddressInfo } from 'node:net'

import { gotoWithTransientNetworkRetry, openWithSubresourceRetry } from './navigation-resilience.ts'

// A fake page whose goto fails one request per attempt as `failures` says, and whose readiness never
// settles on an attempt that lost a request -- the shape the T-709 capture recorded.
function storyPage(failures: Array<{ url: string; errorText: string } | undefined>) {
  const listeners = new Set<(request: { url(): string; failure(): { errorText: string } }) => void>()
  const gotos: string[] = []
  const waits: number[] = []
  const page = {
    on(_: 'requestfailed', listener: (request: { url(): string; failure(): { errorText: string } }) => void) { listeners.add(listener) },
    off(_: 'requestfailed', listener: (request: { url(): string; failure(): { errorText: string } }) => void) { listeners.delete(listener) },
    async goto(url: string) {
      gotos.push(url)
      const failure = failures[gotos.length - 1]
      if (failure) for (const listener of listeners) listener({ url: () => failure.url, failure: () => ({ errorText: failure.errorText }) })
      return { ok: true }
    },
    async waitForTimeout(milliseconds: number) { waits.push(milliseconds) },
  }
  const ready = () => failures[gotos.length - 1]?.errorText === 'net::ERR_NO_BUFFER_SPACE'
    ? new Promise<never>(() => {})
    : Promise.resolve()
  return { page, ready, gotos, waits }
}

const origin = 'http://127.0.0.1:6106'
const story = `${origin}/iframe.html?id=x`

test('a story page that loses one of its own requests is reloaded, not waited out', async () => {
  const { page, ready, gotos, waits } = storyPage([{ url: `${origin}/assets/preload-helper.js`, errorText: 'net::ERR_NO_BUFFER_SPACE' }])
  await openWithSubresourceRetry(page, story, origin, ready)
  expect(gotos).toEqual([story, story])
  expect(waits).toEqual([250])
})

test('an aborted request or another origin\'s failure does not reload the story', async () => {
  const { page, gotos } = storyPage([{ url: `${origin}/assets/a.js`, errorText: 'net::ERR_ABORTED' }])
  await openWithSubresourceRetry(page, story, origin, () => Promise.resolve())
  expect(gotos).toEqual([story])
  const other = storyPage([{ url: 'http://127.0.0.1:6107/_framework/x.dll', errorText: 'net::ERR_FAILED' }])
  await openWithSubresourceRetry(other.page, story, origin, () => Promise.resolve())
  expect(other.gotos).toEqual([story])
})

test('story reloads are bounded, and the last attempt fails on readiness', async () => {
  const lost = { url: `${origin}/assets/a.js`, errorText: 'net::ERR_NO_BUFFER_SPACE' }
  const { page, gotos, waits } = storyPage([lost, lost, lost])
  await expect(openWithSubresourceRetry(page, story, origin, () => Promise.reject(new Error('probe never visible'))))
    .rejects.toThrow('probe never visible')
  expect(gotos).toHaveLength(3)
  expect(waits).toEqual([250, 750])
})

// The T-709 mechanism in a real browser: a module the entry imports fails once at the network level,
// the entry never evaluates, and the probe never appears -- unless the page is reloaded.
test('a failed module import leaves the page unbooted, and the story retry recovers it', async ({ page }) => {
  const server: Server = createServer((request, response) => {
    if (request.url === '/dep.js') {
      response.writeHead(200, { 'content-type': 'text/javascript' })
      response.end("const probe = document.createElement('p'); probe.dataset.galleryProbe = ''; probe.textContent = 'ready'; document.body.append(probe)")
      return
    }
    response.writeHead(200, { 'content-type': 'text/html' })
    response.end("<!doctype html><body><script type=\"module\">import './dep.js'</script></body>")
  })
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve))
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`
  try {
    let depRequests = 0
    await page.route(`${base}/dep.js`, route => (depRequests += 1) === 1 ? route.abort('failed') : route.continue())
    const probe = () => expect(page.locator('[data-gallery-probe]')).toBeVisible({ timeout: 2_000 })

    await page.goto(`${base}/`)
    await expect(probe()).rejects.toThrow()

    depRequests = 0
    await openWithSubresourceRetry(page, `${base}/`, base, probe)
    expect(depRequests).toBe(2)
  }
  finally {
    await new Promise(resolve => server.close(resolve))
  }
})

test('retries ERR_NO_BUFFER_SPACE navigation failures at the page boundary', async () => {
  const attempts: string[] = []
  const waits: number[] = []
  const page = {
    async goto(url: string) {
      attempts.push(url)
      if (attempts.length === 1) throw new Error(`page.goto: net::ERR_NO_BUFFER_SPACE at ${url}`)
      return { ok: true }
    },
    async waitForTimeout(milliseconds: number) {
      waits.push(milliseconds)
    },
  }

  await expect(gotoWithTransientNetworkRetry(page, 'http://127.0.0.1:6307/')).resolves.toEqual({ ok: true })
  expect(attempts).toEqual(['http://127.0.0.1:6307/', 'http://127.0.0.1:6307/'])
  expect(waits).toEqual([250])
})

test('does not retry non-transient navigation failures', async () => {
  let attempts = 0
  const page = {
    async goto() {
      attempts += 1
      throw new Error('page.goto: net::ERR_CONNECTION_REFUSED')
    },
    async waitForTimeout() {
      throw new Error('must not wait for a non-transient failure')
    },
  }

  await expect(gotoWithTransientNetworkRetry(page, 'http://127.0.0.1:6307/'))
    .rejects.toThrow('net::ERR_CONNECTION_REFUSED')
  expect(attempts).toBe(1)
})

test('bounds ERR_NO_BUFFER_SPACE retries', async () => {
  let attempts = 0
  const waits: number[] = []
  const page = {
    async goto() {
      attempts += 1
      throw new Error('page.goto: net::ERR_NO_BUFFER_SPACE')
    },
    async waitForTimeout(milliseconds: number) {
      waits.push(milliseconds)
    },
  }

  await expect(gotoWithTransientNetworkRetry(page, 'http://127.0.0.1:6307/'))
    .rejects.toThrow('net::ERR_NO_BUFFER_SPACE')
  expect(attempts).toBe(3)
  expect(waits).toEqual([250, 750])
})
