import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {copyFileSync, mkdirSync, mkdtempSync, realpathSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, isAbsolute, join, relative, resolve, sep} from 'node:path'
import test from 'node:test'

const repositoryRoot = resolve(import.meta.dirname, '../..')

function withLedger(body, {id = 'Fixture.Package', version = '2.0.0', justification = 'R-0001'} = {}) {
  const temporaryRoot = realpathSync(tmpdir())
  const root = mkdtempSync(join(temporaryRoot, 'dependency-ledger-'))
  const cacheRoot = join(root, 'cache')
  const fixtureProfile = join(root, 'profile')
  const controlRoot = join(root, 'control')
  const globalCache = join(fixtureProfile, '.nuget', 'packages')
  const write = (file, text) => {
    mkdirSync(dirname(file), {recursive: true})
    writeFileSync(file, text)
  }
  const nuspec = (cache, cachedVersion, licence) => write(
    join(cache, id.toLowerCase(), cachedVersion.toLowerCase(), `${id}.nuspec`),
    `<package><metadata>${licence ? `<license type="expression">${licence}</license>` : ''}</metadata></package>\n`,
  )
  try {
    mkdirSync(join(root, 'tooling'), {recursive: true})
    mkdirSync(cacheRoot, {recursive: true})
    mkdirSync(globalCache, {recursive: true})
    copyFileSync(join(repositoryRoot, 'tooling/dependency-ledger.mjs'), join(root, 'tooling/dependency-ledger.mjs'))
    write(join(root, 'Directory.Packages.props'), `<Project><ItemGroup><PackageVersion Include="${id}" Version="${version}" Justification="${justification}" /></ItemGroup></Project>\n`)
    write(join(root, 'Fixture.csproj'), `<Project><ItemGroup><PackageReference Include="${id}" /></ItemGroup></Project>\n`)
    write(join(controlRoot, 'research/R-0001-fixture/note.md'), 'reach-platform: test\n')
    for (const args of [['init', '--quiet'], ['add', '--', 'Directory.Packages.props', 'Fixture.csproj']]) {
      const result = spawnSync('git', ['-c', `safe.directory=${root.replaceAll('\\', '/')}`, ...args], {cwd: root, encoding: 'utf8'})
      assert.ifError(result.error)
      assert.equal(result.status, 0, `${result.stdout}${result.stderr}`)
    }
    const run = (packages = cacheRoot) => {
      const result = spawnSync(process.execPath, [join(root, 'tooling/dependency-ledger.mjs')], {
        cwd: root,
        encoding: 'utf8',
        env: {...process.env, USERPROFILE: fixtureProfile, NUGET_PACKAGES: packages, HARBORLINE_CONTROL_REPO: controlRoot},
      })
      assert.ifError(result.error)
      assert.equal(result.signal, null, `${result.stdout}${result.stderr}`)
      return result
    }
    body({cacheRoot, globalCache, nuspec, run})
  } finally {
    const cleanupTarget = realpathSync(root)
    const ownedPath = relative(temporaryRoot, cleanupTarget)
    assert.ok(ownedPath && !isAbsolute(ownedPath) && ownedPath !== '..' && !ownedPath.startsWith(`..${sep}`))
    assert.equal(cleanupTarget, root)
    rmSync(cleanupTarget, {recursive: true, force: true})
  }
}

test('only an older MIT version cannot approve a missing 2.0.0 pin', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '1.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 1, result.stdout)
  assert.match(result.stdout, /FAIL nuspec missing for Fixture\.Package 2\.0\.0/)
}))

test('the exact primary pin with an allowed licence passes', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '2.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}))

test('an older primary package cannot hide the exact global pin', () => withLedger(({cacheRoot, globalCache, nuspec, run}) => {
  nuspec(cacheRoot, '1.0.0', 'GPL-3.0-only')
  nuspec(globalCache, '2.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}))

test('a primary version directory without a nuspec cannot hide the exact global pin', () => withLedger(({cacheRoot, globalCache, nuspec, run}) => {
  mkdirSync(join(cacheRoot, 'fixture.package/2.0.0'), {recursive: true})
  nuspec(globalCache, '2.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}))

test('an exact rejected licence fails even when an older MIT version exists', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '1.0.0', 'MIT')
  nuspec(cacheRoot, '2.0.0', 'GPL-3.0-only')
  const result = run()
  assert.equal(result.status, 1, result.stdout)
  assert.match(result.stdout, /FAIL Fixture\.Package licence GPL-3\.0-only is not allowed/)
}))

test('the first exact nuspec retains precedence over an allowed global licence', () => withLedger(({cacheRoot, globalCache, nuspec, run}) => {
  nuspec(cacheRoot, '2.0.0', 'GPL-3.0-only')
  nuspec(globalCache, '2.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 1, result.stdout)
  assert.match(result.stdout, /FAIL Fixture\.Package licence GPL-3\.0-only is not allowed/)
}))

test('empty caches report the missing package and pinned version', () => withLedger(({run}) => {
  const result = run()
  assert.equal(result.status, 1, result.stdout)
  assert.match(result.stdout, /FAIL nuspec missing for Fixture\.Package 2\.0\.0/)
}))

test('an unset package cache still skips the licence check', () => withLedger(({run}) => {
  const result = run('')
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}))

test('property-based pins still skip the licence check', () => withLedger(({run}) => {
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}, {version: '$(FixtureVersion)'}))

test('first-party pins with a platform justification still skip the licence check', () => withLedger(({run}) => {
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}, {id: 'Harborline.Fixture', justification: 'platform'}))

test('the existing development-only MPL allowance still passes', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '2.0.0', 'MPL-2.0')
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.doesNotMatch(result.stdout, /FAIL /)
}, {id: 'BlazingStory'}))

test('the existing named licence exception still passes without an expression', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '2.0.0')
  const result = run()
  assert.equal(result.status, 0, result.stdout)
  assert.match(result.stdout, /ok JsonSchema\.Net licence exception:/)
  assert.doesNotMatch(result.stdout, /FAIL /)
}, {id: 'JsonSchema.Net'}))

test('an allowed exact licence does not bypass justification validation', () => withLedger(({cacheRoot, nuspec, run}) => {
  nuspec(cacheRoot, '2.0.0', 'MIT')
  const result = run()
  assert.equal(result.status, 1, result.stdout)
  assert.match(result.stdout, /FAIL PackageVersion Fixture\.Package lacks Justification=/)
}, {justification: 'invalid'}))
