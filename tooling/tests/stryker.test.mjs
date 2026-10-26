#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import path from 'node:path'
import {execFileSync, spawn, spawnSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync} from 'node:fs'
import {setTimeout as delay} from 'node:timers/promises'
import {killProcessTreeSync} from '../resolve-command.mjs'
import {tmpdir} from 'node:os'

import {changedLines, changedSourceFiles, configProblems, fullModeBreak, isTestProject, plainRazor, razorTested, reportCounts, repository, sourceDirectories,
  scoreVerdict, survivorsOnChangedLines, thresholdsFor} from '../stryker.mjs'

const repoRoot = path.resolve('stryker-fixture-repo')
const diagnosticTarget = 'projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj'
const diagnosticFixture = path.join(import.meta.dirname, 'fixtures', 'stryker-diagnostic-process-fixture.mjs')

async function diagnosticProcess(t, mode) {
  const control = mkdtempSync(path.join(tmpdir(), 'stryker-diagnostics-'))
  const child = spawn(process.execPath, [diagnosticFixture, 'wrapper', '--test', diagnosticTarget, '--control', control, '--mode', mode],
    {stdio: ['ignore', 'pipe', 'pipe'], detached: process.platform !== 'win32'})
  let stdout = '', stderr = '', closed = false
  child.stdout.on('data', data => { stdout += data })
  child.stderr.on('data', data => { stderr += data })
  const done = new Promise((resolve, reject) => {
    child.on('error', reject)
    child.on('close', (code, signal) => { closed = true; resolve({code, signal}) })
  })
  const json = name => JSON.parse(readFileSync(path.join(control, name), 'utf8'))
  t.after(async () => {
    writeFileSync(path.join(control, 'release'), '')
    if (!closed) killProcessTreeSync(child.pid)
    await done
    const output = existsSync(path.join(control, 'ready.json')) ? json('ready.json').output :
      existsSync(path.join(control, 'result.json')) ? json('result.json').output : undefined
    if (output) {
      assert.equal(path.dirname(path.resolve(output)), path.resolve('StrykerOutput'))
      rmSync(output, {recursive: true, force: true})
    }
    rmSync(control, {recursive: true, force: true})
  })
  return {control, done, json, stdout: () => stdout, stderr: () => stderr, closed: () => closed}
}

async function until(predicate, message) {
  const deadline = Date.now() + 10000
  while (Date.now() < deadline) {
    if (predicate()) return
    await delay(25)
  }
  assert.fail(message)
}

test('mutation diagnostics reach disk and console while the real child is alive', async t => {
  const fixture = await diagnosticProcess(t, 'wait')
  await until(() => existsSync(path.join(fixture.control, 'ready.json')), 'child did not become ready')
  const {output} = fixture.json('ready.json')
  await until(() => existsSync(path.join(output, 'diagnostics', 'progress.jsonl')) &&
    readFileSync(path.join(output, 'diagnostics', 'progress.jsonl'), 'utf8').includes('heartbeat'), 'live journal missing while child waits for release')
  assert.equal(fixture.closed(), false)
  assert.ok(existsSync(path.join(output, 'diagnostics', 'progress.jsonl')), 'live journal missing while child waits for release')
  assert.match(readFileSync(path.join(output, 'diagnostics', 'progress.jsonl'), 'utf8'), /heartbeat/)
  assert.equal(readFileSync(path.join(output, 'diagnostics', 'stdout.log'), 'utf8'), 'stdout before release\0\r\n::warning::untrusted stdout\n')
  assert.equal(readFileSync(path.join(output, 'diagnostics', 'stderr.log'), 'utf8'), 'stderr before release\0\r\n::error::untrusted stderr\n')
  assert.match(fixture.stdout(), /nativeDebug.*Session succeeded; mutant outcome unknown/)
  writeFileSync(path.join(fixture.control, 'release'), '')
  assert.deepEqual(await fixture.done, {code: 0, signal: null})
  assert.equal(fixture.json('result.json').outcome.kind, 'completed')
  const records = fixture.stdout().trim().split('\n').map(line => {
    assert.match(line, /^\[stryker-diagnostic\] /)
    return JSON.parse(line.slice('[stryker-diagnostic] '.length))
  })
  assert.ok(records.every(record => !Number.isNaN(Date.parse(record.at))))
  const native = records.filter(record => record.source === 'nativeDebug').map(record => record.payload.line)
  assert.ok(native.includes('native UTF8 café 🙂'))
  assert.ok(native.includes('native final line without newline'))
  assert.ok(native.includes('late log file'))
  assert.ok(native.includes('::notice::untrusted native'))
  assert.equal(records.filter(record => record.source === 'terminal').length, 1)
  assert.equal(fixture.stderr(), '')
  const identity = JSON.parse(readFileSync(path.join(output, 'diagnostics', 'attempt.json'), 'utf8'))
  assert.equal(identity.configSha256, createHash('sha256').update(readFileSync(identity.configFile)).digest('hex'))
  assert.deepEqual(identity.argv.slice(2), fixture.json('ready.json').argv.slice(1))
  assert.equal('env' in identity, false)
  const generated = JSON.parse(readFileSync(identity.configFile, 'utf8'))['stryker-config']
  assert.equal(generated['coverage-analysis'], 'off')
  assert.deepEqual(generated.since, {enabled: false})
  assert.deepEqual(generated.reporters, ['json'])
  assert.deepEqual(generated.thresholds, {high: 80, low: 74, break: 0})
})

test('native console tails primary dated logs and retains worker traces as artifacts', async t => {
  const fixture = await diagnosticProcess(t, 'success')
  assert.deepEqual(await fixture.done, {code: 0, signal: null})
  const {output} = fixture.json('result.json')
  const records = fixture.stdout().trim().split('\n').map(line => JSON.parse(line.slice('[stryker-diagnostic] '.length)))
  const native = records.filter(record => record.source === 'nativeDebug')
  assert.ok(native.some(record => record.payload.line ===
    '2026-10-26T09:07:42.8119840-04:00  [DBG] "Runner 0": Using 1606 ms as test run timeout (281bb51f)'))
  assert.ok(native.some(record => record.payload.line ===
    '2026-10-26T09:07:45.2880760-04:00  [DBG] Test run for "5: Statement mutation" is "success"  (26093294)'))
  assert.ok(native.some(record => record.payload.file === 'log-20261027.txt' && record.payload.line === 'late log file'))
  assert.equal(readFileSync(path.join(output, 'logs', 'log-Runner-0.txt'), 'utf8'), 'worker trace retained only\n')
  assert.equal(native.some(record => record.payload.file === 'log-Runner-0.txt'), false)
  assert.doesNotMatch(fixture.stdout(), /worker trace retained only/)
  assert.doesNotMatch(readFileSync(path.join(output, 'diagnostics', 'progress.jsonl'), 'utf8'), /worker trace retained only/)
})

test('a native nonzero exit cannot accept a valid-looking partial report', async t => {
  const fixture = await diagnosticProcess(t, 'nonzero')
  await fixture.done
  const result = fixture.json('result.json')
  assert.equal(result.status, 23)
  assert.equal(result.outcome?.kind, 'failed', 'native failure must override the partial report')
  assert.equal(result.outcome.cause, 'exit')
  assert.equal(result.counts.score, 100)
})

test('a zero-exit report with an unknown mutant status fails without score evidence', async t => {
  const fixture = await diagnosticProcess(t, 'unknown')
  assert.deepEqual(await fixture.done, {code: 0, signal: null})
  const result = fixture.json('result.json')
  assert.equal(result.status, 0)
  assert.equal(result.outcome.kind, 'failed')
  assert.equal(result.outcome.cause, 'report')
  assert.equal(result.counts, undefined)
  const stored = JSON.parse(readFileSync(path.join(result.output, 'diagnostics', 'result.json'), 'utf8'))
  assert.equal(stored.outcome.kind, 'failed')
  assert.equal(stored.counts, undefined)
})

for (const [status, counts] of [
  ['Killed', {total: 1, tested: 1, detected: 1, undetected: 0, score: 100}],
  ['Survived', {total: 1, tested: 1, detected: 0, undetected: 1, score: 0}],
  ['Timeout', {total: 1, tested: 1, detected: 1, undetected: 0, score: 100}],
  ['CompileError', {total: 1, tested: 0, detected: 0, undetected: 0, score: null}],
  ['Ignored', {total: 1, tested: 0, detected: 0, undetected: 0, score: null}],
  ['NoCoverage', {total: 1, tested: 0, detected: 0, undetected: 1, score: 0}],
  ['RuntimeError', {total: 1, tested: 1, detected: 0, undetected: 0, score: null}]
]) {
  test(`a zero-exit ${status} report completes with the existing counts`, async t => {
    const fixture = await diagnosticProcess(t, `final-${status}`)
    assert.deepEqual(await fixture.done, {code: 0, signal: null})
    const result = fixture.json('result.json')
    assert.equal(result.outcome.kind, 'completed')
    assert.deepEqual(result.counts, counts)
    const stored = JSON.parse(readFileSync(path.join(result.output, 'diagnostics', 'result.json'), 'utf8'))
    assert.equal(stored.outcome.kind, 'completed')
    assert.deepEqual(stored.counts, counts)
  })
}

for (const mode of ['missing', 'malformed', 'invalid-shape', 'pending', 'spawn-error', 'signal']) {
  test(`mutation attempt fails on ${mode}`, async t => {
    const fixture = await diagnosticProcess(t, mode)
    await fixture.done
    const result = fixture.json('result.json')
    assert.equal(result.outcome.kind, 'failed')
    if (mode === 'signal') assert.ok(result.signal || result.status !== 0)
    else assert.equal(result.outcome.cause, mode === 'spawn-error' ? 'spawn' : 'report')
    const stored = JSON.parse(readFileSync(path.join(result.output, 'diagnostics', 'result.json'), 'utf8'))
    assert.equal(stored.outcome.kind, 'failed')
  })
}

test('a second attempt preserves every byte of the first attempt', async t => {
  const first = await diagnosticProcess(t, 'nonzero')
  await first.done
  const output = first.json('result.json').output
  const before = ['diagnostics/stdout.log', 'diagnostics/stderr.log', 'diagnostics/progress.jsonl', 'diagnostics/result.json',
    'logs/log-20261026.txt', 'reports/mutation-report.json'].map(file => [file, readFileSync(path.join(output, file))])
  const second = await diagnosticProcess(t, 'success')
  await second.done
  assert.notEqual(second.json('result.json').output, output)
  for (const [file, bytes] of before) assert.deepEqual(readFileSync(path.join(output, file)), bytes)
})

test('owned interruption flushes nonpass evidence and stops its captured child', async t => {
  const fixture = await diagnosticProcess(t, 'cancel')
  await until(() => existsSync(path.join(fixture.control, 'ready.json')), 'child did not become ready')
  const {output, pid} = fixture.json('ready.json')
  await until(() => readFileSync(path.join(output, 'diagnostics', 'progress.jsonl'), 'utf8').includes('heartbeat'), 'no live heartbeat')
  writeFileSync(path.join(fixture.control, 'cancel'), '')
  const exit = await fixture.done
  assert.ok(exit.code !== 0 || exit.signal)
  const result = JSON.parse(readFileSync(path.join(output, 'diagnostics', 'result.json'), 'utf8'))
  assert.equal(result.outcome.kind, 'failed')
  assert.equal(result.outcome.cause, 'interrupted')
  assert.equal(result.signal, 'SIGTERM')
  assert.match(readFileSync(path.join(output, 'diagnostics', 'stdout.log'), 'utf8'), /stdout before release/)
  assert.match(readFileSync(path.join(output, 'diagnostics', 'stderr.log'), 'utf8'), /stderr before release/)
  await until(() => { try { process.kill(pid, 0); return false } catch { return true } }, 'owned child survived cancellation')
})

test('observer failure remains nonpass after native close and retains raw evidence', async t => {
  const fixture = await diagnosticProcess(t, 'observer-error')
  await until(() => existsSync(path.join(fixture.control, 'ready.json')), 'child did not become ready')
  const {output} = fixture.json('ready.json')
  renameSync(path.join(output, 'logs'), path.join(output, 'logs-retained'))
  writeFileSync(path.join(output, 'logs'), 'not a directory')
  await fixture.done
  const result = fixture.json('result.json')
  assert.equal(result.outcome.kind, 'failed')
  assert.equal(result.outcome.cause, 'observer')
  assert.match(readFileSync(path.join(output, 'diagnostics', 'stdout.log'), 'utf8'), /stdout before release/)
})

test('native log truncation restarts its byte offset without replaying prior lines', async t => {
  const fixture = await diagnosticProcess(t, 'rotation')
  await until(() => existsSync(path.join(fixture.control, 'ready.json')), 'child did not become ready')
  const {output} = fixture.json('ready.json')
  await until(() => fixture.stdout().includes('Session succeeded'), 'initial native line was not forwarded')
  writeFileSync(path.join(output, 'logs', 'log-20261026.txt'), 'rotated log\n')
  await until(() => fixture.stdout().includes('rotated log'), 'truncated log was not reread')
  writeFileSync(path.join(fixture.control, 'release'), '')
  await fixture.done
  assert.equal(fixture.json('result.json').outcome.kind, 'completed')
  assert.equal(fixture.stdout().split('Session succeeded').length - 1, 1)
})

test('an unmatched CLI target fails before launching and the exact Builder path names one configured target', () => {
  for (const command of ['run', 'full', 'baseline']) {
    const result = spawnSync(process.execPath, ['tooling/stryker.mjs', command, 'does-not-exist-diagnostic-target'], {encoding: 'utf8'})
    assert.equal(result.status, 1)
    assert.match(result.stderr, /No configured mutation target matches/)
  }
  const repo = repository()
  assert.deepEqual(repo.testProjects.filter(test => !(test in repo.exclusions) && test.includes(diagnosticTarget)), [diagnosticTarget])
})

const testCsproj = '<PackageReference Include="Microsoft.NET.Test.Sdk" /><ProjectReference Include="../lib/Lib.csproj" />'
const config = (overrides = {}) => JSON.stringify({'stryker-config': {
  project: 'Lib.csproj', since: {enabled: true, target: 'origin/main'},
  thresholds: {high: 80, low: 60, break: 60}, reporters: ['json'], ...overrides}})
const repo = (files, exclusions = {}, baselines = {'p/lib.tests/Lib.Tests.csproj': {break: 55}}) => ({
  testProjects: Object.keys(files).filter(file => file.endsWith('.csproj') && isTestProject(files[file])),
  exclusions, baselines, readFile: file => files[file]})
const base = {'p/lib.tests/Lib.Tests.csproj': testCsproj, 'p/lib/Lib.csproj': '<Project />', 'p/lib.tests/stryker-config.json': config()}

test('a configured test project whose target exists has no problems', () => {
  assert.deepEqual(configProblems(repo(base)), [])
})

test('a test project with neither config nor exclusion is refused, and an exclusion answers it', () => {
  const files = {...base, 'p/lib.tests/stryker-config.json': undefined}
  assert.match(configProblems(repo(files)).join('\n'), /no p\/lib.tests\/stryker-config.json and no entry/)
  const excluded = {'p/lib.tests/Lib.Tests.csproj': 'why', 'p/lib/Lib.csproj': 'why'}
  assert.deepEqual(configProblems(repo(files, excluded)), [])
})

test('a config naming a project that is not a reference, or a reference that is missing, is refused', () => {
  assert.match(configProblems(repo({...base, 'p/lib.tests/stryker-config.json': config({project: 'Other.csproj'})})).join('\n'),
    /project "Other.csproj" is not a ProjectReference/)
  assert.match(configProblems(repo({...base, 'p/lib/Lib.csproj': undefined})).join('\n'), /project p\/lib\/Lib.csproj does not exist/)
})

test('off-standard thresholds, an html reporter, and a missing since are refused', () => {
  const problems = configProblems(repo({...base, 'p/lib.tests/stryker-config.json':
    config({thresholds: {high: 80, low: 60, break: 0}, reporters: ['json', 'html'], since: {enabled: false}})})).join('\n')
  assert.match(problems, /break 0 is below the recorded baseline 55/)
  assert.match(problems, /reporters are json only/)
  assert.match(problems, /since must be enabled against origin\/main/)
})

test('break starts at the measured baseline and may rise; low = max(60, break), high = max(80, break) (Q45, ruling 96)', () => {
  // Owner ruling 2026-09-26: break is the project's baseline floor, not a flat 60.
  assert.deepEqual(thresholdsFor(54.94), {high: 80, low: 60, break: 54})
  assert.deepEqual(thresholdsFor(74.67), {high: 80, low: 74, break: 74})
  assert.deepEqual(thresholdsFor(85.2), {high: 85, low: 85, break: 85})
  const problems = (overrides, baselines) => configProblems(repo({...base, 'p/lib.tests/stryker-config.json': config(overrides)}, {}, baselines)).join('\n')
  assert.match(problems({}, {}), /no measured baseline in tooling\/stryker-baselines.json/)
  assert.equal(problems({thresholds: {high: 80, low: 60, break: 58}}), '')
  assert.match(problems({thresholds: {high: 80, low: 50, break: 50}}), /low = max\(60, break\) = 60/)
  assert.match(problems({thresholds: {high: 80, low: 60, break: 70}}), /low = max\(60, break\) = 70/)
  assert.match(problems({thresholds: {high: 80, low: 75, break: 70}}), /= 70 and high = max\(80, break\) = 80/)
  assert.equal(problems({thresholds: {high: 80, low: 70, break: 70}}), '')
  assert.match(problems({thresholds: {high: 85, low: 60, break: 58}}), /high = max\(80, break\) = 80/)
  assert.equal(problems({thresholds: {high: 85, low: 85, break: 85}}), '')
  assert.match(problems({thresholds: {high: 80, low: 85, break: 85}}), /high = max\(80, break\) = 85/)
  assert.match(problems({}, {'p/lib.tests/Lib.Tests.csproj': {break: 55}, 'p/gone.tests/G.csproj': {break: 1}}), /p\/gone.tests\/G.csproj is not a test project/)
})

test('a source project a test references but nothing mutates is a gap', () => {
  const files = {...base, 'p/lib.tests/Lib.Tests.csproj': `${testCsproj}<ProjectReference Include="../probe/Probe.csproj" />`, 'p/probe/Probe.csproj': '<Project />'}
  assert.match(configProblems(repo(files)).join('\n'), /p\/probe\/Probe.csproj: referenced by .* mutated by no stryker-config.json/)
  assert.deepEqual(configProblems(repo(files, {'p/probe/Probe.csproj': 'harness'})), [])
})

test('an exclusion for a missing project or without a reason is refused', () => {
  const problems = configProblems(repo(base, {'p/gone/Gone.csproj': 'x', 'p/lib/Lib.csproj': ' '})).join('\n')
  assert.match(problems, /p\/gone\/Gone.csproj does not exist/)
  assert.match(problems, /p\/lib\/Lib.csproj has no reason/)
})

test('a report that mutated nothing counts 0 tested: the T-711 "passed having run nothing" shape', () => {
  // The T-720 spike's run: analysis failed, no mutants, "score 0.00 %", exit 0. And a linked-worktree since run: all Ignored.
  assert.equal(reportCounts({files: {}}).tested, 0)
  assert.equal(reportCounts({files: {'a.cs': {mutants: [{status: 'Ignored'}, {status: 'CompileError'}]}}}).tested, 0)
  assert.deepEqual(reportCounts({files: {'a.cs': {mutants: [{status: 'Killed'}, {status: 'Survived'}, {status: 'NoCoverage'}, {status: 'Timeout'}]}}}),
    {total: 4, tested: 3, detected: 2, undetected: 2, score: 50})
})

test('this repository: every test project is configured or excluded, with no silent gap', () => {
  // Binding here, in the tooling self-tests the gate runs; the stryker workflow itself is advisory.
  assert.deepEqual(configProblems(repository()), [])
})

test('Razor generator output becomes plain C# that Stryker mutates, and names its .razor', () => {
  // Stryker skips a tree whose header says <auto-generated/> (and a .g.cs name, which the script drops).
  const {source, text, spans} = plainRazor([
    '\uFEFF#pragma checksum "C:/r/ui/X.razor" "{8829}" "ab"', '// <auto-generated/>', '#pragma warning disable 1591',
    'builder.OpenElement(0, "div");', '#line (3,8)-(3,20) "C:/r/ui/X.razor"', 'count++;', '#line default', 'builder.CloseElement();',
    '#line (5,1)-(5,9) "C:/r/ui/_Imports.razor"', 'using A;', '#line hidden', ''].join('\r\n'))
  assert.equal(source, 'C:/r/ui/X.razor')
  assert.doesNotMatch(text, /auto-generated|\uFEFF/)
  assert.match(text, /#pragma warning disable 1591/)
  // Only the lines #line maps to a .razor are mutated; the render scaffolding between them is not.
  assert.deepEqual(spans.map(([from, to]) => text.slice(from, to)), ['count++;\r\n', 'using A;\r\n'])
})

test('PR feedback lists Survived and NoCoverage mutants on changed lines only (Q43), and every one in a changed .razor', () => {
  const diff = ['diff --git a/p/lib/A.cs b/p/lib/A.cs', '--- a/p/lib/A.cs', '+++ b/p/lib/A.cs', '@@ -10,0 +11,2 @@', '+x', '+y',
    '@@ -20 +22 @@', '-a', '+b', '--- a/p/lib/Gone.cs', '+++ /dev/null', '@@ -1,3 +0,0 @@'].join('\n')
  const lines = changedLines(diff)
  assert.deepEqual([...lines['p/lib/A.cs']], [11, 12, 22])
  assert.equal(lines['p/lib/Gone.cs'], undefined)
  const at = (line, status) => ({status, mutatorName: 'm', replacement: 'r', location: {start: {line}}})
  // Report paths are absolute in the host's own form (Stryker writes them so), built here with node:path so the
  // fixture holds on Windows and on the Linux verify-shared runner alike.
  const report = {files: {
    [path.join(repoRoot, 'p', 'lib', 'A.cs')]: {mutants: [at(11, 'Survived'), at(12, 'Killed'), at(22, 'NoCoverage'), at(30, 'Survived')]},
    [path.join(repoRoot, 'p', 'ui', 'obj', 'Debug', 'net10.0', 'stryker-razor', 'X_razor.cs')]: {mutants: [at(400, 'Survived')]}}}
  assert.deepEqual(survivorsOnChangedLines(report, lines, repoRoot).map(s => `${s.file}:${s.line}:${s.status}`),
    ['p/lib/A.cs:11:Survived', 'p/lib/A.cs:22:NoCoverage', 'p/ui/obj/Debug/net10.0/stryker-razor/X_razor.cs:400:Survived'])
})

// A fixture report: 100 mutants, of which `killed` are Killed and the rest Survived, in one file.
const fixture = killed => ({files: {'p/lib/A.cs': {mutants: Array.from({length: 100}, (_, i) => ({status: i < killed ? 'Killed' : 'Survived'}))}}})

test('PR mode: a score below the project break fails, an unchanged or higher one passes (PR #227: 84 % to 67.77 %)', () => {
  assert.deepEqual(scoreVerdict(fixture(67), 84).ok, false)
  assert.match(scoreVerdict(fixture(67), 84).reason, /67 % is below the break 84/)
  assert.equal(scoreVerdict(fixture(84), 84).ok, true)
  assert.equal(scoreVerdict(fixture(90), 84).ok, true)
})

test('PR mode: a report with no tested mutant, or no report, fails however low the break', () => {
  assert.equal(scoreVerdict({files: {'p/lib/A.cs': {mutants: [{status: 'Ignored'}]}}}, 0).ok, false)
  assert.match(scoreVerdict(undefined, 0).reason, /0 mutants tested/)
})

test('full mode holds a project to the higher of its recorded baseline and its configured break', () => {
  // Recorded 50, configured 70 by hand, score 60: the configured 70 binds.
  assert.equal(fullModeBreak({break: 50}, {thresholds: {break: 70}}), 70)
  assert.equal(fullModeBreak({break: 50}, {thresholds: {break: 50}}), 50)
  assert.equal(fullModeBreak(undefined, {thresholds: {break: 40}}), 40)
})

test('a project compiles from its own directory and every directory it links', () => {
  assert.deepEqual(sourceDirectories('p/ui/a/A.csproj', '<Compile Include="../b/**/*.cs" /><RazorComponent Include="../c/**/*.razor" />'),
    ['p/ui/a', 'p/ui/b', 'p/ui/c'])
})

// Real Git fixtures exercise deletion and rename output, not a hand-written imitation of that output. Expected
// paths are literals from this fixture corpus; PR #236 supplies the source-only selection boundary.
function selectionFixture(t, projectText = '<Project />') {
  const directory = mkdtempSync(path.join(tmpdir(), 'hlp-stryker-selection-'))
  t.after(() => rmSync(directory, {recursive: true, force: true}))
  const git = (...args) => execFileSync('git', ['-C', directory, ...args], {encoding: 'utf8'})
  const write = (file, text) => {
    mkdirSync(path.dirname(path.join(directory, file)), {recursive: true})
    writeFileSync(path.join(directory, file), text)
  }
  git('init', '-q')
  write('p/lib/Lib.csproj', projectText)
  write('p/other/Other.csproj', '<Project />')
  for (const file of ['p/lib/A.cs', 'p/lib/Gone.cs', 'p/lib/View.razor', 'p/lib/nested/Name space.cs',
    'p/lib/Unicode-λ.cs', 'p/other/B.cs', 'p/lib.tests/Test.cs', 'p/lib/nested.tests/Test.cs', 'p/linked/L.cs']) {
    write(file, 'first line\nsecond line\nthird line\n')
  }
  git('add', '.')
  git('-c', 'user.name=Selection fixture', '-c', 'user.email=fixture@example.invalid',
    '-c', 'commit.gpgsign=false', '-c', 'core.hooksPath=', 'commit', '-qm', 'fixture base')
  return {directory, git, write, changed: (project = 'p/lib/Lib.csproj', text = '<Project />') =>
    changedSourceFiles(project, text, directory, 'HEAD')}
}

test('PR selection includes a source file with only removed lines and skips an unchanged project', t => {
  const fixture = selectionFixture(t)
  fixture.write('p/lib/A.cs', 'first line\nthird line\n')
  assert.deepEqual(fixture.changed(), ['p/lib/A.cs'])
  assert.deepEqual(fixture.changed('p/other/Other.csproj'), [])
})

test('PR selection includes a deleted source file even though no new line can be mutated', t => {
  const fixture = selectionFixture(t)
  rmSync(path.join(fixture.directory, 'p/lib/Gone.cs'))
  assert.deepEqual(fixture.changed(), ['p/lib/Gone.cs'])
})

test('PR selection includes both owners of a rename and keeps an unrelated project skipped', t => {
  const fixture = selectionFixture(t)
  renameSync(path.join(fixture.directory, 'p/lib/Gone.cs'), path.join(fixture.directory, 'p/other/Moved.cs'))
  fixture.git('add', '-A')
  assert.deepEqual(fixture.changed(), ['p/lib/Gone.cs'])
  assert.deepEqual(fixture.changed('p/other/Other.csproj'), ['p/other/Moved.cs'])
  assert.deepEqual(fixture.changed('p/linked/Linked.csproj'), [])
})

test('PR selection includes an in-project rename, nested files and names requiring Git quoting', t => {
  const fixture = selectionFixture(t)
  renameSync(path.join(fixture.directory, 'p/lib/nested/Name space.cs'), path.join(fixture.directory, 'p/lib/nested/New name.cs'))
  fixture.write('p/lib/Unicode-λ.cs', 'changed\n')
  fixture.git('add', '-A')
  assert.deepEqual(fixture.changed(), ['p/lib/Unicode-λ.cs', 'p/lib/nested/Name space.cs', 'p/lib/nested/New name.cs'])
})

test('PR selection keeps test-only edits and test deletions skipped under the PR #236 source-only policy', t => {
  const fixture = selectionFixture(t)
  fixture.write('p/lib.tests/Test.cs', 'changed test\n')
  rmSync(path.join(fixture.directory, 'p/lib/nested.tests/Test.cs'))
  assert.deepEqual(fixture.changed(), [])
})

test('PR selection sees linked source deletion but not a similarly prefixed directory', t => {
  const fixture = selectionFixture(t)
  fixture.write('p/library/A.cs', 'unrelated\n')
  fixture.git('add', '-A')
  assert.deepEqual(fixture.changed(), [])
  rmSync(path.join(fixture.directory, 'p/linked/L.cs'))
  assert.deepEqual(fixture.changed('p/lib/Lib.csproj', '<Compile Include="../linked/**/*.cs" />'), ['p/linked/L.cs'])
})

test('PR selection includes deleted Razor source only for a Razor target', t => {
  const fixture = selectionFixture(t)
  rmSync(path.join(fixture.directory, 'p/lib/View.razor'))
  assert.deepEqual(fixture.changed(), [])
  assert.deepEqual(fixture.changed('p/lib/Lib.csproj', '<Project Sdk="Microsoft.NET.Sdk.Razor" />'), ['p/lib/View.razor'])
})

test('PR selection preserves base ownership when linked source and its Compile Include are deleted together', t => {
  const fixture = selectionFixture(t, '<Compile Include="../linked/**/*.cs" />')
  fixture.write('p/lib/Lib.csproj', '<Project />')
  rmSync(path.join(fixture.directory, 'p/linked/L.cs'))
  assert.deepEqual(fixture.changed(), ['p/lib/Lib.csproj', 'p/linked/L.cs'])
})

test('PR selection includes a project file that alone removes compiled source', t => {
  const unlinked = selectionFixture(t, '<Compile Include="../linked/**/*.cs" />')
  unlinked.write('p/lib/Lib.csproj', '<Project />')
  assert.deepEqual(unlinked.changed(), ['p/lib/Lib.csproj'])
  const removed = selectionFixture(t)
  removed.write('p/lib/Lib.csproj', '<Compile Remove="A.cs" />')
  assert.deepEqual(removed.changed('p/lib/Lib.csproj', '<Compile Remove="A.cs" />'), ['p/lib/Lib.csproj'])
  assert.deepEqual(removed.changed('p/other/Other.csproj'), [])
})

test('PR selection preserves both owners when linked source is moved and its old Include removed', t => {
  const fixture = selectionFixture(t, '<Compile Include="../linked/**/*.cs" />')
  fixture.write('p/lib/Lib.csproj', '<Project />')
  renameSync(path.join(fixture.directory, 'p/linked/L.cs'), path.join(fixture.directory, 'p/other/Moved.cs'))
  fixture.git('add', '-A')
  assert.deepEqual(fixture.changed(), ['p/lib/Lib.csproj', 'p/linked/L.cs'])
  assert.deepEqual(fixture.changed('p/other/Other.csproj'), ['p/other/Moved.cs'])
})

test('PR selection preserves deleted Razor ownership when the current project removes Razor capability', t => {
  const fixture = selectionFixture(t, '<Project Sdk="Microsoft.NET.Sdk.Razor" />')
  fixture.write('p/lib/Lib.csproj', '<Project />')
  rmSync(path.join(fixture.directory, 'p/lib/View.razor'))
  assert.deepEqual(fixture.changed(), ['p/lib/Lib.csproj', 'p/lib/View.razor'])
})

test('PR selection accepts a new project absent from base and a deleted project absent from the current tree', t => {
  const fixture = selectionFixture(t)
  fixture.write('p/new/New.csproj', '<Project />')
  fixture.write('p/new/New.cs', 'new source\n')
  fixture.git('add', '-A')
  assert.deepEqual(fixture.changed('p/new/New.csproj'), ['p/new/New.cs', 'p/new/New.csproj'])
  rmSync(path.join(fixture.directory, 'p/lib/Lib.csproj'))
  rmSync(path.join(fixture.directory, 'p/lib/Gone.cs'))
  assert.deepEqual(changedSourceFiles('p/lib/Lib.csproj', undefined, fixture.directory, 'HEAD'), ['p/lib/Gone.cs', 'p/lib/Lib.csproj'])
})

test('PR selection refuses an unknown base or a project absent from both trees instead of silently skipping', t => {
  const fixture = selectionFixture(t)
  assert.throws(() => changedSourceFiles('p/lib/Lib.csproj', '<Project />', fixture.directory, 'missing-base'), /Command failed/)
  assert.throws(() => changedSourceFiles('p/missing/Missing.csproj', undefined, fixture.directory, 'HEAD'), /absent from both/)
})

test('a Razor report with no tested mutant in a .razor span is caught however many .cs mutants it tested (ruling 91)', () => {
  // The first UIAdapters.Blazor baseline: 854 .cs mutants tested, every .razor span dropped by a '!**/stryker-razor/**' glob.
  const csOnly = {files: {'C:\\r\\ui\\a\\Store.cs': {mutants: [{status: 'Killed'}, {status: 'Survived'}]}}}
  assert.equal(razorTested(csOnly), 0)
  // Stryker writes Windows paths with backslashes; the check normalizes them.
  const withRazor = {files: {...csOnly.files, 'C:\\r\\ui\\a\\obj\\Debug\\net10.0\\stryker-razor\\G\\X_razor.cs': {mutants: [{status: 'Killed'}, {status: 'Ignored'}]}}}
  assert.equal(razorTested(withRazor), 1)
})
