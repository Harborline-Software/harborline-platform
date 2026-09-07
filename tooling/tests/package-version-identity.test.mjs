// Ticket 143. Positive control for the one property the whole fix rests on: a changed packaged
// source produces a different package version. If this file could not go red, every packed .nupkg
// could still be shadowed by yesterday's extraction of the same id+version, which is the bug.
import assert from 'node:assert/strict'
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve, sep } from 'node:path'
import test from 'node:test'
import { fileURLToPath } from 'node:url'

import {
  BASE_VERSION, computePackageVersion, PACKAGE_INPUT_ROOTS, PROPS_RELATIVE_PATH,
  readPackageVersionProps, writePackageVersionProps,
} from '../package-version.mjs'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const componentSource = 'projections/blazor/ui/Toaster.razor.cs'

function write(root, relativePath, text) {
  const path = resolve(root, relativePath)
  mkdirSync(dirname(path), { recursive: true })
  writeFileSync(path, text)
  return path
}

// A tree with the shape computePackageVersion reads, small enough to mutate a byte in.
function stageTree() {
  const root = mkdtempSync(join(tmpdir(), 'harborline-package-version-'))
  for (const entry of PACKAGE_INPUT_ROOTS.filter(name => name.endsWith('.props'))) {
    copyFileSync(resolve(repositoryRoot, entry), resolve(root, entry))
  }
  write(root, 'projections/dotnet/foundation/Foundation.cs', 'public static class Foundation { }\n')
  write(root, 'projections/blazor/ui/Toaster.razor', '<div>@DurationMilliseconds</div>\n')
  write(root, componentSource, 'public int? DurationMilliseconds { get; set; }\n')
  // Two byte-identical files, so the rename control below changes only a NAME: the sequence of
  // content hashes stays the same and only the path can tell the two trees apart.
  write(root, 'projections/blazor/ui/Bridge.a.cs', 'public static class Bridge { }\n')
  write(root, 'projections/blazor/ui/Bridge.b.cs', 'public static class Bridge { }\n')
  return root
}

function withTree(body) {
  const root = stageTree()
  try {
    body(root)
  } finally {
    rmSync(root, { recursive: true, force: true })
  }
}

test('the version is a function of the packaged content, not of the clock', () => withTree(root => {
  const first = computePackageVersion(root)
  assert.equal(first, computePackageVersion(root), 'the same tree must pack at the same version')
  assert.match(first, /^0\.0\.0-alpha\.0\.h[0-9a-f]{12}$/)
  assert.ok(first.startsWith(`${BASE_VERSION}.`))
}))

test('a one-character edit to a packaged source produces a version NuGet cannot confuse with the last one', () => withTree(root => {
  const before = computePackageVersion(root)
  // The ticket's actual change: int? -> int on one component parameter.
  const path = resolve(root, componentSource)
  writeFileSync(path, readFileSync(path, 'utf8').replace('int?', 'int'))
  assert.notEqual(computePackageVersion(root), before, 'a changed parameter type must change the package version')
}))

test('a renamed file changes the version even when every byte is unchanged', () => withTree(root => {
  const before = computePackageVersion(root)
  const path = resolve(root, 'projections/blazor/ui/Bridge.a.cs')
  write(root, 'projections/blazor/ui/Bridge.c.cs', readFileSync(path))
  rmSync(path)
  assert.notEqual(computePackageVersion(root), before)
}))

test('the stamped props file is what MSBuild would read back', () => withTree(root => {
  const { path, version } = writePackageVersionProps(root)
  assert.ok(path.endsWith(PROPS_RELATIVE_PATH.replaceAll('/', sep)))
  assert.equal(readPackageVersionProps(root), version)
  assert.equal(version, computePackageVersion(root))
  assert.match(readFileSync(path, 'utf8'), /<HarborlinePackedVersion>0\.0\.0-alpha\.0\.h[0-9a-f]{12}<\/HarborlinePackedVersion>/)
}))

test('an unpacked tree reads back as nothing rather than as a stale version', () => withTree(root => {
  assert.equal(readPackageVersionProps(root), null)
}))

// Review 1, B2. The root LICENSE is packed into all 24 .nupkg but lives outside the input roots, so
// editing it used to leave the version — and therefore the extraction NuGet serves — unchanged.
test('a packed file outside the input roots still changes the version', () => withTree(root => {
  write(root, 'LICENSE', 'Copyright\n')
  write(root, 'projections/dotnet/foundation/Foundation.csproj', [
    '<Project Sdk="Microsoft.NET.Sdk">',
    '  <ItemGroup><None Include="../../../LICENSE" Pack="true" PackagePath="/" /></ItemGroup>',
    '</Project>',
    '',
  ].join('\n'))
  const before = computePackageVersion(root)
  write(root, 'LICENSE', 'Copyright, amended\n')
  assert.notEqual(computePackageVersion(root), before, 'a packed LICENSE must be part of the package identity')
}))

// Review 1, B1. Five inline restore/run pairs bypassed runNugetConsumer, so the fixtures — copied
// out of reach of the repository props — restored with an empty version (NU1015). One helper owns
// the consumer restore; this fence keeps a sixth from being written by hand.
test('every consumer restore goes through the helper that passes the packed version', () => {
  const source = readFileSync(resolve(repositoryRoot, 'tooling/verify-package-fixtures.mjs'), 'utf8')
  const restores = source.split('\n').filter(line => /'restore', 'Consumer\.csproj'/.test(line))
  assert.equal(restores.length, 1, `consumer restores outside runNugetConsumer: ${JSON.stringify(restores)}`)
  assert.match(restores[0], /packedVersion/)
})
