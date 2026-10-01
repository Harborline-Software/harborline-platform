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
    'needs.pr-preflight.outputs.headless-required': 'headless' in c ? c.headless : 'false',
  }
  // Fixtures model a run that was not cancelled; a missing job output is GitHub's empty string.
  let expression = guard(job).replace(/always\(\)/g, 'true').replace(/cancelled\(\)/g, 'false')
    .replace(/contains\(github\.event\.pull_request\.labels\.\*\.name, 'stacked'\)/g,
      String(c.payload?.pull_request?.labels?.some(label => label.name === 'stacked') ?? false))
  for (const [name, value] of Object.entries(values).sort((a, b) => b[0].length - a[0].length)) {
    expression = expression.replaceAll(name, JSON.stringify(value) ?? 'undefined')
  }
  assert.doesNotMatch(expression, /github\.|vars\.|needs\.|contains|always|cancelled/, 'unsupported guard token')
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
  assert.equal(requiresPrHeadless(['CONTRIBUTING.md', 'tooling/tests/ci-verification.test.mjs']), false)
  // Wiring and policy changes always take the headless gate (owner 2026-10-01).
  for (const path of ['.github/workflows/verify.yml', '.github/workflows/validate.yml', 'tooling/plan-pr-validation.mjs',
    'tooling/run-pr-preflight.mjs', 'tooling/verify-ci-lanes.mjs', 'tooling/run-base-policy.mjs']) {
    assert.equal(requiresPrHeadless(['CONTRIBUTING.md', path]), true, path)
  }
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
  // A failed preflight or an unset plan must not stop the gate from starting; only an explicit `false` skips it.
  assert.equal(selected('phase-4-gate', {...c, preflightResult: 'failure'}), true)
  for (const headless of ['', 'garbage']) assert.equal(selected('phase-4-gate', {...context(), headless}), true, headless)
  assert.equal(selected('phase-4-gate', {...context(), headless: 'false'}), false)
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
  const plan = (...args) => spawnSync(process.execPath, [resolve(root, 'tooling/plan-pr-validation.mjs'), ...args],
    {cwd: dir, encoding: 'utf8'})
  const classified = (base, head) => {
    const run = plan(base, head)
    assert.equal(run.status, 0, run.stderr)
    return run.stdout
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
    assert.equal(classified(base, docs), 'false\n')
    // The destination alone is allowlisted. The removed implementation path must still be seen.
    git('mv', 'projections/source.cs', 'tooling/verify-ci-lanes.mjs')
    const renamed = commit([]) // git mv already staged both sides.
    assert.equal(classified(docs, renamed), 'true\n')
    git('rm', 'projections/other.cs')
    const deleted = commit([]) // git rm already staged the deletion.
    assert.equal(classified(renamed, deleted), 'true\n')
    assert.equal(classified(deleted, deleted), 'true\n')
    // An unreadable diff or malformed commits is a failure, never a classification.
    const unreadable = plan(deleted, 'ffffffffffffffffffffffffffffffffffffffff')
    assert.notEqual(unreadable.status, 0)
    assert.equal(unreadable.stdout, '')
    for (const args of [[], [deleted], ['main', deleted]]) {
      const malformed = plan(...args)
      assert.equal(malformed.status, 1, JSON.stringify(args))
      assert.equal(malformed.stdout, '')
    }
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
  assert.match(block('pr-preflight'), /ref: \$\{\{ github\.event\.pull_request\.base\.sha \}\}\n\s+path: \.base-policy\n\s+fetch-depth: 0\n\s+persist-credentials: false/)
  assert.match(block('pr-preflight'), /BASE_SHA: \$\{\{ github\.event\.pull_request\.base\.sha \}\}\n\s+HEAD_SHA: \$\{\{ github\.event\.pull_request\.head\.sha \}\}/)
  assert.match(block('pr-preflight'), /node tooling\/run-base-policy\.mjs plan/)
  assert.doesNotMatch(block('pr-preflight'), /node tooling\/plan-pr-validation\.mjs/, 'the candidate planner never classifies its own PR')
  assert.ok(block('pr-preflight').indexOf('run-base-policy.mjs plan') < block('pr-preflight').indexOf('run-pr-preflight.mjs'),
    'the plan is published before a preflight failure can stop the job')
  // The gate is expensive work: it stops on cancellation. Only the verify collector reports regardless.
  assert.match(guard('phase-4-gate'), /^!cancelled\(\) &&/)
  assert.doesNotMatch(guard('phase-4-gate'), /always\(\)/)
  assert.match(guard('verify'), /^always\(\) &&/)
  assert.match(block('phase-4-gate'), /node tooling\/run-phase-4-gate\.mjs/)
  assert.match(block('gallery-shard'), /fail-fast: false/)
  assert.match(block('gallery-shard'), /shard: \[1, 2, 3, 4, 5, 6\]/)
  assert.match(block('gallery-collect'), /needs: gallery-shard/)
  assert.match(block('gallery-collect'), /node tooling\/collect-gallery-shards\.mjs/)
  assert.match(block('verify'), /NEEDS: \$\{\{ toJSON\(needs\) \}\}/)
  assert.match(block('verify'), /ACTIONS_ENABLED: \$\{\{ vars.ACTIONS_ENABLED \}\}/)
  assert.match(block('verify'), /ref: \$\{\{ github\.event\.merge_group\.base_sha \}\}\n\s+path: \.base-policy\n\s+persist-credentials: false/)
  assert.match(block('verify'), /BASE_SHA: \$\{\{ github\.event\.merge_group\.base_sha \}\}/)
  assert.match(block('verify'), /if \[ "\$GITHUB_EVENT_NAME" = "merge_group" \]; then\n\s+node tooling\/run-base-policy\.mjs aggregate\n\s+else\n\s+node tooling\/verify-ci-lanes\.mjs/)
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
    const unclassified = spawnSync(process.execPath, ['tooling/plan-pr-validation.mjs'], {cwd: root, env, encoding: 'utf8'})
    assert.equal(unclassified.status, 1)
    assert.equal(unclassified.stdout, '')
  } finally {
    rmSync(dir, {recursive: true, force: true})
  }
})

// Base-sourced policy (owner 2026-10-01). Oracles: the owner's rulings (one validated plan output, headless on
// any doubt; fallback only for an absent base aggregator, with the three named lanes), literal fixture results,
// and real child processes for every planner and aggregator, never stubs of the wrapper's own decisions.
function policyRepo(files) {
  const dir = mkdtempSync(resolve(tmpdir(), 'base-policy-'))
  const git = (...args) => execFileSync('git', ['-C', dir, ...args], {encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
  git('init', '-q')
  for (const [file, text] of Object.entries({'README.md': 'base\n', ...files})) {
    mkdirSync(resolve(dir, file, '..'), {recursive: true})
    writeFileSync(resolve(dir, file), text)
  }
  git('add', '-A')
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=', 'commit', '-qm', 'base policy')
  return {dir, git, sha: git('rev-parse', 'HEAD')}
}
const wrapper = (mode, env) => spawnSync(process.execPath, [resolve(root, 'tooling/run-base-policy.mjs'), mode],
  {cwd: root, encoding: 'utf8', env: {...process.env, ...env}})
const planned = (policyRoot, env = {}) => {
  const output = resolve(mkdtempSync(resolve(tmpdir(), 'plan-output-')), 'output.txt')
  writeFileSync(output, '')
  const run = wrapper('plan', {BASE_POLICY_ROOT: policyRoot, BASE_SHA: 'a'.repeat(40), HEAD_SHA: 'b'.repeat(40), GITHUB_OUTPUT: output, ...env})
  assert.equal(run.status, 0, `the plan step never fails: ${run.stderr}`)
  const published = readFileSync(output, 'utf8')
  rmSync(resolve(output, '..'), {recursive: true, force: true})
  return published
}

// PROC-0001 (Control #936): ANY deletion or rename requires headless, even between allowed paths.
for (const operation of ['deletion', 'rename']) {
  test(`the base planner retains headless for an allowlisted ${operation}`, () => {
    const base = policyRepo({
      'tooling/plan-pr-validation.mjs': readFileSync(resolve(root, 'tooling/plan-pr-validation.mjs'), 'utf8'),
      'CONTRIBUTING.md': 'fixture guidance\n', 'tooling/tests/ci-load.test.mjs': 'fixture test\n',
    })
    try {
      writeFileSync(resolve(base.dir, 'CONTRIBUTING.md'), 'modified fixture guidance\n')
      const commit = () => {
        base.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=', 'commit', '-qam', 'fixture change')
        return base.git('rev-parse', 'HEAD')
      }
      const modified = commit()
      assert.equal(planned(base.dir, {BASE_SHA: base.sha, HEAD_SHA: modified}), 'headless-required=false\n',
        'ordinary allowlisted modification remains fast')
      if (operation === 'deletion') base.git('rm', 'CONTRIBUTING.md')
      else base.git('mv', 'tooling/tests/ci-load.test.mjs', 'tooling/tests/ci-verification.test.mjs')
      const changed = commit()
      assert.equal(planned(base.dir, {BASE_SHA: modified, HEAD_SHA: changed}), 'headless-required=true\n')
    } finally {
      rmSync(base.dir, {recursive: true, force: true})
    }
  })
}

test('the plan wrapper runs the base planner and publishes exactly one validated output', () => {
  const base = policyRepo({'tooling/plan-pr-validation.mjs': readFileSync(resolve(root, 'tooling/plan-pr-validation.mjs'), 'utf8'),
    'CONTRIBUTING.md': 'guidance\n', 'projections/source.cs': 'source\n'})
  try {
    const commit = (file, text) => {
      writeFileSync(resolve(base.dir, file), text)
      base.git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=', 'commit', '-qam', file)
      return base.git('rev-parse', 'HEAD')
    }
    const docs = commit('CONTRIBUTING.md', 'new guidance\n')
    assert.equal(planned(base.dir, {BASE_SHA: base.sha, HEAD_SHA: docs}), 'headless-required=false\n')
    const source = commit('projections/source.cs', 'changed source\n')
    assert.equal(planned(base.dir, {BASE_SHA: base.sha, HEAD_SHA: source}), 'headless-required=true\n')
  } finally {
    rmSync(base.dir, {recursive: true, force: true})
  }
})

test('every planner failure keeps the headless gate, and the planner cannot publish its own output', () => {
  const planners = {
    'non-zero exit': 'process.exit(3)',
    'syntax error': 'console.log(',
    'partial output': "console.log('fals')",
    'two answers': "console.log('true'); console.log('false')",
    'false then failure': "console.log('false'); process.exit(1)",
    'writes the workflow output itself': "import {appendFileSync} from 'node:fs'\nappendFileSync(process.env.GITHUB_OUTPUT ?? 'unset', 'headless-required=false\\n')\nconsole.log('true')",
  }
  for (const [name, source] of Object.entries(planners)) {
    const base = policyRepo({'tooling/plan-pr-validation.mjs': source})
    try {
      assert.equal(planned(base.dir), 'headless-required=true\n', name)
    } finally {
      rmSync(base.dir, {recursive: true, force: true})
    }
  }
  const clean = policyRepo({'tooling/plan-pr-validation.mjs': "console.log('false')"})
  const empty = policyRepo({})
  try {
    assert.equal(planned(clean.dir), 'headless-required=false\n', 'the one accepted skip: a clean, exact `false`')
    for (const [name, env] of Object.entries({'malformed base': {BASE_SHA: 'main'}, 'missing head': {HEAD_SHA: ''}})) {
      assert.equal(planned(clean.dir, env), 'headless-required=true\n', name)
    }
    assert.equal(planned(empty.dir), 'headless-required=true\n', 'a base commit without a planner')
    assert.equal(planned(resolve(empty.dir, 'absent')), 'headless-required=true\n', 'a missing base checkout')
    assert.equal(planned(''), 'headless-required=true\n', 'no base checkout configured')
  } finally {
    rmSync(clean.dir, {recursive: true, force: true})
    rmSync(empty.dir, {recursive: true, force: true})
  }
})

test('merge-group aggregation falls back to the named lanes only when the base commit has no aggregator', () => {
  const lanes = {'pr-preflight': {result: 'skipped'}, 'phase-4-gate': {result: 'success'}, 'gallery-shard': {result: 'success'}, 'gallery-collect': {result: 'success'}}
  const absent = policyRepo({})
  const aggregate = (policy, needs, env = {}) => wrapper('aggregate', {BASE_POLICY_ROOT: policy.dir, BASE_SHA: policy.sha,
    NEEDS: typeof needs === 'string' ? needs : JSON.stringify(needs), ...env})
  try {
    const bootstrap = aggregate(absent, lanes)
    assert.equal(bootstrap.status, 0, bootstrap.stdout)
    assert.match(bootstrap.stdout, /PASS \(bootstrap aggregator\)/)
    for (const lane of ['phase-4-gate', 'gallery-shard', 'gallery-collect']) {
      for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
        const run = aggregate(absent, {...lanes, [lane]: result === undefined ? undefined : {result}})
        assert.equal(run.status, 1, `${lane} ${result}`)
        assert.match(run.stdout, new RegExp(`${lane}: expected success, observed ${result ?? 'missing'}`))
      }
    }
    // A lane removed from `needs` is missing, not passing.
    const {['gallery-collect']: _, ...removed} = lanes
    assert.equal(aggregate(absent, removed).status, 1)
    assert.equal(aggregate(absent, '{bad JSON').status, 1)
    // Absence must be the base commit's, never a checkout that is missing or at another commit.
    assert.equal(aggregate({dir: absent.dir, sha: 'c'.repeat(40)}, lanes).status, 1)
    assert.equal(aggregate({dir: resolve(absent.dir, 'absent'), sha: absent.sha}, lanes).status, 1)
    assert.equal(aggregate({dir: absent.dir, sha: 'main'}, lanes).status, 1)
  } finally {
    rmSync(absent.dir, {recursive: true, force: true})
  }
})

test('an existing base aggregator decides; its failure or error blocks and never falls back', () => {
  const lanes = {'pr-preflight': {result: 'skipped'}, 'phase-4-gate': {result: 'success'}, 'gallery-shard': {result: 'success'}, 'gallery-collect': {result: 'success'}}
  const aggregators = {
    'rejects the evidence': 'process.exit(1)',
    'throws': "throw new Error('aggregator bug')",
    'syntax error': 'export const = 1',
  }
  for (const [name, source] of Object.entries(aggregators)) {
    const base = policyRepo({'tooling/verify-ci-lanes.mjs': source})
    try {
      // Every bootstrap lane succeeded, so a fallback would pass: the block proves no fallback happened.
      const run = wrapper('aggregate', {BASE_POLICY_ROOT: base.dir, BASE_SHA: base.sha, NEEDS: JSON.stringify(lanes)})
      assert.equal(run.status, 1, name)
      assert.match(run.stdout, /FAIL \(base aggregator\)/, name)
      assert.doesNotMatch(run.stdout, /bootstrap/, name)
    } finally {
      rmSync(base.dir, {recursive: true, force: true})
    }
  }
  // The real aggregator, sourced from the base commit, judges the merge group itself.
  const base = policyRepo({'tooling/verify-ci-lanes.mjs': readFileSync(resolve(root, 'tooling/verify-ci-lanes.mjs'), 'utf8')})
  try {
    const payload = resolve(base.dir, 'event.json')
    writeFileSync(payload, '{}')
    const env = {BASE_POLICY_ROOT: base.dir, BASE_SHA: base.sha, GITHUB_EVENT_NAME: 'merge_group', GITHUB_EVENT_PATH: payload,
      GITHUB_REPOSITORY: repository, ACTIONS_ENABLED: 'true', GITHUB_STEP_SUMMARY: ''}
    const passed = wrapper('aggregate', {...env, NEEDS: JSON.stringify(lanes)})
    assert.equal(passed.status, 0, passed.stdout)
    assert.match(passed.stdout, /PASS \(base aggregator\)/)
    const skipped = wrapper('aggregate', {...env, NEEDS: JSON.stringify({...lanes, 'gallery-shard': {result: 'skipped'}})})
    assert.equal(skipped.status, 1)
    assert.match(skipped.stdout, /gallery-shard: expected success, observed skipped/)
  } finally {
    rmSync(base.dir, {recursive: true, force: true})
  }
})
