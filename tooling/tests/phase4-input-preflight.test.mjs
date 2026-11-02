import assert from 'node:assert/strict'
import {execFileSync, spawnSync} from 'node:child_process'
import {copyFileSync, existsSync, mkdtempSync, mkdirSync, readFileSync, readdirSync, rmSync, symlinkSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'
import test from 'node:test'

import {assertPhase4SubjectUnchanged, observePhase4Forge, projectPhase4InputEvidence, prunePhase4InputAttempts, runPhase4InputAttempt} from '../phase4-input-preflight.mjs'
import {recordPhase4Gate} from '../gate-contract.mjs'

const AT = '2026-10-18T01:00:00.000Z'
const FETCHED = '2026-10-18T00:58:00.000Z'
const MAIN = 'refs/remotes/origin/main'
const git = (root, ...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8'}).trim()

function graph(t) {
  const root = mkdtempSync(path.join(tmpdir(), 'phase4-preflight-test-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  git(root, 'init', '-b', 'main')
  git(root, 'config', 'user.name', 'Phase4 fixture')
  git(root, 'config', 'user.email', 'phase4-fixture@example.invalid')
  git(root, 'config', 'commit.gpgsign', 'false')
  mkdirSync(path.join(root, '.git/empty-hooks'))
  git(root, 'config', 'core.hooksPath', path.join(root, '.git/empty-hooks'))
  mkdirSync(path.join(root, 'docs/evidence/design-review'), {recursive: true})
  mkdirSync(path.join(root, 'docs/evidence/phase-4'), {recursive: true})
  writeFileSync(path.join(root, 'seed'), 'initial\n')
  writeFileSync(path.join(root, 'docs/evidence/phase-4/gate.json'), 'canonical-full-pass\n')
  git(root, 'add', '.')
  git(root, 'commit', '-m', 'initial')
  const base = git(root, 'rev-parse', 'HEAD')
  const record = path.join(root, 'docs/evidence/design-review/current.json')
  writeFileSync(record, JSON.stringify({reference: {provenance: {kind: 'git-index-surface-v1', baseHead: base}}}))
  git(root, 'add', '.')
  git(root, 'commit', '-m', 'review')
  const main = git(root, 'rev-parse', 'HEAD')
  git(root, 'update-ref', MAIN, main)
  git(root, 'checkout', '-b', 'work')
  writeFileSync(path.join(root, 'seed'), 'work\n')
  git(root, 'add', '.')
  git(root, 'commit', '-m', 'work')
  const head = git(root, 'rev-parse', 'HEAD')
  const tree = git(root, 'write-tree')
  const historical = path.join(root, '.git/harborline-phase4-receipt.json')
  const consumed = path.join(root, '.git/receipt-consumed')
  writeFileSync(historical, 'historical-receipt\n')
  writeFileSync(consumed, 'consumed-marker\n')
  const childScript = path.join(root, '.git/exit-child.mjs')
  writeFileSync(childScript, 'process.exit(Number(process.argv[2]))\n')
  return {root, base, main, head, tree, record, historical, consumed, childScript}
}

function observed(f, {forgeMain = f.main, fetch = true, method = 'direct-forge-fetch', ...rest} = {}) {
  return {
    forgeMain,
    fetch: fetch ? {
      method,
      source: 'https://github.com/Harborline-Software/harborline-platform.git',
      mainRef: 'refs/remotes/origin/main',
      sha: f.main,
      startedAt: '2026-10-18T00:57:59.000Z',
      completedAt: FETCHED,
      fetchHeadEntry: `${f.main}\t\tbranch 'main' of https://github.com/Harborline-Software/harborline-platform\n`,
    } : null,
    ...rest,
  }
}

async function attempt(f, options = {}) {
  const calls = {allowance: 0, heavy: 0}
  const outcome = await runPhase4InputAttempt({
    repositoryRoot: f.root, kind: 'test', subject: {mode: 'current-index'},
    now: () => new Date(AT), observeForge: () => observed(f),
    launch: () => {
      calls.allowance++
      calls.heavy++
      const child = spawnSync(process.execPath, [f.childScript, '0'])
      return {exitCode: child.status}
    },
    ...options,
  })
  return {outcome, calls}
}

function unchangedHistory(f) {
  assert.equal(readFileSync(path.join(f.root, 'docs/evidence/phase-4/gate.json'), 'utf8'), 'canonical-full-pass\n')
  assert.equal(readFileSync(f.historical, 'utf8'), 'historical-receipt\n')
  assert.equal(readFileSync(f.consumed, 'utf8'), 'consumed-marker\n')
}

function refused(f, result, code) {
  assert.equal(result.outcome.exitCode, 1)
  assert.equal(result.outcome.launched, false)
  assert.deepEqual(result.calls, {allowance: 0, heavy: 0})
  assert.ok(result.outcome.preflight.issues.some(issue => issue.code === code), code)
  const persisted = JSON.parse(readFileSync(path.join(result.outcome.directory, 'result.json'), 'utf8'))
  assert.deepEqual(persisted.preflight, result.outcome.preflight)
  assert.equal(persisted.status, 'FAIL')
  unchangedHistory(f)
}

test('equal forge/tracking identities bind the real graph, known fetch age and every modern record', async t => {
  const f = graph(t)
  const {outcome, calls} = await attempt(f)
  assert.equal(outcome.exitCode, 0)
  assert.deepEqual(calls, {allowance: 1, heavy: 1})
  assert.equal(outcome.preflight.trackingToForge, 'equal')
  assert.equal(outcome.preflight.fetchAgeMs, 120000)
  assert.equal(outcome.preflight.forgeMain, f.main)
  assert.equal(outcome.preflight.inputs.head, f.head)
  assert.equal(outcome.preflight.inputs.trackingMain, f.main)
  assert.equal(outcome.preflight.inputs.mergeBase, f.main)
  assert.equal(outcome.preflight.inputs.subject.testedTree, f.tree)
  assert.deepEqual(outcome.preflight.inputs.reviews.map(({path: name, baseHead}) => [name, baseHead]),
    [['docs/evidence/design-review/current.json', f.base]])
  assert.deepEqual(outcome.preflight.issues, [])
  unchangedHistory(f)
})

test('ancestor tracking is reported as ancestor rather than substituted with the measured forge tip', async t => {
  const f = graph(t)
  const {outcome} = await attempt(f, {observeForge: () => observed(f, {forgeMain: f.head})})
  assert.equal(outcome.exitCode, 0)
  assert.equal(outcome.preflight.trackingToForge, 'ancestor')
  assert.equal(outcome.preflight.forgeMain, f.head)
  assert.equal(outcome.preflight.inputs.trackingMain, f.main)
  assert.equal(outcome.preflight.inputs.mergeBase, f.main)
})

for (const [name, prepare, authority, code] of [
  ['missing tracking', f => git(f.root, 'update-ref', '-d', MAIN), f => observed(f), 'tracking-main-missing'],
  ['missing HEAD', f => git(f.root, 'update-ref', '-d', 'HEAD'), f => observed(f), 'head-missing'],
  ['unrelated tracking', f => {
    const other = git(f.root, 'commit-tree', f.tree, '-m', 'unrelated')
    git(f.root, 'update-ref', MAIN, other)
  }, f => observed(f), 'tracking-main-unrelated-to-forge'],
  ['HEAD behind main', f => git(f.root, 'reset', '--soft', f.base), f => observed(f), 'head-main-divergence'],
  ['HEAD/main divergence', f => {
    const other = git(f.root, 'commit-tree', f.tree, '-p', f.base, '-m', 'other branch')
    git(f.root, 'update-ref', MAIN, other)
    f.other = other
  }, f => ({...observed(f), forgeMain: f.other}), 'head-main-divergence'],
  ['unknown fetch history', () => {}, f => observed(f, {fetch: false}), 'main-fetch-history-unknown'],
  ['unrelated fetch entry', () => {}, f => {
    const value = observed(f)
    value.fetch.fetchHeadEntry = `${f.main}\t\tbranch 'topic' of a parent\n`
    return value
  }, 'main-fetch-history-unknown'],
  ['copied parent differs from forge', () => {}, f => observed(f, {method: 'verified-parent-fallback', forgeMain: f.head}), 'parent-fallback-forge-mismatch'],
  ['forge lookup failed', () => {}, () => { throw new Error('controlled forge lookup failure') }, 'forge-lookup-failed'],
]) {
  test(`${name} refuses before any allowance or heavy callback`, async t => {
    const f = graph(t)
    prepare(f)
    refused(f, await attempt(f, {observeForge: () => authority(f)}), code)
  })
}

test('a corroborated parent fallback with exact forge identity remains usable', async t => {
  const f = graph(t)
  const {outcome} = await attempt(f, {observeForge: () => observed(f, {method: 'verified-parent-fallback'})})
  assert.equal(outcome.exitCode, 0)
  assert.equal(outcome.preflight.fetch.method, 'verified-parent-fallback')
  assert.equal(outcome.preflight.forgeMain, f.main)
})

for (const [name, mutate] of [
  ['tracking ref', f => git(f.root, 'update-ref', MAIN, f.head)],
  ['HEAD', f => git(f.root, 'reset', '--soft', f.main)],
  ['staged tree', f => {
    writeFileSync(path.join(f.root, 'seed'), 'staged after preflight\n')
    git(f.root, 'add', 'seed')
  }],
  ['review census', f => {
    writeFileSync(path.join(f.root, 'docs/evidence/design-review/added.json'),
      JSON.stringify({reference: {provenance: {kind: 'git-index-surface-v1', baseHead: f.base}}}))
    git(f.root, 'add', '.')
  }],
]) {
  test(`${name} changes after preflight cannot reach the launch callback`, async t => {
    const f = graph(t)
    const result = await attempt(f, {beforeLaunch: () => mutate(f)})
    assert.equal(result.outcome.error.code, 'launch-inputs-changed')
    assert.deepEqual(result.calls, {allowance: 0, heavy: 0})
    assert.equal(result.outcome.exitCode, 1)
    const saved = JSON.parse(readFileSync(path.join(result.outcome.directory, 'result.json'), 'utf8'))
    assert.equal(saved.preflight.inputs.head, f.head)
    assert.equal(saved.preflight.inputs.trackingMain, f.main)
    assert.equal(saved.preflight.fetchAgeMs, 120000)
    unchangedHistory(f)
  })
}

for (const [name, raw, code] of [
  ['missing base', JSON.stringify({reference: {provenance: {kind: 'git-index-surface-v1', baseHead: '1'.repeat(40)}}}), 'review-base-missing'],
  ['unrelated base', null, 'review-base-ancestry'],
  ['malformed candidate JSON', '{', 'review-record-unreadable'],
  ['nonobject candidate JSON', 'null', 'review-record-unreadable'],
]) {
  test(`${name} is retained as a preflight refusal`, async t => {
    const f = graph(t)
    const base = raw ?? JSON.stringify({reference: {provenance: {
      kind: 'git-index-surface-v1', baseHead: git(f.root, 'commit-tree', f.tree, '-m', 'unrelated review'),
    }}})
    writeFileSync(f.record, base)
    git(f.root, 'add', '.')
    refused(f, await attempt(f), code)
  })
}

test('the census reads the actual staged subject, not an unstaged working copy', async t => {
  const f = graph(t)
  writeFileSync(f.record, '{')
  const {outcome} = await attempt(f, {subject: {mode: 'exact-staged-tree'}})
  assert.equal(outcome.exitCode, 0)
  assert.equal(outcome.preflight.inputs.reviews.length, 1)
  assert.equal(outcome.preflight.inputs.reviews[0].baseHead, f.base)
})

test('receipt-child binding rechecks a real staged worktree without refetching or losing its observations', async t => {
  const f = graph(t)
  writeFileSync(path.join(f.root, 'staged-only'), 'staged\n')
  git(f.root, 'add', '.')
  const childRoot = path.join(f.root, 'tested-worktree')
  let child
  const parent = await runPhase4InputAttempt({
    repositoryRoot: f.root, kind: 'receipt', subject: {mode: 'exact-staged-tree'},
    now: () => new Date(AT), observeForge: () => observed(f),
    launch: async context => {
      git(f.root, 'worktree', 'add', '--detach', childRoot, f.head)
      try {
        git(childRoot, 'read-tree', '--reset', '-u', context.preflight.inputs.subject.testedTree)
        context.recheck()
        child = await runPhase4InputAttempt({
          repositoryRoot: childRoot, kind: 'gate', inherited: context.binding,
          subject: context.preflight.inputs.subject, now: () => new Date(AT),
          observeForge: () => { throw new Error('the child must never refetch') },
          launch: () => ({exitCode: spawnSync(process.execPath, [f.childScript, '0']).status}),
        })
        return {exitCode: child.exitCode}
      } finally {
        git(f.root, 'worktree', 'remove', '--force', childRoot)
      }
    },
  })
  assert.equal(parent.exitCode, 0)
  assert.equal(child.exitCode, 0)
  assert.equal(child.preflight.parentPreflightId, parent.preflight.preflightId)
  assert.deepEqual(child.preflight.inputs.subject, parent.preflight.inputs.subject)
  assert.equal(child.preflight.forgeMain, f.main)
  assert.ok(readFileSync(path.join(child.directory, 'result.json')).length > 0)
  unchangedHistory(f)
})

test('distinct refusal, child failure, success and headless observations preserve canonical/history bytes', async t => {
  const f = graph(t)
  const attempts = []
  attempts.push((await attempt(f, {observeForge: () => observed(f, {fetch: false})})).outcome)
  for (const [exit, headless] of [[7, false], [0, false], [0, true]]) {
    attempts.push((await attempt(f, {launch: () => {
      const child = spawnSync(process.execPath, [f.childScript, String(exit)])
      if (child.status !== 0 || headless)
        assert.equal(recordPhase4Gate(path.join(f.root, 'docs/evidence/phase-4/gate.json'),
          {status: child.status === 0 ? 'PASS' : 'FAIL'}, {headless}), false)
      return {exitCode: child.status, headless}
    }})).outcome)
  }
  assert.deepEqual(attempts.map(value => value.exitCode), [1, 7, 0, 0])
  assert.equal(new Set(attempts.map(value => value.directory)).size, 4)
  const original = readFileSync(path.join(attempts[0].directory, 'preflight.json'))
  for (const value of attempts) {
    const persisted = JSON.parse(readFileSync(path.join(value.directory, 'result.json'), 'utf8'))
    assert.equal(persisted.exitCode, value.exitCode)
    assert.equal(persisted.preflight.inputs.head, f.head)
    assert.equal(persisted.preflight.inputs.trackingMain, f.main)
  }
  assert.deepEqual(readFileSync(path.join(attempts[0].directory, 'preflight.json')), original)
  assert.throws(() => writeFileSync(path.join(attempts[0].directory, 'preflight.json'), 'replacement', {flag: 'wx'}), {code: 'EEXIST'})
  assert.equal(readdirSync(path.join(f.root, '.git/harborline-phase4-inputs')).length, 4)
  unchangedHistory(f)
})

test('direct gate checks working review JSON even when its staged version is valid', async t => {
  const f = graph(t)
  writeFileSync(f.record, '{')
  refused(f, await attempt(f), 'review-record-unreadable')
})

test('known fetch age is reported without inventing an expiry ceiling', async t => {
  const f = graph(t)
  const authority = observed(f)
  authority.fetch.startedAt = '2025-10-18T00:59:59.000Z'
  authority.fetch.completedAt = '2025-10-18T01:00:00.000Z'
  const {outcome} = await attempt(f, {observeForge: () => authority})
  assert.equal(outcome.exitCode, 0)
  assert.equal(outcome.preflight.fetchAgeMs, 31536000000)
})

for (const [mode, apiValue, expectedExit, expectedCode] of [
  ['direct', undefined, 0, undefined],
  ['fallback', 'same', 0, undefined],
  ['fallback', 'different', 1, 'parent-fallback-forge-mismatch'],
  ['fallback', 'failure', 1, 'forge-lookup-failed'],
]) {
  test(`actual forge command adapter with controlled local Git: ${mode}/${apiValue ?? 'fetch'}`, async t => {
    const f = graph(t)
    const remote = path.join(f.root, '.git/local-forge.git')
    git(f.root, 'clone', '--bare', '--no-hardlinks', f.root, remote)
    git(f.root, 'remote', 'add', 'origin', remote)
    const commands = []
    const execute = (command, args, options) => {
      commands.push([command, ...args])
      if (command === 'gh') {
        assert.deepEqual(args, ['api', 'repos/Harborline-Software/harborline-platform/commits/main', '--jq', '.sha'])
        if (apiValue === 'failure') throw new Error('controlled API lookup failure')
        return apiValue === 'different' ? f.head : f.main
      }
      assert.equal(command, 'git')
      if (args[3] === 'https://github.com/Harborline-Software/harborline-platform.git') {
        if (mode === 'fallback') throw new Error('controlled direct fetch failure')
        const local = [...args]
        local[3] = remote
        return execFileSync(command, local, options)
      }
      return execFileSync(command, args, options)
    }
    const result = await attempt(f, {
      observeForge: root => observePhase4Forge(root, {now: () => new Date(AT), execute}),
    })
    assert.equal(result.outcome.exitCode, expectedExit)
    assert.deepEqual(commands[0], ['git', 'fetch', '--no-tags', '--no-recurse-submodules',
      'https://github.com/Harborline-Software/harborline-platform.git',
      '+refs/heads/main:refs/remotes/origin/main'])
    assert.equal(result.outcome.preflight.fetch.sha, f.main)
    assert.equal(result.outcome.preflight.fetchAgeMs, 0)
    assert.ok(result.outcome.preflight.fetch.fetchHeadEntry.includes("branch 'main' of "))
    if (expectedCode) refused(f, result, expectedCode)
    else assert.deepEqual(result.calls, {allowance: 1, heavy: 1})
    unchangedHistory(f)
  })
}

test('every modern record is inspected, including a newly added invalid base', async t => {
  const f = graph(t)
  writeFileSync(path.join(f.root, 'docs/evidence/design-review/another.json'),
    JSON.stringify({reference: {provenance: {kind: 'git-index-surface-v1', baseHead: '2'.repeat(40)}}}))
  git(f.root, 'add', '.')
  const result = await attempt(f)
  refused(f, result, 'review-base-missing')
  assert.equal(result.outcome.preflight.inputs.reviews.length, 2)
  assert.ok(result.outcome.preflight.issues.some(issue =>
    issue.path === 'docs/evidence/design-review/another.json' && issue.baseHead === '2'.repeat(40)))
})

test('a working JSON candidate replaced by a directory is not silently omitted', async t => {
  const f = graph(t)
  rmSync(f.record)
  mkdirSync(f.record)
  refused(f, await attempt(f), 'review-record-unreadable')
})

test('a caller cannot substitute a different tested subject', async t => {
  const f = graph(t)
  refused(f, await attempt(f, {subject: {mode: 'current-index', baseHead: f.base, testedTree: f.tree}}),
    'tested-subject-mismatch')
})

test('a changed inherited ref refuses without refreshing authority and retains the parent measurement', async t => {
  const f = graph(t)
  let binding
  const first = await attempt(f, {launch: context => {
    binding = context.binding
    return {exitCode: 0}
  }})
  git(f.root, 'update-ref', MAIN, f.head)
  let lookups = 0
  const second = await attempt(f, {inherited: binding, observeForge: () => {
    lookups++
    throw new Error('inherited attempts must not refresh')
  }})
  refused(f, second, 'launch-inputs-changed')
  assert.equal(lookups, 0)
  assert.equal(second.outcome.preflight.parentMeasurement.inputs.head, f.head)
  assert.equal(second.outcome.preflight.parentMeasurement.inputs.trackingMain, f.main)
  assert.equal(second.outcome.preflight.inputs.trackingMain, f.head)
  assert.equal(second.outcome.preflight.parentPreflightId, first.outcome.preflight.preflightId)
})

test('an invalid inherited binding is refused without a forge lookup or launch', async t => {
  const f = graph(t)
  let lookups = 0
  const result = await attempt(f, {inherited: {}, observeForge: () => {
    lookups++
    throw new Error('no refresh on invalid parent input')
  }})
  refused(f, result, 'inherited-preflight-invalid')
  assert.equal(lookups, 0)
})

for (const exitCode of [0, 7]) test(`tracking movement after launch keeps the completed subject and child exit ${exitCode}`, async t => {
  const f = graph(t)
  const {outcome} = await attempt(f, {launch: context => {
    context.recheck()
    git(f.root, 'update-ref', MAIN, f.head)
    assertPhase4SubjectUnchanged(f.root, {head: f.head, testedTree: f.tree})
    return {exitCode}
  }})
  assert.equal(outcome.exitCode, exitCode)
  assert.equal(outcome.error, undefined)
  assert.equal(outcome.observation.result.exitCode, exitCode)
  assert.equal(outcome.preflight.inputs.trackingMain, f.main)
})

for (const [name, mutate] of [
  ['HEAD', f => git(f.root, 'reset', '--soft', f.main)],
  ['staged tree', f => {
    writeFileSync(path.join(f.root, 'seed'), 'changed after launch\n')
    git(f.root, 'add', 'seed')
  }],
]) {
  test(`final subject check refuses changed ${name} before evidence is written`, async t => {
    const f = graph(t)
    let evidenceWrites = 0
    const {outcome} = await attempt(f, {launch: () => {
      mutate(f)
      assertPhase4SubjectUnchanged(f.root, {head: f.head, testedTree: f.tree})
      evidenceWrites++
      return {exitCode: 0}
    }})
    assert.equal(outcome.exitCode, 1)
    assert.equal(evidenceWrites, 0)
    assert.equal(outcome.error.message, 'Platform HEAD or staged tree changed while the phase-4 gate was running')
    unchangedHistory(f)
  })
}

test('committed input evidence uses a literal safe projection without private sentinels', () => {
  const privateText = 'PRIVATE-SENTINEL C:\\Users\\operator\\private /home/operator/private'
  const projected = JSON.parse(JSON.stringify(projectPhase4InputEvidence({
    schemaVersion: 999,
    attemptId: '10000000-0000-4000-8000-000000000001',
    preflightId: '20000000-0000-4000-8000-000000000001',
    parentPreflightId: '20000000-0000-4000-8000-000000000002',
    kind: 'gate', measuredAt: '2026-10-18T01:00:00.000Z', status: 'PASS',
    forgeMain: '1111111111111111111111111111111111111111',
    directForgeFailure: privateText, authorityFailure: privateText, recordPath: privateText,
    parentMeasurement: {inputs: privateText, fetch: privateText}, extra: privateText,
    inputs: {
      head: '2222222222222222222222222222222222222222',
      trackingMain: '1111111111111111111111111111111111111111',
      testedTree: '3333333333333333333333333333333333333333',
      mergeBase: '1111111111111111111111111111111111111111',
      subject: {baseHead: '2222222222222222222222222222222222222222',
        testedTree: '3333333333333333333333333333333333333333', mode: 'exact-staged-tree', extra: privateText},
      reviews: [{path: 'docs/evidence/design-review/current.json', blob: '4444444444444444444444444444444444444444',
        baseHead: '1111111111111111111111111111111111111111', detail: privateText}],
      reviewCandidates: [], censusIssues: [{detail: privateText}], extra: privateText,
    },
    fetch: {method: 'verified-parent-fallback', mainRef: 'refs/remotes/origin/main',
      sha: '1111111111111111111111111111111111111111', startedAt: '2026-10-18T00:57:59.000Z',
      completedAt: '2026-10-18T00:58:00.000Z', source: privateText, fetchHeadEntry: privateText, error: privateText},
    issues: [{code: 'review-record-unreadable', path: 'docs/evidence/design-review/broken.json', detail: privateText},
      {code: privateText, path: privateText}],
  })))
  assert.deepEqual(projected, {
    schemaVersion: 1,
    attemptId: '10000000-0000-4000-8000-000000000001',
    preflightId: '20000000-0000-4000-8000-000000000001',
    parentPreflightId: '20000000-0000-4000-8000-000000000002',
    kind: 'gate', measuredAt: '2026-10-18T01:00:00.000Z', status: 'PASS',
    forgeMain: '1111111111111111111111111111111111111111',
    inputs: {
      head: '2222222222222222222222222222222222222222',
      trackingMain: '1111111111111111111111111111111111111111',
      testedTree: '3333333333333333333333333333333333333333',
      mergeBase: '1111111111111111111111111111111111111111',
      subject: {baseHead: '2222222222222222222222222222222222222222',
        testedTree: '3333333333333333333333333333333333333333', mode: 'exact-staged-tree'},
      reviews: [{path: 'docs/evidence/design-review/current.json', blob: '4444444444444444444444444444444444444444',
        baseHead: '1111111111111111111111111111111111111111'}], reviewCandidates: [],
    },
    fetch: {method: 'verified-parent-fallback', mainRef: 'refs/remotes/origin/main',
      sha: '1111111111111111111111111111111111111111', startedAt: '2026-10-18T00:57:59.000Z', completedAt: '2026-10-18T00:58:00.000Z'},
    issues: [{code: 'review-record-unreadable', path: 'docs/evidence/design-review/broken.json'}],
  })
  assert.doesNotMatch(JSON.stringify(projected), /PRIVATE-SENTINEL|parentMeasurement|fetchHeadEntry|source|Failure|detail/)
})

for (const unsafe of ['/home/operator/review.json', 'C:/Users/operator/review.json',
  '\\\\server\\review.json', '../review.json', 'docs/../review.json', 'docs\\review.json',
  'docs//review.json', 'docs/review\n.json']) {
  test(`evidence projection omits unsafe path ${JSON.stringify(unsafe)} and unknown vocabulary`, () => {
    const projected = JSON.parse(JSON.stringify(projectPhase4InputEvidence({
      attemptId: unsafe, preflightId: unsafe, parentPreflightId: unsafe, kind: unsafe, status: unsafe,
      forgeMain: unsafe, measuredAt: unsafe,
      inputs: {head: unsafe, subject: {mode: unsafe}, reviews: [{path: unsafe, blob: unsafe, baseHead: unsafe}]},
      fetch: {method: unsafe, mainRef: unsafe, sha: unsafe, startedAt: unsafe, completedAt: unsafe},
      issues: [{code: 'review-base-missing', path: unsafe, blob: unsafe, baseHead: unsafe}, {code: unsafe}],
    })))
    assert.deepEqual(projected, {
      schemaVersion: 1, inputs: {subject: {}, reviews: [{}], reviewCandidates: []}, fetch: {},
      issues: [{code: 'review-base-missing'}],
    })
  })
}

function history(t) {
  const scratch = mkdtempSync(path.join(tmpdir(), 'phase4-history-test-'))
  const root = path.join(scratch, 'harborline-phase4-inputs')
  mkdirSync(root)
  t.after(() => rmSync(scratch, {recursive: true, force: true}))
  return root
}

function completed(root, number, {parent, incomplete = false} = {}) {
  const attemptId = `10000000-0000-4000-8000-${String(number).padStart(12, '0')}`
  const preflightId = `20000000-0000-4000-8000-${String(number).padStart(12, '0')}`
  const directory = path.join(root, attemptId)
  mkdirSync(directory)
  const preflight = {schemaVersion: 1, attemptId, preflightId, kind: parent ? 'gate' : 'receipt', status: 'PASS',
    ...(parent ? {parentPreflightId: parent.preflightId} : {})}
  writeFileSync(path.join(directory, 'started.json'), JSON.stringify({schemaVersion: 1, attemptId,
    kind: preflight.kind, parentPreflightId: parent?.preflightId ?? null}))
  writeFileSync(path.join(directory, 'preflight.json'), JSON.stringify(preflight))
  writeFileSync(path.join(directory, 'launch.json'), JSON.stringify({attemptId, preflightId}))
  if (!incomplete) writeFileSync(path.join(directory, 'result.json'), JSON.stringify({schemaVersion: 1,
    attemptId, preflightId, preflight, finishedAt: new Date(Date.UTC(2020, 0, 1, 0, 0, number)).toISOString(),
    launched: true, exitCode: 0, status: 'PASS'}))
  return {attemptId, preflightId, directory}
}

test('completed retention keeps the newest 32 including the current returned observation', t => {
  const root = history(t)
  const records = Array.from({length: 35}, (_, i) => completed(root, i + 1))
  prunePhase4InputAttempts(root, records[34].attemptId)
  assert.equal(readdirSync(root).length, 32)
  for (const record of records.slice(0, 3)) assert.equal(existsSync(record.directory), false)
  for (const record of records.slice(3)) assert.equal(existsSync(record.directory), true)
})

test('retention keeps a boundary family whole and prunes an older complete family whole', t => {
  const root = history(t)
  const oldest = completed(root, 1)
  const oldestChild = completed(root, 2, {parent: oldest})
  const boundary = completed(root, 3)
  const boundaryChild = completed(root, 4, {parent: boundary})
  for (let i = 5; i <= 35; i++) completed(root, i)
  prunePhase4InputAttempts(root)
  assert.equal(readdirSync(root).length, 33)
  for (const record of [oldest, oldestChild]) assert.equal(existsSync(record.directory), false)
  for (const record of [boundary, boundaryChild]) assert.equal(existsSync(record.directory), true)
})

test('retention protects whole incomplete families, inherited bindings and malformed identified records', t => {
  const root = history(t)
  const active = completed(root, 1, {incomplete: true})
  const completedChild = completed(root, 2, {parent: active})
  const completedParent = completed(root, 3)
  const activeChild = completed(root, 4, {parent: completedParent, incomplete: true})
  const inheritedParent = completed(root, 5)
  const inheritedChild = completed(root, 6, {parent: inheritedParent})
  const malformed = completed(root, 7)
  const malformedChild = completed(root, 8, {parent: malformed})
  writeFileSync(path.join(malformed.directory, 'result.json'), '{')
  mkdirSync(path.join(root, 'unknown-entry'))
  const expired = completed(root, 9)
  for (let i = 10; i <= 41; i++) completed(root, i)
  prunePhase4InputAttempts(root, undefined, {recordPath: path.join(inheritedChild.directory, 'preflight.json')})
  assert.equal(existsSync(expired.directory), false)
  for (const record of [active, completedChild, completedParent, activeChild, inheritedParent, inheritedChild, malformed, malformedChild])
    assert.equal(existsSync(record.directory), true)
  assert.equal(readFileSync(path.join(malformed.directory, 'result.json'), 'utf8'), '{')
  assert.equal(existsSync(path.join(root, 'unknown-entry')), true)
})

test('an unresolved parent and ambiguous duplicate identities preserve their families', t => {
  const root = history(t)
  const unresolved = completed(root, 1, {parent: {preflightId: '20000000-0000-4000-8000-999999999999'}})
  const parent = completed(root, 2)
  const child = completed(root, 3, {parent})
  const duplicate = completed(root, 4)
  for (const file of ['preflight.json', 'launch.json', 'result.json']) {
    const original = readFileSync(path.join(duplicate.directory, file), 'utf8')
    writeFileSync(path.join(duplicate.directory, file), original.replaceAll(duplicate.preflightId, parent.preflightId))
  }
  const expired = completed(root, 5)
  for (let i = 6; i <= 37; i++) completed(root, i)
  prunePhase4InputAttempts(root)
  assert.equal(existsSync(expired.directory), false)
  for (const record of [unresolved, parent, child, duplicate]) assert.equal(existsSync(record.directory), true)
})

test('unidentifiable legacy records conservatively defer pruning', t => {
  const root = history(t)
  const records = Array.from({length: 34}, (_, i) => completed(root, i + 1))
  const unknown = path.join(root, '10000000-0000-4000-8000-999999999999')
  mkdirSync(unknown)
  writeFileSync(path.join(unknown, 'started.json'), '{')
  prunePhase4InputAttempts(root)
  assert.equal(readdirSync(root).length, 35)
  for (const record of records) assert.equal(existsSync(record.directory), true)
})

test('retention never follows a UUID directory symlink', t => {
  const root = history(t)
  const outside = path.join(path.dirname(root), 'outside')
  mkdirSync(outside)
  writeFileSync(path.join(outside, 'sentinel'), 'outside bytes\n')
  for (let i = 1; i <= 34; i++) completed(root, i)
  const link = path.join(root, '10000000-0000-4000-8000-999999999999')
  symlinkSync(outside, link, process.platform === 'win32' ? 'junction' : 'dir')
  prunePhase4InputAttempts(root)
  assert.equal(readFileSync(path.join(outside, 'sentinel'), 'utf8'), 'outside bytes\n')
  assert.equal(readdirSync(root).length, 35)
})

test('the real attempt applies completed retention without changing canonical or historical evidence', async t => {
  const f = graph(t)
  const root = path.join(f.root, '.git/harborline-phase4-inputs')
  mkdirSync(root)
  const old = Array.from({length: 34}, (_, i) => completed(root, i + 1))
  const {outcome} = await attempt(f)
  assert.equal(outcome.exitCode, 0)
  assert.equal(readdirSync(root).length, 32)
  assert.equal(existsSync(outcome.directory), true)
  for (const record of old.slice(0, 3)) assert.equal(existsSync(record.directory), false)
  unchangedHistory(f)
})

test('real gate preflight refusal keeps observations off the receipt report channel', t => {
  const f = graph(t)
  const remote = path.join(f.root, '.git/local-forge.git')
  git(f.root, 'clone', '--bare', '--no-hardlinks', f.root, remote)
  git(f.root, 'config', `url.${remote.replaceAll('\\', '/')}.insteadOf`,
    'https://github.com/Harborline-Software/harborline-platform.git')
  for (const name of [
    'run-phase-4-gate.mjs', 'phase4-input-preflight.mjs', 'gate-step-evidence.mjs',
    'resolve-appshell-feed.mjs', 'resolve-command.mjs', 'resolve-dotnet.mjs', 'gate-contract.mjs',
    'phase4-gate-lock.mjs', 'phase4-step-runner.mjs', 'gates/design-review-status.mjs',
    'shared-module-scheduler.mjs', 'module-catalog.mjs',
  ]) {
    const target = path.join(f.root, 'tooling', name)
    mkdirSync(path.dirname(target), {recursive: true})
    copyFileSync(path.resolve(import.meta.dirname, '..', name), target)
  }
  writeFileSync(f.record, '{broken review JSON\n')
  const result = spawnSync(process.execPath, [path.join(f.root, 'tooling/run-phase-4-gate.mjs')], {
    cwd: f.root, encoding: 'utf8', timeout: 10000,
    env: {...process.env, GITHUB_ACTIONS: 'false', GIT_ALLOW_PROTOCOL: 'file'},
  })
  assert.equal(result.status, 1)
  assert.equal(result.signal, null)
  assert.match(result.stderr, /^Phase 4 input preflight refused launch\r?\n/)
  const observationRoot = path.join(f.root, '.git/harborline-phase4-inputs')
  const attempts = readdirSync(observationRoot)
  assert.equal(attempts.length, 1)
  const directory = path.join(observationRoot, attempts[0])
  const persisted = JSON.parse(readFileSync(path.join(directory, 'result.json'), 'utf8'))
  assert.equal(persisted.launched, false)
  assert.equal(persisted.preflight.fetch.method, 'direct-forge-fetch')
  assert.equal(persisted.preflight.fetch.sha, f.main)
  assert.ok(persisted.preflight.issues.some(issue => issue.code === 'review-record-unreadable'))
  assert.equal(existsSync(path.join(directory, 'launch.json')), false)
  unchangedHistory(f)
  assert.equal(result.stdout, '')
  assert.deepEqual(JSON.parse(result.stderr.slice(result.stderr.indexOf('\n') + 1)), persisted)
})

for (const actions of ['true', 'false']) {
  test(`real gate refuses --only before IPC, lock and preflight with GITHUB_ACTIONS=${actions}`, () => {
    const gate = path.resolve(import.meta.dirname, '../run-phase-4-gate.mjs')
    const result = spawnSync(process.execPath, [gate, '--only', '--phase4-lock-reentry'], {
      encoding: 'utf8', timeout: 10000, env: {...process.env, GITHUB_ACTIONS: actions},
    })
    assert.equal(result.status, 1)
    assert.match(result.stderr, /phase-4 gate refuses --only; package evidence must cover every fixture/)
    assert.doesNotMatch(result.stderr, /re-entry requires|timed out waiting|input preflight/)
  })
}

test('Actions receipt reentry without IPC cannot fall through to the direct gate', () => {
  const gate = path.resolve(import.meta.dirname, '../run-phase-4-gate.mjs')
  const result = spawnSync(process.execPath, [gate, '--phase4-lock-reentry'], {
    encoding: 'utf8', timeout: 10000, env: {...process.env, GITHUB_ACTIONS: 'true'},
  })
  assert.equal(result.status, 1)
  assert.match(result.stderr, /phase-4 gate lock: re-entry requires the receipt parent IPC channel/)
  assert.equal(result.stdout, '')
})
