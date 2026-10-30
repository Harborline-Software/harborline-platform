import {test} from 'node:test'
import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {availableParallelism, cpus, release, tmpdir, totalmem} from 'node:os'
import {join} from 'node:path'
import {spawnSync} from 'node:child_process'

const source = readFileSync(new URL('../workflows/stryker.yml', import.meta.url), 'utf8').replaceAll('\r\n', '\n')
const helper = 'tooling/stryker-builder-partitions.mjs'
const plan = 'StrykerOutput/builder-plan/plan.json'
const downloads = 'StrykerOutput/builder-partitions'
function job(name) {
  const start = source.indexOf(`  ${name}:\n`)
  assert.notEqual(start, -1, `Missing ${name}`)
  const tail = source.slice(start), end = tail.slice(1).search(/\n  [a-z][a-z-]+:\n/)
  return end < 0 ? tail : tail.slice(0, end + 1)
}
function context({event = 'pull_request', action = 'synchronize', label = '', fork = false, draft = false, run = 7} = {}) {
  return {event_name: event, repository: 'owner/repo', run_id: run, ref: 'refs/heads/main', sha: 'b'.repeat(40),
    event: {action, label: {name: label}, pull_request: {number: 359, draft, head: {sha: 'a'.repeat(40), repo: {full_name: fork ? 'fork/repo' : 'owner/repo'}}}}}
}
function evaluate(expression, options = {}) {
  const needs = {route: {result: options.routeResult ?? 'success', outputs: {enabled: options.route === false ? 'false' : 'true'}},
    plan: {result: options.planResult ?? 'success', outputs: {selected: options.selected ?? 'false', mode: options.mode ?? 'normal', subject: options.subject ?? 'c'.repeat(40)}},
    partitions: {result: options.partitionsResult ?? 'skipped'}}
  return Function('github', 'inputs', 'vars', 'needs', 'labels', 'contains', 'always', 'format', `return (${expression
    .replaceAll('github.event.pull_request.labels.*.name', 'labels')
    .replaceAll('needs.workload-route', 'needs.route').replaceAll('needs.builder-plan', 'needs.plan')
    .replaceAll('needs.builder-partitions', 'needs.partitions')})`)(context(options), options.inputs ?? {},
    {ACTIONS_ENABLED: options.enabled === false ? 'false' : 'true'}, needs, options.labels ?? [],
    (items, value) => items.includes(value), () => true,
    (template, ...values) => template.replace(/\{(\d+)\}/g, (_match, index) => values[index]))
}
function admits(name, options) {
  const match = /^    if: >-\n((?:      .*\n)+)/m.exec(job(name))
  assert.ok(match, `${name} explicit multiline guard`)
  return Boolean(evaluate(match[1].trim(), options))
}
function step(name, title) {
  const block = job(name), start = block.indexOf(`      - name: ${title}\n`)
  assert.notEqual(start, -1, `${name}: ${title}`)
  const tail = block.slice(start), end = tail.slice(1).indexOf('\n      - ')
  return end < 0 ? tail : tail.slice(0, end + 1)
}
function script(name, title) {
  const match = /          @'\n([\s\S]*?)          '@ \| node --input-type=module/.exec(step(name, title))
  assert.ok(match, `${name}: ${title} executable Node guard`)
  return match[1].replace(/^          /gm, '')
}
function runGuard(name, title, env = {}) {
  return spawnSync(process.execPath, ['--input-type=module'], {
    input: script(name, title), encoding: 'utf8', env: {...process.env, ...env}, timeout: 10000,
  })
}

test('normal events retain owner selection and one-shot labels cannot persist admission', () => {
  const jobs = ['builder-plan', 'builder-partitions', 'stryker', 'mutation-diagnostic', 'mutation-qualification']
  for (const labels of [[], ['mutation-diagnostic'], ['mutation-qualification'], ['mutation-diagnostic', 'mutation-qualification']]) {
    for (const action of ['opened', 'synchronize', 'reopened', 'ready_for_review']) {
      assert.equal(admits('stryker', {action, labels}), true)
      assert.equal(admits('builder-plan', {action, labels}), true)
      assert.equal(admits('builder-partitions', {action, labels}), false)
      assert.equal(admits('builder-partitions', {action, labels, selected: 'true'}), true)
      assert.equal(admits('mutation-diagnostic', {action, labels}), false)
      assert.equal(admits('mutation-qualification', {action, labels}), false)
    }
    for (const label of ['mutation-diagnostic', 'mutation-qualification']) {
      const options = {action: 'labeled', label, labels, selected: 'true'}
      assert.equal(admits('builder-plan', options), true)
      assert.equal(admits('builder-partitions', options), true)
      assert.equal(admits('stryker', options), false)
      assert.equal(admits('mutation-diagnostic', options), label === 'mutation-diagnostic')
      assert.equal(admits('mutation-qualification', options), label === 'mutation-qualification')
    }
    for (const name of ['workload-route', ...jobs]) assert.equal(admits(name, {action: 'labeled', label: 'triage', labels, selected: 'true'}), false, name)
  }
})

test('every new hosted PR job retains same-repo Ready route and negative stacked guards', () => {
  for (const name of ['builder-plan', 'builder-partitions', 'stryker', 'mutation-diagnostic', 'mutation-qualification']) {
    const command = name === 'mutation-diagnostic' || name === 'mutation-qualification'
      ? {action: 'labeled', label: name, selected: 'true'} : {selected: 'true'}
    assert.equal(admits(name, command), true)
    for (const refusal of [{enabled: false}, {route: false}, {routeResult: 'failure'}, {routeResult: 'cancelled'}, {fork: true}, {draft: true}, {labels: ['stacked']}])
      assert.equal(admits(name, {...command, ...refusal}), false, `${name} ${JSON.stringify(refusal)}`)
  }
  const reads = source.split('\n').filter(line => line.includes('github.event.pull_request.labels.*.name'))
  assert.equal(reads.length, 5)
  for (const line of reads) assert.ok(line.includes("!contains(github.event.pull_request.labels.*.name, 'stacked')"))
})

test('plan mode comes only from the current one-shot event', () => {
  const expression = /^      PARTITION_MODE: \$\{\{ (.*) \}\}$/m.exec(job('builder-plan'))?.[1]
  assert.ok(expression)
  assert.equal(evaluate(expression), 'normal')
  assert.equal(evaluate(expression, {action: 'ready_for_review', labels: ['mutation-qualification']}), 'normal')
  assert.equal(evaluate(expression, {action: 'labeled', label: 'mutation-diagnostic'}), 'diagnostic')
  assert.equal(evaluate(expression, {action: 'labeled', label: 'mutation-qualification'}), 'qualification')
})

test('cancellation groups isolate normal PRs commands and unrelated label events', () => {
  const expression = /^  group: \$\{\{ (.*) \}\}$/m.exec(source)?.[1]
  assert.ok(expression)
  assert.equal(evaluate(expression), 'stryker-pr-359')
  assert.equal(evaluate(expression, {action: 'labeled', label: 'mutation-diagnostic'}), 'stryker-command-359-mutation-diagnostic')
  assert.equal(evaluate(expression, {action: 'labeled', label: 'mutation-qualification'}), 'stryker-command-359-mutation-qualification')
  assert.equal(evaluate(expression, {action: 'labeled', label: 'triage', run: 12}), 'stryker-label-12')
  assert.equal(evaluate(expression, {event: 'schedule'}), 'stryker-refs/heads/main')
  assert.ok(source.includes("cancel-in-progress: ${{ github.event_name == 'pull_request' }}"))
  const name = /^    name: \$\{\{ (.*) \}\}$/m.exec(job('stryker'))?.[1]
  assert.equal(evaluate(name), 'stryker')
  assert.equal(evaluate(name, {action: 'labeled', label: 'mutation-diagnostic'}), 'stryker-command-owner')
})

test('normal merge subjects and explicit label heads stay distinct across all checkouts', () => {
  const ref = name => /^          ref: \$\{\{ (.*) \}\}$/m.exec(job(name))?.[1]
  const expected = name => /^          EXPECTED_HEAD: \$\{\{ (.*) \}\}$/m.exec(job(name))?.[1]
  assert.equal(evaluate(ref('builder-plan')), 'b'.repeat(40))
  assert.equal(evaluate(expected('builder-plan')), 'b'.repeat(40))
  assert.equal(evaluate(ref('builder-plan'), {action: 'labeled', label: 'mutation-diagnostic'}), 'a'.repeat(40))
  assert.equal(evaluate(expected('builder-plan'), {action: 'labeled', label: 'mutation-qualification'}), 'a'.repeat(40))
  for (const name of ['builder-partitions', 'stryker']) {
    assert.equal(evaluate(ref(name)), 'c'.repeat(40))
    assert.equal(evaluate(expected(name)), 'c'.repeat(40))
  }
  for (const name of ['mutation-diagnostic', 'mutation-qualification']) assert.equal(evaluate(ref(name)), 'a'.repeat(40))
  assert.equal(evaluate(ref('stryker'), {event: 'schedule'}), 'b'.repeat(40))
})

test('failed or cancelled routes cannot launch always-owners even with stale enabled output', () => {
  for (const routeResult of ['failure', 'cancelled', 'skipped']) {
    assert.equal(admits('stryker', {event: 'schedule', routeResult}), false)
    assert.equal(admits('stryker', {routeResult}), false)
    for (const name of ['mutation-diagnostic', 'mutation-qualification'])
      assert.equal(admits(name, {action: 'labeled', label: name, routeResult, selected: 'true'}), false)
  }
})

test('normal Control mirror pins only the selected Builder path', () => {
  const mirror = step('stryker', 'Mirror harborline-control for the tests that read it')
  assert.ok(mirror.includes('BUILDER_SELECTED: ${{ needs.builder-plan.outputs.selected }}'))
  assert.ok(mirror.includes("if ($env:GITHUB_EVENT_NAME -eq 'pull_request' -and $env:BUILDER_SELECTED -eq 'true') {"))
  assert.ok(mirror.includes('checkout --quiet --detach 69b2798f55790ef5c29056d04a998b8c626db823'))
})

test('matrix is selected-only four complete-file candidates with separate bounded budgets', () => {
  const matrix = job('builder-partitions')
  assert.ok(matrix.includes('partition: [1, 2, 3, 4]'))
  assert.ok(matrix.includes('max-parallel: 4'))
  assert.ok(matrix.includes('fail-fast: false'))
  assert.ok(matrix.includes("timeout-minutes: ${{ needs.builder-plan.outputs.mode == 'diagnostic' && 30 || 360 }}"))
  assert.ok(matrix.includes("timeout-minutes: ${{ needs.builder-plan.outputs.mode == 'diagnostic' && 20 || 330 }}"))
  assert.equal(admits('builder-partitions', {selected: 'false'}), false)
  assert.equal(admits('builder-partitions', {selected: 'true', planResult: 'failure'}), false)
  assert.equal(admits('builder-partitions', {selected: 'true', planResult: 'cancelled'}), false)
  assert.equal(admits('builder-partitions', {selected: 'true', planResult: 'skipped'}), false)
  assert.ok(matrix.includes(`node ${helper} shard '${plan}' $env:PARTITION_INDEX`))
})

test('full Builder execution uses the measured complete-file profile before SDK work', () => {
  const block = step('builder-plan', 'Refuse unselected full partition execution')
  assert.ok(block.includes("if: steps.plan.outputs.selected == 'true' && env.PARTITION_MODE != 'diagnostic'"))
  const result = runGuard('builder-plan', 'Refuse unselected full partition execution')
  assert.equal(result.status, 0, result.stderr)
  assert.equal(result.signal, null)
  assert.ok(block.includes("const fullPartitionProfile = 'four-complete-files-default-two'"))
  const refused = spawnSync(process.execPath, ['--input-type=module'], {
    input: script('builder-plan', 'Refuse unselected full partition execution')
      .replace("const fullPartitionProfile = 'four-complete-files-default-two'", "const fullPartitionProfile = 'unselected'"),
    encoding: 'utf8', timeout: 10000,
  })
  assert.equal(refused.status, 1, refused.stderr)
  assert.match(refused.stderr, /Full partition execution is not frozen/)
  assert.ok(!job('builder-plan').includes('actions/setup-dotnet'))
  assert.ok(!source.includes('MUTATION_PROFILE:'))
})

test('stable normal owner runs after failed dependencies but refuses them before SDK', () => {
  assert.ok(job('stryker').includes('needs: [workload-route, builder-plan, builder-partitions]'))
  assert.match(job('stryker'), /if: >-\n      always\(\)/)
  assert.equal(admits('stryker', {selected: 'true', partitionsResult: 'failure'}), true)
  assert.equal(admits('stryker', {planResult: 'failure'}), true)
  for (const [selected, planResult, partitionsResult, pass] of [
    ['false', 'success', 'skipped', true], ['true', 'success', 'success', true],
    ['true', 'success', 'failure', false], ['true', 'success', 'cancelled', false],
    ['true', 'success', 'skipped', false], ['false', 'success', 'success', false],
    ['false', 'failure', 'skipped', false], ['', 'success', 'skipped', false],
  ]) {
    const result = runGuard('stryker', 'Require admitted PR dependencies', {
      BUILDER_SELECTED: selected, PLAN_RESULT: planResult, PARTITIONS_RESULT: partitionsResult,
    })
    assert.equal(result.status === 0, pass, JSON.stringify({selected, planResult, partitionsResult}))
  }
  assert.ok(job('stryker').indexOf('Require admitted PR dependencies') < job('stryker').indexOf('actions/setup-dotnet'))
})

test('host context is whitelisted real resource data after admission and before SDK without changing plan identity', () => {
  const matrix = job('builder-partitions'), title = 'Record whitelisted partition host context'
  const block = step('builder-partitions', title)
  assert.ok(matrix.indexOf('Admit exact partition plan before SDK setup') < matrix.indexOf(title))
  assert.ok(matrix.indexOf(title) < matrix.indexOf('actions/setup-dotnet'))
  assert.ok(block.includes('PARTITION_INDEX: ${{ matrix.partition }}'))
  assert.ok(!block.includes('...process.env'))
  const root = mkdtempSync(join(tmpdir(), 'builder-host-context-'))
  try {
    mkdirSync(join(root, 'StrykerOutput/builder-plan'), {recursive: true})
    const admitted = {head: '1'.repeat(40), tree: '2'.repeat(40), runId: '73', runAttempt: '2', mode: 'diagnostic'}
    const planBytes = JSON.stringify(admitted) + '\n'
    writeFileSync(join(root, plan), planBytes)
    const before = Date.now()
    const execute = () => spawnSync(process.execPath, ['--input-type=module'], {
      input: script('builder-partitions', title), cwd: root, encoding: 'utf8', timeout: 10000,
      env: {...process.env, PARTITION_INDEX: '3', RUNNER_NAME: 'literal-runner', ImageVersion: 'literal-image', UNLISTED_SECRET: 'never-write-this'},
    })
    const result = execute()
    assert.equal(result.status, 0, result.stderr)
    const file = join(root, 'StrykerOutput/builder-host/diagnostics/host.json')
    const bytes = readFileSync(file, 'utf8'), observed = JSON.parse(bytes)
    assert.deepEqual(Object.keys(observed).sort(), ['arch', 'availableParallelism', 'capturedUtc', 'head', 'imageVersion',
      'logicalCpuCount', 'mode', 'nodeVersion', 'osRelease', 'partition', 'platform', 'runAttempt', 'runId', 'runnerName', 'totalMemory', 'tree'].sort())
    assert.deepEqual({...observed, capturedUtc: null}, {...admitted, partition: 3, capturedUtc: null,
      availableParallelism: availableParallelism(), logicalCpuCount: cpus().length, totalMemory: totalmem(),
      platform: process.platform, arch: process.arch, osRelease: release(), nodeVersion: process.version,
      runnerName: 'literal-runner', imageVersion: 'literal-image'})
    assert.match(observed.capturedUtc, /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$/)
    assert.ok(Date.parse(observed.capturedUtc) >= before && Date.parse(observed.capturedUtc) <= Date.now())
    assert.deepEqual(JSON.parse(result.stdout), observed)
    assert.ok(!bytes.includes('never-write-this'))
    assert.equal(readFileSync(join(root, plan), 'utf8'), planBytes)
    const repeated = execute()
    assert.notEqual(repeated.status, 0)
    assert.match(repeated.stderr, /EEXIST/)
    assert.equal(readFileSync(file, 'utf8'), bytes)
    assert.ok(block.includes("openSync(file, 'wx')"))
    assert.ok(block.includes('fsyncSync(fd)'))
  } finally { rmSync(root, {recursive: true, force: true}) }
})

test('normal PR owner emits canonical configuration check before helper execution and stops on nonzero exit', () => {
  const mutation = step('stryker', 'Mutate changed projects and hold each to its break (pull request)')
  assert.ok(mutation.includes(`          dotnet tool restore
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          node tooling/stryker.mjs check
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          node tooling/stryker-builder-partitions.mjs pr 'StrykerOutput/builder-plan/plan.json' 'StrykerOutput/builder-partitions'
          exit $LASTEXITCODE`))
  assert.equal((mutation.match(/node tooling\/stryker\.mjs check/g) ?? []).length, 1)
})

test('all source admission is delegated before SDK without local ref rewriting or a Builder fallback', () => {
  assert.ok(job('builder-plan').includes(`node ${helper} plan $env:PARTITION_MODE '${plan}'`))
  assert.ok(job('builder-plan').includes('base: ${{ steps.plan.outputs.base }}'))
  for (const name of ['builder-partitions', 'stryker', 'mutation-diagnostic', 'mutation-qualification'])
    assert.ok(job(name).includes('EXPECTED_BASE: ${{ needs.builder-plan.outputs.base }}'))
  for (const name of ['stryker', 'builder-partitions']) {
    const block = job(name)
    assert.ok(block.includes(`node ${helper} admit '${plan}'`))
    assert.ok(block.indexOf(`node ${helper} admit`) < block.indexOf('actions/setup-dotnet'))
    assert.ok(block.includes('EXPECTED_HEAD: ${{ needs.builder-plan.outputs.subject }}'))
  }
  assert.ok(job('stryker').includes(`node ${helper} pr '${plan}' '${downloads}'`))
  assert.ok(!job('stryker').includes('node tooling/stryker.mjs run'))
  assert.ok(!source.includes('git update-ref'))
  for (const forbidden of ['--mutate', '--test-case-filter', '--timeout-ratio', '--additional-timeout', '--concurrency', '--reporter', '--verbosity', 'Invoke-Expression', 'JSON.stringify(process.env)'])
    assert.ok(!source.includes(forbidden), `workflow must not duplicate helper-native policy: ${forbidden}`)
})

test('diagnostic and qualification owners stay Node-only and do not turn partial reports into qualification', () => {
  const diagnostic = job('mutation-diagnostic'), qualification = job('mutation-qualification')
  for (const block of [diagnostic, qualification]) {
    assert.ok(!block.includes('setup-dotnet'))
    assert.ok(!block.includes('dotnet tool'))
    assert.ok(!block.includes('PINS_TOKEN'))
    assert.ok(block.includes(`node ${helper} admit '${plan}'`))
  }
  assert.ok(!diagnostic.includes(`node ${helper} collect`))
  assert.ok(qualification.includes(`node ${helper} collect '${plan}' '${downloads}'`))
  assert.ok(qualification.includes('timeout-minutes: 360'))
  assert.ok(qualification.includes('timeout-minutes: 330'))
  for (const result of ['failure', 'cancelled', 'skipped', '']) {
    assert.notEqual(runGuard('mutation-qualification', 'Require all four complete dependencies', {PARTITIONS_RESULT: result}).status, 0)
    assert.notEqual(runGuard('mutation-diagnostic', 'Diagnostic observations are not whole qualification', {PARTITIONS_RESULT: result}).status, 0)
  }
  assert.equal(runGuard('mutation-qualification', 'Require all four complete dependencies', {PARTITIONS_RESULT: 'success'}).status, 0)
})

test('complete packet artifacts contain only the adjacent receipt and report for a flat LCA', () => {
  const packet = step('builder-partitions', 'Keep the flat complete partition packet')
  assert.ok(packet.includes('if: always()\n        uses: actions/upload-artifact@v7'))
  assert.ok(packet.includes('name: builder-partition-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.partition }}'))
  assert.ok(packet.includes('if-no-files-found: ignore'))
  const paths = packet.split('          path: |\n')[1].trim().split('\n').map(line => line.trim())
  assert.deepEqual(paths, [
    'StrykerOutput/builder-partition-${{ matrix.partition }}/diagnostics/receipt.json',
    'StrykerOutput/builder-partition-${{ matrix.partition }}/diagnostics/report.json',
  ])
  assert.ok(!packet.includes('**'))
})

test('raw native artifacts stay separate from packet downloads and retain partial diagnostics', () => {
  assert.ok(job('builder-plan').includes('name: builder-plan-${{ github.run_id }}-${{ github.run_attempt }}'))
  assert.ok(job('builder-plan').includes(`path: ${plan}`))
  const native = step('builder-partitions', 'Keep partial native diagnostics')
  assert.ok(native.includes('if: always()\n        uses: actions/upload-artifact@v7'))
  assert.ok(native.includes('name: builder-native-${{ github.run_id }}-${{ github.run_attempt }}-${{ matrix.partition }}'))
  assert.ok(native.includes('if-no-files-found: ignore'))
  const paths = native.split('          path: |\n')[1].trim().split('\n').map(line => line.trim())
  assert.deepEqual(paths, ['StrykerOutput/*/reports/mutation-report.json', 'StrykerOutput/*/stryker-config.json',
    'StrykerOutput/*/logs/*.txt', 'StrykerOutput/*/diagnostics/**', 'StrykerOutput/builder-plan/plan.json'])
  assert.ok(!native.includes('name: builder-partition-'))
  for (const name of ['stryker', 'mutation-qualification']) {
    assert.ok(job(name).includes('pattern: builder-partition-${{ github.run_id }}-${{ github.run_attempt }}-*'))
    assert.ok(job(name).includes('merge-multiple: false'))
    assert.ok(!job(name).includes('pattern: builder-native-'))
  }
  assert.ok(job('mutation-qualification').includes('StrykerOutput/builder-owner/**'))
  assert.ok(!source.includes('overwrite: true'))
})

test('partial diagnostic attempts require only their plan and never download absent complete packets', () => {
  const diagnostic = job('mutation-diagnostic')
  assert.equal((diagnostic.match(/uses: actions\/download-artifact@v8/g) ?? []).length, 1)
  assert.ok(diagnostic.includes('name: builder-plan-${{ github.run_id }}-${{ github.run_attempt }}'))
  assert.ok(!diagnostic.includes('pattern: builder-partition-'))
  assert.ok(!diagnostic.includes('path: StrykerOutput/builder-partitions'))
  assert.ok(diagnostic.includes(`node ${helper} admit '${plan}'`))
  assert.notEqual(runGuard('mutation-diagnostic', 'Diagnostic observations are not whole qualification', {PARTITIONS_RESULT: 'failure'}).status, 0)
})

test('schedule and dispatch keep the original all-project commands budgets and Windows lock choice', () => {
  for (const event of ['schedule', 'workflow_dispatch']) {
    assert.equal(admits('stryker', {event}), true)
    for (const name of ['builder-plan', 'builder-partitions', 'mutation-diagnostic', 'mutation-qualification']) assert.equal(admits(name, {event}), false)
  }
  const normal = job('stryker')
  assert.ok(normal.includes("timeout-minutes: ${{ github.event_name == 'pull_request' && 360 || 720 }}"))
  const scheduled = step('stryker', 'Mutate every project and hold each to its baseline (scheduled)')
  assert.ok(scheduled.includes("if: github.event_name != 'pull_request'"))
  assert.ok(scheduled.includes("$env:RUNNER_OS -eq 'Windows'"))
  const windows = scheduled.slice(scheduled.indexOf("$env:RUNNER_OS -eq 'Windows'"), scheduled.indexOf('python .github/workloads/host-workload-lock.py'))
  assert.ok(windows.includes('& ./.github/workloads/full-mutation.ps1'))
  assert.ok(!windows.includes('host-workload-lock.py'))
  assert.ok(scheduled.includes('python .github/workloads/host-workload-lock.py -- pwsh -NoProfile -File .github/workloads/full-mutation.ps1'))
  assert.match(normal, /if: .*runner\.os != 'Windows'\n        with:\n          python-version: '3\.14'/)
  assert.equal(readFileSync(new URL('full-mutation.ps1', import.meta.url), 'utf8').replaceAll('\r\n', '\n'),
    'dotnet tool restore\nif ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }\nnode tooling/stryker.mjs full\nexit $LASTEXITCODE\n')
})

test('weekly full run, and a manual changed-files run that never takes the full or Windows route', () => {
  assert.ok(source.includes("    - cron: '40 7 * * 0'\n"))
  assert.ok(!source.includes("cron: '40 7 * * *'"))
  const changed = {event: 'workflow_dispatch', inputs: {scope: 'changed'}}
  assert.equal(admits('stryker', changed), false)
  assert.equal(admits('stryker', {event: 'workflow_dispatch', inputs: {scope: 'full'}}), true)
  assert.equal(admits('stryker-changed', changed), true)
  for (const refusal of [{event: 'workflow_dispatch', inputs: {scope: 'full'}}, {event: 'workflow_dispatch'}, {event: 'schedule'},
    {...changed, enabled: false}, {}, {action: 'labeled', label: 'mutation-qualification'}])
    assert.equal(admits('stryker-changed', refusal), false, JSON.stringify(refusal))
  for (const name of ['builder-plan', 'builder-partitions', 'mutation-diagnostic', 'mutation-qualification']) assert.equal(admits(name, changed), false, name)
  const manual = job('stryker-changed')
  assert.ok(manual.includes('runs-on: ubuntu-latest'))
  assert.ok(!manual.includes('needs:'))
  assert.ok(!manual.includes('full-mutation.ps1'))
  assert.ok(!manual.includes('stryker.mjs full'))
  const mutation = step('stryker-changed', 'Mutate changed files and hold each project to its break (manual)')
  assert.ok(mutation.includes(`          dotnet tool restore
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          node tooling/stryker.mjs check
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
          node tooling/stryker.mjs run
          exit $LASTEXITCODE`))
})
