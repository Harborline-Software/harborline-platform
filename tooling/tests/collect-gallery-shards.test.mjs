import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {mkdtempSync, readFileSync, rmSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join, resolve} from 'node:path'
import test from 'node:test'

import {collectorFailures, specScenarioCount} from '../collect-gallery-shards.mjs'

const root = resolve(import.meta.dirname, '../..')
const passing = {
  status: 'PASS',
  counts: {scenarios: 7, scenarioBrowserTests: 7},
  scenarioReconciliation: 'exact',
  checkReconciliation: 'exact',
}

test('the collector passes only a PASS merge that reconciles against the spec scenario count', () => {
  assert.deepEqual(collectorFailures(passing, 7), [])
  assert.match(collectorFailures(passing, 8).join(), /spec scenarios 8 != gallery scenarios 7/)
  assert.match(collectorFailures({...passing, status: 'FAIL', failure: 'shard 3 missing'}, 7).join(), /shard 3 missing/)
  assert.match(collectorFailures({...passing, scenarioReconciliation: 'drift'}, 7).join(), /scenarioReconciliation: drift/)
  assert.match(collectorFailures({...passing, checkReconciliation: undefined}, 7).join(), /checkReconciliation/)
  assert.match(collectorFailures({...passing, counts: {scenarios: 7, scenarioBrowserTests: 6}}, 7).join(), /scenarioBrowserTests 6/)
  assert.ok(collectorFailures(undefined, 7).length > 0, 'an unparseable merge report fails')
})

test('the spec scenario count reads the UI spec without a build', () => {
  assert.ok(specScenarioCount() > 0)
})

test('the collector fails closed when no shard report is present', () => {
  const empty = mkdtempSync(join(tmpdir(), 'collect-gallery-shards-'))
  try {
    const run = spawnSync(process.execPath, ['tooling/collect-gallery-shards.mjs', empty], {cwd: root, encoding: 'utf8'})
    assert.equal(run.status, 1)
    assert.match(run.stderr, /gallery collector:/)
  } finally {
    rmSync(empty, {recursive: true, force: true})
  }
})

test('CI runs the headless gate beside the gallery shards, and verify needs all three', () => {
  const verify = readFileSync(resolve(root, '.github/workflows/verify.yml'), 'utf8')
  assert.doesNotMatch(verify, /runs-on: macos|\n  verify-shared:/)
  assert.match(verify, /needs: \[pr-preflight, phase-4-gate, gallery-shard, gallery-collect\]/)
  assert.match(verify, /HARBORLINE_GATE_HEADLESS: "1"/)
  assert.match(verify, /node tooling\/collect-gallery-shards\.mjs/)
  const gate = verify.split('\n  phase-4-gate:\n')[1].split('\n  gallery-shard:\n')[0]
  assert.doesNotMatch(gate, /needs:.*gallery/, 'the headless gate must not wait for the shards')
  // T-577: the gallery lanes run in the merge group and on dispatch, never on a pull request, and
  // the preliminary PR check never claims full proof (ci-verification.test.mjs checks outcomes).
  for (const lane of ['gallery-shard', 'gallery-collect']) {
    const guard = verify.split(`\n  ${lane}:\n`)[1].split('runs-on:')[0]
    assert.match(guard, /github\.event_name == 'merge_group' \|\| github\.event_name == 'workflow_dispatch'\)/)
    assert.doesNotMatch(guard, /pull_request/)
  }
  assert.match(verify, /node tooling\/run-base-policy\.mjs aggregate\n\s+else\n\s+node tooling\/verify-ci-lanes\.mjs/)
  const validate = readFileSync(resolve(root, '.github/workflows/validate.yml'), 'utf8')
  assert.doesNotMatch(validate, /run-phase-4-gate|run-gallery-gate/, 'one copy of the gate only')
})
