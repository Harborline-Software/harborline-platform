#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import {changedLines, configProblems, isTestProject, plainRazor, reportCounts, repository, sourceDirectories, survivorsOnChangedLines, thresholdsFor} from '../stryker.mjs'

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

test('break starts at the measured baseline and may rise; low = max(60, break) and high = 80 (Q45)', () => {
  // Owner ruling 2026-09-26: break is the project's baseline floor, not a flat 60.
  assert.deepEqual(thresholdsFor(54.94), {high: 80, low: 60, break: 54})
  assert.deepEqual(thresholdsFor(74.67), {high: 80, low: 74, break: 74})
  const problems = (overrides, baselines) => configProblems(repo({...base, 'p/lib.tests/stryker-config.json': config(overrides)}, {}, baselines)).join('\n')
  assert.match(problems({}, {}), /no measured baseline in tooling\/stryker-baselines.json/)
  assert.equal(problems({thresholds: {high: 80, low: 60, break: 58}}), '')
  assert.match(problems({thresholds: {high: 80, low: 50, break: 50}}), /low = max\(60, break\) = 60/)
  assert.match(problems({thresholds: {high: 80, low: 60, break: 70}}), /low = max\(60, break\) = 70/)
  assert.match(problems({thresholds: {high: 80, low: 75, break: 70}}), /= 70 and high = 80/)
  assert.equal(problems({thresholds: {high: 80, low: 70, break: 70}}), '')
  assert.match(problems({thresholds: {high: 85, low: 60, break: 58}}), /high = 80/)
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
  const report = {files: {
    'C:/repo/p/lib/A.cs': {mutants: [at(11, 'Survived'), at(12, 'Killed'), at(22, 'NoCoverage'), at(30, 'Survived')]},
    'C:/repo/p/ui/obj/Debug/net10.0/stryker-razor/X_razor.cs': {mutants: [at(400, 'Survived')]}}}
  assert.deepEqual(survivorsOnChangedLines(report, lines, 'C:/repo').map(s => `${s.file}:${s.line}:${s.status}`),
    ['p/lib/A.cs:11:Survived', 'p/lib/A.cs:22:NoCoverage', 'p/ui/obj/Debug/net10.0/stryker-razor/X_razor.cs:400:Survived'])
})

test('a project compiles from its own directory and every directory it links', () => {
  assert.deepEqual(sourceDirectories('p/ui/a/A.csproj', '<Compile Include="../b/**/*.cs" /><RazorComponent Include="../c/**/*.razor" />'),
    ['p/ui/a', 'p/ui/b', 'p/ui/c'])
})
