#!/usr/bin/env node
// The weekly full mutation run, split across GitHub-hosted runners (owner, 2026-10-30). `node tooling/stryker.mjs full`
// mutates every project in one job, which no single runner can finish: on hosted runners a tested mutant costs about
// 14 to 25 seconds (the Builder partitions, the Blazor qualification partitions and the Scheduling PR run, Oct 27-29),
// so the 27,327 tested mutants in tooling/stryker-baselines.json are well over a hundred runner-hours. Hosted jobs stop
// at six hours, so the run is cut into parts that each fit, and one last job adds the parts back up.
//   node tooling/stryker-weekly.mjs plan <plan.json> <parts>      weigh every configured project by its baseline's
//                                    tested mutants; a project heavier than one part is split by source file (weighed
//                                    by size) into as many chunks as it needs; chunks and whole projects are packed into
//                                    <parts> parts, heaviest first, each into the lightest part so far.
//   node tooling/stryker-weekly.mjs shard <plan.json> <part> [-- args]   mutate one part: a whole project exactly as
//                                    `stryker.mjs full` does, a chunk as an explicit mutate list of its files. Each result
//                                    goes to StrykerOutput/weekly/results/item-<id>.json, slimmed to what scoring reads.
//   node tooling/stryker-weekly.mjs collect <plan.json> <results-dir>   join every part's results back into one report per
//                                    project and hold it to the same floor and checks as `stryker.mjs full`. A planned
//                                    chunk with no result, or a result the plan does not list, fails the run: a part that
//                                    timed out never passes as a smaller project.
import {execFileSync} from 'node:child_process'
import {appendFileSync, existsSync, mkdirSync, readdirSync, readFileSync, statSync, writeFileSync} from 'node:fs'
import path from 'node:path'
import {configOf, configProblems, fullModeBreak, mutateProject, razorTested, reportCounts, repository, selectedReport,
  sourceDirectories, targetOf, unmatchedChanges} from './stryker.mjs'

const root = path.resolve(import.meta.dirname, '..')
const git = (...args) => execFileSync('git', ['-C', root, ...args], {encoding: 'utf8'}).split(/\r?\n/).filter(Boolean)
const ordinal = (a, b) => a < b ? -1 : a > b ? 1 : 0
const round = value => Math.round(value * 10) / 10

// The production sources a project mutates: tracked .cs (and .razor for a Razor project) under the directories it
// compiles from, never a test project's. The same rule `stryker.mjs` uses to list a Razor project's sources.
export function projectFiles(target, text, listFiles = patterns => git('ls-files', '--', ...patterns)) {
  const razor = text.includes('Microsoft.NET.Sdk.Razor')
  const directories = [...new Set(sourceDirectories(target, text))]
  const patterns = directories.flatMap(directory => razor ? [`${directory}/*.cs`, `${directory}/*.razor`] : [`${directory}/*.cs`])
  return [...new Set(listFiles(patterns))].filter(file => !/\.tests\//.test(file)).sort(ordinal)
}

// Longest-processing-time packing: heaviest first, each into the lightest bin so far (ties to the lowest index), so
// the same input always gives the same plan.
function pack(weighted, binCount) {
  const bins = Array.from({length: binCount}, () => ({load: 0, items: []}))
  for (const item of [...weighted].sort((a, b) => b.weight - a.weight || ordinal(a.key, b.key))) {
    const bin = bins.reduce((best, next) => next.load < best.load ? next : best)
    bin.load += item.weight
    bin.items.push(item)
  }
  return bins
}

// projects: [{test, weight, files: [{file, size}]}]. A project up to a quarter over one part's share runs whole (files
// null, exactly the full run): one build beats two half-chunks. A heavier one is cut into ceil(weight / share) chunks
// of whole files, its weight shared out by file size. Every chunk is a mutate list, so its mutants are scored once.
const wholeUpTo = 1.25
export function planShards(projects, parts) {
  if (!Number.isInteger(parts) || parts < 1) throw new Error(`Part count must be a positive integer, not ${parts}`)
  const total = projects.reduce((sum, project) => sum + project.weight, 0), share = total / parts
  const items = []
  for (const project of [...projects].sort((a, b) => ordinal(a.test, b.test))) {
    const files = project.files ?? []
    const chunks = project.weight > share * wholeUpTo ? Math.min(Math.ceil(project.weight / share), files.length) : 1
    if (chunks <= 1) {
      items.push({test: project.test, chunk: 1, chunks: 1, files: null, weight: project.weight})
      continue
    }
    const size = files.reduce((sum, file) => sum + Math.max(file.size, 1), 0)
    const bins = pack(files.map(file => ({key: file.file, weight: Math.max(file.size, 1), file: file.file})), chunks)
      .filter(bin => bin.items.length)
    bins.forEach((bin, index) => items.push({test: project.test, chunk: index + 1, chunks: bins.length,
      files: bin.items.map(item => item.file).sort(ordinal), weight: project.weight * bin.load / size}))
  }
  const keyed = items.map(item => ({...item, key: `${item.test}#${String(item.chunk).padStart(3, '0')}`}))
  const bins = pack(keyed, parts).filter(bin => bin.items.length)
  const planned = bins.flatMap((bin, index) => bin.items.map(({key, ...item}) => ({...item, weight: round(item.weight), shard: index + 1})))
    .sort((a, b) => a.shard - b.shard || ordinal(a.test, b.test) || a.chunk - b.chunk)
    .map((item, index) => ({id: String(index + 1).padStart(4, '0'), ...item}))
  return {parts, share: round(share), total: round(total), shards: bins.map((bin, index) => ({shard: index + 1, weight: round(bin.load)})), items: planned}
}

// A project with no baseline yet is weighed as the mean of those that have one, so it still lands somewhere sensible.
export function weighProjects(repo, listFiles, sizeOf) {
  const tests = repo.testProjects.filter(test => !(test in repo.exclusions)).sort(ordinal)
  const known = tests.map(test => repo.baselines[test]?.tested).filter(Number.isFinite)
  const fallback = known.length ? known.reduce((a, b) => a + b, 0) / known.length : 1
  return tests.map(test => {
    const target = targetOf(test, repo.readFile)
    const files = projectFiles(target, repo.readFile(target), listFiles).map(file => ({file, size: sizeOf(file)}))
    return {test, weight: Math.max(repo.baselines[test]?.tested ?? fallback, 1), files}
  })
}

// Scoring reads only file keys and each mutant's status, so a result keeps those (and where the mutant is, for a
// reader) and drops the source text Stryker copies into every report.
export const slimReport = report => ({files: Object.fromEntries(Object.entries(report?.files ?? {}).map(([file, entry]) =>
  [file, {mutants: (entry.mutants ?? []).map(({id, status, mutatorName, location, statusReason}) =>
    ({id, status, mutatorName, location, ...(statusReason ? {statusReason} : {})}))}]))})

export function itemResult(item, result) {
  const report = result.report ? selectedReport(result.report) : undefined
  return {id: item.id, test: item.test, chunk: item.chunk, chunks: item.chunks, files: item.files,
    outcome: result.outcome, status: result.status ?? null,
    ...(result.observerFailed ? {observerFailed: true} : {}), ...(result.cleanupUnavailable ? {cleanupUnavailable: true} : {}),
    razor: Boolean(result.razor), razorPatterns: result.razorPatterns ?? 0,
    unmatched: item.files && result.report ? unmatchedChanges(result.report, item.files) : [],
    report: report ? slimReport(report) : null}
}

// One part, item by item. A part that cannot finish an item says so and goes on: the collect job reports every gap.
export async function shard(plan, part, strykerArgs, write, mutate = mutateProject, log = console.log) {
  const items = plan.items.filter(item => item.shard === part)
  if (!items.length) throw new Error(`Part ${part} has no items in the plan`)
  let failed = 0
  for (const item of items) {
    log(`- ${item.test}${item.files ? `, chunk ${item.chunk} of ${item.chunks} (${item.files.length} files)` : ', whole project'}`)
    const result = await mutate(item.test, item.files ?? undefined, strykerArgs)
    if (result.signal) return {signal: result.signal}
    write(item, itemResult(item, result))
    if (result.observerFailed || result.cleanupUnavailable) return result.status > 0 ? result.status : 1
    if (result.outcome.kind === 'failed') failed ||= result.status > 0 ? result.status : 1
  }
  return failed
}

// Every chunk of a project, joined: the same checks `stryker.mjs full` makes on one whole report.
export function collect(plan, results, repo) {
  const byId = new Map(), problems = []
  for (const result of results) {
    if (byId.has(result.id)) problems.push(`result ${result.id} arrived twice`)
    byId.set(result.id, result)
  }
  const planned = new Set(plan.items.map(item => item.id))
  for (const id of byId.keys()) if (!planned.has(id)) problems.push(`result ${id} is not in the plan`)
  const rows = []
  for (const test of [...new Set(plan.items.map(item => item.test))].sort(ordinal)) {
    const items = plan.items.filter(item => item.test === test), reasons = [], files = {}
    let razor = false
    for (const item of items) {
      const part = item.chunks > 1 ? `chunk ${item.chunk} of ${item.chunks} (part ${item.shard})` : `part ${item.shard}`
      const result = byId.get(item.id)
      if (!result) { reasons.push(`${part} has no result (the part did not finish)`); continue }
      if (result.test !== test || result.chunk !== item.chunk) { reasons.push(`${part} result names another item`); continue }
      if (result.outcome?.kind !== 'completed' || result.observerFailed || result.cleanupUnavailable || !result.report) {
        reasons.push(`${part} failed: ${JSON.stringify(result.outcome)}`)
        continue
      }
      if (result.unmatched?.length) reasons.push(`${part}: the mutate list matched nothing in ${result.unmatched.map(file => `\`${file}\``).join(', ')}`)
      razor ||= result.razor
      for (const [file, entry] of Object.entries(result.report.files)) {
        if (file in files) reasons.push(`${part}: \`${file}\` was scored by two chunks`)
        files[file] = entry
      }
    }
    const report = {files}, counts = reportCounts(report)
    const baseline = repo.baselines[test], floor = fullModeBreak(baseline, configOf(test, repo.readFile))
    if (!reasons.length) {
      if (!counts.tested) reasons.push('0 mutants tested')
      else if (razor && !razorTested(report)) reasons.push('Razor project but 0 Razor mutants tested')
      else if (!(counts.score >= floor)) reasons.push(`score ${counts.score} % is below the break ${floor}`)
    }
    rows.push({test, chunks: items.length, tested: counts.tested, baselineTested: baseline?.tested ?? null,
      score: counts.score, floor, ok: !reasons.length, reasons})
  }
  return {ok: !problems.length && rows.every(row => row.ok), problems, rows}
}

export function markdown(verdict) {
  const short = test => path.posix.basename(test).replace(/\.Tests\.csproj$/, '')
  return ['### Weekly full mutation run', '',
    verdict.ok ? 'Every project scored at or above its break.' : '**FAIL**: at least one project is below its break or incomplete.', '',
    ...verdict.problems.map(problem => `- **FAIL**: ${problem}`),
    '| project | chunks | tested (baseline) | score | break | result |', '|---|---|---|---|---|---|',
    ...verdict.rows.map(row => `| ${short(row.test)} | ${row.chunks} | ${row.tested} (${row.baselineTested ?? 'none'}) | ${row.score ?? 'n/a'} % | ${row.floor} | ${row.ok ? 'pass' : `**FAIL**: ${row.reasons.join('; ')}`} |`)].join('\n')
}

const readJson = file => JSON.parse(readFileSync(file, 'utf8'))
const resultFiles = directory => existsSync(directory)
  ? readdirSync(directory, {recursive: true}).map(String).filter(name => /(^|[\\/])item-\d+\.json$/.test(name)).sort(ordinal).map(name => path.join(directory, name))
  : []

if (import.meta.main) {
  const argv = process.argv.slice(2), dashes = argv.indexOf('--')
  const [command, ...rest] = dashes < 0 ? argv : argv.slice(0, dashes), strykerArgs = dashes < 0 ? [] : argv.slice(dashes + 1)
  const summary = text => {
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, `${text}\n`)
    console.log(text)
  }
  if (command === 'plan' && rest.length === 2) {
    const repo = repository(), problems = configProblems(repo)
    problems.forEach(problem => console.error(problem))
    if (problems.length) process.exit(1)
    const plan = {commit: git('rev-parse', 'HEAD')[0],
      ...planShards(weighProjects(repo, undefined, file => statSync(path.join(root, file)).size), Number(rest[1]))}
    mkdirSync(path.dirname(path.resolve(rest[0])), {recursive: true})
    writeFileSync(rest[0], `${JSON.stringify(plan, null, 2)}\n`)
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `shards=${JSON.stringify(plan.shards.map(entry => entry.shard))}\n`)
    summary([`### Weekly mutation plan`, '', `${plan.items.length} items in ${plan.shards.length} parts; about ${plan.share} tested mutants per part.`, '',
      '| part | weight | items |', '|---|---|---|',
      ...plan.shards.map(({shard: part, weight}) => `| ${part} | ${weight} | ${plan.items.filter(item => item.shard === part)
        .map(item => `${path.posix.basename(item.test).replace(/\.Tests\.csproj$/, '')}${item.chunks > 1 ? ` ${item.chunk}/${item.chunks}` : ''}`).join(', ')} |`)].join('\n'))
  } else if (command === 'shard' && rest.length === 2) {
    const plan = readJson(rest[0]), part = Number(rest[1])
    if (plan.commit !== git('rev-parse', 'HEAD')[0]) throw new Error(`The plan was made for ${plan.commit}, not this checkout`)
    const directory = path.join(root, 'StrykerOutput', 'weekly', 'results')
    mkdirSync(directory, {recursive: true})
    const outcome = await shard(plan, part, strykerArgs, (item, result) =>
      writeFileSync(path.join(directory, `item-${item.id}.json`), `${JSON.stringify(result)}\n`))
    if (outcome.signal) process.kill(process.pid, outcome.signal)
    else process.exitCode = outcome
  } else if (command === 'collect' && rest.length === 2) {
    const plan = readJson(rest[0]), verdict = collect(plan, resultFiles(rest[1]).map(readJson), repository())
    summary(markdown(verdict))
    process.exitCode = verdict.ok ? 0 : 1
  } else {
    throw new Error('usage: node tooling/stryker-weekly.mjs plan <plan.json> <parts> | shard <plan.json> <part> [-- stryker args] | collect <plan.json> <results-dir>')
  }
}
