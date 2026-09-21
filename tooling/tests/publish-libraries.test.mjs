import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { spawnSync } from 'node:child_process'
import test from 'node:test'
import { producerIds } from '../package-producers.mjs'

const root = resolve(import.meta.dirname, '../..')

test('publication uses exactly the gate producer ids and follows a push to main, the receipt-proven landing, never a pull request', () => {
  const workflow = readFileSync(resolve(root, '.github/workflows/validate.yml'), 'utf8')
  const job = workflow.split('  publish-libraries:\n')[1]
  assert.ok(job, 'publish-libraries job is required')
  const ids = /PACKAGE_IDS: >-\n([\s\S]*?)    steps:/.exec(job)[1].trim().split(/\s+/).sort()
  const produced = producerIds()
  assert.equal(new Set(produced).size, produced.length, 'one producer per package id')
  assert.ok(produced.includes('Harborline.Foundation.FieldRuntime'), 'the shared field runtime must be published')
  assert.deepEqual(ids, produced, 'workflow package list must equal the producer inventory')
  // Publication follows the landing the repository's own gate proved by receipt (2026-09-07); it must not
  // wait on the ubuntu rerun of that gate, and it must never run for a pull request.
  assert.doesNotMatch(job, /needs: phase-4-gate/)
  assert.match(job, /if: github.event_name == 'push' && github.ref == 'refs\/heads\/main'\n/)
  // T-579: the push to main is publication only. The ubuntu gate runs in the merge group and never on
  // the push, so the push run is green exactly when publication is.
  assert.match(workflow, /  phase-4-gate:\n    if: github.event_name == 'merge_group'\n/)
  assert.doesNotMatch(workflow, /ACTIONS_ENABLED/)
  assert.match(job, /packages: write/)
  assert.match(job, /node tooling\/verify-package-fixtures\.mjs --pack-libraries/)
  assert.match(job, /node --test tooling\/tests\/publish-libraries\.test\.mjs tooling\/tests\/publish-libraries\.packed\.mjs/)
  assert.match(job, /for id in \$PACKAGE_IDS; do/)
  assert.match(job, /--source github --api-key "\$\{GH_TOKEN\}" --skip-duplicate/)
  assert.match(job, /GH_TOKEN: \$\{\{ secrets\.GITHUB_TOKEN \}\}/)
  assert.match(job, /https:\/\/nuget\.pkg\.github\.com\/\$\{GITHUB_REPOSITORY_OWNER\}\/index\.json/)
  const refused = spawnSync(process.execPath, ['tooling/verify-package-fixtures.mjs', '--pack-libraries', '--phase-4-gate'], { cwd: root, encoding: 'utf8' })
  assert.equal(refused.status, 1)
  assert.match(refused.stderr, /unknown argument: --pack-libraries/)
})
