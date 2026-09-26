#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import {configProblems, isTestProject, reportCounts, repository} from '../stryker.mjs'

const testCsproj = '<PackageReference Include="Microsoft.NET.Test.Sdk" /><ProjectReference Include="../lib/Lib.csproj" />'
const config = (overrides = {}) => JSON.stringify({'stryker-config': {
  project: 'Lib.csproj', since: {enabled: true, target: 'origin/main'},
  thresholds: {high: 80, low: 60, break: 60}, reporters: ['json'], ...overrides}})
const repo = (files, exclusions = {}) => ({
  testProjects: Object.keys(files).filter(file => file.endsWith('.csproj') && isTestProject(files[file])),
  exclusions, readFile: file => files[file]})
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
  assert.match(problems, /thresholds must be high 80, low 60, break 60/)
  assert.match(problems, /reporters are json only/)
  assert.match(problems, /since must be enabled against origin\/main/)
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
