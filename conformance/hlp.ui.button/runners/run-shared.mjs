#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { resolveCommand } from '../../../tooling/resolve-command.mjs'
import { resolvePinnedDotnet } from '../../../tooling/resolve-dotnet.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../../..')
const moduleId = 'hlp.ui.button'
const specification = JSON.parse(readFileSync(resolve(root, 'specs/modules/ui/hlp.ui.button/interface.yaml'), 'utf8'))
const fixtureCatalog = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.button/fixtures.yaml'), 'utf8'))
const catalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8'))
const reactRoot = resolve(root, catalog.modules[moduleId].projections.react.path)
const dotnet = resolvePinnedDotnet(root)

const interfaceIds = specification.cases.map(entry => entry.id)
const fixtureIds = fixtureCatalog.cases.map(entry => entry.id)
if (JSON.stringify(interfaceIds) !== JSON.stringify(fixtureIds)) {
  throw new Error('Button interface and fixture case order differ')
}

function run(projection, fixture) {
  const fixtureJson = JSON.stringify(fixture)
  const command = projection === 'react'
    ? ['npm', 'run', 'test:conformance', '--', '-t', fixture.id]
    : [dotnet.executable, 'test', 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj', '--configuration', 'Release', '--no-build', '--no-restore', '--filter', `ConformanceCase=${fixture.id}`, '-v:minimal']
  const started = performance.now()
  const resolved = resolveCommand(command[0], command.slice(1))
  const result = spawnSync(resolved.executable, resolved.args, {
    cwd: projection === 'react' ? reactRoot : root,
    encoding: 'utf8',
    maxBuffer: 32 * 1024 * 1024,
    env: {
      ...process.env,
      CI: '1',
      NO_COLOR: '1',
      HARBORLINE_SHARED_RUNNER: '1',
      HARBORLINE_CONFORMANCE_FIXTURE: fixtureJson,
    },
  })
  const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`
  const testCount = projection === 'react'
    ? Number(/Tests\s+(\d+)\s+passed/.exec(output)?.[1] ?? 0)
    : Number(/Passed:\s+(\d+)/.exec(output)?.[1] ?? 0)
  const passed = result.status === 0 && testCount === 1
  return {
    moduleId,
    caseId: fixture.id,
    projection,
    normalizedExpected: fixture.expected,
    command,
    exitCode: result.status,
    testCount,
    durationMs: Math.round(performance.now() - started),
    passed,
    failureOutput: passed ? undefined : output.trimEnd().split('\n').slice(-40).join('\n'),
  }
}

if (process.argv.includes('--list')) {
  process.stdout.write(`${JSON.stringify({ moduleId, cases: interfaceIds, projections: ['react', 'blazor'] }, null, 2)}\n`)
  process.exit(0)
}

if (process.env.HARBORLINE_SHARED_SKIP_BUILD !== '1') {
  const setupCommands = [
    ['npm', ['run', 'build'], reactRoot],
    [dotnet.executable, ['build', 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj', '--configuration', 'Release', '-v:minimal'], root],
  ]
  for (const [executable, args, cwd] of setupCommands) {
    const resolvedSetup = resolveCommand(executable, args)
    const setup = spawnSync(resolvedSetup.executable, resolvedSetup.args, { cwd, encoding: 'utf8', env: { ...process.env, CI: '1', NO_COLOR: '1' } })
    if (setup.status !== 0) {
      throw new Error(`Shared runner setup failed: ${executable} ${args.join(' ')}\n${setup.stdout}\n${setup.stderr}`)
    }
  }
}

const results = fixtureCatalog.cases.flatMap(fixture => ['react', 'blazor'].map(projection => run(projection, fixture)))
const expectedResults = interfaceIds.length * 2
const passed = results.length === expectedResults && results.every(result => result.passed)
process.stdout.write(`${JSON.stringify({
  schemaVersion: 1,
  runnerBoundary: 'The runner spawns public native test entry points and imports no projection implementation.',
  status: passed ? 'PASS' : 'FAIL',
  dotnetSdk: dotnet.version,
  counts: {
    interfaceCases: interfaceIds.length,
    projections: 2,
    expectedResults,
    executedResults: results.length,
    passedResults: results.filter(result => result.passed).length,
  },
  caseIds: interfaceIds,
  results,
}, null, 2)}\n`)
process.exitCode = passed ? 0 : 1
