import {test} from 'node:test'
import assert from 'node:assert/strict'
import {readFileSync, mkdtempSync, mkdirSync, writeFileSync, rmSync} from 'node:fs'
import {spawnSync} from 'node:child_process'
import {tmpdir} from 'node:os'
import {join} from 'node:path'
test('enabled Windows default uses direct execution; non-Windows keeps the lock', () => {
  const source=readFileSync(new URL('../workflows/stryker.yml',import.meta.url),'utf8').replaceAll('\r\n','\n')
  const step=source.slice(source.indexOf('      - name: Mutate every project and hold each to its baseline (scheduled)'), source.indexOf('      - name: Mutation report'))
  assert.ok(step.includes("$env:RUNNER_OS -eq 'Windows'"))
  const windows=step.slice(step.indexOf("$env:RUNNER_OS -eq 'Windows'"),step.indexOf('python .github/workloads/host-workload-lock.py'))
  assert.ok(windows.includes('& ./.github/workloads/full-mutation.ps1'))
  assert.ok(!windows.includes('host-workload-lock.py'))
  assert.match(source,/if: .*runner\.os != 'Windows'\n        with:\n          python-version: '3\.14'/)
})

test('PR mutation keeps full selection and reserves time to collect diagnostics after step failure', () => {
  const source=readFileSync(new URL('../workflows/stryker.yml',import.meta.url),'utf8').replaceAll('\r\n','\n')
  const pr=source.slice(source.indexOf('      - name: Mutate changed projects'),source.indexOf('      - name: Mutate every project'))
  assert.ok(pr.includes("        if: github.event_name == 'pull_request'\n"))
  assert.ok(pr.includes('        timeout-minutes: 330\n'))
  assert.ok(pr.includes('          node tooling/stryker.mjs run -- --log-to-file\n'))
  const reports=source.slice(source.indexOf('      - name: Mutation reports'))
  assert.ok(reports.includes('        if: always()\n        uses: actions/upload-artifact@v7\n'))
  assert.ok(reports.includes('          path: |\n            StrykerOutput/*/reports/mutation-report.json\n            StrykerOutput/*/stryker-config.json\n            StrykerOutput/*/logs/*.txt\n'))
})


const mutationWorkflow = () => readFileSync(new URL('../workflows/stryker.yml',import.meta.url),'utf8').replaceAll('\r\n','\n')
const fullBuilder = 'projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj'
function mutationJob(name) {
  const source = mutationWorkflow(), start = source.indexOf(`  ${name}:\n`)
  assert.notEqual(start, -1, `Missing ${name} job`)
  const tail = source.slice(start), end = tail.slice(1).search(/\n  [a-z][a-z-]+:\n/)
  return end < 0 ? tail : tail.slice(0, end + 1)
}
// Evaluate only the repository-owned Boolean guard syntax used here; inputs are independent literal event cases.
function admits(name, {event = 'pull_request', action = 'synchronize', label = '', labels = [], fork = false, draft = false, enabled = true, route = true} = {}) {
  const block = mutationJob(name), match = /^    if: >-\n((?:      .*\n)+)/m.exec(block)
  assert.ok(match, `${name} must have an explicit multiline guard`)
  const expression = match[1].trim().replaceAll('github.event.pull_request.labels.*.name', 'labels').replaceAll('needs.workload-route.outputs', 'needs.route.outputs')
  const github = {event_name: event, repository: 'owner/repo', event: {action, label: {name: label}, pull_request: {draft, head: {repo: {full_name: fork ? 'fork/repo' : 'owner/repo'}}}}}
  return Boolean(Function('github', 'vars', 'needs', 'labels', 'contains', `return (${expression})`)(github,
    {ACTIONS_ENABLED: enabled ? 'true' : 'false'}, {route: {outputs: {enabled: route ? 'true' : 'false'}}}, labels, (items, value) => items.includes(value)))
}

test('one-shot mutation label events select one job without persistent label admission', () => {
  const jobs = ['stryker', 'mutation-diagnostic', 'mutation-qualification']
  const labelStates = [[], ['mutation-diagnostic'], ['mutation-qualification'], ['mutation-diagnostic', 'mutation-qualification']]
  for (const labels of labelStates) {
    for (const name of jobs) assert.equal(admits(name, {action: 'labeled', label: 'triage', labels}), false, name)
    assert.equal(admits('workload-route', {action: 'labeled', label: 'triage', labels}), false)
    for (const label of ['mutation-diagnostic', 'mutation-qualification']) {
      for (const name of jobs) assert.equal(admits(name, {action: 'labeled', label, labels}), name === label)
      assert.equal(admits('workload-route', {action: 'labeled', label, labels}), true)
    }
    for (const action of ['opened', 'synchronize', 'reopened', 'ready_for_review']) {
      assert.equal(admits('stryker', {action, labels}), true)
      for (const name of ['mutation-diagnostic', 'mutation-qualification']) assert.equal(admits(name, {action, labels}), false)
    }
  }
  const source = mutationWorkflow()
  assert.match(source, /types: \[opened, synchronize, reopened, ready_for_review, labeled\]/)
  assert.ok(source.includes("format('stryker-label-{0}', github.run_id)"), 'unrelated labels must not cancel an active PR measurement group')
  const collectionReads = source.split('\n').filter(line => line.includes('github.event.pull_request.labels.*.name'))
  assert.equal(collectionReads.length, 3)
  for (const line of collectionReads) assert.ok(line.includes("!contains(github.event.pull_request.labels.*.name, 'stacked')"))
})

test('one-shot jobs retain enabled route same-repository non-draft and not-stacked admission', () => {
  for (const name of ['mutation-diagnostic', 'mutation-qualification']) {
    const command = {action: 'labeled', label: name}
    assert.equal(admits(name, command), true)
    for (const refusal of [{enabled: false}, {route: false}, {fork: true}, {draft: true}, {labels: ['stacked']}])
      assert.equal(admits(name, {...command, ...refusal}), false, `${name} ${JSON.stringify(refusal)}`)
    for (const event of ['schedule', 'workflow_dispatch']) assert.equal(admits(name, {...command, event}), false)
  }
})

test('ordinary PR and scheduled manual mutation keep their existing path without diagnostic fanout', () => {
  for (const event of ['pull_request', 'schedule', 'workflow_dispatch']) {
    assert.equal(admits('stryker', {event}), true)
    for (const name of ['mutation-diagnostic', 'mutation-qualification']) assert.equal(admits(name, {event}), false)
  }
  const ordinary = mutationJob('stryker')
  assert.ok(ordinary.includes("timeout-minutes: ${{ github.event_name == 'pull_request' && 360 || 720 }}"))
  assert.ok(ordinary.includes('node tooling/stryker.mjs run -- --log-to-file'))
  assert.ok(ordinary.includes('& ./.github/workloads/full-mutation.ps1'))
})

test('diagnostic profiles are experimental and bounded independently without fail-fast cancellation', () => {
  const job = mutationJob('mutation-diagnostic')
  assert.match(job, /timeout-minutes: 30/)
  assert.match(job, /fail-fast: false/)
  assert.match(job, /max-parallel: 1/)
  assert.match(job, /profile: \[default, full-host-CPU\]/)
  assert.match(job, /timeout-minutes: 20/)
  assert.ok(job.includes('experimental: profile === \'full-host-CPU\''))
})

test('qualification remains unselected before SDK until measured profile is frozen', () => {
  const job = mutationJob('mutation-qualification')
  assert.match(job, /MUTATION_PROFILE: unselected/)
  assert.match(job, /timeout-minutes: 360/)
  assert.match(job, /timeout-minutes: 330/)
  assert.ok(job.indexOf("if (!['default', 'full-host-CPU'].includes(profile))") < job.indexOf('uses: actions/setup-dotnet@v6'))
  assert.ok(job.includes('Mutation profile is not frozen or is unsupported; no SDK run admitted.'))
})

test('both opt-in jobs validate exactly one configured full Builder scope and record whitelisted identity', () => {
  for (const name of ['mutation-diagnostic', 'mutation-qualification']) {
    const job = mutationJob(name)
    assert.ok(job.includes(`const selector = '${fullBuilder}'`))
    assert.ok(job.includes('matches.length !== 1 || matches[0] !== selector || targetOf(selector, repo.readFile) !== expectedTarget'))
    assert.ok(job.includes("config['coverage-analysis'] !== 'off'"))
    assert.ok(job.includes('floor < 74'))
    assert.ok(job.includes('configSha256: digest(configBytes), baselinesSha256: digest(baselinesBytes)'))
    assert.ok(job.includes('availableParallelism: cpus, logicalCpuCount: os.cpus().length'))
    assert.ok(job.includes('effectiveConcurrency: null, effectiveTimeouts: null'))
    assert.ok(job.includes("const expected = process.env.EXPECTED_HEAD"))
    assert.ok(job.includes("ref: ${{ github.event.pull_request.head.sha }}"))
    assert.ok(job.includes("if (git('status', '--porcelain').trim())"))
    assert.ok(!job.includes('JSON.stringify(process.env'))
    assert.ok(job.indexOf('id: scope') < job.indexOf('uses: actions/setup-dotnet@v6'))
    assert.ok(job.includes(`node tooling/stryker.mjs full '${fullBuilder}' -- @strykerArgs`))
    const args = name === 'mutation-diagnostic'
      ? "$strykerArgs = @('--log-to-file', '--reporter', 'json', '--reporter', 'dots', '--verbosity', 'error')"
      : "$strykerArgs = @('--log-to-file')"
    assert.ok(job.includes(args))
    assert.ok(job.includes("$strykerArgs += @('--concurrency', [string]$concurrency)"))
    assert.ok(job.includes("[int]::TryParse($env:MUTATION_CONCURRENCY, [ref]$concurrency)"))
    for (const forbidden of ['--mutate', '--test-case-filter', '--timeout-ratio', '--additional-timeout', 'Invoke-Expression']) assert.ok(!job.includes(forbidden), `${name} ${forbidden}`)
  }
})

test('all mutation uploads retain durable diagnostics with unique run attempt and profile names', () => {
  for (const name of ['stryker', 'mutation-diagnostic', 'mutation-qualification']) {
    const job = mutationJob(name)
    assert.ok(job.includes('if: always()\n        uses: actions/upload-artifact@v7'))
    assert.ok(job.includes('StrykerOutput/*/reports/mutation-report.json'))
    assert.ok(job.includes('StrykerOutput/*/stryker-config.json'))
    assert.ok(job.includes('StrykerOutput/*/logs/*.txt'))
    assert.ok(job.includes('StrykerOutput/*/diagnostics/**'))
    assert.ok(job.includes('${{ github.run_id }}-${{ github.run_attempt }}-'))
    assert.ok(!job.includes('overwrite: true'))
  }
})


test('inline admission scripts compile and use literal environment names before any SDK step', t => {
  const directory = mkdtempSync(join(tmpdir(), 'mutation-profile-'))
  t.after(() => rmSync(directory, {recursive: true, force: true}))
  const configDirectory = join(directory, 'projections/dotnet/blocks/hlp.blocks.builder-definitions.tests')
  mkdirSync(configDirectory, {recursive: true})
  mkdirSync(join(directory, 'tooling'))
  writeFileSync(join(configDirectory, 'stryker-config.json'), JSON.stringify({'stryker-config': {
    project: 'Harborline.Blocks.BuilderDefinitions.csproj', 'coverage-analysis': 'off',
    since: {enabled: true, target: 'origin/main'}, thresholds: {high: 80, low: 74, break: 74}, reporters: ['json']}}))
  writeFileSync(join(directory, 'tooling/stryker-baselines.json'), '{}')
  const head = 'a'.repeat(40), tree = 'b'.repeat(40), output = join(directory, 'output')
  for (const name of ['mutation-diagnostic', 'mutation-qualification']) {
    const job = mutationJob(name), match = /          @'\n([\s\S]*?)          '@ \| node --input-type=module/.exec(job)
    assert.ok(match, `${name} must contain the maintained inline Node admission script`)
    const script = match[1].replace(/^          /gm, '')
    const syntax = spawnSync(process.execPath, ['--check', '--input-type=module'], {input: script, encoding: 'utf8'})
    assert.equal(syntax.status, 0, syntax.stderr)
    assert.ok(script.includes('process.env.MUTATION_PROFILE, mode = process.env.MUTATION_MODE'))
    assert.ok(job.includes('$env:MUTATION_PROFILE'))
    assert.ok(!/MUTATION_(?:diagnostic|qualification|unselected|\$)/.test(job))
    // Substitute only repository/Git I/O; run the actual admission body with independent literal fixtures.
    const executable = script
      .replace("import {execFileSync} from 'node:child_process'", `const execFileSync = (_command, args) => args[0] === 'status' ? '' : args[1] === 'HEAD' ? '${head}' : '${tree}'`)
      .replace("import {repository, targetOf, fullModeBreak} from './tooling/stryker.mjs'", `
        const repository = () => ({testProjects: ['${fullBuilder}'], exclusions: {}, baselines: {}, readFile: () => ''})
        const targetOf = () => 'projections/dotnet/blocks/hlp.blocks.builder-definitions/Harborline.Blocks.BuilderDefinitions.csproj'
        const fullModeBreak = () => 74`)
    for (const profile of ['default', 'full-host-CPU', 'unselected']) {
      const result = spawnSync(process.execPath, ['--input-type=module'], {input: executable, cwd: directory, encoding: 'utf8',
        env: {EXPECTED_HEAD: head, MUTATION_MODE: name === 'mutation-diagnostic' ? 'diagnostic' : 'qualification', MUTATION_PROFILE: profile, GITHUB_OUTPUT: output}})
      if (profile === 'unselected') {
        assert.notEqual(result.status, 0)
        assert.match(result.stderr, /profile is not frozen/)
      } else {
        assert.equal(result.status, 0, result.stderr)
        const identity = JSON.parse(result.stdout)
        assert.equal(identity.profile, profile)
        assert.equal(identity.head, head)
        assert.equal(identity.tree, tree)
        assert.equal(identity.selector, fullBuilder)
        assert.equal(identity.floor, 74)
        assert.equal(identity.experimental, profile === 'full-host-CPU')
        assert.deepEqual(identity.requestedReporters, name === 'mutation-diagnostic' ? ['json', 'dots'] : ['json'])
        assert.equal(identity.requestedConsoleVerbosity, name === 'mutation-diagnostic' ? 'error' : null)
        assert.equal(identity.requestedConcurrency, profile === 'default' ? null : identity.availableParallelism)
        assert.equal(identity.effectiveConcurrency, null)
        assert.equal(identity.effectiveTimeouts, null)
      }
    }
  }
})


test('only diagnostic adds native JSON plus dots and error console output without changing canonical reporters', () => {
  const diagnostic = mutationJob('mutation-diagnostic'), qualification = mutationJob('mutation-qualification')
  assert.equal((diagnostic.match(/'--reporter'/g) ?? []).length, 2)
  assert.equal((diagnostic.match(/'--verbosity'/g) ?? []).length, 1)
  assert.equal((qualification.match(/'--reporter'/g) ?? []).length, 0)
  assert.equal((qualification.match(/'--verbosity'/g) ?? []).length, 0)
  for (const job of [diagnostic, qualification]) {
    assert.ok(job.includes("config.reporters.some(reporter => reporter !== 'json')"))
    for (const forbidden of ["'html'", "'dashboard'", '--diag', '--reporter-plugin']) assert.ok(!job.includes(forbidden))
  }
  assert.ok(qualification.includes('MUTATION_PROFILE: unselected'))
  assert.ok(!mutationJob('stryker').includes("'dots'"))
})
