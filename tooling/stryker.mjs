#!/usr/bin/env node
// Stryker.NET across every .NET test project (control T-720; PROC-0002 "Mutation evidence as an artefact").
//   node tooling/stryker.mjs check   every test project has a stryker-config.json beside it or an entry in
//                                    tooling/stryker-exclusions.json; every config names one of its test project's
//                                    ProjectReferences, which exists, and holds the standard (since origin/main, json
//                                    reporter, low = max(60, break), high = max(80, break), break >= the project's baseline in
//                                    tooling/stryker-baselines.json); every source project a test references is
//                                    mutated by some config or excluded. No silent gaps.
//   node tooling/stryker.mjs run     PR mode: mutate each project whose .cs/.razor source differs from origin/main,
//                                    report survivors on changed lines, fail only if 0 mutants were tested (Q43).
//   node tooling/stryker.mjs full    scheduled mode: every mutant of every project; fail on 0 tested or a score
//                                    below the project's recorded baseline.
//   node tooling/stryker.mjs baseline   a full run that records each project's baseline and sets its break.
// Any of them takes a test-project path fragment to select projects, and `-- <args>` passed on to Stryker.
// Stryker's exit code is not evidence: 5.0.0 exits 0 having mutated nothing (T-720 spike, 2026-09-24), so every mode
// reads the report. Run it from a plain clone: in a linked worktree `since` diffs the main checkout, marks every
// mutant Ignored and exits 0 (the report assertion catches that too).
import {execFileSync, spawnSync} from 'node:child_process'
import {existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import path from 'node:path'

const root = path.resolve(import.meta.dirname, '..')
const exclusionsFile = 'tooling/stryker-exclusions.json', baselinesFile = 'tooling/stryker-baselines.json'
const testProjectMarker = /Include="(Microsoft\.NET\.Test\.Sdk|xunit|MSTest[\w.]*|NUnit)"/
const git = (...args) => execFileSync('git', ['-C', root, ...args], {encoding: 'utf8'}).split(/\r?\n/).filter(Boolean)
const read = file => existsSync(path.join(root, file)) ? readFileSync(path.join(root, file), 'utf8') : undefined

export const isTestProject = text => testProjectMarker.test(text)

// Tested mutants in the rewritten Razor output, i.e. in #line spans of .razor files. Ruling 91 (Q40): a Razor project
// whose run tested none of them mutated no Razor, however many .cs mutants it tested.
export const razorTested = report => reportCounts({files: Object.fromEntries(Object.entries(report?.files ?? {})
  .filter(([file]) => file.replaceAll('\\', '/').includes('/stryker-razor/')))}).tested

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
export function configProblems({testProjects, exclusions, baselines = {}, readFile}) {
  const problems = [], mutated = new Set()
  for (const test of Object.keys(baselines)) if (!testProjects.includes(test)) problems.push(`${baselinesFile}: ${test} is not a test project`)
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
    // Owner ruling 2026-09-26: break starts at the project's measured baseline and only rises. Q45 option 1: low is
    // max(60, break) and high is max(80, break) (ruling 96), exactly; Stryker refuses break > low > high.
    const {high, low, break: breakAt} = config.thresholds ?? {}, baseline = baselines[test]
    if (!Number.isInteger(baseline?.break)) problems.push(`${test}: no measured baseline in ${baselinesFile}; run node tooling/stryker.mjs baseline`)
    else if (!(breakAt >= baseline.break)) problems.push(`${configPath}: break ${breakAt} is below the recorded baseline ${baseline.break}`)
    if (low !== Math.max(60, breakAt) || high !== Math.max(80, breakAt)) problems.push(`${configPath}: thresholds must be low = max(60, break) = ${Math.max(60, breakAt)} and high = max(80, break) = ${Math.max(80, breakAt)} (Q45, ruling 96)`)
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
  return {testProjects, exclusions: JSON.parse(read(exclusionsFile)), baselines: JSON.parse(read(baselinesFile) ?? '{}'), readFile: read}
}

// Full mode holds a project to the higher of its recorded baseline and its configured break. The checker already
// refuses a configured break below the baseline; a break raised by hand must bind too.
export const fullModeBreak = (baseline, config) => Math.max(baseline?.break ?? 0, config?.thresholds?.break ?? 0)

// The thresholds a measured score implies: break is its floor, low = max(60, break) (Q45), high = max(80, break) (ruling 96).
export function thresholdsFor(score) {
  const breakAt = Math.floor(score ?? 0)
  return {high: Math.max(80, breakAt), low: Math.max(60, breakAt), break: breakAt}
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
  const text = generated.replace(/^\uFEFF/, '').replace(/^\/\/ <auto-generated\/>\r?\n/m, '')
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
// (the file it mutates is under obj/, not in git), so the run gets its own mutate list: what changed, or (changed
// undefined, for a baseline) every .cs and every .razor span.
function razorRun(target, changed) {
  dotnet('build', target, '-c', 'Debug', '-nologo', '-v', 'q', '-p:EmitCompilerGeneratedFiles=true')
  const projectDirectory = path.join(root, path.posix.dirname(target))
  const intermediate = path.join(projectDirectory, dotnet('msbuild', target, '-nologo', '-getProperty:IntermediateOutputPath', '-p:Configuration=Debug').trim())
  const generated = path.join(intermediate, 'generated', 'Microsoft.CodeAnalysis.Razor.Compiler'), plain = path.join(intermediate, 'stryker-razor')
  rmSync(plain, {recursive: true, force: true})
  // Full mode lists the tracked .cs sources by name rather than '**/*.cs' plus '!**/stryker-razor/**': Stryker lets an
  // exclude beat every include, so that negation silently dropped all the .razor spans (T-720 baseline, 2026-09-26).
  const sources = changed ?? git('ls-files', '--', ...sourceDirectories(target, read(target)).map(directory => `${directory}/*.cs`))
  const mutate = sources.filter(file => file.endsWith('.cs') && !/\.tests\//.test(file)).map(file => `**/${path.posix.basename(file)}`)
  for (const file of readdirSync(generated, {recursive: true}).filter(name => name.endsWith('.g.cs'))) {
    const {source, text, spans} = plainRazor(readFileSync(path.join(generated, file), 'utf8'))
    const destination = path.join(plain, file.replace(/\.g\.cs$/, '.cs'))
    mkdirSync(path.dirname(destination), {recursive: true})
    writeFileSync(destination, text)
    if (spans.length && source && (!changed || changed.includes(path.relative(root, source).replaceAll('\\', '/')))) {
      mutate.push(`**/stryker-razor/**/${path.basename(destination)}${spans.map(([from, to]) => `{${from}..${to}}`).join('')}`)
    }
  }
  return {mutate, env: {
    CustomAfterMicrosoftCommonTargets: path.join(root, 'tooling', 'stryker-razor.targets'),
    HarborlineStrykerRazorProject: path.join(root, target)}}
}

// One Stryker run for a configured test project. changed: the source files changed since origin/main (PR mode), or
// undefined for a full run over every mutant. Stryker never judges the score itself (break 0 here): PR mode reports
// and does not compare (owner ruling Q43), and full mode compares against the baselines file below.
function mutateProject(test, changed, strykerArgs) {
  const testDirectory = path.posix.dirname(test), target = targetOf(test, read), config = configOf(test, read)
  const output = path.join(root, 'StrykerOutput', path.posix.basename(testDirectory))
  rmSync(output, {recursive: true, force: true})
  mkdirSync(output, {recursive: true})
  const razor = read(target).includes('Microsoft.NET.Sdk.Razor') ? razorRun(target, changed) : undefined
  const override = {
    ...(razor || !changed ? {since: {enabled: false}} : {}),
    ...(razor ? {mutate: razor.mutate} : {}),
    thresholds: {...config.thresholds, break: 0}}
  const configFile = path.join(output, 'stryker-config.json')
  writeFileSync(configFile, JSON.stringify({'stryker-config': {...config, ...override}}, null, 2))
  const stryker = spawnSync('dotnet', ['stryker', '--output', output, '--config-file', configFile, ...msbuildArgs(), ...strykerArgs],
    {cwd: path.join(root, testDirectory), stdio: 'inherit', env: {...process.env, ...razor?.env}})
  const reportPath = path.join(output, 'reports', 'mutation-report.json')
  const report = existsSync(reportPath) ? JSON.parse(readFileSync(reportPath, 'utf8')) : undefined
  const counts = report && reportCounts(report)
  console.log(`${test}: exit ${stryker.status}, ${JSON.stringify(counts ?? 'no json report')}, report ${reportPath}`)
  return {status: stryker.status, counts, report, reportPath, razor: Boolean(razor)}
}

// Lines added or changed since origin/main, per repository-relative file, from `git diff -U0` hunk headers.
export function changedLines(diff) {
  const lines = {}
  let file
  for (const line of diff.split(/\r?\n/)) {
    if (line.startsWith('+++ ')) file = line.startsWith('+++ b/') ? line.slice(6) : undefined
    const hunk = /^@@ -\S+ \+(\d+)(?:,(\d+))? @@/.exec(line)
    if (file && hunk) {
      const start = Number(hunk[1]), count = hunk[2] === undefined ? 1 : Number(hunk[2])
      for (let n = start; n < start + count; n += 1) (lines[file] ??= new Set()).add(n)
    }
  }
  return lines
}

// Review feedback (Q43): the Survived and NoCoverage mutants on changed lines. Generated Razor files are not in git,
// so every survivor in them is listed; they were only mutated because their .razor changed.
// Mutants tested in the changed source itself: a changed .cs, or the Razor output when a .razor changed. A PR that also
// changes tests can have Stryker test mutants elsewhere, so the report's overall tested count proves nothing here.
export function testedInChanged(report, changed, repositoryRoot = root) {
  const razorChanged = changed.some(name => name.endsWith('.razor'))
  const files = Object.fromEntries(Object.entries(report?.files ?? {}).filter(([file]) => {
    const relative = path.relative(repositoryRoot, path.resolve(repositoryRoot, file)).replaceAll('\\', '/')
    return changed.includes(relative) || (razorChanged && relative.includes('/stryker-razor/'))
  }))
  return reportCounts({files}).tested
}

export function survivorsOnChangedLines(report, lines, repositoryRoot = root) {
  const found = []
  for (const [file, {mutants = []}] of Object.entries(report.files ?? {})) {
    const relative = path.relative(repositoryRoot, path.resolve(repositoryRoot, file)).replaceAll('\\', '/')
    const generated = relative.includes('/stryker-razor/')
    for (const mutant of mutants) {
      if (mutant.status !== 'Survived' && mutant.status !== 'NoCoverage') continue
      if (!generated && !lines[relative]?.has(mutant.location?.start?.line)) continue
      found.push({file: relative, line: mutant.location?.start?.line, status: mutant.status, mutator: mutant.mutatorName, replacement: mutant.replacement})
    }
  }
  return found
}

function summarize(markdown) {
  if (process.env.GITHUB_STEP_SUMMARY) writeFileSync(process.env.GITHUB_STEP_SUMMARY, `${markdown}\n`, {flag: 'a'})
  console.log(markdown)
}

const selected = (repo, only) => repo.testProjects.filter(test => !(test in repo.exclusions) && (!only || test.includes(only)))

// PR mode: mutate what changed, report the survivors on changed lines, fail only when mutable code changed and 0 mutants were tested.
function run(repo, only, strykerArgs) {
  let failed = false
  for (const test of selected(repo, only)) {
    const target = targetOf(test, read), targetText = read(target), razor = targetText.includes('Microsoft.NET.Sdk.Razor')
    const changed = git('diff', '--name-only', 'origin/main', '--', ...sourceDirectories(target, targetText).flatMap(directory =>
      razor ? [`${directory}/*.cs`, `${directory}/*.razor`] : [`${directory}/*.cs`])).filter(file => !/\.tests\//.test(file))
    if (!changed.length) { console.log(`${test}: no source change in ${path.posix.dirname(target)} since origin/main, skipped`); continue }
    const {counts, report, razor: isRazor} = mutateProject(test, changed, strykerArgs)
    if (isRazor && changed.some(file => file.endsWith('.razor')) && !razorTested(report)) {
      summarize(`### ${test}\n\n**FAIL**: a .razor file changed but 0 Razor mutants were tested.`); failed = true; continue
    }
    // ponytail: a changed .cs file with nothing mutable in it (an interface, a comment) fails here; judge it by the report.
    if (!testedInChanged(report, changed)) { summarize(`### ${test}\n\n**FAIL**: ${changed.length} changed source file(s) but 0 mutants tested in them.`); failed = true; continue }
    const survivors = survivorsOnChangedLines(report, changedLines(git('diff', '-U0', 'origin/main', '--', ...changed).join('\n')))
    summarize([`### ${test}`, '', `${counts.tested} mutants tested, score ${counts.score} % (advisory: PR runs are not compared with the project floor).`, '',
      survivors.length ? '| file | line | status | mutator | replacement |\n|---|---|---|---|---|' : 'No surviving or uncovered mutant on a changed line.',
      ...survivors.map(s => `| ${s.file} | ${s.line} | ${s.status} | ${s.mutator} | \`${String(s.replacement ?? '').replaceAll('|', '\\|').replaceAll('\n', ' ').slice(0, 80)}\` |`)].join('\n'))
  }
  return failed
}

// Full mode (the scheduled run on main): every mutant of every selected project; fail on 0 tested or a score below
// the project's recorded baseline. `record` measures instead of judging and writes the baseline and the config's break.
function full(repo, only, strykerArgs, record) {
  let failed = false
  const commit = git('rev-parse', 'HEAD')[0], measured = new Date().toISOString().slice(0, 10)
  for (const test of selected(repo, only)) {
    const {counts, report, razor: isRazor} = mutateProject(test, undefined, strykerArgs)
    if (!counts?.tested) { summarize(`- ${test}: **FAIL**, 0 mutants tested`); failed = true; continue }
    if (isRazor && !razorTested(report)) { summarize(`- ${test}: **FAIL**, Razor project but 0 Razor mutants tested; nothing recorded`); failed = true; continue }
    if (!record) {
      const floor = fullModeBreak(repo.baselines[test], configOf(test, read)), below = !(counts.score >= floor)
      summarize(`- ${test}: ${counts.tested} tested, score ${counts.score} %, break ${floor}${below ? ' **FAIL**' : ''}`)
      if (below) failed = true
      continue
    }
    const thresholds = thresholdsFor(counts.score)
    const baselines = JSON.parse(read(baselinesFile) ?? '{}')
    baselines[test] = {break: thresholds.break, score: counts.score, tested: counts.tested, total: counts.total,
      detected: counts.detected, undetected: counts.undetected, measured, commit}
    writeFileSync(path.join(root, baselinesFile), `${JSON.stringify(Object.fromEntries(Object.entries(baselines).sort()), null, 2)}\n`)
    const configPath = path.join(root, path.posix.dirname(test), 'stryker-config.json'), config = JSON.parse(readFileSync(configPath, 'utf8'))
    config['stryker-config'].thresholds = thresholds
    writeFileSync(configPath, `${JSON.stringify(config, null, 2)}\n`)
  }
  return failed
}

if (import.meta.main) {
  const argv = process.argv.slice(2), dashes = argv.indexOf('--')
  const [command, only] = dashes < 0 ? argv : argv.slice(0, dashes), strykerArgs = dashes < 0 ? [] : argv.slice(dashes + 1)
  if (!['check', 'run', 'full', 'baseline'].includes(command)) {
    throw new Error('usage: node tooling/stryker.mjs check | run | full | baseline [test-project-path-fragment] [-- stryker args]')
  }
  const repo = repository()
  if (command === 'baseline') process.exit(full(repo, only, strykerArgs, true) ? 1 : 0)
  const problems = configProblems(repo)
  problems.forEach(problem => console.error(problem))
  if (problems.length) process.exit(1)
  console.log(`Stryker config: ${repo.testProjects.length} test project(s), each configured or excluded: PASS`)
  if (command === 'run' && run(repo, only, strykerArgs)) process.exit(1)
  if (command === 'full' && full(repo, only, strykerArgs, false)) process.exit(1)
}
