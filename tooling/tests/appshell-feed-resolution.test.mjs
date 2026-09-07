#!/usr/bin/env node

import assert from 'node:assert/strict'
import { createHash } from 'node:crypto'
import { execFileSync, spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, resolve } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'

import { resolveAppshellFeed } from '../resolve-appshell-feed.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')

function writeJson(path, value) {
  writeFileSync(path, `${JSON.stringify(value, null, 2)}\n`)
}

function sha256(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex')
}

function fixture() {
  const scratch = mkdtempSync(resolve(tmpdir(), 'hlp-appshell-feed-'))
  const repository = resolve(scratch, 'api')
  const cache = resolve(scratch, 'cache')
  mkdirSync(repository, { recursive: true })
  const packages = [
    ['packages/contracts', '@fixture/contracts', 'fixture-contracts-1.0.0.tgz'],
    ['apps/capability-host', '@fixture/capability-host', 'fixture-capability-host-1.0.0.tgz'],
  ]
  for (const [directory, name] of packages) {
    const packageDirectory = resolve(repository, directory)
    mkdirSync(resolve(packageDirectory, 'dist'), { recursive: true })
    writeJson(resolve(packageDirectory, 'package.json'), {
      name,
      version: '1.0.0',
      files: ['dist'],
      scripts: { build: 'node -e "process.exit(0)"' },
    })
    writeJson(resolve(packageDirectory, 'package-lock.json'), {
      name,
      version: '1.0.0',
      lockfileVersion: 3,
      requires: true,
      packages: { '': { name, version: '1.0.0' } },
    })
    writeFileSync(resolve(packageDirectory, 'dist/index.js'), `export const packageName = '${name}'\n`)
  }
  execFileSync('git', ['init', '--quiet'], { cwd: repository })
  execFileSync('git', ['config', 'user.email', 'feed-test@harborline.invalid'], { cwd: repository })
  execFileSync('git', ['config', 'user.name', 'Feed Test'], { cwd: repository })
  execFileSync('git', ['add', '.'], { cwd: repository })
  execFileSync('git', ['commit', '--quiet', '-m', 'fixture'], { cwd: repository })
  const ref = execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repository, encoding: 'utf8' }).trim()
  const manifestPath = resolve(scratch, 'appshell-feed-provenance.json')
  writeJson(manifestPath, {
    schemaVersion: 1,
    sourceRepository: { name: 'fixture-api', remote: 'unused', localPathEnv: 'HARBORLINE_TEST_API_REPO' },
    cache: { pathEnv: 'HARBORLINE_TEST_FEED_CACHE', defaultPath: resolve(scratch, 'unused-cache') },
    artifacts: packages.map(([packagePath, packageName, tarball], index) => ({
      id: index === 0 ? 'contracts' : 'capability-host',
      env: index === 0 ? 'HARBORLINE_TEST_CONTRACTS_TARBALL' : 'HARBORLINE_TEST_HOST_TARBALL',
      packageName,
      tarball,
      packagePath,
      ref,
      sha256: '',
      buildOrder: index,
      requiresSiblingBuild: index === 0 ? [] : ['contracts'],
    })),
  })
  return { cache, manifestPath, ref, repository, scratch }
}

test('blank pins require recording, record computed hashes, and then reproduce', () => {
  const { cache, manifestPath, repository } = fixture()
  process.env.HARBORLINE_TEST_API_REPO = repository
  process.env.HARBORLINE_TEST_FEED_CACHE = cache
  process.env.npm_config_cache = resolve(cache, 'npm')

  assert.throws(
    () => resolveAppshellFeed({ manifestPath }),
    /blank sha256 pin\(s\).*--record-blank-pins/,
  )

  const recordedEnvironment = resolveAppshellFeed({ manifestPath, recordBlankPins: true })
  const recorded = JSON.parse(readFileSync(manifestPath, 'utf8'))
  for (const artifact of recorded.artifacts) {
    assert.match(artifact.sha256, /^[0-9a-f]{64}$/)
    assert.equal(sha256(recordedEnvironment[artifact.env]), artifact.sha256)
    assert.equal(recordedEnvironment[artifact.env], resolve(cache, `${artifact.sha256}.tgz`))
  }

  const recordedText = readFileSync(manifestPath, 'utf8')
  assert.deepEqual(resolveAppshellFeed({ manifestPath }), recordedEnvironment)
  assert.equal(readFileSync(manifestPath, 'utf8'), recordedText)

  recorded.artifacts[0].sha256 = '0'.repeat(64)
  writeJson(manifestPath, recorded)
  assert.throws(
    () => resolveAppshellFeed({ manifestPath }),
    /expected 0{64}\n  actual   [0-9a-f]{64}/,
  )
  assert.ok(existsSync(recordedEnvironment.HARBORLINE_TEST_CONTRACTS_TARBALL))
})

test('corrupt content-addressed cache entries are refused without overwriting them', () => {
  const { cache, manifestPath, repository } = fixture()
  process.env.HARBORLINE_TEST_API_REPO = repository
  process.env.HARBORLINE_TEST_FEED_CACHE = cache
  process.env.npm_config_cache = resolve(cache, 'npm')

  resolveAppshellFeed({ manifestPath, recordBlankPins: true })
  const recorded = JSON.parse(readFileSync(manifestPath, 'utf8'))
  const artifact = recorded.artifacts[0]
  const expected = artifact.sha256
  const address = resolve(cache, `${expected}.tgz`)
  const original = readFileSync(address)
  const sentinel = 'tampered-cache-entry'
  const assertRefusal = run => assert.throws(run, error => {
    assert.ok(error.message.includes(address))
    assert.ok(error.message.includes(`expected ${expected}`))
    assert.ok(error.message.includes(`actual   ${sha256(address)}`))
    return true
  })

  writeFileSync(address, sentinel)
  assertRefusal(() => resolveAppshellFeed({ manifestPath }))
  assert.equal(readFileSync(address, 'utf8'), sentinel)

  writeFileSync(address, original)
  artifact.sha256 = ''
  writeJson(manifestPath, recorded)
  writeFileSync(address, sentinel)
  assertRefusal(() => resolveAppshellFeed({ manifestPath, recordBlankPins: true }))
  assert.equal(readFileSync(address, 'utf8'), sentinel)
})

test('repin tool records both artifacts and leaves existing cache entries intact', () => {
  const { cache, manifestPath, ref, repository, scratch } = fixture()
  process.env.HARBORLINE_TEST_API_REPO = repository
  process.env.HARBORLINE_TEST_FEED_CACHE = cache
  process.env.npm_config_cache = resolve(cache, 'npm')
  const seededEnvironment = resolveAppshellFeed({ manifestPath, recordBlankPins: true })
  const platform = resolve(scratch, 'platform')
  const tooling = resolve(platform, 'tooling')
  mkdirSync(tooling, { recursive: true })
  for (const script of ['repin-appshell-feed.mjs', 'resolve-appshell-feed.mjs', 'resolve-command.mjs']) {
    copyFileSync(resolve(root, 'tooling', script), resolve(tooling, script))
  }
  copyFileSync(manifestPath, resolve(tooling, 'appshell-feed-provenance.json'))
  const sentinel = resolve(cache, 'preexisting-cache-entry.tgz')
  writeFileSync(sentinel, 'keep')

  const blank = JSON.parse(readFileSync(resolve(tooling, 'appshell-feed-provenance.json'), 'utf8'))
  for (const artifact of blank.artifacts) artifact.sha256 = ''
  writeJson(resolve(tooling, 'appshell-feed-provenance.json'), blank)

  const ordinary = spawnSync(process.execPath, [resolve(tooling, 'resolve-appshell-feed.mjs')], {
    cwd: platform,
    encoding: 'utf8',
    env: {
      ...process.env,
      HARBORLINE_TEST_API_REPO: repository,
      HARBORLINE_TEST_FEED_CACHE: cache,
    },
  })
  assert.notEqual(ordinary.status, 0)
  assert.match(`${ordinary.stdout}\n${ordinary.stderr}`, /blank sha256 pin\(s\).*--record-blank-pins/)

  const result = spawnSync(process.execPath, [resolve(tooling, 'repin-appshell-feed.mjs'), ref], {
    cwd: platform,
    encoding: 'utf8',
    env: {
      ...process.env,
      HARBORLINE_TEST_API_REPO: repository,
      HARBORLINE_TEST_FEED_CACHE: cache,
      npm_config_cache: resolve(cache, 'npm'),
    },
  })
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
  const recorded = JSON.parse(readFileSync(resolve(tooling, 'appshell-feed-provenance.json'), 'utf8'))
  assert.ok(recorded.artifacts.every(artifact => artifact.ref === ref && /^[0-9a-f]{64}$/.test(artifact.sha256)))
  for (const artifact of recorded.artifacts) {
    assert.equal(sha256(resolve(cache, `${artifact.sha256}.tgz`)), artifact.sha256)
    assert.ok(Object.values(seededEnvironment).includes(resolve(cache, `${artifact.sha256}.tgz`)))
  }
  assert.equal(readFileSync(sentinel, 'utf8'), 'keep')
})
