import { expect, test } from '@playwright/test'

import { gotoWithTransientNetworkRetry } from './navigation-resilience.ts'

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
