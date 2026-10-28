import assert from 'node:assert/strict'
import { spawn, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { createServer } from 'node:http'
import { tmpdir } from 'node:os'
import { resolve } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

import { runFixtureStep } from '../resolve-command.mjs'

const helper = fileURLToPath(new URL('../fetch-nuget-package.mjs', import.meta.url))
const url = 'https://api.nuget.org/v3-flatcontainer/literal/1.2.3/literal.1.2.3.nupkg'
const payload = Buffer.from([0, 255, 128, 80, 75, 3, 4, 10, 13, 0, 42, 17])

function fixture(t, scenario) {
  const directory = mkdtempSync(resolve(tmpdir(), 'hlp-fetch-package-'))
  t.after(() => rmSync(directory, { recursive: true, force: true }))
  const destination = resolve(directory, 'literal.nupkg')
  const attempts = resolve(directory, 'attempts.jsonl')
  const hook = resolve(directory, 'fetch.mjs')
  writeFileSync(hook, `
import { appendFileSync, existsSync } from 'node:fs'
let count = 0
globalThis.fetch = async (request, options) => {
  count += 1
  appendFileSync(${JSON.stringify(attempts)}, JSON.stringify({
    url: request, method: options?.method ?? 'GET',
    packageExists: existsSync(${JSON.stringify(destination)})
  }) + '\\n')
  const scenario = ${JSON.stringify(scenario)}
  if (scenario === 'timeout-always' || ((scenario === 'timeout-once' || scenario === 'timeout-then-hang') && count === 1)) {
    const cause = new AggregateError([new Error('opaque socket one'), new Error('opaque socket two')], 'connection failed')
    cause.code = 'ETIMEDOUT'
    throw new TypeError('fetch failed', { cause })
  }
  if (scenario === 'timeout-then-hang') {
    await new Promise(resolve => setTimeout(resolve, 10000))
  }
  if (scenario === 'other-error') throw new TypeError('fetch failed', { cause: Object.assign(new Error('certificate failed'), { code: 'CERT_HAS_EXPIRED' }) })
  if (scenario === 'http500') return new Response('failure', { status: 500 })
  if (scenario === 'body-error') return { ok: true, status: 200, arrayBuffer: async () => { throw new Error('literal truncated body') } }
  return new Response(new Uint8Array([0, 255, 128, 80, 75, 3, 4, 10, 13, 0, 42, 17]))
}
`)
  return { directory, destination, attempts, args: ['--import', hook, helper, url, destination] }
}

function requests(f) {
  return existsSync(f.attempts) ? readFileSync(f.attempts, 'utf8').trim().split('\n').map(JSON.parse) : []
}

for (const [scenario, expectedAttempts] of [['success', 1], ['timeout-once', 2]]) {
  test(`${scenario} writes the complete literal package after ${expectedAttempts} GETs`, t => {
    const f = fixture(t, scenario)
    const result = spawnSync(process.execPath, f.args, { encoding: 'utf8', timeout: 5000 })
    assert.equal(result.status, 0, result.stderr)
    assert.deepEqual(readFileSync(f.destination), payload)
    assert.deepEqual(requests(f), Array.from({ length: expectedAttempts }, () => ({ url, method: 'GET', packageExists: false })))
    if (scenario === 'timeout-once') {
      assert.match(result.stderr, /attempt 1 failed/)
      assert.match(result.stderr, /attempt 2 received HTTP 200/)
      assert.match(result.stderr, /opaque socket one/)
      assert.match(result.stderr, /opaque socket two/)
    }
  })
}

for (const [scenario, expectedAttempts, message] of [
  ['timeout-always', 2, /ETIMEDOUT/],
  ['other-error', 1, /CERT_HAS_EXPIRED/],
  ['http500', 1, /500/],
  ['body-error', 1, /literal truncated body/],
]) {
  test(`${scenario} fails without publishing a package or making extra requests`, t => {
    const f = fixture(t, scenario)
    const result = spawnSync(process.execPath, f.args, { encoding: 'utf8', timeout: 5000 })
    assert.equal(result.status, 1, result.stderr)
    assert.equal(requests(f).length, expectedAttempts)
    assert.equal(existsSync(f.destination), false)
    assert.match(result.stderr, message)
    if (scenario === 'timeout-always') {
      assert.match(result.stderr, /attempt 1 failed/)
      assert.match(result.stderr, /attempt 2 failed/)
      assert.doesNotMatch(result.stderr, /attempt 3/)
    }
  })
}

test('a destination write error does not make another GET', t => {
  const f = fixture(t, 'success')
  mkdirSync(f.destination)
  const result = spawnSync(process.execPath, f.args, { encoding: 'utf8', timeout: 5000 })
  assert.equal(result.status, 1)
  assert.equal(requests(f).length, 1)
  assert.match(result.stderr, /EISDIR/)
})

test('the existing fixture budget covers both GETs in the same child', t => {
  const f = fixture(t, 'timeout-then-hang')
  const previous = process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS
  process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS = '1500'
  try {
    assert.throws(() => runFixtureStep(process.execPath, f.args), /exceeded its 1500ms budget/)
    assert.equal(requests(f).length, 2)
    assert.equal(existsSync(f.destination), false)
  } finally {
    if (previous === undefined) delete process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS
    else process.env.HARBORLINE_GALLERY_STEP_BUDGET_MS = previous
  }
})

test('the actual CLI reads complete binary bytes and refuses a truncated HTTP body', async t => {
  const directory = mkdtempSync(resolve(tmpdir(), 'hlp-fetch-http-'))
  t.after(() => rmSync(directory, { recursive: true, force: true }))
  const seen = []
  const server = createServer((request, response) => {
    seen.push(request.url)
    if (request.url === '/truncated') {
      response.writeHead(200, { 'Content-Length': 1000, Connection: 'close' })
      response.write(Buffer.from([1, 2, 3]))
      response.end()
    } else {
      response.end(payload)
    }
  })
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve))
  t.after(() => {
    server.closeAllConnections()
    return new Promise(resolve => server.close(resolve))
  })
  async function child(path) {
    const destination = resolve(directory, path)
    const processChild = spawn(process.execPath, [helper, `http://127.0.0.1:${server.address().port}/${path}`, destination],
      { timeout: 5000, killSignal: 'SIGKILL' })
    let stderr = ''
    processChild.stderr.on('data', chunk => { stderr += chunk })
    const status = await new Promise((resolve, reject) => {
      processChild.once('error', reject)
      processChild.once('close', resolve)
    })
    return { destination, status, stderr }
  }
  const complete = await child('complete')
  assert.equal(complete.status, 0, complete.stderr)
  assert.deepEqual(readFileSync(complete.destination), payload)
  const truncated = await child('truncated')
  assert.equal(truncated.status, 1, truncated.stderr)
  assert.match(truncated.stderr, /received HTTP 200/)
  assert.equal(existsSync(truncated.destination), false)
  assert.deepEqual(seen, ['/complete', '/truncated'])
})
