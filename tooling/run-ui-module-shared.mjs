#!/usr/bin/env node

import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {resolveCommand, runnerEnvironment} from './resolve-command.mjs'
import {resolvePinnedDotnet} from './resolve-dotnet.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const catalog = JSON.parse(readFileSync(resolve(root, 'catalog/modules.yaml'), 'utf8'))

function execute(command, cwd, env = {}) {
  const started = performance.now()
  const resolved = resolveCommand(command[0], command.slice(1))
  const run = spawnSync(resolved.executable, resolved.args, {
    cwd,
    encoding: 'utf8',
    maxBuffer: 64 * 1024 * 1024,
    env: {...process.env, ...runnerEnvironment, ...env},
  })
  return {
    command,
    exitCode: run.status,
    durationMs: Math.round(performance.now() - started),
    output: `${run.stdout ?? ''}\n${run.stderr ?? ''}`,
  }
}

export function runUiModuleShared(moduleId) {
  const slug = moduleId.replace('hlp.ui.', '')
  const module = catalog.modules[moduleId]
  const reactProjection = module?.projections?.react
  const dotnetProjection = module?.projections?.blazor ?? module?.projections?.dotnet
  if (!moduleId.startsWith('hlp.ui.') || !reactProjection?.conformanceRunner || !dotnetProjection?.conformanceRunner) {
    throw new Error(`Unsupported shared UI module ${moduleId}`)
  }
  const specification = JSON.parse(readFileSync(resolve(root, `specs/modules/ui/${moduleId}/interface.yaml`), 'utf8'))
  const fixtures = JSON.parse(readFileSync(resolve(root, `conformance/${moduleId}/fixtures.yaml`), 'utf8'))
  const caseIds = specification.cases.map(entry => entry.id)
  if (JSON.stringify(caseIds) !== JSON.stringify(fixtures.cases.map(entry => entry.id))) {
    throw new Error(`${moduleId} interface and fixture case order differ`)
  }

  const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
  // This runner is invoked by tooling/run-shared.mjs at concurrency 6, so six module runners
  // and their dotnet children compete while each React suite runs. That is fine for the
  // fixture-driven conformance this stage exists to prove, and wrong for a wall-clock budget:
  // schema-form's 500ms-field render measures 190-226ms idle and 479-498ms here, against a
  // committed 400ms ceiling, so the assertion reports the scheduler. A module carrying
  // wall-clock budgets excludes them on this flag and keeps asserting them in the sequential
  // native-tests step, which passed in every gate where this step failed.
  const react = execute(['npm', 'run', `test:${slug}`], reactRoot, {HARBORLINE_SHARED_CONFORMANCE: '1'})
  const reactTestCount = [...react.output.matchAll(/Tests\s+(\d+)\s+passed/g)].reduce((total, match) => total + Number(match[1]), 0)
  const reactPassed = react.exitCode === 0 && reactTestCount > 0

  const dotnet = resolvePinnedDotnet(root)
  const project = module.projections.blazor
    ? 'projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj'
    : 'projections/dotnet/foundation/hlp.ui.support.tests/Harborline.Foundation.UI.Tests.csproj'
  if (process.env.HARBORLINE_SHARED_SKIP_BUILD !== '1') {
    const build = execute([dotnet.executable, 'build', project, '--configuration', 'Release', '--no-restore', '-v:minimal'], root)
    if (build.exitCode !== 0) throw new Error(`${moduleId} .NET shared setup failed\n${build.output}`)
  }

  const results = []
  // Wave-1 finding platform-appshell-2: the React suite runs ONCE, so it is recorded ONCE.
  // Stamping its single aggregate outcome onto every fixture case fabricated per-case evidence
  // ("case executed twice per lane") that nothing had produced. The dotnet lane below genuinely
  // executes per case and keeps per-case rows; making React equally case-addressable is the
  // recorded follow-up (ticket 081), not something to simulate in the report.
  results.push({
    moduleId,
    caseId: '(react-suite)',
    projection: 'react',
    command: react.command,
    testCount: reactTestCount,
    exitCode: react.exitCode,
    passed: reactPassed,
    failureOutput: reactPassed ? undefined : react.output.split('\n').slice(-50).join('\n'),
  })
  for (const fixture of fixtures.cases) {
    const dotnetRun = execute([
      dotnet.executable,
      'test',
      project,
      '--configuration', 'Release',
      '--no-build', '--no-restore',
      '--filter', `ModuleConformance=${moduleId}`,
      '-v:minimal',
    ], root, {HARBORLINE_CONFORMANCE_FIXTURE: JSON.stringify(fixture)})
    const testCount = Number(/Passed:\s+(\d+)/.exec(dotnetRun.output)?.[1] ?? 0)
    const passed = dotnetRun.exitCode === 0 && testCount === 1
    results.push({
      moduleId,
      caseId: fixture.id,
      projection: 'blazor-support',
      command: dotnetRun.command,
      testCount,
      exitCode: dotnetRun.exitCode,
      passed,
      failureOutput: passed ? undefined : dotnetRun.output.split('\n').slice(-50).join('\n'),
    })
  }

  const expectedResults = caseIds.length + 1
  const passed = results.length === expectedResults && results.every(result => result.passed)
  const report = {
    schemaVersion: 1,
    moduleId,
    runnerBoundary: 'The runner invokes public TypeScript and .NET test entry points and imports no projection implementation.',
    status: passed ? 'PASS' : 'FAIL',
    dotnetSdk: dotnet.version,
    counts: {
      interfaceCases: caseIds.length,
      projections: 2,
      expectedResults,
      executedResults: results.length,
      passedResults: results.filter(result => result.passed).length,
    },
    caseIds,
    results,
  }
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
  return passed
}

if (import.meta.main) {
  process.exitCode = runUiModuleShared(process.argv[2]) ? 0 : 1
}
