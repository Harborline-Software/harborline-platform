// Oracles: explicit owner-approved event/lane table, T-577, PROC-0001 and GitHub event semantics.
import assert from 'node:assert/strict'
import {execFileSync, spawnSync} from 'node:child_process'
import {readFileSync, mkdtempSync, mkdirSync, writeFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'
import {verifyCiLanes} from '../verify-ci-lanes.mjs'
import {runPrPreflight, toolingTestsPassed} from '../run-pr-preflight.mjs'
import {requiresPrHeadless} from '../plan-pr-validation.mjs'

const root = resolve(import.meta.dirname, '../..')
const workflow = readFileSync(resolve(root, '.github/workflows/verify.yml'), 'utf8')
const repository = 'Harborline-Software/harborline-platform'
const pr = (draft = false, labels = [], head = repository) => ({pull_request: {draft, labels: labels.map(name => ({name})), head: {repo: {full_name: head}}}})
const context = (event = 'pull_request', payload = pr(), actionsEnabled = 'true') => ({event, payload, repository, actionsEnabled})
const preliminary = {'pr-preflight': {result: 'success', outputs: {'headless-required': 'false'}}, 'phase-4-gate': {result: 'skipped'}, 'gallery-shard': {result: 'skipped'}, 'gallery-collect': {result: 'skipped'}}
const implementation = {...preliminary, 'pr-preflight': {result: 'success', outputs: {'headless-required': 'true'}}, 'phase-4-gate': {result: 'success'}}
const full = {'pr-preflight': {result: 'skipped'}, 'phase-4-gate': {result: 'success'}, 'gallery-shard': {result: 'success'}, 'gallery-collect': {result: 'success'}}
const block = job => workflow.split(`\n  ${job}:\n`)[1].split(/\n  [a-z][\w-]*:\n/)[0]
const guard = job => block(job).split('    if:')[1].split('\n    runs-on:')[0].replace(/^\s*>-\s*/, '').trim()

// Evaluate the workflow's small Boolean guard subset, so fixture results exercise the actual YAML
// wiring as well as the aggregator. Refuse an expression outside this subset rather than guessing.
function selected(job, c) {
  const values = {
    'vars.ACTIONS_ENABLED': c.actionsEnabled,
    'github.event_name': c.event,
    'github.repository': c.repository,
    'github.event.pull_request.head.repo.full_name': c.payload?.pull_request?.head?.repo?.full_name,
    'github.event.pull_request.draft': c.payload?.pull_request?.draft,
    'needs.pr-preflight.result': c.preflightResult ?? 'success',
    'needs.pr-preflight.outputs.headless-required': c.headless ?? 'false',
  }
  let expression = guard(job).replace(/always\(\)/g, 'true')
    .replace(/contains\(github\.event\.pull_request\.labels\.\*\.name, 'stacked'\)/g,
      String(c.payload?.pull_request?.labels?.some(label => label.name === 'stacked') ?? false))
  for (const [name, value] of Object.entries(values).sort((a, b) => b[0].length - a[0].length)) {
    expression = expression.replaceAll(name, JSON.stringify(value) ?? 'undefined')
  }
  assert.doesNotMatch(expression, /github\.|vars\.|needs\.|contains|always/, 'unsupported guard token')
  return Function(`"use strict"; return (${expression});`)()
}

test('PR event types produce preliminary feedback; full lanes are reserved for queue/manual', () => {
  assert.match(workflow, /types: \[opened, synchronize, reopened, ready_for_review\]/)
  assert.match(workflow, /\n  merge_group:\n  workflow_dispatch:\n/)
  assert.doesNotMatch(workflow.split('\nconcurrency:')[0], /\n  push:/)
  for (const action of ['opened', 'synchronize', 'reopened', 'ready_for_review']) {
    const c = context('pull_request', {...pr(), action})
    assert.equal(selected('pr-preflight', c), true, action)
    for (const job of ['phase-4-gate', 'gallery-shard', 'gallery-collect']) assert.equal(selected(job, c), false, `${action}: ${job}`)
    assert.equal(selected('verify', c), true)
    assert.deepEqual(verifyCiLanes(c, preliminary), {status: 'PASS', scope: 'preliminary', failures: []})
  }
  for (const event of ['merge_group', 'workflow_dispatch']) {
    const c = context(event, {})
    assert.equal(selected('pr-preflight', c), false)
    for (const job of ['phase-4-gate', 'gallery-shard', 'gallery-collect', 'verify']) assert.equal(selected(job, c), true, `${event}: ${job}`)
    assert.deepEqual(verifyCiLanes(c, full), {status: 'PASS', scope: 'full', failures: []})
  }
})

test('implementation and uncertain inputs retain early headless behavioral validation', () => {
  assert.equal(requiresPrHeadless(['CONTRIBUTING.md', '.github/workflows/verify.yml', 'tooling/verify-ci-lanes.mjs']), false)
  for (const paths of [[], undefined, ['package.json'], ['pnpm-lock.yaml'], ['global.json'],
    ['projections/dotnet/blocks/hlp.blocks.entity-views/Index.cs'],
    ['projections/react/ui/hlp.ui.button/src/index.ts'], ['conformance/hlp.ui.button/corpus/basic.json'],
    ['tooling/run-native.mjs'], ['tooling/tests/other.test.mjs'], ['docs/evidence/phase-4/gate.json'],
    ['CONTRIBUTING.md', 'Directory.Build.props']]) assert.equal(requiresPrHeadless(paths), true, JSON.stringify(paths))
  const c = {...context(), headless: 'true'}
  assert.equal(selected('phase-4-gate', c), true)
  assert.deepEqual(verifyCiLanes(c, implementation), {status: 'PASS', scope: 'preliminary', failures: []})
  for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
    assert.equal(verifyCiLanes(c, {...implementation, 'phase-4-gate': {result}}).status, 'FAIL')
  }
  assert.equal(verifyCiLanes(context(), {...preliminary, 'pr-preflight': {result: 'success'}}).status, 'FAIL')
  assert.equal(selected('phase-4-gate', {...c, preflightResult: 'failure'}), false)
  assert.equal(selected('phase-4-gate', {...c, payload: pr(true)}), false)
  assert.equal(selected('phase-4-gate', {...c, payload: pr(false, ['stacked'])}), false)
  assert.equal(selected('phase-4-gate', {...c, payload: pr(false, [], 'outsider/platform')}), false)
  for (const event of ['merge_group', 'workflow_dispatch']) {
    assert.equal(selected('phase-4-gate', {...context(event, {}), preflightResult: 'skipped'}), true)
  }
})

test('planner CLI handles real Git docs, cross-boundary renames, deletions, empty and unreadable diffs', () => {
  const dir = mkdtempSync(resolve(tmpdir(), 'pr-plan-git-'))
  const git = (...args) => execFileSync('git', ['-C', dir, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
  const commit = paths => {
    if (paths.length) git('add', '--', ...paths)
    git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=', 'commit', '-qm', 'fixture change')
    return git('rev-parse', 'HEAD')
  }
  const event = resolve(dir, '.fixture-event.json')
  const output = resolve(dir, '.fixture-output.txt')
  const plan = (base, head) => {
    writeFileSync(event, JSON.stringify({pull_request: {base: {sha: base}, head: {sha: head}}}))
    writeFileSync(output, '')
    const run = spawnSync(process.execPath, [resolve(root, 'tooling/plan-pr-validation.mjs')], {
      cwd: dir, encoding: 'utf8', env: {...process.env, GITHUB_EVENT_PATH: event, GITHUB_OUTPUT: output},
    })
    assert.equal(run.status, 0, run.stderr)
    return {output: readFileSync(output, 'utf8'), stdout: run.stdout}
  }
  try {
    git('init', '-q')
    mkdirSync(resolve(dir, 'projections'), {recursive: true})
    mkdirSync(resolve(dir, 'tooling'), {recursive: true})
    writeFileSync(resolve(dir, 'CONTRIBUTING.md'), 'fixture guidance\n')
    writeFileSync(resolve(dir, 'projections/source.cs'), 'fixture source\n')
    writeFileSync(resolve(dir, 'projections/other.cs'), 'other fixture source\n')
    const base = commit(['CONTRIBUTING.md', 'projections/source.cs', 'projections/other.cs'])
    writeFileSync(resolve(dir, 'CONTRIBUTING.md'), 'updated fixture guidance\n')
    const docs = commit(['CONTRIBUTING.md'])
    assert.equal(plan(base, docs).output, 'headless-required=false\n')
    // The destination alone is allowlisted. The removed implementation path must still be seen.
    git('mv', 'projections/source.cs', 'tooling/verify-ci-lanes.mjs')
    const renamed = commit([]) // git mv already staged both sides.
    assert.equal(plan(docs, renamed).output, 'headless-required=true\n')
    git('rm', 'projections/other.cs')
    const deleted = commit([]) // git rm already staged the deletion.
    assert.equal(plan(renamed, deleted).output, 'headless-required=true\n')
    assert.equal(plan(deleted, deleted).output, 'headless-required=true\n')
    const unreadable = plan(deleted, 'ffffffffffffffffffffffffffffffffffffffff')
    assert.equal(unreadable.output, 'headless-required=true\n')
    assert.match(unreadable.stdout, /Unable to classify PR inputs; retaining headless validation/)
  } finally {
    rmSync(dir, {recursive: true, force: true})
  }
})

test('draft, stacked, fork and kill-switch policy cannot produce full proof', () => {
  const draft = context('pull_request', pr(true))
  assert.equal(selected('pr-preflight', draft), true)
  assert.equal(selected('verify', draft), true)
  assert.deepEqual(verifyCiLanes(draft, preliminary), {status: 'BLOCKED', scope: 'none', reason: 'draft PR: admission deferred; mark ready for review'})
  for (const isDraft of [false, true]) {
    const stacked = context('pull_request', pr(isDraft, ['stacked']))
    assert.equal(selected('pr-preflight', stacked), true)
    assert.equal(selected('verify', stacked), false)
    assert.equal(verifyCiLanes(stacked, full).status, 'BLOCKED')
  }
  const fork = context('pull_request', pr(false, [], 'outsider/platform'))
  assert.equal(selected('pr-preflight', fork), false)
  assert.equal(selected('verify', fork), true)
  assert.equal(verifyCiLanes(fork, preliminary).status, 'BLOCKED')
  assert.equal(verifyCiLanes(context('pull_request', {}), preliminary).status, 'BLOCKED')
  for (const event of ['pull_request', 'merge_group', 'workflow_dispatch']) {
    const c = context(event, event === 'pull_request' ? pr() : {}, 'false')
    for (const job of ['pr-preflight', 'phase-4-gate', 'gallery-shard', 'gallery-collect']) assert.equal(selected(job, c), false)
    assert.equal(verifyCiLanes(c, full).status, 'BLOCKED')
  }
  assert.equal(verifyCiLanes(context('push', {}), full).status, 'BLOCKED')
  assert.equal(verifyCiLanes(context('schedule', {}), full).status, 'BLOCKED')
})

test('each full lane must succeed; missing, red, cancelled, skipped or partial proof fails', () => {
  for (const event of ['merge_group', 'workflow_dispatch']) {
    for (const job of ['phase-4-gate', 'gallery-shard', 'gallery-collect']) {
      for (const result of ['failure', 'cancelled', 'skipped', 'timed_out', undefined]) {
        const lanes = {...full, [job]: result === undefined ? undefined : {result}}
        assert.equal(verifyCiLanes(context(event, {}), lanes).status, 'FAIL', `${event} ${job} ${result}`)
      }
    }
    assert.equal(verifyCiLanes(context(event, {}), preliminary).status, 'FAIL')
  }
  for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
    assert.equal(verifyCiLanes(context(), {...preliminary, 'pr-preflight': {result}}).status, 'FAIL')
  }
  assert.equal(verifyCiLanes(context(), full).status, 'FAIL')
  assert.equal(verifyCiLanes(context(), {...preliminary, unexpected: {result: 'success'}}).status, 'FAIL')
  assert.equal(verifyCiLanes(context(), null).status, 'FAIL')
})

test('workflow wiring preserves full coverage, guard order, permissions and source SHA', () => {
  assert.match(workflow, /needs: \[pr-preflight, phase-4-gate, gallery-shard, gallery-collect\]/)
  assert.match(block('pr-preflight'), /node tooling\/run-pr-preflight\.mjs/)
  assert.match(block('pr-preflight'), /headless-required: \$\{\{ steps.plan.outputs.headless-required \}\}/)
  assert.match(block('pr-preflight'), /id: plan\n\s+run: node tooling\/plan-pr-validation\.mjs/)
  assert.match(guard('phase-4-gate'), /always\(\)/)
  assert.match(block('phase-4-gate'), /node tooling\/run-phase-4-gate\.mjs/)
  assert.match(block('gallery-shard'), /fail-fast: false/)
  assert.match(block('gallery-shard'), /shard: \[1, 2, 3, 4, 5, 6\]/)
  assert.match(block('gallery-collect'), /needs: gallery-shard/)
  assert.match(block('gallery-collect'), /node tooling\/collect-gallery-shards\.mjs/)
  assert.match(block('verify'), /NEEDS: \$\{\{ toJSON\(needs\) \}\}/)
  assert.match(block('verify'), /ACTIONS_ENABLED: \$\{\{ vars.ACTIONS_ENABLED \}\}/)
  assert.match(block('verify'), /run: node tooling\/verify-ci-lanes\.mjs/)
  assert.ok(block('verify').indexOf('Admission guard') < block('verify').indexOf('actions/checkout@'))
  assert.match(block('verify'), /if \[ "\$HEAD_REPOSITORY" != "\$GITHUB_REPOSITORY" \]; then[\s\S]*?exit 1/)
  assert.match(block('verify'), /if \[ "\$DRAFT" != "false" \]; then[\s\S]*?exit 1/)
  assert.match(workflow, /permissions:\n  contents: read\n/)
  assert.doesNotMatch(block('pr-preflight'), /secrets\.|run-phase-4-gate|pnpm install|setup-dotnet/)
})

test('preflight refuses failed/unstarted checks and empty/skipped/cancelled test evidence', () => {
  const tap = '# tests 2\n# pass 2\n# fail 0\n# cancelled 0\n# skipped 0\n# todo 0\n'
  assert.equal(toolingTestsPassed({status: 0, stdout: tap}), true)
  for (const key of ['fail', 'cancelled', 'skipped', 'todo']) assert.equal(toolingTestsPassed({status: 0, stdout: tap.replace(`# ${key} 0`, `# ${key} 1`)}), false)
  assert.equal(toolingTestsPassed({status: 0, stdout: tap.replace('# tests 2\n# pass 2', '# tests 0\n# pass 0')}), false)
  assert.equal(toolingTestsPassed({status: 0, stdout: ''}), false)
  assert.equal(toolingTestsPassed({status: 1, stdout: tap}), false)
  const calls = []
  assert.equal(runPrPreflight(root, (command, args) => {
    calls.push([command, args])
    return {status: 0, stdout: tap}
  }), true)
  assert.equal(calls.length, 5)
  assert.deepEqual(calls[0], ['bash', ['eng/verify-boundaries.sh']])
  assert.deepEqual(calls[3][1], ['tooling/validate-repository.mjs', '--allow-stale-gate'])
  assert.ok(calls[4][1].includes('tooling/tests/ci-verification.test.mjs'))
  for (const status of [1, null]) {
    let count = 0
    assert.equal(runPrPreflight(root, () => { count++; return {status, stdout: ''} }), false)
    assert.equal(count, 1, 'stop at the first failed or unstarted check')
  }
  const empty = mkdtempSync(resolve(tmpdir(), 'missing-ci-tests-'))
  try {
    let count = 0
    assert.equal(runPrPreflight(empty, () => { count++; return {status: 0, stdout: tap} }), false)
    assert.equal(count, 4, 'missing declared tests cannot be hidden by Node test discovery')
  } finally {
    rmSync(empty, {recursive: true, force: true})
  }
})

test('CLI labels preliminary success, blocks a draft without a test-failure claim, and refuses malformed inputs', () => {
  const dir = mkdtempSync(resolve(tmpdir(), 'ci-verification-'))
  try {
    const payloadPath = resolve(dir, 'event.json')
    const summaryPath = resolve(dir, 'summary.md')
    const env = {...process.env, GITHUB_EVENT_NAME: 'pull_request', GITHUB_EVENT_PATH: payloadPath,
      GITHUB_REPOSITORY: repository, ACTIONS_ENABLED: 'true', NEEDS: JSON.stringify(preliminary),
      GITHUB_SHA: '0123456789012345678901234567890123456789', GITHUB_STEP_SUMMARY: summaryPath}
    const run = () => spawnSync(process.execPath, ['tooling/verify-ci-lanes.mjs'], {cwd: root, env, encoding: 'utf8'})
    writeFileSync(payloadPath, JSON.stringify(pr()))
    assert.equal(run().status, 0)
    assert.match(readFileSync(summaryPath, 'utf8'), /Preliminary PR validation passed.*Full landing proof awaits/)
    assert.match(readFileSync(summaryPath, 'utf8'), /0123456789012345678901234567890123456789/)
    writeFileSync(payloadPath, JSON.stringify(pr(true)))
    const draft = run()
    assert.equal(draft.status, 1)
    assert.match(draft.stdout, /BLOCKED: draft PR/)
    assert.doesNotMatch(draft.stdout, /test.*fail/i)
    writeFileSync(payloadPath, '{bad JSON')
    assert.equal(run().status, 1)
    env.GITHUB_OUTPUT = resolve(dir, 'output.txt')
    const fallback = spawnSync(process.execPath, ['tooling/plan-pr-validation.mjs'], {cwd: root, env, encoding: 'utf8'})
    assert.equal(fallback.status, 0)
    assert.equal(readFileSync(env.GITHUB_OUTPUT, 'utf8'), 'headless-required=true\n')
  } finally {
    rmSync(dir, {recursive: true, force: true})
  }
})
