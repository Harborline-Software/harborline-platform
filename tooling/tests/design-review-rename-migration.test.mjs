import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync} from 'node:child_process'
import {mkdirSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import {legacyRecords, migrateRenames} from '../gates/migrate-design-review-surface.mjs'
import {designSurfaceInputs} from '../gates/design-surface-inputs.mjs'
import {recordVerdict, recordsRoot} from '../gates/design-review.mjs'

test('migrated corpus references match the current identity through the diagnostic', () => {
  const root = resolve(import.meta.dirname, '../..')
  const migrated = readdirSync(recordsRoot).filter(name => name !== 'expired-backlog.json')
    .map(name => JSON.parse(readFileSync(resolve(recordsRoot, name), 'utf8')))
    .filter(record => record.reference.migration)
  assert.ok(migrated.length > 0, 'migration must carry at least one real approval')
  for (const record of migrated) {
    const diagnostic = designSurfaceInputs(root, record.moduleId)
    assert.deepEqual(diagnostic.recordedReference.surface, diagnostic.surface)
    assert.equal(diagnostic.recordedReference.revision, diagnostic.revision)
    assert.equal(diagnostic.verdict[0], 'PASS')
    assert.equal(record.reference.migration.judgedAgainst, record.reference.migration.reviewedReference.pin)
  }
})

test('rename migration proves both pins, refuses changed content and is idempotent', t => {
  const platformRoot = mkdtempSync(resolve(tmpdir(), 'design-review-rename-test-'))
  t.after(() => rmSync(platformRoot, {recursive: true, force: true}))
  const git = (...args) => execFileSync('git', ['-c', 'user.name=Fixture Reviewer', '-c', 'user.email=fixture@example.invalid',
    ...args], {cwd: platformRoot, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe']}).trim()
  const put = (file, content) => {
    mkdirSync(dirname(resolve(platformRoot, file)), {recursive: true})
    writeFileSync(resolve(platformRoot, file), content)
  }
  const moduleId = 'hlp.ui.canary'
  const component = `projections/blazor/ui/${moduleId}/AsterCanary.razor`
  const renamed = component.replace('Aster', 'Harborline')
  const css = `specs/modules/ui/${moduleId}/style.css`
  put(component, '@namespace Aster.UI\n<button>Original</button>\n')
  put(css, '.canary { color: red; }\n')
  put(`specs/modules/ui/${moduleId}/scenarios.json`, '{}\n')
  put(`gallery/scenarios/${moduleId}.json`, '{}\n')
  put('gallery/projections/blazor/Stories/Canary.stories.razor', '<AsterCanary />\n')
  put(`projections/react/ui/${moduleId}/src/Canary.tsx`, 'export const Canary = () => <button>Original</button>\n')
  put(`conformance/${moduleId}/fixtures.yaml`, '{}\n')
  git('init', '--quiet')
  git('add', '.')
  git('commit', '--quiet', '--no-verify', '-m', 'reviewed surface')
  const judgedAgainst = git('rev-parse', 'HEAD')
  const root = resolve(platformRoot, 'docs/evidence/design-review')
  const record = recordVerdict({platformRoot, moduleId, reviewer: 'Fixture Reviewer', verdict: 'approved', root})
  record.reference.pin = judgedAgainst
  const recordPath = resolve(root, `${moduleId}.json`)
  const resetRecord = () => writeFileSync(recordPath, JSON.stringify(record))
  resetRecord()
  const backlogPath = resolve(root, 'expired-backlog.json')
  const backlogBytes = JSON.stringify({ticket: 334, deadline: '2026-09-30', commit: judgedAgainst, modules: [moduleId]})
  writeFileSync(backlogPath, backlogBytes)
  assert.deepEqual(legacyRecords(root), [], 'the backlog is not a legacy review record')
  renameSync(resolve(platformRoot, component), resolve(platformRoot, renamed))
  put(renamed, '@namespace Harborline.UI\n<button>Original</button>\n')
  put('gallery/projections/blazor/Stories/Canary.stories.razor', '<HarborlineCanary />\n')
  git('add', '.')
  git('commit', '--quiet', '--no-verify', '-m', 'family rename')
  const pin = git('rev-parse', 'HEAD')
  const run = () => migrateRenames({platformRoot, root, from: 'Aster', to: 'Harborline'})
  const first = run()
  assert.equal(first.migrated, 1)
  assert.equal(first.refused, 0, 'the backlog is not a refused review record')
  assert.equal(readFileSync(backlogPath, 'utf8'), backlogBytes)
  const diagnostic = designSurfaceInputs(platformRoot, moduleId)
  assert.deepEqual(diagnostic.recordedReference.surface, diagnostic.surface)
  assert.equal(diagnostic.verdict[0], 'PASS')
  assert.equal(diagnostic.recordedReference.pin, pin)
  assert.equal(diagnostic.recordedReference.migration.judgedAgainst, judgedAgainst)
  assert.deepEqual(diagnostic.recordedReference.migration.reviewedReference, record.reference)
  const bytes = readFileSync(recordPath, 'utf8')
  assert.equal(run().unchanged, 1)
  assert.equal(readFileSync(recordPath, 'utf8'), bytes)
  resetRecord()
  put(css, '.canary { color: blue; }\n')
  assert.match(run().records[0].reason, /working surface/)
  git('add', '.')
  git('commit', '--quiet', '--no-verify', '-m', 'appearance change')
  assert.match(run().records[0].reason, /content changed beyond the rename.*style.css/)
  assert.equal(readFileSync(recordPath, 'utf8'), JSON.stringify(record), 'refusal must preserve the record bytes')
  record.reference.surface[css] = 'corrupted'
  resetRecord()
  assert.match(run().records[0].reason, /historical pin/)
})
