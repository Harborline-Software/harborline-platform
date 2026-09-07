import assert from 'node:assert/strict'
import { spawnSync } from 'node:child_process'
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import test from 'node:test'
import { resolvePinnedDotnet } from '../resolve-dotnet.mjs'

const root = fileURLToPath(new URL('../../', import.meta.url)).replaceAll('\\', '/')

test('the shared framework rule selects packable Harborline libraries after project declarations', () => {
  const scratch = mkdtempSync(join(tmpdir(), 'harborline-package-framework-'))
  const dotnet = resolvePinnedDotnet(root)
  try {
    for (const [name, declarations, expected] of [
      ['library', '<IsPackable>true</IsPackable><PackageId>Harborline.Example</PackageId>', 'net10.0'],
      ['explicit-preview', '<IsPackable>true</IsPackable><PackageId>Harborline.Example</PackageId><TargetFramework>net11.0</TargetFramework>', 'net10.0'],
      ['test', '<IsPackable>false</IsPackable><PackageId>Harborline.Example.Tests</PackageId>', 'net11.0'],
      ['foreign-package', '<IsPackable>true</IsPackable><PackageId>Example.Library</PackageId>', 'net11.0'],
      ['host', '<OutputType>Exe</OutputType>', 'net11.0'],
    ]) {
      const directory = join(scratch, name)
      mkdirSync(directory)
      const project = resolve(directory, 'Probe.csproj')
      writeFileSync(project, `<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="${root}/Directory.Build.props" />
  <Import Project="${root}/Directory.Packages.props" />
  <PropertyGroup>${declarations}</PropertyGroup>
</Project>\n`)
      const run = spawnSync(dotnet.executable, ['msbuild', project,
        '-getProperty:TargetFramework,TargetFrameworkVersion,OutputPath', '-getItem:PackageVersion',
        '-nodeReuse:false', '-maxcpucount:6'], { cwd: root, encoding: 'utf8' })
      assert.equal(run.status, 0, `${name}: ${run.stdout}\n${run.stderr}`)
      const result = JSON.parse(run.stdout)
      assert.equal(result.Properties.TargetFramework, expected, name)
      assert.equal(result.Properties.TargetFrameworkVersion, expected.replace('net', 'v'), name)
      assert.ok(result.Properties.OutputPath.replaceAll('\\', '/').endsWith(`${expected}/`), name)
      const dependency = result.Items.PackageVersion.find(item => item.Identity === 'Microsoft.AspNetCore.Components.Web')
      assert.match(dependency.Version, expected === 'net10.0' ? /^10\.[0-9]+\.[0-9]+$/ : /^11\..*-preview\./, name)
    }
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
})
