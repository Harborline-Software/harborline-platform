#!/usr/bin/env node
// Stryker.NET across every .NET test project (control T-720; PROC-0002 "Mutation evidence as an artefact").
//   node tooling/stryker.mjs check   every test project has a stryker-config.json beside it or an entry in
//                                    tooling/stryker-exclusions.json; every config names one of its test project's
//                                    ProjectReferences, which exists, and holds the standard (since origin/main,
//                                    high 80 / low 60 / break 60, json reporter); every source project a test
//                                    references is mutated by some config or excluded. No silent gaps.
//   node tooling/stryker.mjs run     check, then mutate each configured project whose .cs source differs from
//                                    origin/main, and fail unless its json report shows mutants tested.
// Stryker's exit code is not evidence: 5.0.0 exits 0 having mutated nothing (T-720 spike, 2026-09-24), so `run`
// reads the report. Run it from a plain clone: in a linked worktree `since` diffs the main checkout, marks every
// mutant Ignored and exits 0 (the report assertion below catches that too).
import {execFileSync, spawnSync} from 'node:child_process'
import {existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const exclusionsFile = 'tooling/stryker-exclusions.json'
const testProjectMarker = /Include="(Microsoft\.NET\.Test\.Sdk|xunit|MSTest[\w.]*|NUnit)"/
const git = (...args) => execFileSync('git', ['-C', root, ...args], {encoding: 'utf8'}).split(/\r?\n/).filter(Boolean)
const read = file => existsSync(path.join(root, file)) ? readFileSync(path.join(root, file), 'utf8') : undefined

export const isTestProject = text => testProjectMarker.test(text)

// Status names from the mutation-testing-report schema; the score is Stryker's own: detected / (detected + undetected).
export function reportCounts(report) {
  const mutants = Object.values(report.files ?? {}).flatMap(file => file.mutants ?? [])
  const count = (...statuses) => mutants.filter(mutant => statuses.includes(mutant.status)).length
  const detected = count('Killed', 'Timeout'), undetected = count('Survived', 'NoCoverage')
  return {total: mutants.length, tested: count('Killed', 'Survived', 'Timeout', 'RuntimeError'), detected, undetected,
    score: detected + undetected ? Math.floor(10000 * detected / (detected + undetected)) / 100 : null}
}

const references = (testProject, text) => [...text.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)]
  .map(match => path.posix.normalize(`${path.posix.dirname(testProject)}/${match[1].replaceAll('\\', '/')}`))

const configOf = (test, readFile) => {
  const raw = readFile(`${path.posix.dirname(test)}/stryker-config.json`)
  return raw === undefined ? undefined : JSON.parse(raw)['stryker-config'] ?? {}
}

// The ProjectReference a configured test project mutates, or undefined when the config names none of them.
export const targetOf = (test, readFile) => {
  const config = configOf(test, readFile)
  return references(test, readFile(test)).find(ref => path.posix.basename(ref) === config?.project)
}

// testProjects: repository-relative csproj paths; readFile(path) returns the text or undefined when absent.
export function configProblems({testProjects, exclusions, readFile}) {
  const problems = [], mutated = new Set()
  for (const [project, reason] of Object.entries(exclusions)) {
    if (readFile(project) === undefined) problems.push(`${exclusionsFile}: ${project} does not exist`)
    if (!String(reason ?? '').trim()) problems.push(`${exclusionsFile}: ${project} has no reason`)
  }
  for (const test of testProjects) {
    const configPath = `${path.posix.dirname(test)}/stryker-config.json`, config = configOf(test, readFile)
    if (config === undefined) {
      if (!(test in exclusions)) problems.push(`${test}: no ${configPath} and no entry in ${exclusionsFile}`)
      continue
    }
    if (test in exclusions) problems.push(`${test}: has both ${configPath} and an exclusion`)
    const target = targetOf(test, readFile)
    if (!target) problems.push(`${configPath}: project "${config.project}" is not a ProjectReference of ${test}`)
    else if (readFile(target) === undefined) problems.push(`${configPath}: project ${target} does not exist`)
    else mutated.add(target)
    const {high, low, break: breakAt} = config.thresholds ?? {}
    if (high !== 80 || low !== 60 || breakAt !== 60) problems.push(`${configPath}: thresholds must be high 80, low 60, break 60`)
    if (!config.reporters?.includes('json')) problems.push(`${configPath}: reporters must include json`)
    if (config.reporters?.includes('html')) problems.push(`${configPath}: reporters are json only (owner, 2026-09-26)`)
    if (config.since?.enabled !== true || config.since?.target !== 'origin/main') problems.push(`${configPath}: since must be enabled against origin/main`)
  }
  // A source project that tests exercise but no config mutates is the gap a per-test-project check cannot see.
  for (const test of testProjects) {
    for (const ref of references(test, readFile(test) ?? '')) {
      const text = readFile(ref)
      if (text === undefined || isTestProject(text) || mutated.has(ref) || ref in exclusions) continue
      problems.push(`${ref}: referenced by ${test} but mutated by no stryker-config.json and not in ${exclusionsFile}`)
      mutated.add(ref)
    }
  }
  return problems
}

export function repository() {
  const testProjects = git('ls-files', '*.csproj').filter(file => isTestProject(read(file)))
  return {testProjects, exclusions: JSON.parse(read(exclusionsFile)), readFile: read}
}

// Buildalyzer reads TargetFramework literally from the csproj; ours comes from Directory.Build.props, so it guesses
// .NET Framework and, on a host with Visual Studio Build Tools (winbox), runs that MSBuild.exe, which cannot resolve
// Microsoft.NET.Sdk: "No project found" (stryker-net#3758). Point it at the global.json SDK's own MSBuild.
function msbuildArgs() {
  if (process.platform !== 'win32') return []
  const version = execFileSync('dotnet', ['--version'], {cwd: root, encoding: 'utf8'}).trim()
  const line = execFileSync('dotnet', ['--list-sdks'], {cwd: root, encoding: 'utf8'}).split(/\r?\n/).find(sdk => sdk.startsWith(`${version} `))
  const directory = /\[(.*)\]/.exec(line ?? '')?.[1]
  if (!directory) throw new Error(`dotnet --list-sdks does not list ${version}`)
  return ['--msbuild-path', path.join(directory, version, 'MSBuild.exe')]
}

// The directories a project compiles from: its own, plus any it links (<Compile Include="../hlp.ui.x/**/*.cs" />).
export const sourceDirectories = (project, text) => [path.posix.dirname(project), ...[...text.matchAll(/<(?:Compile|RazorComponent)\s+Include="([^"*]+)\/\*\*/g)]
  .map(match => path.posix.normalize(`${path.posix.dirname(project)}/${match[1].replaceAll('\\', '/')}`))]

// Generator output -> plain C# Stryker will mutate: drop the BOM and the auto-generated marker (and the script drops
// the .g.cs name; Stryker skips all three). Return the .razor the file came from, named by its #pragma checksum line,
// and the character spans #line maps to that .razor: the author's code, not the generator's render scaffolding.
export function plainRazor(generated) {
  const text = generated.replace(/^﻿/, '').replace(/^\/\/ <auto-generated\/>\r?\n/m, '')
  const source = /^#pragma checksum "([^"]+)"/.exec(text)?.[1]
  const spans = []
  let offset = 0, start
  for (const line of text.split(/(?<=\n)/)) {
    if (line.startsWith('#line')) {
      if (start !== undefined) spans.push([start, offset])
      start = /^#line \(.*"[^"]+\.razor"/.test(line) ? offset + line.length : undefined
    }
    offset += line.length
  }
  if (start !== undefined) spans.push([start, offset])
  return {source, text, spans: spans.filter(([from, to]) => to > from)}
}

const dotnet = (...args) => execFileSync('dotnet', args, {cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024})

// Q40 option 3: Razor projects are mutated. Emit the Razor generator's output with the real SDK, rewrite it as plain C#
// into obj/.../stryker-razor/, and have tooling/stryker-razor.targets compile that instead of running the generator
// (Stryker's Roslyn 5.9 cannot run the RC1 generator, stryker-net#3813). Stryker's `since` cannot see a changed .razor
// (the file it mutates is under obj/, not in git), so the run gets its own config: since off, mutate = what changed.
function razorRun(target, config, changed, output) {
  dotnet('build', target, '-c', 'Debug', '-nologo', '-v', 'q', '-p:EmitCompilerGeneratedFiles=true')
  const projectDirectory = path.join(root, path.posix.dirname(target))
  const intermediate = path.join(projectDirectory, dotnet('msbuild', target, '-nologo', '-getProperty:IntermediateOutputPath', '-p:Configuration=Debug').trim())
  const generated = path.join(intermediate, 'generated', 'Microsoft.CodeAnalysis.Razor.Compiler'), plain = path.join(intermediate, 'stryker-razor')
  rmSync(plain, {recursive: true, force: true})
  const mutate = changed.filter(file => file.endsWith('.cs')).map(file => `**/${path.posix.basename(file)}`)
  for (const file of readdirSync(generated, {recursive: true}).filter(name => name.endsWith('.g.cs'))) {
    const {source, text, spans} = plainRazor(readFileSync(path.join(generated, file), 'utf8'))
    const destination = path.join(plain, file.replace(/\.g\.cs$/, '.cs'))
    mkdirSync(path.dirname(destination), {recursive: true})
    writeFileSync(destination, text)
    if (spans.length && source && changed.includes(path.relative(root, source).replaceAll('\\', '/'))) {
      mutate.push(`**/stryker-razor/**/${path.basename(destination)}${spans.map(([from, to]) => `{${from}..${to}}`).join('')}`)
    }
  }
  const configFile = path.join(output, 'stryker-config.json')
  mkdirSync(output, {recursive: true})
  writeFileSync(configFile, JSON.stringify({'stryker-config': {...config, since: {enabled: false}, mutate}}, null, 2))
  return {args: ['--config-file', configFile], env: {
    CustomAfterMicrosoftCommonTargets: path.join(root, 'tooling', 'stryker-razor.targets'),
    HarborlineStrykerRazorProject: path.join(root, target)}}
}

function run(repo, only) {
  let failed = false
  const tests = repo.testProjects.filter(test => !(test in repo.exclusions) && (!only || test.includes(only)))
  for (const test of tests) {
    const testDirectory = path.posix.dirname(test), target = targetOf(test, read), targetText = read(target)
    const razor = targetText.includes('Microsoft.NET.Sdk.Razor')
    // ponytail: a changed .cs file with nothing mutable in it (an interface, a comment) fails the assertion below; judge it by the report.
    const changed = git('diff', '--name-only', 'origin/main', '--', ...sourceDirectories(target, targetText).flatMap(directory =>
      razor ? [`${directory}/*.cs`, `${directory}/*.razor`] : [`${directory}/*.cs`])).filter(file => !/\.tests\//.test(file))
    if (!changed.length) { console.log(`${test}: no source change in ${path.posix.dirname(target)} since origin/main, skipped`); continue }
    const output = path.join(root, 'StrykerOutput', path.posix.basename(testDirectory))
    rmSync(output, {recursive: true, force: true})
    const extra = razor ? razorRun(target, configOf(test, read), changed, output) : {args: [], env: {}}
    const stryker = spawnSync('dotnet', ['stryker', '--output', output, ...msbuildArgs(), ...extra.args],
      {cwd: path.join(root, testDirectory), stdio: 'inherit', env: {...process.env, ...extra.env}})
    const reportPath = path.join(output, 'reports', 'mutation-report.json')
    const counts = existsSync(reportPath) ? reportCounts(JSON.parse(readFileSync(reportPath, 'utf8'))) : undefined
    console.log(`${test}: exit ${stryker.status}, ${JSON.stringify(counts ?? 'no json report')}`)
    if (!counts?.tested) { console.error(`${test}: ${changed.length} changed source file(s) but 0 mutants tested`); failed = true }
    if (stryker.status !== 0) failed = true
  }
  return failed
}

if (import.meta.main) {
  const [command, only] = process.argv.slice(2)
  if (command !== 'check' && command !== 'run') throw new Error('usage: node tooling/stryker.mjs check | run [test-project-path-fragment]')
  const repo = repository(), problems = configProblems(repo)
  problems.forEach(problem => console.error(problem))
  if (problems.length) process.exit(1)
  console.log(`Stryker config: ${repo.testProjects.length} test project(s), each configured or excluded: PASS`)
  if (command === 'run' && run(repo, only)) process.exit(1)
}
