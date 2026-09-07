#!/usr/bin/env node

import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { cleanUpScratchOnSignal, runnerEnvironment, sweepStaleScratchTrees, writeScratchPidFile } from './resolve-command.mjs'
import { resolvePinnedDotnet } from './resolve-dotnet.mjs'

const repositoryRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')

function fail(message) {
  throw new Error(`generation-smoke: ${message}`)
}

function readJson(path, label) {
  let value
  try {
    value = JSON.parse(readFileSync(path, 'utf8'))
  } catch (error) {
    fail(`${label} is not readable JSON: ${error instanceof Error ? error.message : String(error)}`)
  }
  return value
}

function requireNonEmptyString(value, label) {
  if (typeof value !== 'string' || value.trim() === '') fail(`${label} must be a non-empty string`)
}

function razorEncode(value) {
  return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;').replaceAll('>', '&gt;')
}

function identifier(value) {
  return value.split(/[^A-Za-z0-9]+/).filter(Boolean).map(part => `${part[0].toUpperCase()}${part.slice(1)}`).join('')
}

export function loadModuleSpec(specRoot, moduleId) {
  requireNonEmptyString(moduleId, 'module id')
  const moduleRoot = resolve(specRoot, moduleId)
  const required = ['interface.yaml', 'quality.yaml', 'scenarios.json']
  for (const artifact of required) {
    if (!existsSync(join(moduleRoot, artifact))) fail(`${moduleId} is missing required spec artifact ${artifact}`)
  }
  const contract = readJson(join(moduleRoot, 'interface.yaml'), `${moduleId}/interface.yaml`)
  const quality = readJson(join(moduleRoot, 'quality.yaml'), `${moduleId}/quality.yaml`)
  const scenarios = readJson(join(moduleRoot, 'scenarios.json'), `${moduleId}/scenarios.json`)
  for (const [artifact, document] of [['interface.yaml', contract], ['quality.yaml', quality], ['scenarios.json', scenarios]]) {
    if (document.schemaVersion !== 1) fail(`${moduleId}/${artifact} must have schemaVersion 1`)
    if (document.moduleId !== moduleId) fail(`${moduleId}/${artifact} moduleId does not match its directory`)
  }
  if (!Array.isArray(contract.invariants) || contract.invariants.length === 0) fail(`${moduleId}/interface.yaml has no invariants`)
  if (!Array.isArray(scenarios.scenarios) || scenarios.scenarios.length === 0) fail(`${moduleId}/scenarios.json has no scenarios`)
  const seen = new Set()
  for (const [index, scenario] of scenarios.scenarios.entries()) {
    for (const field of ['id', 'name', 'surface']) requireNonEmptyString(scenario[field], `${moduleId}/scenarios.json scenarios[${index}].${field}`)
    if (seen.has(scenario.id)) fail(`${moduleId}/scenarios.json repeats scenario id ${scenario.id}`)
    seen.add(scenario.id)
    if (!Array.isArray(scenario.sourceCaseIds) || scenario.sourceCaseIds.length === 0) fail(`${moduleId}/${scenario.id} has no sourceCaseIds`)
  }
  return { moduleId, contract, quality, scenarios }
}

export function generateSmokeProject({ specRoot, moduleIds, outputRoot }) {
  if (!Array.isArray(moduleIds) || moduleIds.length === 0) fail('at least one visual module is required')
  mkdirSync(outputRoot, { recursive: true })
  const loaded = moduleIds.map(moduleId => loadModuleSpec(specRoot, moduleId))
  writeFileSync(join(outputRoot, 'GenerationSmoke.csproj'), `<Project Sdk="Microsoft.NET.Sdk.Razor">\n  <PropertyGroup>\n    <TargetFramework>net11.0</TargetFramework>\n    <Nullable>enable</Nullable>\n    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>\n  </PropertyGroup>\n  <ItemGroup>\n    <FrameworkReference Include="Microsoft.AspNetCore.App" />\n  </ItemGroup>\n</Project>\n`)
  const generated = []
  for (const module of loaded) {
    const component = `${identifier(module.moduleId)}Smoke`
    const scenarios = module.scenarios.scenarios.map(scenario => `  <section data-gallery-scenario="${razorEncode(scenario.id)}" data-gallery-surface="${razorEncode(scenario.surface)}"><h2>${razorEncode(scenario.name)}</h2></section>`).join('\n')
    writeFileSync(join(outputRoot, `${component}.razor`), `<div data-generation-smoke-module="${razorEncode(module.moduleId)}">\n${scenarios}\n</div>\n`)
    generated.push({ moduleId: module.moduleId, component, scenarios: module.scenarios.scenarios.length })
  }
  writeFileSync(join(outputRoot, 'generation-smoke.json'), `${JSON.stringify({ schemaVersion: 1, generated }, null, 2)}\n`)
  return generated
}

function parseArguments(argv) {
  const moduleIds = []
  for (let index = 0; index < argv.length; index += 1) {
    if (argv[index] === '--module' && argv[index + 1]) moduleIds.push(argv[++index])
    else fail(`unknown or incomplete argument ${argv[index]}`)
  }
  return moduleIds
}

// Ticket 289: this step runs a Release `dotnet build` inside its scratch tree as a child of the
// phase-4 receipt, so a kill of the receipt orphaned a multi-GB obj/bin tree that nothing swept.
// Same three lines as the receipt and the consumer step: sweep the prefix at start, claim the tree
// with a pid file, remove it on a signal as well as on the normal `finally`.
const GENERATION_SMOKE_SCRATCH_PREFIX = 'harborline-generation-smoke-'

export function runGenerationSmoke({ root = repositoryRoot, moduleIds, keep = false }) {
  for (const removed of sweepStaleScratchTrees(GENERATION_SMOKE_SCRATCH_PREFIX)) {
    process.stderr.write(`removed stale generation-smoke scratch tree (owner gone, older than 2h): ${removed}\n`)
  }
  const scratch = mkdtempSync(join(tmpdir(), GENERATION_SMOKE_SCRATCH_PREFIX))
  writeScratchPidFile(scratch)
  const disposeSignalCleanup = cleanUpScratchOnSignal(() => { if (!keep) rmSync(scratch, { recursive: true, force: true }) })
  try {
    const generated = generateSmokeProject({ specRoot: resolve(root, 'specs/modules/ui'), moduleIds, outputRoot: scratch })
    const dotnet = resolvePinnedDotnet(root)
    const build = spawnSync(dotnet.executable, ['build', join(scratch, 'GenerationSmoke.csproj'), '--configuration', 'Release', '--nologo', '-v:minimal'], {
      cwd: scratch, encoding: 'utf8', maxBuffer: 32 * 1024 * 1024,
      env: { ...process.env, ...runnerEnvironment },
    })
    if (build.status !== 0) fail(`generated project did not compile\n${`${build.stdout}\n${build.stderr}`.trim()}`)
    return { schemaVersion: 1, status: 'PASS', dotnetSdk: dotnet.version, modules: generated.length, scenarios: generated.reduce((sum, item) => sum + item.scenarios, 0), generated }
  } finally {
    disposeSignalCleanup()
  }
}

if (import.meta.main) {
  try {
    process.stdout.write(`${JSON.stringify(runGenerationSmoke({ moduleIds: parseArguments(process.argv.slice(2)) }), null, 2)}\n`)
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.stack : String(error)}\n`)
    process.exitCode = 1
  }
}
