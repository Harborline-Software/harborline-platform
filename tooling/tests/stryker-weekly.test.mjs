#!/usr/bin/env node
import assert from 'node:assert/strict'
import {test} from 'node:test'

import {collect, itemResult, markdown, planShards, projectFiles, shard, slimReport, weighProjects} from '../stryker-weekly.mjs'

const mutants = (statuses, extra = {}) => statuses.map((status, index) => ({id: `${index}`, status, mutatorName: 'Equality mutation',
  location: {start: {line: index + 1, column: 1}, end: {line: index + 1, column: 2}}, ...extra}))
const project = (test, weight, files = []) => ({test, weight, files: files.map(([file, size]) => ({file, size}))})
const small = 'p/small.tests/Small.Tests.csproj', big = 'p/big.tests/Big.Tests.csproj', ui = 'p/ui.tests/Ui.Tests.csproj'
const bigFiles = Array.from({length: 9}, (_, index) => [`p/big/F${index}.cs`, 1000 * (index + 1)])

test('a project within a part runs whole and a heavy one is cut into chunks of whole files', () => {
  const plan = planShards([project(small, 100, [['p/small/A.cs', 10]]), project(big, 900, bigFiles)], 4)
  assert.equal(plan.share, 250)
  const smallItems = plan.items.filter(item => item.test === small)
  assert.deepEqual(smallItems.map(item => item.files), [null])
  const chunks = plan.items.filter(item => item.test === big)
  assert.equal(chunks.length, 4)
  assert.ok(chunks.every(item => item.chunks === 4 && item.files.length))
  assert.deepEqual(chunks.flatMap(item => item.files).sort(), bigFiles.map(([file]) => file).sort(), 'every file once, none twice')
  assert.equal(Math.round(chunks.reduce((sum, item) => sum + item.weight, 0)), 900)
  assert.deepEqual(plan.shards.map(entry => entry.shard), [1, 2, 3, 4])
  assert.deepEqual(new Set(plan.items.map(item => item.id)).size, plan.items.length)
})

test('a project a little over its share stays whole and the plan never splits more ways than it has files', () => {
  const plan = planShards([project(small, 120, [['p/small/A.cs', 10], ['p/small/B.cs', 10]]), project(big, 100, [['p/big/A.cs', 5]])], 2)
  assert.deepEqual(plan.items.map(item => item.files), [null, null])
  const one = planShards([project(big, 1000, [['p/big/A.cs', 5], ['p/big/B.cs', 5]])], 10)
  assert.equal(one.items.length, 2)
  assert.deepEqual(one.shards.map(entry => entry.shard), [1, 2], 'empty parts are left out of the matrix')
})

test('the plan is the same however the projects arrive', () => {
  const projects = [project(small, 100, [['p/small/A.cs', 10]]), project(big, 900, bigFiles), project(ui, 300, [['p/ui/X.razor', 40], ['p/ui/Y.cs', 60]])]
  assert.deepEqual(planShards(projects, 5), planShards([...projects].reverse(), 5))
  assert.throws(() => planShards(projects, 0), /positive integer/)
  assert.throws(() => planShards(projects, 2.5), /positive integer/)
})

test('project files are the tracked sources, .razor only for a Razor project, never a test project', () => {
  const listed = []
  const list = patterns => { listed.push(...patterns); return ['p/ui/B.razor', 'p/ui/A.cs', 'p/ui.tests/T.cs', 'p/ui/A.cs'] }
  assert.deepEqual(projectFiles('p/ui/Ui.csproj', '<Project Sdk="Microsoft.NET.Sdk.Razor"></Project>', list), ['p/ui/A.cs', 'p/ui/B.razor'])
  assert.deepEqual(listed, ['p/ui/*.cs', 'p/ui/*.razor'])
  listed.length = 0
  projectFiles('p/lib/Lib.csproj', '<Project Sdk="Microsoft.NET.Sdk"><Compile Include="../shared/**/*.cs" /></Project>', list)
  assert.deepEqual(listed, ['p/lib/*.cs', 'p/shared/*.cs'])
})

test('a project with no baseline is weighed as the mean, and excluded projects are not planned', () => {
  const files = {
    [small]: '<ProjectReference Include="../small/Small.csproj" />', 'p/small.tests/stryker-config.json': '{"stryker-config":{"project":"Small.csproj"}}',
    'p/small/Small.csproj': '<Project Sdk="Microsoft.NET.Sdk"></Project>',
    [big]: '<ProjectReference Include="../big/Big.csproj" />', 'p/big.tests/stryker-config.json': '{"stryker-config":{"project":"Big.csproj"}}',
    'p/big/Big.csproj': '<Project Sdk="Microsoft.NET.Sdk"></Project>'}
  const repo = {testProjects: [big, small, ui], exclusions: {[ui]: 'no config'}, baselines: {[big]: {tested: 400}}, readFile: file => files[file]}
  const weighed = weighProjects(repo, patterns => patterns.map(pattern => pattern.replace('*', 'X')), () => 7)
  assert.deepEqual(weighed.map(entry => [entry.test, entry.weight]), [[big, 400], [small, 400]])
  assert.deepEqual(weighed[1].files, [{file: 'p/small/X.cs', size: 7}])
})

test('a part mutates whole projects as the full run does and chunks as explicit lists, and records each result', async () => {
  const plan = planShards([project(small, 100, [['p/small/A.cs', 10]]), project(big, 900, bigFiles)], 4)
  const part = plan.items.find(item => item.test === small).shard
  const calls = [], written = []
  const mutate = async (testProject, files) => {
    calls.push([testProject, files])
    return {outcome: {kind: 'completed'}, status: 0, report: {files: {'p/x.cs': {source: 'text', mutants: mutants(['Killed'])}}}, razor: false, razorPatterns: 0}
  }
  assert.equal(await shard(plan, part, [], (item, result) => written.push(result), mutate, () => {}), 0)
  const mine = plan.items.filter(item => item.shard === part)
  assert.deepEqual(calls, mine.map(item => [item.test, item.files ?? undefined]))
  assert.deepEqual(written.map(result => result.id), mine.map(item => item.id))
  assert.equal(written[0].report.files['p/x.cs'].source, undefined, 'source text is dropped')
  await assert.rejects(shard(plan, 99, [], () => {}, mutate, () => {}), /no items/)
})

test('a part keeps going past a failed item, and stops on a signal', async () => {
  const plan = planShards([project(small, 100, [['p/small/A.cs', 10]]), project(big, 100, [['p/big/A.cs', 10]])], 1)
  const written = []
  const failing = async () => ({outcome: {kind: 'failed', cause: 'report'}, status: 3, report: undefined})
  assert.equal(await shard(plan, 1, [], (item, result) => written.push(result), failing, () => {}), 3)
  assert.equal(written.length, 2)
  assert.deepEqual(await shard(plan, 1, [], () => {}, async () => ({signal: 'SIGTERM'}), () => {}), {signal: 'SIGTERM'})
})

test('an item result drops filtered mutants and names a listed file the mutate list never matched', () => {
  const item = {id: '0001', test: big, chunk: 1, chunks: 2, files: ['p/big/A.cs', 'p/big/B.cs']}
  const result = itemResult(item, {outcome: {kind: 'completed'}, status: 0, razor: false, razorPatterns: 0, report: {files: {
    'p/big/A.cs': {mutants: mutants(['Killed', 'Survived'])},
    'p/big/B.cs': {source: 'File ignored by mutate filter', mutants: []},
    'p/big/C.cs': {mutants: mutants(['Ignored'], {statusReason: 'Removed by mutate filter'})}}}})
  assert.deepEqual(result.unmatched, ['p/big/B.cs'])
  assert.deepEqual(Object.keys(result.report.files), ['p/big/A.cs'])
  assert.deepEqual(slimReport({files: {a: {source: 'x', language: 'cs', mutants: mutants(['Killed'])}}}).files.a.mutants.map(m => m.status), ['Killed'])
})

const configs = {'p/big.tests/stryker-config.json': '{"stryker-config":{"thresholds":{"break":50}}}',
  'p/small.tests/stryker-config.json': '{"stryker-config":{"thresholds":{"break":10}}}', 'p/ui.tests/stryker-config.json': '{"stryker-config":{}}'}
const repo = {baselines: {[big]: {break: 40, tested: 4}, [small]: {break: 70, tested: 2}, [ui]: {break: 0, tested: 2}}, readFile: file => configs[file]}
const twoChunks = {items: [
  {id: '0001', test: big, chunk: 1, chunks: 2, files: ['p/big/A.cs'], shard: 1},
  {id: '0002', test: big, chunk: 2, chunks: 2, files: ['p/big/B.cs'], shard: 2},
  {id: '0003', test: small, chunk: 1, chunks: 1, files: null, shard: 2}]}
const done = (id, test, chunk, files, extra = {}) => ({id, test, chunk, outcome: {kind: 'completed'}, razor: false, unmatched: [], report: {files}, ...extra})
const passing = () => [
  done('0001', big, 1, {'p/big/A.cs': {mutants: mutants(['Killed', 'Survived'])}}),
  done('0002', big, 2, {'p/big/B.cs': {mutants: mutants(['Killed', 'Timeout', 'Ignored'])}}),
  done('0003', small, 1, {'p/small/A.cs': {mutants: mutants(['Killed', 'Killed'])}})]

test('the chunks of a project add up to one score, held to the higher of baseline and configured break', () => {
  const verdict = collect(twoChunks, passing(), repo)
  assert.equal(verdict.ok, true)
  assert.deepEqual(verdict.rows.map(row => [row.test, row.chunks, row.tested, row.score, row.floor]), [[big, 2, 4, 75, 50], [small, 1, 2, 100, 70]])
  assert.match(markdown(verdict), /Every project scored at or above its break/)
})

test('a chunk with no result fails its project: a part that timed out never passes as a smaller project', () => {
  const verdict = collect(twoChunks, passing().filter(result => result.id !== '0002'), repo)
  assert.equal(verdict.ok, false)
  assert.match(verdict.rows[0].reasons.join(), /chunk 2 of 2 \(part 2\) has no result/)
  assert.match(markdown(verdict), /\*\*FAIL\*\*/)
})

test('failed, unmatched, duplicated and unplanned results all fail the run', () => {
  const failed = passing(); failed[0] = {...failed[0], outcome: {kind: 'failed', cause: 'report'}}
  assert.match(collect(twoChunks, failed, repo).rows[0].reasons.join(), /chunk 1 of 2 \(part 1\) failed/)
  const unmatched = passing(); unmatched[1] = {...unmatched[1], unmatched: ['p/big/B.cs']}
  assert.match(collect(twoChunks, unmatched, repo).rows[0].reasons.join(), /matched nothing in `p\/big\/B.cs`/)
  const twice = passing(); twice[1] = {...twice[1], report: {files: {...twice[1].report.files, 'p/big/A.cs': {mutants: mutants(['Killed'])}}}}
  assert.match(collect(twoChunks, twice, repo).rows[0].reasons.join(), /scored by two chunks/)
  const extra = collect(twoChunks, [...passing(), done('0009', big, 3, {})], repo)
  assert.equal(extra.ok, false)
  assert.deepEqual(extra.problems, ['result 0009 is not in the plan'])
  const repeated = collect(twoChunks, [...passing(), passing()[2]], repo)
  assert.deepEqual(repeated.problems, ['result 0003 arrived twice'])
  const observer = passing(); observer[2] = {...observer[2], observerFailed: true}
  assert.equal(collect(twoChunks, observer, repo).rows[1].ok, false)
})

test('a low score, nothing tested, or a Razor project with no Razor mutant tested fails like the full run', () => {
  const low = passing(); low[2] = {...low[2], report: {files: {'p/small/A.cs': {mutants: mutants(['Killed', 'Survived', 'Survived'])}}}}
  assert.match(collect(twoChunks, low, repo).rows[1].reasons.join(), /score 33.33 % is below the break 70/)
  const none = passing(); none[2] = {...none[2], report: {files: {'p/small/A.cs': {mutants: mutants(['CompileError'])}}}}
  assert.match(collect(twoChunks, none, repo).rows[1].reasons.join(), /0 mutants tested/)
  const razorPlan = {items: [
    {id: '0001', test: ui, chunk: 1, chunks: 2, files: ['p/ui/A.cs'], shard: 1},
    {id: '0002', test: ui, chunk: 2, chunks: 2, files: ['p/ui/B.razor'], shard: 2}]}
  const csOnly = [done('0001', ui, 1, {'p/ui/A.cs': {mutants: mutants(['Killed'])}}, {razor: true}),
    done('0002', ui, 2, {'p/ui/C.cs': {mutants: mutants(['Killed'])}}, {razor: true})]
  assert.match(collect(razorPlan, csOnly, repo).rows[0].reasons.join(), /0 Razor mutants tested/)
  const withRazor = [csOnly[0], done('0002', ui, 2, {'p/ui/obj/Debug/stryker-razor/B.cs': {mutants: mutants(['Killed'])}}, {razor: true})]
  assert.equal(collect(razorPlan, withRazor, repo).ok, true)
})
