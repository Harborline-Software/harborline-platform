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
  const pack = workflow.split('  pack-libraries:\n')[1]?.split('  publish-libraries:\n')[0]
  assert.ok(pack, 'pack-libraries job is required')
  const ids = /PACKAGE_IDS: >-\n([\s\S]*?)    steps:/.exec(job)[1].trim().split(/\s+/).sort()
  const produced = producerIds()
  assert.equal(new Set(produced).size, produced.length, 'one producer per package id')
  assert.ok(produced.includes('Harborline.Foundation.FieldRuntime'), 'the shared field runtime must be published')
  assert.deepEqual(ids, produced, 'workflow package list must equal the producer inventory')
  // Publication follows the landing the repository's own gate proved by receipt (2026-09-07); it must not
  // wait on the ubuntu rerun of that gate, and it must never run for a pull request.
  assert.doesNotMatch(job, /needs: phase-4-gate/)
  assert.match(job, /if: github.event_name == 'push' && github.ref == 'refs\/heads\/main'\n/)
  // T-579: the push to main is publication only. The gate runs in verify.yml (pull request and merge
  // group) and never on the push, so the push run is green exactly when publication is.
  assert.doesNotMatch(workflow, /merge_group|run-phase-4-gate/)
  assert.doesNotMatch(workflow, /ACTIONS_ENABLED/)
  // Least privilege (owner ruling 2026-11-02): pack-libraries builds, tests and packs read-only; only
  // publish-libraries holds the write scopes, and it builds nothing. The literals here are the oracle.
  assert.match(pack, /if: github.event_name == 'push' && github.ref == 'refs\/heads\/main'\n    runs-on: ubuntu-latest\n    permissions:\n      contents: read\n    steps:/)
  assert.doesNotMatch(pack, /: write|GH_TOKEN|nuget push|actions\/attest/)
  assert.match(pack, /node tooling\/verify-package-fixtures\.mjs --pack-libraries/)
  assert.match(pack, /node --test tooling\/tests\/publish-libraries\.test\.mjs tooling\/tests\/publish-libraries\.packed\.mjs/)
  assert.match(job, /^    needs: pack-libraries\n/)
  assert.match(job, /permissions:\n      contents: read\n      packages: write\n      id-token: write\n      attestations: write\n/)
  for (const build of ['verify-package-fixtures', 'dotnet pack', 'dotnet build', 'dotnet test', 'pnpm', 'node --test']) {
    assert.ok(!job.includes(build), `publish-libraries must not build or test: ${build}`)
  }
  assert.match(job, /node tooling\/library-promotion-proof\.mjs/)
  for (const scope of ['packages', 'id-token', 'attestations']) {
    assert.equal(workflow.match(new RegExp(`^ +${scope}: write$`, 'gm'))?.length, 1, `${scope}: write is granted once`)
  }
  assert.match(job, /for id in \$PACKAGE_IDS; do/)
  assert.match(job, /--source github --api-key "\$\{GH_TOKEN\}" --skip-duplicate/)
  assert.match(job, /GH_TOKEN: \$\{\{ secrets\.GITHUB_TOKEN \}\}/)
  assert.match(job, /https:\/\/nuget\.pkg\.github\.com\/\$\{GITHUB_REPOSITORY_OWNER\}\/index\.json/)
  const refused = spawnSync(process.execPath, ['tooling/verify-package-fixtures.mjs', '--pack-libraries', '--phase-4-gate'], { cwd: root, encoding: 'utf8' })
  assert.equal(refused.status, 1)
  assert.match(refused.stderr, /unknown argument: --pack-libraries/)
})
