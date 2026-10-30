#!/usr/bin/env node
// Stryker.NET across every .NET test project (control T-720; PROC-0002 "Mutation evidence as an artefact").
//   node tooling/stryker.mjs check   every test project has a stryker-config.json beside it or an entry in
//                                    tooling/stryker-exclusions.json; every config names one of its test project's
//                                    ProjectReferences, which exists, and holds the standard (since origin/main, json
//                                    reporter, low = max(60, break), high = max(80, break), break >= the project's baseline in
//                                    tooling/stryker-baselines.json); every source project a test references is
//                                    mutated by some config or excluded. No silent gaps.
//   node tooling/stryker.mjs run     PR mode: for each project whose .cs/.razor source differs from origin/main, mutate
//                                    only the changed files that still exist; fail when their score is below
//                                    max(baseline, configured break), or when they have mutants and none was tested;
//                                    list survivors on changed lines as feedback.
//   node tooling/stryker.mjs full    weekly/manual mode: every mutant of every project; fail on 0 tested or a score
//                                    below the project's recorded baseline. This is the whole-project floor.
//   node tooling/stryker.mjs baseline   a full run that records each project's baseline and sets its break.
// Any of them takes a test-project path fragment to select projects, and `-- <args>` passed on to Stryker.
// Stryker's exit code is not evidence: 5.0.0 exits 0 having mutated nothing (T-720 spike, 2026-09-24), so every mode
// reads the report. Run it from a plain clone: in a linked worktree `since` diffs the main checkout, marks every
// mutant Ignored and exits 0 (the report assertion catches that too).
import {execFileSync, spawn} from 'node:child_process'
import {closeSync, existsSync, fstatSync, fsyncSync, mkdirSync, mkdtempSync, openSync, readdirSync, readFileSync, readSync, rmSync, writeFileSync} from 'node:fs'
import {createHash} from 'node:crypto'
import {StringDecoder} from 'node:string_decoder'
import {cleanUpScratchOnSignal, killProcessTreeSync} from './resolve-command.mjs'
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

// Selection concerns the whole project's score, not just new lines: deletions change the remaining behaviour too.
// Disable rename folding so a move selects both the old and new owners. NUL delimiters preserve spaces, tabs and
// non-ASCII names without Git's pathname quoting. Reconcile base and current ownership: deleting an Include must
// not erase the old owner's source change. The project file itself counts: an Include or Remove edit alone changes
// what compiles. Test-only changes stay outside PR #236's source-change policy.
export function changedSourceFiles(project, text, repositoryRoot = root, base = 'origin/main') {
  const command = (...args) => execFileSync('git', ['-C', repositoryRoot, ...args],
    {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']})
  // Resolve the base first: a bad ref is an error, not an absent project. A new project legitimately has no base
  // blob; a deleted project has only its base blob. The normal config checker still refuses deleted targets.
  const baseCommit = command('rev-parse', '--verify', '--end-of-options', `${base}^{commit}`).trim()
  const existed = command('ls-tree', '--name-only', '-z', baseCommit, '--', project).length > 0
  const before = existed ? command('show', `${baseCommit}:${project}`) : undefined
  const versions = [before, text].filter(version => version !== undefined)
  if (!versions.length) throw new Error(`${project}: absent from both ${base} and the current tree`)
  const razor = versions.some(version => version.includes('Microsoft.NET.Sdk.Razor'))
  const directories = [...new Set(versions.flatMap(version => sourceDirectories(project, version)))]
  const patterns = [project, ...directories.flatMap(directory =>
    razor ? [`${directory}/*.cs`, `${directory}/*.razor`] : [`${directory}/*.cs`])]
  return command('diff', '--name-only', '-z', '--no-renames', baseCommit, '--', ...patterns)
    .split('\0').filter(file => file && !/\.tests\//.test(file))
}

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
  // MSBuild's default BaseIntermediateOutputPath is `obj\` on every OS, so off Windows the property reads `obj\Debug/...`.
  const intermediate = path.join(projectDirectory, dotnet('msbuild', target, '-nologo', '-getProperty:IntermediateOutputPath', '-p:Configuration=Debug').trim().replaceAll('\\', '/'))
  const generated = path.join(intermediate, 'generated', 'Microsoft.CodeAnalysis.Razor.Compiler'), plain = path.join(intermediate, 'stryker-razor')
  rmSync(plain, {recursive: true, force: true})
  // Full mode lists the tracked .cs sources by name rather than '**/*.cs' plus '!**/stryker-razor/**': Stryker lets an
  // exclude beat every include, so that negation silently dropped all the .razor spans (T-720 baseline, 2026-09-26).
  const sources = changed ?? git('ls-files', '--', ...sourceDirectories(target, read(target)).map(directory => `${directory}/*.cs`))
  const mutate = sources.filter(file => file.endsWith('.cs') && !/\.tests\//.test(file))
    .map(file => changed ? path.posix.relative(path.posix.dirname(target), file) : `**/${path.posix.basename(file)}`)
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

function writeConsole(bytes, signal) {
  const stream = process.stdout
  return new Promise((resolve, reject) => {
    let returned = false, callbackDone = false, drained = false, needsDrain = false, settled = false, failed = false
    const unguard = () => {
      stream.removeListener('error', onError)
      stream.removeListener('close', onClose)
      stream.removeListener('drain', onDrain)
    }
    const settle = error => {
      if (error) failed = true
      if (settled) return
      settled = true
      signal?.removeEventListener('abort', onAbort)
      if (error) reject(error)
      else resolve()
      if (returned && callbackDone && (!needsDrain || drained) && !failed) unguard()
    }
    const complete = () => {
      if (returned && callbackDone && (!needsDrain || drained) && !failed) { settle(); unguard() }
    }
    const onError = error => { settle(error); unguard() }
    const onClose = () => { settle(new Error('Console closed before delivery')); unguard() }
    const onDrain = () => { drained = true; complete() }
    const onAbort = () => settle()
    if (signal?.aborted) { resolve(); return }
    stream.on('error', onError)
    stream.on('close', onClose)
    stream.on('drain', onDrain)
    signal?.addEventListener('abort', onAbort, {once: true})
    try {
      needsDrain = !stream.write(bytes, error => {
        callbackDone = true
        if (error) settle(error)
        else complete()
        // A failed callback precedes the error event; cancellation also retains this guard until settlement.
      })
      returned = true
      complete()
    } catch (error) { settle(error); unguard() }
  })
}

// One Stryker run for a configured test project. changed: the source files changed since origin/main (PR mode), or
// undefined for a full run over every mutant. Stryker never judges the score itself (break 0 here): the wrapper
// compares the full-project report against the floor in both PR and full modes (owner order 2026-09-30).
export async function mutateProject(test, changed, strykerArgs, command = ['dotnet', 'stryker'], stopProcess = killProcessTreeSync) {
  const testDirectory = path.posix.dirname(test), target = targetOf(test, read), config = configOf(test, read)
  mkdirSync(path.join(root, 'StrykerOutput'), {recursive: true})
  const output = mkdtempSync(path.join(root, 'StrykerOutput', `${path.posix.basename(testDirectory)}-`))
  const diagnostics = path.join(output, 'diagnostics')
  mkdirSync(diagnostics)
  const razor = read(target).includes('Microsoft.NET.Sdk.Razor') ? razorRun(target, changed) : undefined
  // An explicit mutate list, never Stryker's own `since`: the list is exactly the changed files the caller chose.
  const mutate = razor?.mutate ?? changed?.filter(file => file.endsWith('.cs'))
    .map(file => path.posix.relative(path.posix.dirname(target), file))
  const razorPatterns = razor ? razor.mutate.filter(pattern => pattern.includes('/stryker-razor/')).length : 0
  // Stryker reads an empty mutate list as "mutate everything", so a changed-file run with no pattern never starts it.
  if (changed && !mutate.length) return {outcome: {kind: 'completed'}, status: 0, signal: null, empty: true,
    counts: reportCounts({files: {}}), report: {files: {}}, output, razor: Boolean(razor), razorPatterns}
  const override = {
    since: {enabled: false},
    ...(mutate ? {mutate} : {}),
    thresholds: {...config.thresholds, break: 0}}
  const configFile = path.join(output, 'stryker-config.json')
  writeFileSync(configFile, JSON.stringify({'stryker-config': {...config, ...override}}, null, 2))
  const argv = [...command.slice(1), '--output', output, '--config-file', configFile,
    ...(command[0] === 'dotnet' ? msbuildArgs() : []), ...strykerArgs]
  const reportPath = path.join(output, 'reports', 'mutation-report.json')
  const attempt = {phase: 'prepared', child: undefined, timer: undefined, failure: undefined, status: null, signal: null,
    started: Date.now(), bytes: {stdout: 0, stderr: 0, nativeDebug: 0}, logs: new Map(), handles: [], terminal: false}
  const fail = (cause, error) => {
    if (cause === 'observer') attempt.observerFailed = true
    attempt.failure ??= {kind: 'failed', cause, ...(error ? {error: String(error.message ?? error)} : {})}
  }
  const persist = (name, data) => writeFileSync(path.join(diagnostics, name), `${JSON.stringify(data, null, 2)}\n`, {flush: true})
  const append = (fd, bytes) => { writeFileSync(fd, bytes); fsyncSync(fd) }
  const open = (name, flags = 'a') => {
    const fd = openSync(path.join(diagnostics, name), flags)
    attempt.handles.push(fd)
    return fd
  }
  let journal, reader, report, counts, dispose = () => {}, disposeChild = () => {}, interruption
  const delivery = {end: 0, cursor: 0, lineStart: true, active: undefined, stopped: false,
    abort: new AbortController(), buffer: Buffer.alloc(64 * 1024)}
  const prefix = Buffer.from('[stryker-diagnostic] ')
  const stopDelivery = () => { delivery.stopped = true; delivery.abort.abort() }
  const pump = () => {
    if (delivery.active || delivery.stopped) return
    delivery.active = (async () => {
      while (!delivery.stopped && delivery.cursor < delivery.end) {
        const start = delivery.lineStart ? prefix.copy(delivery.buffer) : 0
        const length = readSync(reader, delivery.buffer, start,
          Math.min(delivery.buffer.length - start, delivery.end - delivery.cursor), delivery.cursor)
        if (!length) throw new Error('Diagnostic journal ended before committed bytes')
        const newline = delivery.buffer.subarray(start, start + length).indexOf(10)
        const size = newline < 0 ? length : newline + 1
        await writeConsole(delivery.buffer.subarray(0, start + size), delivery.abort.signal)
        if (delivery.stopped) break
        delivery.cursor += size
        delivery.lineStart = newline >= 0
      }
    })().catch(error => { if (!delivery.stopped) observerFailure(error) }).finally(() => {
      delivery.active = undefined
      if (!delivery.stopped && delivery.cursor < delivery.end) pump()
    })
  }
  const delivered = async () => { while (delivery.active) await delivery.active }
  const decoders = {stdout: new StringDecoder('utf8'), stderr: new StringDecoder('utf8')}
  const emit = (source, payload) => {
    const record = {at: new Date().toISOString(), source, payload}
    const bytes = Buffer.from(`${JSON.stringify(record)}\n`)
    append(journal, bytes)
    delivery.end += bytes.length
    pump()
  }
  const tail = (final = false) => {
    const directory = path.join(output, 'logs')
    if (!existsSync(directory)) return
    for (const name of readdirSync(directory).filter(name => /^log-\d{8}\.txt$/.test(name))) {
      const fd = openSync(path.join(directory, name), 'r')
      try {
        const stat = fstatSync(fd)
        let log = attempt.logs.get(name)
        if (!log || log.ino !== stat.ino || stat.size < log.offset) {
          if (log) emit('nativeDebug', {file: name, line: log.pending + log.decoder.end(), rotation: true})
          log = {ino: stat.ino, offset: 0, pending: '', decoder: new StringDecoder('utf8')}
          attempt.logs.set(name, log)
        }
        const buffer = Buffer.alloc(64 * 1024)
        while (log.offset < stat.size) {
          const length = readSync(fd, buffer, 0, Math.min(buffer.length, stat.size - log.offset), log.offset)
          if (!length) break
          log.offset += length
          attempt.bytes.nativeDebug += length
          log.pending += log.decoder.write(buffer.subarray(0, length))
          const lines = log.pending.split('\n')
          log.pending = lines.pop()
          for (const line of lines) emit('nativeDebug', {file: name, line: line.replace(/\r$/, '')})
        }
        if (final) {
          log.pending += log.decoder.end()
          if (log.pending) emit('nativeDebug', {file: name, line: log.pending})
          log.pending = ''
        }
      } finally { closeSync(fd) }
    }
  }
  const result = () => ({outcome: attempt.failure ?? {kind: 'completed'}, status: attempt.status, signal: attempt.signal,
    pid: attempt.pid, ...(attempt.cleanupUnavailable ? {cleanupUnavailable: true, descendantsUnknown: true} : {}),
    ...(attempt.observerFailed ? {observerFailed: true} : {}),
    counts, report, reportPath, output, razor: Boolean(razor), razorPatterns})
  const terminal = () => {
    if (attempt.terminal) return
    attempt.terminal = true
    const {report: ignored, counts: ignoredCounts, ...summary} = result()
    emit('terminal', {...summary, elapsedMs: Date.now() - attempt.started,
      bytes: attempt.bytes, reportPath})
  }
  const checkpoint = () => {
    try { const {report: ignored, ...summary} = result(); persist('result.json', summary) } catch (error) { fail('observer', error) }
    try { terminal() } catch (error) { fail('observer', error) }
  }
  const stopOwned = () => {
    if (attempt.cleanupUnavailable) return
    const child = attempt.child
    attempt.child = undefined
    let stopped = attempt.phase !== 'draining'
    if (child) {
      try { stopped = child.exitCode === null && child.signalCode === null && stopProcess(child.pid) === true }
      catch { stopped = false }
    }
    if (!stopped) attempt.cleanupUnavailable = true
  }
  const observerFailure = error => {
    fail('observer', error)
    stopDelivery()
    stopOwned()
    if (attempt.cleanupUnavailable) {
      checkpoint()
      disposeChild()
    }
  }
  const signals = Object.fromEntries(['SIGINT', 'SIGTERM'].map(signal => [signal, () => { interruption = signal }]))
  const consoleClosed = () => observerFailure(new Error('Console closed before delivery'))
  try {
    journal = open('progress.jsonl')
    reader = open('progress.jsonl', 'r')
    const raw = {stdout: open('stdout.log'), stderr: open('stderr.log')}
    persist('attempt.json', {at: new Date().toISOString(), test, target, sourceCommit: git('rev-parse', 'HEAD')[0],
      executable: command[0], argv, cwd: path.join(root, testDirectory), output, configFile,
      wrapperSha256: createHash('sha256').update(readFileSync(import.meta.filename)).digest('hex'),
      configSha256: createHash('sha256').update(readFileSync(configFile)).digest('hex')})
    for (const [signal, listener] of Object.entries(signals)) process.once(signal, listener)
    process.stdout.on('error', observerFailure)
    process.stdout.on('close', consoleClosed)
    dispose = cleanUpScratchOnSignal(() => {
      if (!interruption) return
      fail('interrupted')
      attempt.signal ??= interruption
      stopDelivery()
      stopOwned()
      try { tail(true) } catch (error) { fail('observer', error) }
      checkpoint()
      if (attempt.cleanupUnavailable) disposeChild()
    })
    emit('prepared', {test, output, configFile})
    await delivered()
    if (!attempt.observerFailed && !interruption) await new Promise(resolve => {
      const child = spawn(command[0], argv, {cwd: path.join(root, testDirectory), stdio: ['ignore', 'pipe', 'pipe'],
        detached: process.platform !== 'win32', env: {...process.env, ...razor?.env}})
      attempt.child = child
      attempt.pid = child.pid ?? null
      attempt.phase = 'running'
      disposeChild = () => {
        clearInterval(attempt.timer)
        child.stdout.destroy()
        child.stderr.destroy()
        child.unref()
        resolve()
      }
      for (const source of ['stdout', 'stderr']) {
        child[source].on('data', bytes => {
          try {
            append(raw[source], bytes)
            attempt.bytes[source] += bytes.length
            const text = decoders[source].write(bytes)
            if (text) emit(source, text)
          } catch (error) { observerFailure(error) }
        })
        child[source].on('error', observerFailure)
      }
      child.on('error', error => { fail('spawn', error) })
      child.on('exit', (code, signal) => {
        attempt.child = undefined
        if (attempt.cleanupUnavailable) return
        attempt.phase = 'draining'
        attempt.status = code
        attempt.signal ??= signal
        if (signal || code !== 0) fail(signal ? 'signal' : 'exit')
      })
      child.on('close', (code, signal) => {
        attempt.child = undefined
        if (!attempt.cleanupUnavailable) {
          attempt.phase = 'closed'
          attempt.status = code
          attempt.signal ??= signal
          if (signal || code !== 0) fail(signal ? 'signal' : 'exit')
        }
        resolve()
      })
      attempt.timer = setInterval(() => {
        try {
          tail()
          emit('heartbeat', {phase: attempt.phase, pid: attempt.child?.pid ?? null, childPid: attempt.pid, elapsedMs: Date.now() - attempt.started,
            bytes: attempt.bytes})
        } catch (error) { observerFailure(error) }
      }, 1000)
    })
    clearInterval(attempt.timer)
    if (!attempt.cleanupUnavailable) {
      tail(true)
      for (const source of ['stdout', 'stderr']) {
        const text = decoders[source].end()
        if (text) emit(source, text)
      }
      try {
        report = JSON.parse(readFileSync(reportPath, 'utf8'))
        const finalStatuses = new Set(['Killed', 'Survived', 'Timeout', 'CompileError', 'Ignored', 'NoCoverage', 'RuntimeError'])
        if (!report || !report.files || Array.isArray(report.files) || typeof report.files !== 'object' ||
            Object.values(report.files).some(file => !file || !Array.isArray(file.mutants) ||
              file.mutants.some(mutant => !mutant || !finalStatuses.has(mutant.status)))) throw new Error('Invalid mutation report files/mutants')
        counts = reportCounts(report)
      } catch (error) { fail('report', error) }
    }
  } catch (error) { observerFailure(error) }
  finally {
    clearInterval(attempt.timer)
    attempt.child = undefined
    await delivered()
    checkpoint()
    await delivered()
    stopDelivery()
    dispose()
    for (const [signal, listener] of Object.entries(signals)) process.removeListener(signal, listener)
    process.stdout.removeListener('error', observerFailure)
    process.stdout.removeListener('close', consoleClosed)
    attempt.phase = 'finished'
    for (const fd of attempt.handles) {
      try { closeSync(fd) } catch (error) { fail('observer', error) }
    }
    try { const {report: ignored, ...summary} = result(); persist('result.json', summary) } catch (error) { fail('observer', error) }
  }
  return result()
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

async function summarize(markdown) {
  if (process.env.GITHUB_STEP_SUMMARY) writeFileSync(process.env.GITHUB_STEP_SUMMARY, `${markdown}\n`, {flag: 'a'})
  try { await writeConsole(`${markdown}\n`); return true } catch { return false }
}

const selected = (repo, only) => {
  const tests = repo.testProjects.filter(test => !(test in repo.exclusions) && (!only || test.includes(only)))
  if (only && !tests.length) throw new Error(`No configured mutation target matches ${only}`)
  return tests
}

// The gate both modes share: a run with no tested mutant, or a score below the floor, fails. A null score (nothing
// detected or undetected) fails too, because `null >= floor` is false.
export function scoreVerdict(report, floor) {
  const counts = reportCounts(report ?? {})
  if (!counts.tested) return {ok: false, counts, reason: '0 mutants tested'}
  if (!(counts.score >= floor)) return {ok: false, counts, reason: `score ${counts.score} % is below the break ${floor}`}
  return {ok: true, counts}
}

// The changed files a PR run mutates: production .cs/.razor that still exist. A deleted file has nothing left to mutate,
// and a project-file edit has no mutants of its own; both stay in selection, and the weekly full run covers them.
// Stryker generates mutants in every compiled file and reports those its `mutate` list leaves out as Ignored "by mutate
// filter"; they were never selected, so a changed-files run counts and scores only the rest.
const filteredOut = mutant => mutant.status === 'Ignored' && /mutate filter/i.test(mutant.statusReason ?? '')
export const selectedReport = report => ({...report, files: Object.fromEntries(Object.entries(report.files ?? {})
  .map(([file, entry]) => [file, {...entry, mutants: (entry.mutants ?? []).filter(mutant => !filteredOut(mutant))}])
  .filter(([, entry]) => entry.mutants.length))})
// A changed .cs whose mutants the filter left out entirely was never matched by its mutate pattern.
export const unmatchedChanges = (report, files) => files.filter(file => file.endsWith('.cs')).filter(file => {
  const entry = Object.entries(report.files ?? {}).find(([name]) => {
    const normal = name.replaceAll('\\', '/')
    return normal === file || normal.endsWith(`/${file}`)
  })?.[1]
  return entry?.mutants?.length > 0 && entry.mutants.every(filteredOut)
})

export const mutableChanges = (changed, exists = file => existsSync(path.join(root, file))) =>
  changed.filter(file => /\.(cs|razor)$/.test(file) && exists(file))

// PR mode (owner, 2026-10-30, replacing the 2026-09-30 whole-project PR run): each project whose source changed mutates
// only its changed files, whole files rather than changed lines, and fails when their score is below the project's
// max(baseline, configured break). A changed file is held to the same floor as its project. The whole-project score,
// which catches a regression a one-file run cannot see, is the weekly full run's job. The Blazor library's 25,539
// mutants could not finish within a hosted runner's job limit, so a whole-project PR run never scored it.
export async function run(repo, only, strykerArgs, mutate = mutateProject, sourceChanges = changedSourceFiles, mutable = mutableChanges) {
  let failed = 0
  for (const test of selected(repo, only)) {
    const target = targetOf(test, read), targetText = read(target)
    const changed = sourceChanges(target, targetText)
    if (!changed.length) {
      if (!await summarize(`- ${test}: skipped; no project-file or production .cs${targetText.includes('Microsoft.NET.Sdk.Razor') ? '/.razor' : ''} change since origin/main (test-only changes do not select a project under PR #236).`)) return failed || 1
      continue
    }
    const files = mutable(changed)
    if (!files.length) {
      if (!await summarize(`- ${test}: nothing to mutate; the change is only deletions or the project file (${changed.map(file => `\`${file}\``).join(', ')}). The weekly full run holds the whole project.`)) return failed || 1
      continue
    }
    if (!await summarize(`- ${test}: mutating the changed files only: ${files.map(file => `\`${file}\``).join(', ')}.`)) return failed || 1
    const result = await mutate(test, files, strykerArgs)
    if (result.signal) return {signal: result.signal}
    if (result.observerFailed || result.cleanupUnavailable) return result.status > 0 ? result.status : 1
    if (result.outcome.kind === 'failed') {
      failed ||= result.status > 0 ? result.status : 1
      if (!await summarize(`- ${test}: **FAIL**, ${JSON.stringify(result.outcome)}`)) return failed
      continue
    }
    const report = result.report && selectedReport(result.report)
    const floor = fullModeBreak(repo.baselines[test], configOf(test, read))
    const unmatched = result.report ? unmatchedChanges(result.report, files) : []
    if (unmatched.length) {
      failed ||= 1
      if (!await summarize(`- ${test}: **FAIL**, Stryker's mutate list matched nothing in ${unmatched.map(file => `\`${file}\``).join(', ')}.`)) return failed
      continue
    }
    // A run handed Razor spans that came back with no mutant at all is ruling 91's failure, not an empty change.
    if (report && !reportCounts(report).total && !(result.razorPatterns > 0)) {
      if (!await summarize(`- ${test}: ${result.empty ? 'no changed file gave Stryker anything to mutate' : 'Stryker created no mutant in the changed files'}; there is no logic in them to score.`)) return failed || 1
      continue
    }
    const verdict = scoreVerdict(report, floor)
    // Ruling 91, scoped to the run: when a changed .razor gave Stryker spans to mutate, some Razor mutant must be tested.
    const razorMissing = result.razorPatterns > 0 && !razorTested(report)
    const survivors = report ? survivorsOnChangedLines(report, changedLines(git('diff', '-U0', 'origin/main', '--', ...changed).join('\n'))) : []
    if (!verdict.ok || razorMissing) failed ||= 1
    if (!await summarize([`### ${test}`, '', !verdict.ok ? `**FAIL**: ${verdict.reason}.` : razorMissing ? '**FAIL**: a changed .razor had code to mutate but 0 Razor mutants were tested.'
      : `${verdict.counts.tested} mutants tested in the changed files, score ${verdict.counts.score} %, break ${floor}.`, '',
      survivors.length ? '| file | line | status | mutator | replacement |\n|---|---|---|---|---|' : 'No surviving or uncovered mutant on a changed line.',
      ...survivors.map(s => `| ${s.file} | ${s.line} | ${s.status} | ${s.mutator} | \`${String(s.replacement ?? '').replaceAll('|', '\\|').replaceAll('\n', ' ').slice(0, 80)}\` |`)].join('\n'))) return failed || 1
  }
  return failed
}

// Full mode (the weekly scheduled run on main): every mutant of every selected project; fail on 0 tested or a score below
// the project's recorded baseline. `record` measures instead of judging and writes the baseline and the config's break.
export async function full(repo, only, strykerArgs, record, mutate = mutateProject) {
  let failed = 0
  const commit = git('rev-parse', 'HEAD')[0], measured = new Date().toISOString().slice(0, 10)
  for (const test of selected(repo, only)) {
    const result = await mutate(test, undefined, strykerArgs)
    if (result.signal) return {signal: result.signal}
    if (result.observerFailed || result.cleanupUnavailable) return result.status > 0 ? result.status : 1
    if (result.outcome.kind === 'failed') {
      failed ||= result.status > 0 ? result.status : 1
      if (!await summarize(`- ${test}: **FAIL**, ${JSON.stringify(result.outcome)}; nothing recorded`)) return failed
      continue
    }
    const {counts, report, razor: isRazor} = result
    if (!counts?.tested) {
      if (!await summarize(`- ${test}: **FAIL**, 0 mutants tested`)) return failed || 1
      failed ||= 1
      continue
    }
    if (isRazor && !razorTested(report)) {
      if (!await summarize(`- ${test}: **FAIL**, Razor project but 0 Razor mutants tested; nothing recorded`)) return failed || 1
      failed ||= 1
      continue
    }
    if (!record) {
      const floor = fullModeBreak(repo.baselines[test], configOf(test, read)), below = !(counts.score >= floor)
      if (!await summarize(`- ${test}: ${counts.tested} tested, score ${counts.score} %, break ${floor}${below ? ' **FAIL**' : ''}`)) return failed || 1
      if (below) failed ||= 1
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
  selected(repo, only)
  const problems = command === 'baseline' ? [] : configProblems(repo)
  problems.forEach(problem => console.error(problem))
  if (problems.length) process.exit(1)
  if (command !== 'baseline') await writeConsole(`Stryker config: ${repo.testProjects.length} test project(s), each configured or excluded: PASS\n`)
  const outcome = command === 'run' ? await run(repo, only, strykerArgs)
    : command === 'full' || command === 'baseline' ? await full(repo, only, strykerArgs, command === 'baseline') : 0
  if (outcome.signal) process.kill(process.pid, outcome.signal)
  else process.exitCode = outcome
}
