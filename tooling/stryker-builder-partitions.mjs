#!/usr/bin/env node
import {execFileSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {createReadStream, existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, realpathSync, writeFileSync, appendFileSync} from 'node:fs'
import path from 'node:path'
import {createInterface} from 'node:readline'
import {changedSourceFiles, configProblems, fullModeBreak, mutateProject, reportCounts, repository, run, scoreVerdict, sourceDirectories, targetOf} from './stryker.mjs'

const root = path.resolve(import.meta.dirname, '..')
const selector = 'projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj'
const target = 'projections/dotnet/blocks/hlp.blocks.builder-definitions/Harborline.Blocks.BuilderDefinitions.csproj'
const controlHead = '69b2798f55790ef5c29056d04a998b8c626db823'
const conditionalInput = 'artifacts/packages/nuget/packed-version.props'
const finalStatuses = new Set(['Killed', 'Survived', 'Timeout', 'CompileError', 'Ignored', 'NoCoverage', 'RuntimeError'])
const ordinal = (a, b) => a < b ? -1 : a > b ? 1 : 0
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')
const canonical = value => JSON.stringify(value, (_, item) => item && typeof item === 'object' && !Array.isArray(item)
  ? Object.fromEntries(Object.keys(item).sort(ordinal).map(key => [key, item[key]])) : item)
const bytesOf = value => Buffer.from(`${canonical(value)}\n`)
const same = (actual, expected, message) => requireThat(canonical(actual) === canonical(expected), message)
function requireThat(condition, message) { if (!condition) throw new Error(message) }
const git = (...args) => execFileSync('git', ['-C', root, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']})
const slash = value => value.replaceAll('\\', '/')
const json = bytes => JSON.parse(new TextDecoder('utf-8', {fatal: true}).decode(bytes))

function literal(file) {
  requireThat(typeof file === 'string' && file && !/[<>:"\\|?*\[\]{}!\u0000-\u001f]/u.test(file), `Unsafe literal path: ${file}`)
  requireThat(file.split('/').every(part => part && part !== '.' && part !== '..' && !/^[ -]|[ .]$/.test(part)), `Unsafe path segment: ${file}`)
  return file
}

function directoryPath(directory) {
  const absolute = path.resolve(directory), parsed = path.parse(absolute)
  let current = parsed.root
  for (const part of absolute.slice(parsed.root.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, part)
    if (!existsSync(current)) mkdirSync(current)
    requireThat(lstatSync(current).isDirectory() && !lstatSync(current).isSymbolicLink() && path.resolve(realpathSync(current)) === current, `Linked or aliased directory: ${current}`)
  }
  return absolute
}

function regularPath(file, directory = root) {
  const absolute = path.resolve(directory, file), relative = path.relative(directory, absolute)
  requireThat(relative && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative), `Path escapes directory: ${file}`)
  let current = path.resolve(directory)
  requireThat(!lstatSync(current).isSymbolicLink() && path.resolve(realpathSync(current)) === current, `Linked directory: ${directory}`)
  for (const part of relative.split(path.sep)) {
    current = path.join(current, part)
    requireThat(!lstatSync(current).isSymbolicLink(), `Linked path: ${file}`)
  }
  const stat = lstatSync(absolute)
  requireThat(stat.isFile() && stat.nlink === 1, `Not a unique regular file: ${file}`)
  requireThat(path.resolve(realpathSync(absolute)) === absolute, `Aliased file: ${file}`)
  return absolute
}
const regular = (file, directory = root) => readFileSync(regularPath(file, directory))

function sourceText(bytes) {
  requireThat(!bytes.subarray(0, 3).equals(Buffer.from([239, 187, 191])), 'Unsupported source BOM')
  const text = new TextDecoder('utf-8', {fatal: true, ignoreBOM: true}).decode(bytes)
  requireThat(!text.includes('\0') && Buffer.from(text).equals(bytes), 'Unsupported source encoding')
  return text
}

function configuration() {
  const repo = repository(), problems = configProblems(repo)
  requireThat(!problems.length, problems.join('\n'))
  requireThat(repo.testProjects.includes(selector) && !(selector in repo.exclusions) && targetOf(selector, repo.readFile) === target, 'Unsupported Builder target')
  const config = JSON.parse(repo.readFile(`${path.posix.dirname(selector)}/stryker-config.json`))['stryker-config']
  same(Object.keys(config).sort(), ['coverage-analysis', 'project', 'reporters', 'since', 'thresholds'], 'Unsupported Builder config keys')
  requireThat(config['coverage-analysis'] === 'off', 'Builder requires coverage-analysis off')
  same(config.reporters, ['json'], 'Builder requires JSON-only configuration')
  same(config.since, {enabled: true, target: 'origin/main'}, 'Unsupported since config')
  same(Object.keys(config.thresholds).sort(), ['break', 'high', 'low'], 'Unsupported threshold options')
  const floor = fullModeBreak(repo.baselines[selector], config)
  requireThat(Number.isFinite(floor) && floor >= 74 && floor <= 100, 'Builder floor must be at least 74')
  const sdk = json(regular('global.json')).sdk, tool = json(regular('.config/dotnet-tools.json')).tools?.['dotnet-stryker']
  same(sdk, {version: '11.0.100-rc.1.26425.128', rollForward: 'disable'}, 'Unsupported SDK pin')
  requireThat(tool?.version === '5.0.0' && tool.rollForward === false, 'Unsupported Stryker pin')
  same(sourceDirectories(target, repo.readFile(target)), [path.posix.dirname(target)], 'Linked Builder source is unsupported')
  return {repo, config, floor, sdk, tool}
}

export function makePlan(mode) {
  requireThat(['normal', 'diagnostic', 'qualification'].includes(mode), 'Unsupported partition mode')
  const {repo, config, floor, sdk, tool} = configuration()
  const head = git('rev-parse', 'HEAD').trim(), tree = git('rev-parse', 'HEAD^{tree}').trim()
  requireThat(/^[a-f0-9]{40}$/.test(process.env.EXPECTED_HEAD ?? '') && process.env.EXPECTED_HEAD === head, 'EXPECTED_HEAD must equal checkout HEAD')
  requireThat(git('status', '--porcelain=v1', '--untracked-files=all') === '', 'Checkout must be clean')
  const expectedBase = process.env.EXPECTED_BASE
  requireThat(expectedBase === undefined || /^[a-f0-9]{40}$/.test(expectedBase), 'EXPECTED_BASE must be a full commit ID')
  const base = git('rev-parse', `${expectedBase ?? 'origin/main'}^{commit}`).trim()
  requireThat(expectedBase === undefined || base === expectedBase, 'EXPECTED_BASE must identify the exact commit')
  const runId = process.env.GITHUB_RUN_ID, runAttempt = process.env.GITHUB_RUN_ATTEMPT
  requireThat(/^[1-9]\d*$/.test(runId ?? '') && /^[1-9]\d*$/.test(runAttempt ?? ''), 'Missing run identity')
  // Owner, 2026-10-30: a normal PR mutates only its changed files, Builder included, natively in the stryker job. The four
  // complete-file partitions run only for the explicit diagnostic and qualification label commands.
  const selected = mode !== 'normal'
  const tracked = git('ls-files', '--stage', '-z').split('\0').filter(Boolean).map(entry => {
    const match = /^(\d{6}) ([a-f0-9]{40}) 0\t(.+)$/.exec(entry)
    requireThat(match, 'Unsupported Git index entry')
    return {path: match[3], mode: match[1], blob: match[2]}
  })
  const directory = `${path.posix.dirname(target)}/`, aliases = new Set()
  const sources = tracked.filter(row => row.path.startsWith(directory) && row.path.endsWith('.cs')).map(row => {
    const file = literal(row.path.slice(directory.length)), alias = file.normalize('NFC').toLowerCase()
    requireThat(!aliases.has(alias), `Aliased source: ${file}`)
    aliases.add(alias)
    requireThat(['100644', '100755'].includes(row.mode), `Nonregular Git source: ${file}`)
    const bytes = regular(row.path)
    sourceText(bytes)
    return {...row, file, byteLength: bytes.length, sha256: sha256(bytes), owner: 0}
  }).sort((a, b) => ordinal(a.path, b.path))
  requireThat(sources.length >= 4, 'Four nonempty source partitions are required')
  const loads = [0, 0, 0, 0], sizes = [0, 0, 0, 0]
  for (const row of [...sources].sort((a, b) => b.byteLength - a.byteLength || ordinal(a.path, b.path))) {
    const owner = loads.indexOf(Math.min(...loads))
    row.owner = owner + 1; loads[owner] += row.byteLength; sizes[owner] += 1
  }
  requireThat(sizes.every(Boolean), 'Empty source partition')
  const fixed = new Set(['global.json', '.config/dotnet-tools.json', 'tooling/stryker.mjs', 'tooling/resolve-command.mjs',
    'tooling/stryker-builder-partitions.mjs', 'tooling/stryker-baselines.json', 'tooling/stryker-exclusions.json'])
  const pins = tracked.filter(row => fixed.has(row.path) || /\.(csproj|props|targets)$/.test(row.path) || /(^|\/)(nuget\.config|stryker-config\.json)$/i.test(row.path))
    .sort((a, b) => ordinal(a.path, b.path)).map(row => {
      requireThat(['100644', '100755'].includes(row.mode), `Nonregular build input: ${row.path}`)
      const bytes = regular(row.path)
      return {...row, byteLength: bytes.length, sha256: sha256(bytes)}
    })
  for (const file of fixed) requireThat(pins.some(pin => pin.path === file), `Missing tracked pin: ${file}`)
  const conditional = lstatSync(path.join(root, conditionalInput), {throwIfNoEntry: false}) ? regular(conditionalInput) : undefined
  return {version: 1, mode, selected, head, tree, base, runId, runAttempt, selector, target, sdk, tool, config, floor,
    ...(selected ? {controlHead} : {}), sources, pins,
    conditional: conditional === undefined ? {path: conditionalInput, absent: true} : {path: conditionalInput, byteLength: conditional.length, sha256: sha256(conditional)}}
}

function admit(manifestPath) {
  requireThat(/^[a-f0-9]{40}$/.test(process.env.EXPECTED_BASE ?? ''), 'EXPECTED_BASE must be the planned commit ID')
  const bytes = regular(path.basename(manifestPath), path.dirname(path.resolve(manifestPath))), plan = json(bytes)
  const current = makePlan(plan.mode)
  requireThat(bytes.equals(bytesOf(current)), 'Plan differs from current source, selection, base, pins or run')
  return {plan: current, planHash: sha256(bytes)}
}

function prefixesOf(checkout, plan) {
  requireThat(typeof checkout === 'string', 'Missing checkout prefix')
  const normalized = slash(checkout)
  requireThat(/^(?:\/[^/]|[A-Za-z]:\/[^/])/.test(normalized), 'Invalid checkout prefix')
  literal(normalized.replace(/^(?:\/|[A-Za-z]:\/)/, ''))
  return {checkout: normalized, target: `${normalized}/${path.posix.dirname(plan.target)}`}
}

function sourcePath(file, plan, prefixes) {
  requireThat(typeof file === 'string', 'Missing report source path')
  const normalized = slash(file), targetDirectory = path.posix.dirname(plan.target)
  let relative
  if (normalized.startsWith(`${prefixes.target}/`)) relative = `${targetDirectory}/${normalized.slice(prefixes.target.length + 1)}`
  else if (normalized.startsWith(`${prefixes.checkout}/`)) relative = normalized.slice(prefixes.checkout.length + 1)
  else if (!/^(?:\/|[A-Za-z]:)/.test(normalized)) relative = normalized.startsWith(`${targetDirectory}/`) ? normalized : `${targetDirectory}/${normalized}`
  requireThat(relative !== undefined, `Unverified source prefix: ${file}`)
  literal(relative)
  requireThat(plan.sources.some(row => row.path === relative), `Unexpected source: ${file}`)
  return relative
}

export async function generationInventory(plan, logs, prefixes) {
  same(prefixes, prefixesOf(prefixes.checkout, plan), 'Invalid target prefix')
  requireThat(logs.length > 0 && new Set(logs).size === logs.length, 'Missing or duplicate primary logs')
  const files = new Map(), ids = new Set(), hashes = []
  let current, total
  for (const log of [...logs].sort(ordinal)) {
    requireThat(/^log-\d{8}\.txt$/.test(path.basename(log)), 'Not a primary dated native log')
    regularPath(path.basename(log), path.dirname(log))
    const hash = createHash('sha256')
    for await (const chunk of createReadStream(log)) hash.update(chunk)
    hashes.push({name: path.basename(log), sha256: hash.digest('hex')})
    if (total !== undefined) continue
    const lines = createInterface({input: createReadStream(log), crlfDelay: Infinity})
    try {
      for await (const line of lines) {
        const payload = /^\d{4}-\d\d-\d\dT\S+\s+\[DBG\] (.*)$/.exec(line)?.[1]
        if (!payload) continue
        if (payload.startsWith('Mutating ')) {
          const match = /^Mutating "(.+)" \(5f74b457\)$/.exec(payload)
          requireThat(match && /^(?:\/|[A-Za-z]:[\\/])/.test(match[1]), 'Malformed native source declaration')
          current = sourcePath(match[1], plan, prefixes)
          requireThat(!files.has(current), 'Repeated native source declaration')
          files.set(current, 0)
        } else if (payload.startsWith('Mutant ')) {
          if (current && /^Mutant (?:0|[1-9]\d*) discarded as it is a duplicate of (?:0|[1-9]\d*)(?: \([a-f0-9]{8}\))?$/.test(payload)) continue
          const match = /^Mutant (0|[1-9]\d*) created /.exec(payload)
          requireThat(match && current && !ids.has(match[1]), 'Malformed or duplicate created mutant')
          ids.add(match[1]); files.set(current, files.get(current) + 1)
        } else if (payload.includes('mutants created')) {
          const match = /^(0|[1-9]\d*) mutants created \(017e65ab\)$/.exec(payload)
          requireThat(match, 'Malformed generation boundary')
          total = Number(match[1])
          requireThat(total === ids.size, 'Native generation total mismatch')
          break
        }
      }
    } finally { lines.close(); lines.input.destroy() }
  }
  requireThat(total !== undefined, 'Missing DBG precompile generation boundary')
  same([...files.keys()].sort(ordinal), plan.sources.map(row => row.path).sort(ordinal), 'Native source universe differs from plan')
  return {files: [...files].sort(([a], [b]) => ordinal(a, b)).map(([file, created]) => ({path: file, created})), total, logs: hashes}
}

const nativeArgs = (plan, index) => ['--log-to-file', '--concurrency', '2',
  ...plan.sources.filter(row => row.owner === index).flatMap(row => ['--mutate', row.file]),
  ...(plan.mode === 'diagnostic' ? ['--reporter', 'json', '--reporter', 'dots', '--verbosity', 'error'] : [])]
const encoded = bytes => ({bytes: bytes.toString('base64'), sha256: sha256(bytes)})
function embedded(value) {
  requireThat(value && typeof value.bytes === 'string' && /^[A-Za-z0-9+/]*={0,2}$/.test(value.bytes), 'Invalid embedded native evidence')
  const bytes = Buffer.from(value.bytes, 'base64')
  requireThat(bytes.toString('base64') === value.bytes && sha256(bytes) === value.sha256, 'Embedded native evidence hash mismatch')
  return json(bytes)
}

function completed(result) {
  return result?.outcome?.kind === 'completed' && result.status === 0 && result.signal === null &&
    !result.observerFailed && !result.cleanupUnavailable && !result.descendantsUnknown
}

function validatePacket(plan, packet, index) {
  const receipt = json(packet.receiptBytes), report = json(packet.reportBytes), planHash = sha256(bytesOf(plan))
  same(receipt.binding, {version: 1, planHash, head: plan.head, tree: plan.tree, runId: plan.runId, runAttempt: plan.runAttempt, index}, 'Stale or misbound shard receipt')
  requireThat(receipt.reportSha256 === sha256(packet.reportBytes), 'Raw report hash mismatch')
  const prefixes = prefixesOf(receipt.prefixes?.checkout, plan)
  same(receipt.prefixes, prefixes, 'Unverified target prefix')
  const final = embedded(receipt.finalResult), attempt = embedded(receipt.attempt), override = embedded(receipt.override)
  requireThat(completed(receipt.returned) && completed(final), 'Native attempt did not settle successfully')
  same(receipt.returned, final, 'Authoritative return differs from final result.json')
  const output = slash(final.output ?? ''), outputPrefix = `${prefixes.checkout}/StrykerOutput/${path.posix.basename(path.posix.dirname(plan.selector))}-`
  requireThat(output.startsWith(outputPrefix) && /^[A-Za-z0-9_-]+$/.test(output.slice(outputPrefix.length)), 'Invalid unique native attempt path')
  requireThat(slash(final.reportPath ?? '') === `${output}/reports/mutation-report.json` && final.razor === false && Number.isInteger(final.pid) && final.pid > 0, 'Invalid native final result paths')
  requireThat(attempt.test === plan.selector && attempt.target === plan.target && attempt.sourceCommit === plan.head && attempt.executable === 'dotnet', 'Native attempt identity mismatch')
  requireThat(slash(attempt.cwd ?? '') === `${prefixes.checkout}/${path.posix.dirname(plan.selector)}` && slash(attempt.output ?? '') === output && slash(attempt.configFile ?? '') === `${output}/stryker-config.json`, 'Native attempt paths mismatch')
  const expectedOverride = {'stryker-config': {...plan.config, since: {enabled: false}, thresholds: {...plan.config.thresholds, break: 0}}}
  same(override, expectedOverride, 'Native override changed mutation options')
  requireThat(attempt.configSha256 === receipt.override.sha256 && attempt.wrapperSha256 === plan.pins.find(pin => pin.path === 'tooling/stryker.mjs')?.sha256, 'Native wrapper or config hash mismatch')
  requireThat(Array.isArray(attempt.argv) && attempt.argv.every(arg => typeof arg === 'string'), 'Invalid native arguments')
  const argv = [...attempt.argv]
  same(argv.splice(0, 5).map((arg, position) => position === 2 || position === 4 ? slash(arg) : arg), ['stryker', '--output', output, '--config-file', `${output}/stryker-config.json`], 'Native argument prefix mismatch')
  if (argv[0] === '--msbuild-path') {
    const msbuild = slash(argv[1] ?? '')
    requireThat(/^[A-Za-z]:\//.test(msbuild) && msbuild.endsWith(`/${plan.sdk.version}/MSBuild.exe`) && !msbuild.split('/').includes('..'), 'Unsupported MSBuild path')
    argv.splice(0, 2)
  }
  same(argv, nativeArgs(plan, index), 'Native mutation options mismatch')
  requireThat(receipt.control === plan.controlHead, 'Control evidence mismatch')
  requireThat(report && report.schemaVersion === '2' && report.files && typeof report.files === 'object' && !Array.isArray(report.files), 'Invalid report files')
  requireThat(slash(report.projectRoot ?? '') === prefixes.target, 'Report project root mismatch')
  const generation = receipt.generation
  requireThat(generation && Array.isArray(generation.files) && Array.isArray(generation.logs) && generation.logs.length > 0, 'Missing generation evidence')
  same(generation.files.map(row => row.path), plan.sources.map(row => row.path), 'Generation source universe mismatch')
  requireThat(generation.files.every(row => Number.isSafeInteger(row.created) && row.created >= 0) && generation.total === generation.files.reduce((sum, row) => sum + row.created, 0), 'Invalid generation counts')
  requireThat(new Set(generation.logs.map(log => log.name)).size === generation.logs.length && generation.logs.every(log => /^log-\d{8}\.txt$/.test(log.name) && /^[a-f0-9]{64}$/.test(log.sha256)), 'Invalid generation log hashes')
  const rows = new Map(), ids = new Set()
  for (const [file, row] of Object.entries(report.files)) {
    const key = sourcePath(file, plan, prefixes), source = plan.sources.find(candidate => candidate.path === key)
    requireThat(!rows.has(key) && row && Array.isArray(row.mutants), 'Duplicate or malformed source row')
    const placeholder = row.language === 'none' && row.source === 'File ignored by mutate filter' && row.mutants.length === 0
    if (placeholder) requireThat(source.owner !== index, 'Owner source is an ignored placeholder')
    else {
      requireThat(row.language === 'cs' && typeof row.source === 'string' && Buffer.byteLength(row.source) === source.byteLength && sha256(Buffer.from(row.source)) === source.sha256, 'Report source bytes mismatch')
      const identities = new Set()
      for (const mutant of row.mutants) {
        requireThat(mutant && finalStatuses.has(mutant.status), 'Unknown or unfinished mutant status')
        requireThat(typeof mutant.id === 'string' && /^(0|[1-9]\d*)$/.test(mutant.id) && !ids.has(mutant.id), 'Invalid or duplicate invocation-local mutant ID')
        ids.add(mutant.id)
        requireThat(source.owner === index || ['Ignored', 'CompileError'].includes(mutant.status), 'Non-owner mutant is not Ignored or CompileError')
        const identity = semanticKey(source, mutant)
        requireThat(!identities.has(identity), 'Ambiguous duplicate mutant identity')
        identities.add(identity)
      }
    }
    if (source.owner === index) requireThat(row.mutants.length === generation.files.find(row => row.path === key).created, 'Owner final count differs from fresh generation')
    rows.set(key, row)
  }
  same([...rows.keys()].sort(ordinal), plan.sources.map(row => row.path), 'Report source universe differs from plan')
  same(final.counts, reportCounts(report), 'Final result counts mismatch')
  const owned = {files: Object.fromEntries([...rows].filter(([file]) => plan.sources.find(row => row.path === file).owner === index))}
  requireThat(reportCounts(owned).tested > 0, 'Owner group tested zero mutants')
  return {receipt, report, rows, receiptHash: sha256(packet.receiptBytes)}
}

function semanticKey(source, mutant) {
  const {start, end} = mutant.location ?? {}
  requireThat([start?.line, start?.column, end?.line, end?.column].every(value => Number.isSafeInteger(value) && value > 0) &&
    (end.line > start.line || end.line === start.line && end.column >= start.column), 'Invalid complete mutant location')
  requireThat(typeof mutant.mutatorName === 'string' && mutant.mutatorName.length > 0 && typeof mutant.replacement === 'string', 'Incomplete mutant identity')
  return canonical([source.path, source.sha256, start.line, start.column, end.line, end.column, mutant.mutatorName, mutant.replacement])
}

export function compose(plan, packets) {
  requireThat(plan.selected && packets.length === 4, 'Exactly four selected Builder packets are required')
  const admitted = packets.map((packet, index) => validatePacket(plan, packet, index + 1))
  for (const packet of admitted.slice(1)) same(packet.receipt.generation.files, admitted[0].receipt.generation.files, 'Fresh generation counts disagree across shards')
  const files = {}, owners = []
  for (const source of plan.sources) {
    const owner = admitted[source.owner - 1], row = owner.rows.get(source.path)
    const identitySet = value => value.mutants.map(mutant => semanticKey(source, mutant)).sort(ordinal)
    const rollbackSet = value => value.mutants.filter(mutant => mutant.status === 'CompileError').map(mutant => semanticKey(source, mutant)).sort(ordinal)
    for (const packet of admitted) {
      const other = packet.rows.get(source.path)
      if (other.language !== 'none') {
        same(identitySet(other), identitySet(row), 'Real non-owner semantic identities disagree')
        same(rollbackSet(other), rollbackSet(row), 'CompileError identities disagree in either direction')
      }
    }
    files[source.path] = row
    owners.push({path: source.path, sourceSha256: source.sha256, owner: source.owner, ownerFileSha256: sha256(bytesOf(row)), reportSha256: owner.receipt.reportSha256})
  }
  const provenance = {kind: 'COMPOSED', planHash: sha256(bytesOf(plan)), head: plan.head, owners,
    shards: admitted.map(({receipt, receiptHash}) => ({index: receipt.binding.index, receiptSha256: receiptHash,
      reportSha256: receipt.reportSha256, finalResultSha256: receipt.finalResult.sha256, attemptSha256: receipt.attempt.sha256, generation: receipt.generation}))}
  return {report: {schemaVersion: '2', projectRoot: path.posix.dirname(plan.target), files, composition: provenance}, provenance}
}

function observeControl(plan) {
  const directory = process.env.HARBORLINE_CONTROL_REPO
  requireThat(directory && existsSync(directory), 'Selected native Builder requires HARBORLINE_CONTROL_REPO')
  const controlGit = (...args) => execFileSync('git', ['-C', directory, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
  requireThat(controlGit('rev-parse', 'HEAD') === plan.controlHead && controlGit('status', '--porcelain=v1', '--untracked-files=all') === '', 'Control checkout must be exact and clean')
  return plan.controlHead
}

async function shard(manifestPath, index, native, control) {
  const {plan, planHash} = admit(manifestPath)
  requireThat(plan.selected, 'Cannot execute an unselected Builder plan')
  const outputRoot = directoryPath(path.join(root, 'StrykerOutput'))
  const directory = path.join(outputRoot, `builder-partition-${index}`)
  mkdirSync(directory)
  const diagnostics = path.join(directory, 'diagnostics')
  mkdirSync(diagnostics)
  same(await control(plan), plan.controlHead, 'Control observation mismatch')
  const returned = await native(plan.selector, undefined, nativeArgs(plan, index))
  if (!completed(returned)) return returned?.signal ? {signal: returned.signal} : returned?.status > 0 ? returned.status : 1
  const prefixes = prefixesOf(root, plan), output = path.resolve(returned.output)
  requireThat(path.dirname(output) === outputRoot, 'Native output escaped StrykerOutput')
  const reportBytes = regular('reports/mutation-report.json', output)
  same(returned.report, json(reportBytes), 'Returned report differs from raw native report')
  const {report: ignored, ...summary} = returned
  const generation = await generationInventory(plan, readdirSync(path.join(output, 'logs')).filter(name => /^log-\d{8}\.txt$/.test(name)).map(name => path.join(output, 'logs', name)), prefixes)
  const receipt = {binding: {version: 1, planHash, head: plan.head, tree: plan.tree, runId: plan.runId, runAttempt: plan.runAttempt, index}, prefixes,
    reportSha256: sha256(reportBytes), returned: summary, finalResult: encoded(regular('diagnostics/result.json', output)),
    attempt: encoded(regular('diagnostics/attempt.json', output)), override: encoded(regular('stryker-config.json', output)), generation,
    control: await control(plan)}
  const receiptBytes = bytesOf(receipt)
  validatePacket(plan, {receiptBytes, reportBytes}, index)
  same(admit(manifestPath), {plan, planHash}, 'Post-native admission changed')
  writeFileSync(path.join(diagnostics, 'report.json'), reportBytes, {flag: 'wx', flush: true})
  writeFileSync(path.join(diagnostics, 'receipt.json'), receiptBytes, {flag: 'wx', flush: true})
  return 0
}

function collect(plan, downloadDirectory) {
  const directory = path.resolve(downloadDirectory)
  requireThat(!lstatSync(directory).isSymbolicLink() && path.resolve(realpathSync(directory)) === directory, 'Linked download directory')
  const expected = [1, 2, 3, 4].map(index => `builder-partition-${plan.runId}-${plan.runAttempt}-${index}`)
  same(readdirSync(directory).sort(ordinal), expected, 'Missing, extra or stale artifact directories')
  const packets = expected.map(name => {
    const folder = path.join(directory, name)
    requireThat(!lstatSync(folder).isSymbolicLink() && lstatSync(folder).isDirectory(), 'Invalid artifact directory')
    same(readdirSync(folder).sort(ordinal), ['receipt.json', 'report.json'], 'Incomplete or extra artifact files')
    return {receiptBytes: regular('receipt.json', folder), reportBytes: regular('report.json', folder)}
  })
  return compose(plan, packets)
}

function writeComposition(composition) {
  const directory = path.join(root, 'StrykerOutput', 'builder-owner')
  for (const name of ['reports', 'diagnostics']) directoryPath(path.join(directory, name))
  const reportPath = path.join(directory, 'reports', 'mutation-report.json')
  for (const file of ['reports/mutation-report.json', 'diagnostics/composition.json']) if (lstatSync(path.join(directory, file), {throwIfNoEntry: false})) regularPath(file, directory)
  writeFileSync(reportPath, bytesOf(composition.report), {flush: true})
  writeFileSync(path.join(directory, 'diagnostics', 'composition.json'), bytesOf(composition.provenance), {flush: true})
  return reportPath
}

export async function main(argv = process.argv.slice(2), native = mutateProject, control = observeControl) {
  const [command, first, second] = argv
  requireThat(['plan', 'admit', 'shard', 'collect', 'pr'].includes(command), 'usage: plan <normal|diagnostic|qualification> <manifestPath> | admit <manifestPath> | shard <manifestPath> <1..4> | collect <manifestPath> <downloadDir> | pr <manifestPath> <downloadDir>')
  requireThat(argv.length === (command === 'admit' ? 2 : 3) && first && (command === 'admit' || second), 'Invalid partition command arguments')
  if (command === 'plan') {
    const plan = makePlan(first)
    directoryPath(path.dirname(path.resolve(second)))
    writeFileSync(second, bytesOf(plan), {flag: 'wx', flush: true})
    if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `selected=${plan.selected}\nsubject=${plan.head}\nbase=${plan.base}\n`)
    return 0
  }
  if (command === 'admit') { admit(first); return 0 }
  if (command === 'shard') {
    requireThat(/^[1-4]$/.test(second), 'Partition index must be 1 through 4')
    return shard(first, Number(second), native, control)
  }
  const {plan} = admit(first)
  if (command === 'pr') {
    requireThat(plan.mode === 'normal' && !plan.selected, 'PR requires normal mode')
    // Every changed project, Builder included, mutates its changed files against the planned base; downloads are unused.
    return run(repository(), undefined, ['--log-to-file'], native, (project, text) => changedSourceFiles(project, text, root, plan.base))
  }
  requireThat(plan.selected, 'Cannot collect an unselected Builder plan')
  const composition = collect(plan, second)
  writeComposition(composition)
  const verdict = scoreVerdict(composition.report, fullModeBreak(repository().baselines[selector], plan.config))
  if (!verdict.ok) console.error(verdict.reason)
  return verdict.ok ? 0 : 1
}

if (import.meta.main) {
  try {
    const outcome = await main()
    if (outcome?.signal) process.kill(process.pid, outcome.signal)
    else process.exitCode = outcome
  } catch (error) { console.error(error.message); process.exitCode = 1 }
}
