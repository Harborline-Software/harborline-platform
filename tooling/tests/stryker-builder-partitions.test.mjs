import assert from 'node:assert/strict'
import {createHash} from 'node:crypto'
import {existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {execFileSync, spawnSync} from 'node:child_process'
import {tmpdir} from 'node:os'
import path from 'node:path'
import test from 'node:test'
import {pathToFileURL} from 'node:url'
import {compose, generationInventory} from '../stryker-builder-partitions.mjs'

const hash = bytes => createHash('sha256').update(bytes).digest('hex')
const ownedTemp = mkdtempSync(path.join(tmpdir(), 'builder-partitions-153-'))
const repositoryRoot = path.resolve(import.meta.dirname, '../..')
const builderTest = 'projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj'
const builderTarget = 'projections/dotnet/blocks/hlp.blocks.builder-definitions/Harborline.Blocks.BuilderDefinitions.csproj'
const sourceDirectory = path.posix.dirname(builderTarget)
const controlHead = '69b2798f55790ef5c29056d04a998b8c626db823'
const names = ['A.cs', 'B.cs', 'Nested/C.cs', 'Nested/D.cs', 'Zero.cs']
const sourceTexts = ['// ' + 'a'.repeat(97) + '\n', '// ' + 'b'.repeat(87) + '\n', '// ' + 'c'.repeat(77) + '\n', '// ' + 'd'.repeat(67) + '\n', '\n']
const owners = [1, 2, 3, 4, 4]
const statuses = [['Killed', 'NoCoverage', 'CompileError', 'Ignored'], ['Timeout', 'Survived'], ['RuntimeError', 'Killed', 'Killed'], ['Killed', 'Killed'], []]
const slash = value => value.replaceAll('\\', '/')
const encoded = bytes => ({bytes: Buffer.from(bytes).toString('base64'), sha256: hash(bytes)})
const jsonBytes = value => Buffer.from(JSON.stringify(value))
const git = (directory, ...args) => execFileSync('git', ['-C', directory, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
function write(directory, file, bytes) {
  const absolute = path.join(directory, file)
  mkdirSync(path.dirname(absolute), {recursive: true})
  writeFileSync(absolute, bytes)
  return absolute
}

function counts(report) {
  const values = Object.values(report.files).flatMap(file => file.mutants).map(mutant => mutant.status)
  const detected = values.filter(value => value === 'Killed' || value === 'Timeout').length
  const undetected = values.filter(value => value === 'Survived' || value === 'NoCoverage').length
  return {total: values.length, tested: values.filter(value => ['Killed', 'Survived', 'Timeout', 'RuntimeError'].includes(value)).length,
    detected, undetected, score: detected + undetected ? Math.floor(detected / (detected + undetected) * 10000) / 100 : null}
}

async function checkout({changed = 'builder', mode = 'normal', floor = 74} = {}) {
  const directory = mkdtempSync(path.join(ownedTemp, 'repo-'))
  const tracked = git(repositoryRoot, 'ls-files', '-z').split('\0')
  for (const file of tracked.filter(file => /\.(csproj|props|targets)$/.test(file) || /(^|\/)(stryker-config\.json|nuget\.config)$/i.test(file) ||
    ['global.json', '.config/dotnet-tools.json', 'tooling/stryker.mjs', 'tooling/resolve-command.mjs', 'tooling/stryker-baselines.json', 'tooling/stryker-exclusions.json'].includes(file))) {
    write(directory, file, readFileSync(path.join(repositoryRoot, file)))
  }
  write(directory, 'tooling/stryker-builder-partitions.mjs', readFileSync(path.join(repositoryRoot, 'tooling/stryker-builder-partitions.mjs')))
  write(directory, '.gitignore', 'StrykerOutput/\nartifacts/\n')
  const house = await import(pathToFileURL(path.join(directory, 'tooling/stryker.mjs')))
  const projects = tracked.filter(file => file.endsWith('.csproj') && /Include="(?:Microsoft\.NET\.Test\.Sdk|xunit|MSTest[\w.]*|NUnit)"/.test(readFileSync(path.join(repositoryRoot, file), 'utf8')))
  const read = file => existsSync(path.join(directory, file)) ? readFileSync(path.join(directory, file), 'utf8') : undefined
  const exclusions = JSON.parse(read('tooling/stryker-exclusions.json'))
  const routes = projects.filter(file => !(file in exclusions)).map(file => ({test: file, target: house.targetOf(file, read)}))
  for (const route of routes.filter(row => row.test !== builderTest)) write(directory, `${path.posix.dirname(route.target)}/Route.cs`, '// base\n')
  names.forEach((name, index) => write(directory, `${sourceDirectory}/${name}`, sourceTexts[index]))
  if (floor !== 74) {
    const configPath = `${path.posix.dirname(builderTest)}/stryker-config.json`, config = JSON.parse(read(configPath))
    config['stryker-config'].thresholds = {break: floor, low: Math.max(60, floor), high: Math.max(80, floor)}
    write(directory, configPath, JSON.stringify(config))
  }
  git(directory, 'init', '--quiet'); git(directory, 'config', 'user.name', 'Partition fixture'); git(directory, 'config', 'user.email', 'fixture@example.invalid')
  git(directory, 'config', 'core.autocrlf', 'false'); git(directory, 'add', '.'); git(directory, 'commit', '--quiet', '-m', 'base')
  const base = git(directory, 'rev-parse', 'HEAD')
  git(directory, 'update-ref', 'refs/remotes/origin/main', base)
  if (changed === 'builder' || changed === 'all') write(directory, `${sourceDirectory}/A.cs`, sourceTexts[0].replace('aaa', 'xyz'))
  if (changed === 'all' || changed === 'others') for (const route of routes.filter(row => row.test !== builderTest)) write(directory, `${path.posix.dirname(route.target)}/Route.cs`, '// changed\n')
  if (changed !== 'none') { git(directory, 'add', '.'); git(directory, 'commit', '--quiet', '-m', 'changed') }
  const env = {...process.env, EXPECTED_HEAD: git(directory, 'rev-parse', 'HEAD'), GITHUB_RUN_ID: '314', GITHUB_RUN_ATTEMPT: '2', GITHUB_OUTPUT: path.join(directory, 'StrykerOutput', 'github-output'),
    GITHUB_STEP_SUMMARY: path.join(directory, 'StrykerOutput', 'summary'), HARBORLINE_CONTROL_REPO: undefined}
  const previous = Object.fromEntries(['EXPECTED_HEAD', 'GITHUB_RUN_ID', 'GITHUB_RUN_ATTEMPT', 'GITHUB_OUTPUT', 'GITHUB_STEP_SUMMARY', 'HARBORLINE_CONTROL_REPO'].map(key => [key, process.env[key]]))
  for (const key of Object.keys(previous)) if (env[key] === undefined) delete process.env[key]; else process.env[key] = env[key]
  delete process.env.HARBORLINE_CONTROL_REPO
  const adapter = await import(pathToFileURL(path.join(directory, 'tooling/stryker-builder-partitions.mjs')))
  const manifest = path.join(directory, 'StrykerOutput', 'builder-plan', 'plan.json')
  await adapter.main(['plan', mode, manifest])
  const plan = JSON.parse(readFileSync(manifest, 'utf8'))
  return {directory, adapter, house, manifest, plan, routes, env, base, restore() {
    for (const [key, value] of Object.entries(previous)) if (value === undefined) delete process.env[key]; else process.env[key] = value
  }}
}

function reportFor(plan, index, checkoutDirectory) {
  const files = {}, prefix = `${slash(checkoutDirectory)}/${sourceDirectory}`
  let id = 0
  for (const position of [owners.indexOf(index), ...names.map((_, n) => n).filter(n => n !== owners.indexOf(index))]) {
    const file = names[position], source = readFileSync(path.join(checkoutDirectory, sourceDirectory, file), 'utf8')
    const mutants = statuses[position].map((status, n) => ({id: String(id++), location: {start: {line: 1, column: 1}, end: {line: 1, column: 2}},
      mutatorName: 'Literal mutation', replacement: n === 0 ? '' : `replacement ${n}`,
      status: owners[position] === index || status === 'CompileError' ? status : 'Ignored', static: false, killedBy: ['local-test']}))
    files[`${prefix}/${file}`] = file.startsWith('Nested/') && owners[position] !== index
      ? {language: 'none', source: 'File ignored by mutate filter', mutants: []}
      : {language: 'cs', source, mutants}
  }
  return {schemaVersion: '2', projectRoot: prefix, files, testFiles: {local: {tests: [{id: 'local-test', name: `attempt ${index}`}]}}, thresholds: {high: 80, low: 74}}
}

function nativeEvidence(fixture, index, report = reportFor(fixture.plan, index, fixture.directory)) {
  const {directory, plan} = fixture, output = path.join(directory, 'StrykerOutput', `hlp.blocks.builder-definitions.tests-native${index}`)
  const reportPath = path.join(output, 'reports', 'mutation-report.json'), configFile = path.join(output, 'stryker-config.json')
  const config = {'stryker-config': {...plan.config, since: {enabled: false}, thresholds: {...plan.config.thresholds, break: 0}}}
  const override = jsonBytes(config)
  const args = ['--log-to-file', '--concurrency', '2', ...names.filter((_, position) => owners[position] === index).flatMap(file => ['--mutate', file]),
    ...(plan.mode === 'diagnostic' ? ['--reporter', 'json', '--reporter', 'dots', '--verbosity', 'error'] : [])]
  const attempt = {at: '2026-10-26T19:00:00.000Z', test: builderTest, target: builderTarget, sourceCommit: plan.head, executable: 'dotnet',
    argv: ['stryker', '--output', output, '--config-file', configFile, ...args], cwd: path.join(directory, path.posix.dirname(builderTest)), output, configFile,
    wrapperSha256: hash(readFileSync(path.join(directory, 'tooling/stryker.mjs'))), configSha256: hash(override)}
  const result = {outcome: {kind: 'completed'}, status: 0, signal: null, pid: 111, counts: counts(report), reportPath, output, razor: false}
  const prefix = `${slash(directory)}/${sourceDirectory}`, lines = []
  let localId = 0
  names.forEach((file, position) => {
    lines.push(`2026-10-26T14:37:43.0941460-04:00  [DBG] Mutating "${prefix}/${file}" (5f74b457)`)
    for (let n = 0; n < statuses[position].length; n++) lines.push(`2026-10-26T14:37:43.1021490-04:00  [DBG] Mutant ${localId++} created "x" -> "y" using "Mutation" (62b2bae2)`)
  })
  lines.push('2026-10-26T14:37:43.1208540-04:00  [DBG] 11 mutants created (017e65ab)')
  const logBytes = Buffer.from(lines.join('\n'))
  const reportBytes = jsonBytes(report), finalBytes = jsonBytes(result), attemptBytes = jsonBytes(attempt)
  write(output, 'reports/mutation-report.json', reportBytes); write(output, 'diagnostics/result.json', finalBytes)
  write(output, 'diagnostics/attempt.json', attemptBytes); write(output, 'stryker-config.json', override); write(output, 'logs/log-20261026.txt', logBytes)
  const receipt = {binding: {version: 1, planHash: hash(readFileSync(fixture.manifest)), head: plan.head, tree: plan.tree, runId: '314', runAttempt: '2', index},
    prefixes: {checkout: slash(directory), target: prefix}, reportSha256: hash(reportBytes), returned: result, finalResult: encoded(finalBytes), attempt: encoded(attemptBytes), override: encoded(override),
    generation: {files: names.map((file, position) => ({path: `${sourceDirectory}/${file}`, created: statuses[position].length})), total: 11,
      logs: [{name: 'log-20261026.txt', sha256: hash(logBytes)}]}, control: controlHead}
  return {returned: {...result, report}, packet: {receiptBytes: jsonBytes(receipt), reportBytes}, receipt, report, args}
}

function packetsFor(fixture) { return [1, 2, 3, 4].map(index => nativeEvidence(fixture, index).packet) }
function download(fixture, packets = packetsFor(fixture)) {
  const directory = path.join(fixture.directory, 'StrykerOutput', 'downloads')
  packets.forEach((packet, index) => {
    const folder = path.join(directory, `builder-partition-314-2-${index + 1}`)
    write(folder, 'receipt.json', packet.receiptBytes); write(folder, 'report.json', packet.reportBytes)
  })
  return directory
}
function changePacket(packet, change) {
  const receipt = JSON.parse(packet.receiptBytes), report = JSON.parse(packet.reportBytes)
  change(receipt, report)
  return {receiptBytes: jsonBytes(receipt), reportBytes: jsonBytes(report)}
}
function reseal(receipt, report) {
  receipt.reportSha256 = hash(jsonBytes(report))
  receipt.returned.counts = counts(report)
  receipt.finalResult = encoded(jsonBytes(receipt.returned))
}

function scorePackets(fixture, distribution) {
  return packetsFor(fixture).map((packet, packetIndex) => changePacket(packet, (receipt, report) => {
    let id = 0
    names.forEach((name, position) => {
      const source = readFileSync(path.join(fixture.directory, sourceDirectory, name), 'utf8')
      report.files[`${slash(fixture.directory)}/${sourceDirectory}/${name}`] = {language: 'cs', source, mutants: distribution[position].map((status, n) => ({
        id: String(id++), location: {start: {line: 1, column: 1}, end: {line: 1, column: 2}}, mutatorName: 'Literal mutation', replacement: `value ${n}`,
        status: owners[position] === packetIndex + 1 ? status : 'Ignored'}))}
    })
    receipt.generation.files = names.map((name, position) => ({path: `${sourceDirectory}/${name}`, created: distribution[position].length}))
    receipt.generation.total = distribution.reduce((sum, group) => sum + group.length, 0)
    reseal(receipt, report)
  }))
}

function removeOwned(file) {
  const absolute = path.resolve(file), boundary = path.resolve(ownedTemp) + path.sep
  assert.ok(absolute.startsWith(boundary), `Cleanup outside owned nonce: ${absolute}`)
  rmSync(absolute, {recursive: true, force: true})
}
test.after(() => {
  const target = path.resolve(ownedTemp)
  assert.ok(target.startsWith(path.resolve(tmpdir()) + path.sep))
  assert.ok(path.basename(target).startsWith('builder-partitions-153-'))
  rmSync(target, {recursive: true, force: true})
})

test('generation ends at the literal DBG precompile summary and retains zero files', async () => {
  const log = path.join(ownedTemp, 'log-20261026.txt')
  writeFileSync(log, [
    '2026-10-26T14:37:43.0941460-04:00  [DBG] Mutating "/checkout/Probe/A.cs" (5f74b457)',
    '2026-10-26T14:37:43.1021490-04:00  [DBG] Mutant 0 created "a" -> "b" using "Mutation" (62b2bae2)',
    '2026-10-26T14:37:43.1191020-04:00  [DBG] Mutating "/checkout/Probe/Empty.cs" (5f74b457)',
    '2026-10-26T14:37:43.1208540-04:00  [DBG] 1 mutants created (017e65ab)',
    '2026-10-26T14:37:43.1308540-04:00  [INF] 0 mutants created',
  ].join('\n'))
  const plan = {target: 'Probe/Probe.csproj', sources: [{path: 'Probe/A.cs'}, {path: 'Probe/Empty.cs'}]}
  const result = await generationInventory(plan, [log], {checkout: '/checkout', target: '/checkout/Probe'})
  assert.deepEqual(result.files, [{'path': 'Probe/A.cs', created: 1}, {path: 'Probe/Empty.cs', created: 0}])
  assert.equal(result.total, 1)
  assert.equal(result.logs[0].sha256, hash(readFileSync(log)))
})

test('source-derived duplicate-discard events do not change the generation census', async t => {
  for (const suffix of ['', ' (abcdef12)']) await t.test(suffix ? 'logger hash suffix form' : 'message payload', async () => {
    const log = write(ownedTemp, `discard-${suffix ? 'hash' : 'plain'}/log-20261026.txt`, [
      '2026-10-26T14:37:43.0941460-04:00  [DBG] Mutating "/checkout/Probe/A.cs" (5f74b457)',
      '2026-10-26T14:37:43.1021490-04:00  [DBG] Mutant 0 created "a" -> "b" using "Mutation" (62b2bae2)',
      `2026-10-26T14:37:43.1190000-04:00  [DBG] Mutant 0 discarded as it is a duplicate of 0${suffix}`,
      '2026-10-26T14:37:43.1191020-04:00  [DBG] Mutating "/checkout/Probe/Empty.cs" (5f74b457)',
      '2026-10-26T14:37:43.1208540-04:00  [DBG] 1 mutants created (017e65ab)',
    ].join('\n'))
    const plan = {target: 'Probe/Probe.csproj', sources: [{path: 'Probe/A.cs'}, {path: 'Probe/Empty.cs'}]}
    const inventory = await generationInventory(plan, [log], {checkout: '/checkout', target: '/checkout/Probe'})
    assert.deepEqual(inventory.files, [{path: 'Probe/A.cs', created: 1}, {path: 'Probe/Empty.cs', created: 0}])
    assert.equal(inventory.total, 1)
    assert.deepEqual(inventory.logs, [{name: 'log-20261026.txt', sha256: hash(readFileSync(log))}])
  })
})

test('plan, admit and CLI bind actual checkout identity, selection and fixed options', async () => {
  const fixture = await checkout()
  try {
    const {plan, adapter, manifest, directory} = fixture
    assert.equal(plan.selected, true)
    assert.deepEqual(plan.sources.map(row => [row.file, row.owner]), [['A.cs', 1], ['B.cs', 2], ['Nested/C.cs', 3], ['Nested/D.cs', 4], ['Zero.cs', 4]])
    assert.equal(plan.controlHead, controlHead)
    assert.deepEqual(plan.conditional, {path: 'artifacts/packages/nuget/packed-version.props', absent: true})
    assert.equal(await adapter.main(['admit', manifest]), 0)
    assert.equal(readFileSync(fixture.env.GITHUB_OUTPUT, 'utf8'), `selected=true\nsubject=${fixture.env.EXPECTED_HEAD}\n`)
    const cli = spawnSync(process.execPath, ['tooling/stryker-builder-partitions.mjs', 'admit', manifest], {cwd: directory, env: fixture.env, encoding: 'utf8'})
    assert.equal(cli.status, 0, cli.stderr)
    const before = readFileSync(manifest)
    await assert.rejects(adapter.main(['plan', 'normal', manifest]), /EEXIST/)
    assert.deepEqual(readFileSync(manifest), before)
    await assert.rejects(adapter.main(['shard', manifest, '01']), /index/)
    await assert.rejects(adapter.main(['admit', manifest, '--anything']), /arguments/)
    const bad = spawnSync(process.execPath, ['tooling/stryker-builder-partitions.mjs', 'admit', manifest], {cwd: directory, env: {...fixture.env, EXPECTED_HEAD: '0'.repeat(40)}, encoding: 'utf8'})
    assert.equal(bad.status, 1)
    assert.match(bad.stderr, /EXPECTED_HEAD/)
  } finally { fixture.restore() }
})

test('whole-owner union retains seven statuses, zero rows, full identities and repeated local IDs', async () => {
  const fixture = await checkout()
  try {
    const result = compose(fixture.plan, packetsFor(fixture))
    assert.equal(result.provenance.kind, 'COMPOSED')
    assert.deepEqual(Object.keys(result.report.files), names.map(name => `${sourceDirectory}/${name}`))
    assert.deepEqual(Object.values(result.report.files).map(file => file.mutants.map(mutant => mutant.status)), statuses)
    assert.deepEqual(Object.values(result.report.files).map(file => file.mutants[0]?.id ?? null), ['0', '0', '0', '0', null])
    assert.equal(result.report.files[`${sourceDirectory}/B.cs`].mutants.length, 2)
    assert.deepEqual(result.report.files[`${sourceDirectory}/B.cs`].mutants.map(mutant => mutant.replacement), ['', 'replacement 1'])
    assert.equal('description' in result.report.files[`${sourceDirectory}/A.cs`].mutants[0], false)
    assert.equal(result.report.files[`${sourceDirectory}/Zero.cs`].source, '\n')
    assert.deepEqual(counts(result.report), {total: 11, tested: 8, detected: 6, undetected: 2, score: 75})
    assert.equal(result.provenance.owners.length, 5)
    assert.equal('testFiles' in result.report, false)
  } finally { fixture.restore() }
})

test('packet refusal covers identity, integrity, native settlement, paths, source, statuses and rollback', async t => {
  const fixture = await checkout()
  try {
    const basePackets = packetsFor(fixture), a = `${slash(fixture.directory)}/${sourceDirectory}/A.cs`, b = `${slash(fixture.directory)}/${sourceDirectory}/B.cs`
    const cases = [
      ['stale subject', r => { r.binding.head = 'a'.repeat(40) }],
      ['stale run', r => { r.binding.runId = '315' }],
      ['stale attempt', r => { r.binding.runAttempt = '1' }],
      ['duplicate index', r => { r.binding.index = 2 }],
      ['plan mismatch', r => { r.binding.planHash = '0'.repeat(64) }],
      ['missing report hash', r => { delete r.reportSha256 }],
      ['bad embedded result', r => { r.finalResult.bytes += 'A' }],
      ['bad attempt hash', r => { r.attempt.sha256 = '0'.repeat(64) }],
      ['bad override hash', r => { r.override.sha256 = '0'.repeat(64) }],
      ['native arguments changed', r => { const attempt = JSON.parse(Buffer.from(r.attempt.bytes, 'base64')); attempt.argv.push('--mutate', '**/*.cs'); r.attempt = encoded(jsonBytes(attempt)) }],
      ['native target changed', r => { const attempt = JSON.parse(Buffer.from(r.attempt.bytes, 'base64')); attempt.target = 'Other.csproj'; r.attempt = encoded(jsonBytes(attempt)) }],
      ['native wrapper changed', r => { const attempt = JSON.parse(Buffer.from(r.attempt.bytes, 'base64')); attempt.wrapperSha256 = '0'.repeat(64); r.attempt = encoded(jsonBytes(attempt)) }],
      ['native override changed', r => { const config = JSON.parse(Buffer.from(r.override.bytes, 'base64')); config['stryker-config'].thresholds.break = 74; r.override = encoded(jsonBytes(config)) }],
      ['native final path escaped', r => { r.returned.reportPath += '/../other.json'; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['nonzero native', r => { r.returned.status = 1; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['failed outcome', r => { r.returned.outcome = {kind: 'failed', cause: 'exit'}; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['cancelled native', r => { r.returned.signal = 'SIGTERM'; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['observer failure', r => { r.returned.observerFailed = true; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['cleanup unavailable', r => { r.returned.cleanupUnavailable = true; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['unknown descendants', r => { r.returned.descendantsUnknown = true; r.finalResult = encoded(jsonBytes(r.returned)) }],
      ['early success differs from final return', r => { r.returned.pid = 112 }],
      ['foreign target prefix', r => { r.prefixes.target += '/other' }],
      ['Control mismatch', r => { r.control = '0'.repeat(40) }],
      ['generation missing zero file', r => { r.generation.files.pop() }],
      ['generation mismatch', r => { r.generation.files[1].created++; r.generation.total++ }],
      ['generation invalid count', r => { r.generation.files[0].created = -1 }],
      ['generation missing logs', r => { r.generation.logs = [] }],
      ['generation duplicate logs', r => { r.generation.logs.push({...r.generation.logs[0]}) }],
      ['unknown final status', (r, report) => { report.files[a].mutants[0].status = 'Unknown'; reseal(r, report) }],
      ['unfinished mutant', (r, report) => { report.files[a].mutants[0].status = 'Pending'; reseal(r, report) }],
      ['source mismatch', (r, report) => { report.files[a].source += ' '; reseal(r, report) }],
      ['owner placeholder', (r, report) => { report.files[a] = {language: 'none', source: 'File ignored by mutate filter', mutants: []}; reseal(r, report) }],
      ['missing source', (r, report) => { delete report.files[b]; reseal(r, report) }],
      ['extra source', (r, report) => { report.files['Unknown.cs'] = report.files[b]; reseal(r, report) }],
      ['aliased source', (r, report) => { report.files['A.cs'] = report.files[a]; reseal(r, report) }],
      ['escaped source', (r, report) => { report.files['../A.cs'] = report.files[a]; delete report.files[a]; reseal(r, report) }],
      ['foreign source prefix', (r, report) => { report.files['/foreign/A.cs'] = report.files[a]; delete report.files[a]; reseal(r, report) }],
      ['bad project root', (r, report) => { report.projectRoot += '/other'; reseal(r, report) }],
      ['missing end location', (r, report) => { delete report.files[a].mutants[0].location.end; reseal(r, report) }],
      ['missing replacement', (r, report) => { delete report.files[a].mutants[0].replacement; reseal(r, report) }],
      ['duplicate semantic key', (r, report) => { report.files[a].mutants[1] = {...report.files[a].mutants[0], id: '1'}; reseal(r, report) }],
      ['duplicate local ID', (r, report) => { report.files[a].mutants[1].id = '0'; reseal(r, report) }],
      ['owned count omitted mutant', (r, report) => { report.files[a].mutants.pop(); reseal(r, report) }],
      ['owner CompileError removed', (r, report) => { report.files[a].mutants[2].status = 'Ignored'; reseal(r, report) }],
      ['owner CompileError added', (r, report) => { report.files[a].mutants[1].status = 'CompileError'; reseal(r, report) }],
      ['nonowner identity differs', (r, report) => { report.files[b].mutants[0].replacement = 'other'; reseal(r, report) }],
      ...['NoCoverage', 'Killed', 'Survived', 'Timeout', 'RuntimeError'].map(status => [`nonowner ${status}`, (r, report) => { report.files[b].mutants[0].status = status; reseal(r, report) }]),
    ]
    for (const [name, change] of cases) await t.test(name, () => {
      const packets = [...basePackets]
      packets[0] = changePacket(packets[0], change)
      assert.throws(() => compose(fixture.plan, packets), undefined, name)
    })
    await t.test('missing packet', () => assert.throws(() => compose(fixture.plan, basePackets.slice(1)), /four/))
    await t.test('extra packet', () => assert.throws(() => compose(fixture.plan, [...basePackets, basePackets[0]]), /four/))
  } finally { fixture.restore() }
})

test('native grammar refuses incomplete and contradictory generation evidence', async t => {
  const plan = {target: 'Probe/Probe.csproj', sources: [{path: 'Probe/A.cs'}, {path: 'Probe/Empty.cs'}]}, prefixes = {checkout: '/checkout', target: '/checkout/Probe'}
  const lines = ['2026-10-26T14:37:43.0941460-04:00  [DBG] Mutating "/checkout/Probe/A.cs" (5f74b457)',
    '2026-10-26T14:37:43.1021490-04:00  [DBG] Mutant 0 created "x" -> "y" using "Mutation" (62b2bae2)',
    '2026-10-26T14:37:43.1191020-04:00  [DBG] Mutating "/checkout/Probe/Empty.cs" (5f74b457)',
    '2026-10-26T14:37:43.1208540-04:00  [DBG] 1 mutants created (017e65ab)']
  const cases = [
    ['missing summary', lines.slice(0, 3)], ['INF is not boundary', lines.map(line => line.replace('[DBG] 1 mutants', '[INF] 1 mutants'))],
    ['wrong total', lines.map(line => line.replace('1 mutants created', '2 mutants created'))],
    ['duplicate ID', [lines[0], lines[1], lines[1], lines[2], lines[3]]], ['missing zero source', [lines[0], lines[1], lines[3]]],
    ['duplicate source', [lines[0], lines[0], ...lines.slice(1)]], ['created before source', [lines[1], ...lines]],
    ['unknown source', lines.map(line => line.replace('/A.cs', '/Unknown.cs'))], ['escaped source', lines.map(line => line.replace('/A.cs', '/../A.cs'))],
    ['malformed source event', lines.map(line => line.replace('(5f74b457)', '(bad)'))], ['malformed mutant ID', lines.map(line => line.replace('Mutant 0', 'Mutant x'))],
    ['malformed boundary event', lines.map(line => line.replace('(017e65ab)', '(bad)'))],
    ['unknown mutant event', [lines[0], lines[1].replace('created', 'unknown'), ...lines.slice(1)]],
    ['malformed discard event', [lines[0], '2026-10-26T14:37:43.1190000-04:00  [DBG] Mutant 0 discarded as it is a duplicate of', ...lines.slice(1)]],
    ['malformed discard ID', [lines[0], '2026-10-26T14:37:43.1190000-04:00  [DBG] Mutant x discarded as it is a duplicate of 0', ...lines.slice(1)]],
    ['malformed discard suffix', [lines[0], '2026-10-26T14:37:43.1190000-04:00  [DBG] Mutant 0 discarded as it is a duplicate of 0 (bad)', ...lines.slice(1)]],
    ['discard before source', ['2026-10-26T14:37:43.1190000-04:00  [DBG] Mutant 0 discarded as it is a duplicate of 0', ...lines]],
  ]
  for (const [name, content] of cases) await t.test(name, async () => {
    const file = write(ownedTemp, `grammar-${name.replaceAll(' ', '-')}/log-20261026.txt`, content.join('\n'))
    await assert.rejects(generationInventory(plan, [file], prefixes))
  })
})

test('shard publishes only after awaited native settlement and post-native admission', async () => {
  const fixture = await checkout({mode: 'diagnostic'})
  try {
    const {adapter, manifest, directory} = fixture, receipts = []
    let controlCalls = 0
    for (const index of [1, 2, 3, 4]) {
      let release, entered
      const ready = new Promise(resolve => { entered = resolve }), gate = new Promise(resolve => { release = resolve })
      const expected = nativeEvidence(fixture, index)
      const executing = adapter.main(['shard', manifest, String(index)], async (test, changed, args) => {
        assert.equal(test, builderTest); assert.equal(changed, undefined); assert.deepEqual(args, expected.args)
        entered(); await gate; return expected.returned
      }, () => { controlCalls++; return controlHead })
      await ready
      const receiptFile = path.join(directory, `StrykerOutput/builder-partition-${index}/diagnostics/receipt.json`)
      assert.equal(existsSync(receiptFile), false, 'A native report and early final result are insufficient before await')
      release()
      assert.equal(await executing, 0)
      assert.equal(existsSync(receiptFile), true)
      receipts.push({receiptBytes: readFileSync(receiptFile), reportBytes: readFileSync(path.join(path.dirname(receiptFile), 'report.json'))})
    }
    assert.equal(controlCalls, 8)
    assert.equal(compose(fixture.plan, receipts).provenance.shards.length, 4)
    await assert.rejects(adapter.main(['shard', manifest, '1'], () => { assert.fail('duplicate must refuse before native') }, () => controlHead), /EEXIST/)
    assert.equal(await adapter.main(['collect', manifest, download(fixture, receipts)]), 0)
    const composed = JSON.parse(readFileSync(path.join(directory, 'StrykerOutput/builder-owner/reports/mutation-report.json')))
    assert.equal(composed.composition.kind, 'COMPOSED')
  } finally { fixture.restore() }
})

test('native failures, late observer failure and missing final JSON never publish a receipt', async t => {
  const fixture = await checkout()
  try {
    const {adapter, directory, manifest} = fixture, reservation = path.join(directory, 'StrykerOutput/builder-partition-1')
    for (const [name, change, expected] of [
      ['nonzero', result => { result.status = 9; result.outcome = {kind: 'failed', cause: 'exit'} }, 9],
      ['signal', result => { result.signal = 'SIGTERM'; result.outcome = {kind: 'failed', cause: 'signal'} }, {signal: 'SIGTERM'}],
      ['cleanup unknown', result => { result.cleanupUnavailable = true; result.descendantsUnknown = true }, 1],
      ['descendants unknown alone', result => { result.descendantsUnknown = true }, 1],
      ['late observer', result => { result.observerFailed = true; result.outcome = {kind: 'failed', cause: 'observer'} }, 1],
    ]) await t.test(name, async () => {
      const evidence = nativeEvidence(fixture, 1)
      let release, entered
      const gate = new Promise(resolve => { release = resolve }), ready = new Promise(resolve => { entered = resolve })
      const promise = adapter.main(['shard', manifest, '1'], async () => { entered(); await gate; change(evidence.returned); return evidence.returned }, () => controlHead)
      await ready
      assert.equal(existsSync(path.join(reservation, 'diagnostics/receipt.json')), false)
      release(); assert.deepEqual(await promise, expected)
      assert.equal(existsSync(path.join(reservation, 'diagnostics/receipt.json')), false)
      assert.equal(existsSync(evidence.returned.reportPath), true)
      removeOwned(reservation)
    })
    await t.test('missing final JSON', async () => {
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1)
        removeOwned(path.join(evidence.returned.output, 'diagnostics/result.json'))
        return evidence.returned
      }, () => controlHead), /ENOENT/)
      assert.equal(existsSync(path.join(reservation, 'diagnostics/receipt.json')), false)
    })
  } finally { fixture.restore() }
})

test('collect and PR use the weighted house gate at 74 and a higher configured floor', async t => {
  const fixture = await checkout()
  try {
    const {adapter, manifest} = fixture
    const weighted = [Array(60).fill('Killed').concat(Array(40).fill('Survived')), ['Killed'], ['Killed'], ['Killed'], []]
    const exact = [Array(71).fill('Killed').concat(Array(26).fill('Survived')), ['Killed'], ['Killed'], ['Killed'], []]
    await t.test('passing percentage average cannot rescue a failing weighted union', async () => {
      const directory = download(fixture, scorePackets(fixture, weighted))
      assert.equal(await adapter.main(['collect', manifest, directory]), 1)
      assert.equal(await adapter.main(['pr', manifest, directory], () => { assert.fail('only Builder changed') }), 1)
      const report = JSON.parse(readFileSync(path.join(fixture.directory, 'StrykerOutput/builder-owner/reports/mutation-report.json')))
      assert.equal(counts(report).score, 61.16)
    })
    await t.test('exact 74 passes collect and the actual PR gate', async () => {
      const directory = download(fixture, scorePackets(fixture, exact))
      assert.equal(await adapter.main(['collect', manifest, directory]), 0)
      assert.equal(await adapter.main(['pr', manifest, directory], () => { assert.fail('only Builder changed') }), 0)
    })
    await t.test('zero tested owner group refuses', async () => {
      const directory = download(fixture, scorePackets(fixture, [[], ['Killed'], ['Killed'], ['Killed'], []]))
      await assert.rejects(adapter.main(['collect', manifest, directory]), /zero mutants/)
    })
  } finally { fixture.restore() }
  const higher = await checkout({floor: 75})
  try {
    const exact = [Array(71).fill('Killed').concat(Array(26).fill('Survived')), ['Killed'], ['Killed'], ['Killed'], []]
    assert.equal(await higher.adapter.main(['collect', higher.manifest, download(higher, scorePackets(higher, exact))]), 1)
    assert.equal(await higher.adapter.main(['pr', higher.manifest, path.join(higher.directory, 'StrykerOutput/downloads')], () => assert.fail('only Builder changed')), 1)
  } finally { higher.restore() }
})

test('PR runs all 36 ordinary eligible callbacks without changing their arguments or policy bytes', async () => {
  const fixture = await checkout({changed: 'all'})
  try {
    const policyFiles = ['tooling/stryker-baselines.json', ...fixture.routes.map(route => `${path.posix.dirname(route.test)}/stryker-config.json`)]
    const before = policyFiles.map(file => readFileSync(path.join(fixture.directory, file)))
    const calls = []
    const result = await fixture.adapter.main(['pr', fixture.manifest, download(fixture)], async (test, changed, args) => {
      calls.push(test); assert.equal(changed, undefined); assert.deepEqual(args, ['--log-to-file'])
      return {outcome: {kind: 'completed'}, status: 0, signal: null, report: {files: {'Route.cs': {mutants: [{status: 'Killed'}]}}}, razor: false}
    }, () => assert.fail('PR composition does not execute native Builder'))
    assert.equal(result, 0)
    assert.equal(calls.length, 36)
    assert.equal(new Set(calls).size, 36)
    assert.equal(calls.includes(builderTest), false)
    policyFiles.forEach((file, index) => assert.deepEqual(readFileSync(path.join(fixture.directory, file)), before[index]))
  } finally { fixture.restore() }
})

test('normal no-Builder-change requires no Control or quartet and keeps all other routes', async () => {
  const fixture = await checkout({changed: 'others'})
  try {
    assert.equal(fixture.plan.selected, false)
    assert.equal('controlHead' in fixture.plan, false)
    const calls = []
    const result = await fixture.adapter.main(['pr', fixture.manifest, 'does-not-exist'], async (test, changed, args) => {
      calls.push(test); assert.equal(changed, undefined); assert.deepEqual(args, ['--log-to-file'])
      return {outcome: {kind: 'completed'}, status: 0, signal: null, report: {files: {'Route.cs': {mutants: [{status: 'Killed'}]}}}, razor: false}
    }, () => assert.fail('unselected Builder cannot observe Control'))
    assert.equal(result, 0); assert.equal(calls.length, 36); assert.equal(calls.includes(builderTest), false)
    await assert.rejects(fixture.adapter.main(['shard', fixture.manifest, '1'], () => assert.fail('unselected native callback')), /unselected/)
    await assert.rejects(fixture.adapter.main(['collect', fixture.manifest, 'does-not-exist']), /unselected/)
  } finally { fixture.restore() }
})

test('invalid collection, exact base and selection in both directions refuse before any project', async t => {
  for (const changed of ['all', 'others']) {
    const fixture = await checkout({changed})
    try {
      const {adapter, manifest, directory} = fixture, downloads = fixture.plan.selected ? download(fixture) : 'unused', original = readFileSync(manifest)
      let calls = 0
      const native = () => { calls++; assert.fail('Native must not run before admission') }
      await t.test(`selection flip ${fixture.plan.selected}`, async () => {
        const plan = JSON.parse(original); plan.selected = !plan.selected
        writeFileSync(manifest, `${JSON.stringify(plan)}\n`)
        await assert.rejects(adapter.main(['pr', manifest, downloads], native), /Plan differs/)
        assert.equal(calls, 0); writeFileSync(manifest, original)
      })
      await t.test(`base changed with ${changed}`, async () => {
        git(directory, 'update-ref', 'refs/remotes/origin/main', fixture.plan.head)
        await assert.rejects(adapter.main(['pr', manifest, downloads], native), /Plan differs/)
        assert.equal(calls, 0); git(directory, 'update-ref', 'refs/remotes/origin/main', fixture.base)
      })
      if (fixture.plan.selected) {
        const packetFile = path.join(downloads, 'builder-partition-314-2-1/receipt.json'), packet = readFileSync(packetFile)
        await t.test('bad collector packet', async () => {
          const receipt = JSON.parse(packet); receipt.binding.head = '0'.repeat(40); writeFileSync(packetFile, JSON.stringify(receipt))
          await assert.rejects(adapter.main(['pr', manifest, downloads], native), /misbound/)
          assert.equal(calls, 0); writeFileSync(packetFile, packet)
        })
        await t.test('extra downloaded artifact', async () => {
          mkdirSync(path.join(downloads, 'builder-partition-314-1-1'))
          await assert.rejects(adapter.main(['pr', manifest, downloads], native), /artifact directories/)
          assert.equal(calls, 0); removeOwned(path.join(downloads, 'builder-partition-314-1-1'))
        })
        await t.test('partial artifact pair', async () => {
          const report = path.join(downloads, 'builder-partition-314-2-1/report.json'), bytes = readFileSync(report)
          removeOwned(report); await assert.rejects(adapter.main(['pr', manifest, downloads], native), /artifact files/)
          assert.equal(calls, 0); writeFileSync(report, bytes)
        })
      }
    } finally { fixture.restore() }
  }
})

test('PR preserves ordinary signal or observer failure without launching the next project', async () => {
  const fixture = await checkout({changed: 'all'})
  try {
    const downloads = download(fixture)
    let calls = 0
    assert.deepEqual(await fixture.adapter.main(['pr', fixture.manifest, downloads], () => {
      calls++; return {outcome: {kind: 'failed', cause: 'signal'}, status: null, signal: 'SIGTERM'}
    }), {signal: 'SIGTERM'})
    assert.equal(calls, 1)
    calls = 0
    assert.equal(await fixture.adapter.main(['pr', fixture.manifest, downloads], () => {
      calls++; return {outcome: {kind: 'failed', cause: 'observer'}, status: 5, signal: null, observerFailed: true, cleanupUnavailable: true}
    }), 5)
    assert.equal(calls, 1)
  } finally { fixture.restore() }
})

test('diagnostic and qualification cannot enter the normal PR gate', async () => {
  const fixture = await checkout({mode: 'qualification', changed: 'none'})
  try {
    assert.equal(fixture.plan.selected, true)
    await assert.rejects(fixture.adapter.main(['pr', fixture.manifest, 'missing'], () => assert.fail('label mode must not run PR projects')), /normal mode/)
    const diagnostic = path.join(fixture.directory, 'StrykerOutput/builder-plan/diagnostic.json')
    await fixture.adapter.main(['plan', 'diagnostic', diagnostic])
    await assert.rejects(fixture.adapter.main(['pr', diagnostic, 'missing'], () => assert.fail('diagnostic mode must not run PR projects')), /normal mode/)
    await assert.rejects(fixture.adapter.main(['shard', fixture.manifest, '1'], () => assert.fail('missing Control must refuse before native')), /HARBORLINE_CONTROL_REPO/)
  } finally { fixture.restore() }
})

test('plan refuses unsafe source and unsupported encoding while preserving CRLF bytes', async t => {
  const fixture = await checkout()
  try {
    const {directory, adapter} = fixture, sourcePath = `${sourceDirectory}/A.cs`, original = readFileSync(path.join(directory, sourcePath))
    const cases = [
      ['BOM', Buffer.concat([Buffer.from([239, 187, 191]), original])],
      ['invalid UTF8', Buffer.from([255, 254, 100])],
      ['NUL source', Buffer.from('a\0b')],
    ]
    for (const [name, bytes] of cases) await t.test(name, () => {
      write(directory, sourcePath, bytes); git(directory, 'add', sourcePath); git(directory, 'commit', '--quiet', '-m', name)
      process.env.EXPECTED_HEAD = git(directory, 'rev-parse', 'HEAD')
      assert.throws(() => adapter.makePlan('normal'))
    })
    write(directory, sourcePath, '// exact\r\n')
    git(directory, 'add', sourcePath); git(directory, 'commit', '--quiet', '-m', 'CRLF')
    process.env.EXPECTED_HEAD = git(directory, 'rev-parse', 'HEAD')
    const plan = adapter.makePlan('normal'), row = plan.sources.find(source => source.file === 'A.cs')
    assert.equal(row.byteLength, 10); assert.equal(row.sha256, hash('// exact\r\n'))
    write(directory, `${sourceDirectory}/Unsafe[1].cs`, '// unsafe\n')
    git(directory, 'add', '.'); git(directory, 'commit', '--quiet', '-m', 'unsafe literal')
    process.env.EXPECTED_HEAD = git(directory, 'rev-parse', 'HEAD')
    assert.throws(() => adapter.makePlan('normal'), /Unsafe literal/)
  } finally { fixture.restore() }
})

test('post-native mutations invalidate source, config, baseline, manifest, base, run and ignored build inputs', async t => {
  const fixture = await checkout()
  try {
    const {adapter, directory, manifest} = fixture, reservation = path.join(directory, 'StrykerOutput/builder-partition-1')
    const paths = [`${sourceDirectory}/A.cs`, `${path.posix.dirname(builderTest)}/stryker-config.json`, 'tooling/stryker-baselines.json', 'Directory.Build.props']
    for (const file of paths) await t.test(file, async () => {
      const original = readFileSync(path.join(directory, file))
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1)
        write(directory, file, Buffer.concat([original, Buffer.from(' ')]))
        return evidence.returned
      }, () => controlHead))
      assert.equal(existsSync(path.join(reservation, 'diagnostics/receipt.json')), false)
      write(directory, file, original); removeOwned(reservation)
    })
    await t.test('manifest changed', async () => {
      const original = readFileSync(manifest)
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1); writeFileSync(manifest, Buffer.concat([original, Buffer.from(' ')])); return evidence.returned
      }, () => controlHead), /Plan differs/)
      writeFileSync(manifest, original); removeOwned(reservation)
    })
    await t.test('base changed', async () => {
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1); git(directory, 'update-ref', 'refs/remotes/origin/main', fixture.plan.head); return evidence.returned
      }, () => controlHead), /Plan differs/)
      git(directory, 'update-ref', 'refs/remotes/origin/main', fixture.base); removeOwned(reservation)
    })
    await t.test('run changed', async () => {
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1); process.env.GITHUB_RUN_ATTEMPT = '3'; return evidence.returned
      }, () => controlHead), /Plan differs/)
      process.env.GITHUB_RUN_ATTEMPT = '2'; removeOwned(reservation)
    })
    await t.test('ignored conditional input appeared', async () => {
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => {
        const evidence = nativeEvidence(fixture, 1); write(directory, 'artifacts/packages/nuget/packed-version.props', '<Project/>'); return evidence.returned
      }, () => controlHead), /Plan differs/)
      removeOwned(path.join(directory, 'artifacts')); removeOwned(reservation)
    })
    await t.test('ignored conditional input hash changed', async () => {
      const conditional = write(directory, 'artifacts/packages/nuget/packed-version.props', '<Project/>')
      const withConditional = path.join(directory, 'StrykerOutput/builder-plan/conditional.json')
      await adapter.main(['plan', 'normal', withConditional])
      const changedFixture = {...fixture, manifest: withConditional, plan: JSON.parse(readFileSync(withConditional))}
      assert.equal(changedFixture.plan.conditional.sha256, hash('<Project/>'))
      await assert.rejects(adapter.main(['shard', withConditional, '1'], async () => {
        const evidence = nativeEvidence(changedFixture, 1); writeFileSync(conditional, '<Project>changed</Project>'); return evidence.returned
      }, () => controlHead), /Plan differs/)
      removeOwned(path.join(directory, 'artifacts')); removeOwned(reservation)
    })
    await t.test('Control changed', async () => {
      let observations = 0
      await assert.rejects(adapter.main(['shard', manifest, '1'], async () => nativeEvidence(fixture, 1).returned, () => ++observations === 1 ? controlHead : '0'.repeat(40)), /Control/)
      assert.equal(existsSync(path.join(reservation, 'diagnostics/receipt.json')), false)
    })
  } finally { fixture.restore() }
})
