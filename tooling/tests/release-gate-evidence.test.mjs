// Oracles: T-1138 release.shared-signer-policy; ADR-0198 decision 5.
import assert from 'node:assert/strict'
import {mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join} from 'node:path'
import {spawnSync} from 'node:child_process'
import test from 'node:test'
import * as evidence from '../gates/scan-evidence-provenance.mjs'

const workflow = name => readFileSync(new URL(`../../.github/workflows/${name}.yml`, import.meta.url), 'utf8')
const job = (text, name) => text.split(`\n  ${name}:\n`)[1]?.split(/\n  [a-z][a-z0-9-]*:\n/)[0]

test('release.main-required-computations: reusable SBOM scans data with fixed main configuration and retains byte digests', () => {
  const sbom = workflow('license-sbom')
  assert.match(sbom, /workflow_call:/)
  assert.match(sbom, /tested-commit:/)
  assert.match(sbom, /syft-version: v1\.57\.2/)
  assert.match(sbom, /config: \$\{\{ runner.temp \}\}\/syft.yaml/)
  assert.match(sbom, /path: subject/)
  assert.match(sbom, /persist-credentials: false/)
  assert.match(sbom, /upload-artifact: false/)
  assert.match(sbom, /upload-release-assets: false/)
  assert.match(sbom, /retention-days: 90/)
  assert.match(sbom, /sbom-sha256:/)
  assert.match(sbom, /policy-sha256:/)
  assert.doesNotMatch(job(sbom, 'sbom'), /secrets\.|id-token:|attestations:|npm install|pnpm install/)
})

test('release.gate-evidence-attested: main coordinates designated release work and requires its fresh SBOM job', () => {
  const producer = workflow('verify-gallery-receipt')
  const computation = job(producer, 'main-sbom')
  assert.ok(computation, 'main-side SBOM computation required')
  assert.match(computation, /uses: \.\/\.github\/workflows\/license-sbom.yml/)
  assert.match(computation, /release-eligible == 'true'/)
  assert.match(computation, /tested-commit: \$\{\{ needs.plan.outputs.head \}\}/)
  assert.doesNotMatch(computation, /secrets:|id-token:|attestations:|steps:/)
  assert.match(job(producer, 'phase-4'), /needs.main-sbom.result == 'success'/)
  assert.match(job(producer, 'plan'), /RELEASE_APPROVED_COMMIT: \$\{\{ vars.RELEASE_APPROVED_COMMIT \}\}/)
})

// Exercises the same pinned Syft scanner and main-authored policy/configuration as the workflow.
// The optional executable is a test-host dependency, never a candidate input to a hosted job.
test('release.main-required-computations: actual fresh scanner refuses GPL despite forged MIT and candidate exclusions; MIT admits',
  {skip: !process.env.HARBORLINE_TEST_SYFT}, t => {
    const text = workflow('license-sbom')
    const config = text.match(/cat > "\$RUNNER_TEMP\/syft.yaml" <<'SYFT'\n([\s\S]*?)\n          SYFT/)
    const policy = text.match(/cat > "\$RUNNER_TEMP\/license-policy.cjs" <<'NODE'\n([\s\S]*?)\n          NODE/)
    assert.ok(config, 'main scanner configuration required')
    assert.ok(policy, 'main deny policy required')
    const root = mkdtempSync(join(tmpdir(), 'release-sbom-'))
    t.after(() => rmSync(root, {recursive: true, force: true}))
    const subject = join(root, 'candidate')
    mkdirSync(join(subject, 'node_modules/blocked-package'), {recursive: true})
    writeFileSync(join(root, 'syft.yaml'), config[1].split('\n').map(line => line.slice(10)).join('\n'))
    writeFileSync(join(root, 'policy.cjs'), policy[1].split('\n').map(line => line.slice(10)).join('\n'))
    writeFileSync(join(subject, '.syft.yaml'), 'exclude: ["**/node_modules/**"]\n')
    writeFileSync(join(subject, 'license-sbom.yml'), 'run: exit 0\n')
    writeFileSync(join(subject, 'sbom.spdx.json'), JSON.stringify({packages: [{name: 'blocked-package', licenseDeclared: 'MIT'}]}))
    writeFileSync(join(subject, 'package.json'), JSON.stringify({name: 'fixture-root', version: '1.0.0', dependencies: {'blocked-package': '1.0.0'}}))
    const run = license => {
      writeFileSync(join(subject, 'node_modules/blocked-package/package.json'), JSON.stringify({name: 'blocked-package', version: '1.0.0', license}))
      const report = join(root, 'fresh.spdx.json')
      const scan = spawnSync(process.env.HARBORLINE_TEST_SYFT,
        ['scan', `dir:${subject}`, '--config', join(root, 'syft.yaml'), '-o', `spdx-json=${report}`],
        {encoding: 'utf8', cwd: subject, timeout: 60000, env: {...process.env, SYFT_CHECK_FOR_APP_UPDATE: 'false'}})
      assert.equal(scan.status, 0, scan.stderr)
      const packages = JSON.parse(readFileSync(report)).packages
      assert.ok(packages.some(item => item.name === 'blocked-package' && item.versionInfo === '1.0.0'
        && (item.licenseDeclared === license || item.licenseConcluded === license)), JSON.stringify(packages))
      return spawnSync(process.execPath, [join(root, 'policy.cjs'), report], {encoding: 'utf8'})
    }
    const denied = run('GPL-3.0-only')
    assert.equal(denied.status, 1, denied.stderr)
    assert.match(denied.stdout, /DENY blocked-package@1\.0\.0/)
    const admitted = run('MIT')
    assert.equal(admitted.status, 0, admitted.stderr)
    for (const bytes of ['not-json', '{}', '{"spdxVersion":"SPDX-2.3","packages":[]}']) {
      writeFileSync(join(root, 'invalid.spdx.json'), bytes)
      const invalid = spawnSync(process.execPath, [join(root, 'policy.cjs'), join(root, 'invalid.spdx.json')], {encoding: 'utf8'})
      assert.equal(invalid.status, 1, `invalid or empty SPDX must refuse: ${bytes}`)
    }
    const missing = spawnSync(process.execPath, [join(root, 'policy.cjs'), join(root, 'missing.spdx.json')], {encoding: 'utf8'})
    assert.equal(missing.status, 1, 'missing SPDX must refuse')
  })

// Independent REST documents: T-1138 run/attempt literals and ADR-0198 decision 2.
function prerequisites() {
  const repository = {full_name: 'Harborline-Software/harborline-platform'}
  const head = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
  const queue = {id: 9001, run_attempt: 2, event: 'merge_group', status: 'completed', conclusion: 'success',
    head_sha: head, path: '.github/workflows/verify.yml', name: 'verify', repository, head_repository: repository, check_suite_id: 701}
  const sbom = {...queue, id: 9003, run_attempt: 1, check_suite_id: 702, name: 'license-sbom', path: '.github/workflows/license-sbom.yml'}
  const names = ['phase-4-gate', 'gallery-shard (1)', 'gallery-shard (2)', 'gallery-shard (3)',
    'gallery-shard (4)', 'gallery-shard (5)', 'gallery-shard (6)', 'gallery-collect', 'verify']
  const jobs = names.map((name, i) => ({id: i + 1, name, status: 'completed', conclusion: 'success',
    head_sha: head, run_id: 9001, check_run_url: `https://api.github.com/repos/Harborline-Software/harborline-platform/check-runs/${i + 1}`}))
  const sbomJobs = [{...jobs[0], id: 20, name: 'sbom', run_id: 9003,
    check_run_url: 'https://api.github.com/repos/Harborline-Software/harborline-platform/check-runs/20'}]
  const checks = Object.fromEntries([...jobs, ...sbomJobs].map(item => [item.id, {id: item.id,
    name: item.name, head_sha: head, status: 'completed', conclusion: 'success', app: {id: 15368},
    check_suite: {id: item.run_id === 9001 ? 701 : 702}}]))
  const documents = {
    'actions/runs/9001': queue, 'actions/runs/9001/attempts/2': queue,
    'actions/runs/9001/attempts/2/jobs?per_page=100': {total_count: 9, jobs},
    ['actions/workflows/license-sbom.yml/runs?event=merge_group&head_sha=' + head + '&per_page=100']: {total_count: 1, workflow_runs: [sbom]},
    'actions/runs/9003': sbom, 'actions/runs/9003/attempts/1': sbom,
    'actions/runs/9003/attempts/1/jobs?per_page=100': {total_count: 1, jobs: sbomJobs},
    ...Object.fromEntries(Object.entries(checks).map(([id, check]) => [`check-runs/${id}`, check])),
  }
  const readApi = path => {
    const key = path.replace('repos/Harborline-Software/harborline-platform/', '')
    assert.ok(Object.hasOwn(documents, key), `unexpected API lookup ${key}`)
    return structuredClone(documents[key])
  }
  const verify = () => {
    assert.equal(typeof evidence.releaseRunPrerequisites, 'function', 'release run prerequisite verifier required')
    return evidence.releaseRunPrerequisites({head, queueId: 9001, actionsAppId: 15368, readApi})
  }
  return {head, queue, sbom, jobs, sbomJobs, checks, documents, verify}
}

test('release.gate-evidence-attested: successful exact-source queue and SBOM prerequisites are recorded separately', () => {
  const f = prerequisites()
  assert.deepEqual(f.verify(), {queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}})
})

test('release.gate-evidence-attested: pending SBOM is polled before prerequisite admission', async () => {
  const f = prerequisites()
  f.sbom.status = 'in_progress'
  assert.equal(typeof evidence.waitForReleaseRunPrerequisites, 'function')
  let clock = 0, waits = 0
  const result = await evidence.waitForReleaseRunPrerequisites({
    head: f.head, queueId: 9001, actionsAppId: 15368,
    readApi: path => structuredClone(f.documents[path.replace('repos/Harborline-Software/harborline-platform/', '')]),
    now: () => clock, maxWaitMs: 200, pollMs: 100,
    wait: async ms => { waits++; clock += ms; f.sbom.status = 'completed' },
  })
  assert.equal(waits, 1)
  assert.deepEqual(result, {queue: {id: 9001, attempt: 2}, sbom: {id: 9003, attempt: 1}})
})

test('release.gate-evidence-attested: unfinished prerequisites time out without an admission', async () => {
  const f = prerequisites()
  f.sbom.status = 'in_progress'
  assert.equal(typeof evidence.waitForReleaseRunPrerequisites, 'function')
  let clock = 0
  await assert.rejects(() => evidence.waitForReleaseRunPrerequisites({
    head: f.head, queueId: 9001, actionsAppId: 15368,
    readApi: path => structuredClone(f.documents[path.replace('repos/Harborline-Software/harborline-platform/', '')]),
    now: () => clock, maxWaitMs: 200, pollMs: 100, wait: async ms => { clock += ms },
  }), /release prerequisites: timeout/)
  assert.equal(clock, 200)
})

for (const state of ['in_progress', 'failure', 'skipped', 'cancelled', 'timed_out']) {
  test(`release.gate-evidence-attested: SBOM ${state} cannot admit receipt production`, () => {
    const f = prerequisites()
    if (state === 'in_progress') f.sbom.status = state
    else f.sbom.conclusion = state
    assert.throws(f.verify, /release prerequisites:/)
  })
}

for (const name of ['phase-4-gate', 'gallery-shard (1)', 'gallery-shard (2)', 'gallery-shard (3)',
  'gallery-shard (4)', 'gallery-shard (5)', 'gallery-shard (6)', 'gallery-collect', 'verify']) {
  for (const conclusion of ['failure', 'skipped', 'cancelled', null]) {
    test(`release.gate-evidence-attested: ${name} ${conclusion} refuses despite aggregate success`, () => {
      const f = prerequisites()
      f.jobs.find(item => item.name === name).conclusion = conclusion
      assert.throws(f.verify, /release prerequisites:/)
    })
  }
}

for (const [name, mutate] of [
  ['missing job', f => { f.jobs.pop(); f.documents['actions/runs/9001/attempts/2/jobs?per_page=100'].total_count = 8 }],
  ['duplicate job', f => { f.jobs.push(f.jobs[0]); f.documents['actions/runs/9001/attempts/2/jobs?per_page=100'].total_count = 10 }],
  ['other workflow', f => { f.queue.path = '.github/workflows/fake.yml' }],
  ['wrong event', f => { f.queue.event = 'pull_request' }],
  ['wrong head', f => { f.queue.head_sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }],
  ['latest queue failed', f => {
    const latest = {...f.queue, run_attempt: 3, conclusion: 'failure'}
    f.documents['actions/runs/9001'] = latest
    f.documents['actions/runs/9001/attempts/3'] = latest
    f.documents['actions/runs/9001/attempts/3/jobs?per_page=100'] = {total_count: 9, jobs: f.jobs}
  }],
  ['unknown check source', f => { f.checks[1].app.id = 222 }],
  ['wrong check suite', f => { f.checks[1].check_suite.id = 999 }],
  ['same-name forged check on wrong head', f => { f.checks[1].head_sha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }],
  ['job from another run', f => { f.jobs[0].run_id = 9002 }],
  ['foreign check URL', f => { f.jobs[0].check_run_url = 'https://example.invalid/check-runs/1' }],
  ['unreadable API', f => { delete f.documents['actions/runs/9001'] }],
]) test(`release.gate-evidence-attested: ${name} refuses`, () => {
  const f = prerequisites()
  mutate(f)
  assert.throws(f.verify, /release prerequisites:/)
})

test('release.shared-signer-policy: signing has no App credential or publishing environment', () => {
  const signer = workflow('sign-gallery-receipt')
  assert.doesNotMatch(signer, /secrets:|secrets\.|environment:|create-github-app-token|--receipt post/)
  assert.equal((signer.match(/id-token: write/g) ?? []).length, 1)
  assert.equal((signer.match(/attestations: write/g) ?? []).length, 1)
  assert.doesNotMatch(signer, /contents: write|checks: write|statuses: write|pnpm install|run-phase-4-gate/)
})

test('release.shared-signer-policy: the calling job is step-free and passes no secrets', () => {
  const caller = job(workflow('verify-gallery-receipt'), 'sign')
  assert.ok(caller)
  assert.match(caller, /uses: \.\/\.github\/workflows\/sign-gallery-receipt.yml/)
  assert.match(caller, /id-token: write/)
  assert.match(caller, /attestations: write/)
  assert.doesNotMatch(caller, /steps:|secrets:|environment:/)
})

test('release.shared-signer-policy: a separate main poster checks evidence with no signing rights', () => {
  const poster = job(workflow('verify-gallery-receipt'), 'post')
  assert.ok(poster, 'dedicated App poster must be a separate job')
  assert.match(poster, /environment: gallery-receipt/)
  assert.match(poster, /ref: \$\{\{ github.sha \}\}/)
  assert.match(poster, /GALLERY_RECEIPT_APP_KEY/)
  assert.match(poster, /--receipt post/)
  assert.doesNotMatch(poster, /id-token:|attestations:|contents: write|run-phase-4-gate|pnpm install/)
  assert.match(poster, /needs\.sign\.outputs\.artifact-id/)
})
