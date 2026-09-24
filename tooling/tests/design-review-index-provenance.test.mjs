import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync, spawnSync} from 'node:child_process'
import {existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import {createHash} from 'node:crypto'
import {deflateRawSync, inflateRawSync} from 'node:zlib'

import {referenceSurface, reviewVerdict} from '../gates/design-review.mjs'
import {provenanceDigest, recordIndexVerdict, verifyIndexProvenance} from '../gates/design-review-provenance.mjs'

const moduleId = 'hlp.ui.thing'
const source = 'projections/react/ui/hlp.ui.thing/src/Thing.tsx'
const scenario = `gallery/scenarios/${moduleId}.json`
const recordPath = `docs/evidence/design-review/${moduleId}.json`
const cli = resolve(import.meta.dirname, '../gates/record-design-verdict.mjs')
const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
})
const put = (root, path, text) => {
  mkdirSync(dirname(resolve(root, path)), {recursive: true})
  writeFileSync(resolve(root, path), text)
}
function fixture(t) {
  const root = mkdtempSync(resolve(tmpdir(), 'design-index-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  git(root, 'init', '-b', 'main')
  git(root, 'config', 'user.name', 'Fixture Author')
  git(root, 'config', 'user.email', 'fixture@example.invalid')
  git(root, 'config', 'core.autocrlf', 'false')
  put(root, scenario, '{"stories":[]}\n')
  put(root, `specs/modules/ui/${moduleId}/scenarios.json`, '{"scenarios":[]}\n')
  put(root, `specs/modules/ui/${moduleId}/style.css`, '.thing { color: red; }\n')
  put(root, source, 'export const Thing = () => <span>Original</span>\n')
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'base')
  git(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD')
  return root
}
function stageReview(root) {
  put(root, source, 'export const Thing = () => <span>Reviewed</span>\n')
  put(root, scenario, '{"stories":[{"id":"reviewed"}]}\n')
  git(root, 'add', source, scenario)
}
const record = root => recordIndexVerdict({platformRoot: root, moduleId, reviewer: 'A Person', verdict: 'approved'})
const reseal = candidate => {
  candidate.reference.provenance.digest = provenanceDigest(candidate.reference.provenance)
  return candidate
}

test('the human CLI binds uncommitted staged surface blobs and a reachable main base', t => {
  const root = fixture(t)
  const baseHead = git(root, 'rev-parse', 'HEAD').trim()
  stageReview(root)
  put(root, recordPath, JSON.stringify({reference: {pin: baseHead, migration: {kind: 'family-rename'}}}))
  const result = spawnSync(process.execPath, [cli, root, moduleId, '--reviewer', 'A Person', '--verdict', 'approved'], {encoding: 'utf8'})
  assert.equal(result.status, 0, result.stderr)
  const written = JSON.parse(readFileSync(resolve(root, recordPath), 'utf8'))
  assert.deepEqual(written, JSON.parse(result.stdout))
  assert.equal(written.reference.provenance.kind, 'git-index-surface-v1')
  assert.equal(written.reference.provenance.baseHead, baseHead)
  assert.equal(written.reference.pin, undefined)
  assert.equal(written.reference.migration, undefined)
  const entry = written.reference.provenance.files.find(file => file.path === source)
  assert.deepEqual(entry, {path: source, mode: '100644', blob: git(root, 'rev-parse', `:${source}`).trim()})
  assert.notEqual(entry.blob, git(root, 'rev-parse', `HEAD:${source}`).trim())
  assert.ok(written.reference.provenance.files.some(file => file.mode === null && file.blob === null))
  assert.equal(verifyIndexProvenance(root, written), true)
  assert.equal(reviewVerdict({record: written, revision: written.reference.revision, surface: referenceSurface(root, moduleId)})[0], 'PASS')
})

test('modern provenance survives squash and a fresh clone without the review branch commit', t => {
  const root = fixture(t)
  git(root, 'switch', '-c', 'review')
  stageReview(root)
  const written = record(root)
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'review branch only')
  const branchCommit = git(root, 'rev-parse', 'HEAD').trim()
  git(root, 'switch', 'main')
  git(root, 'merge', '--squash', 'review')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'squashed review')
  git(root, 'branch', '-D', 'review')
  const clone = resolve(root, 'fresh-clone')
  git(root, 'clone', '--no-local', '--single-branch', '--branch', 'main', root, clone)
  assert.throws(() => git(clone, 'cat-file', '-e', `${branchCommit}^{commit}`))
  assert.equal(verifyIndexProvenance(clone, written), true)
  assert.deepEqual(JSON.parse(readFileSync(resolve(clone, recordPath), 'utf8')), JSON.parse(JSON.stringify(written)))
})

test('a neutral edit before the first commit cannot strand the reviewed staged blob after squash', t => {
  const root = fixture(t)
  git(root, 'switch', '-c', 'review')
  stageReview(root)
  const written = record(root)
  const reviewedBlob = written.reference.provenance.files.find(file => file.path === source).blob
  put(root, source, '// render-neutral edit after approval, before any commit\nexport const Thing = () => <span>Reviewed</span>\n')
  git(root, 'add', '.')
  assert.notEqual(git(root, 'rev-parse', `:${source}`).trim(), reviewedBlob)
  assert.equal(reviewVerdict({record: written, revision: written.reference.revision, surface: referenceSurface(root, moduleId)})[0], 'PASS')
  assert.equal(verifyIndexProvenance(root, written), true)
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'review with neutral edit')
  const branchCommit = git(root, 'rev-parse', 'HEAD').trim()
  git(root, 'switch', 'main')
  git(root, 'merge', '--squash', 'review')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'squashed review')
  git(root, 'branch', '-D', 'review')
  const clone = resolve(root, 'fresh-clone')
  git(root, 'clone', '--no-local', '--single-branch', '--branch', 'main', root, clone)
  assert.throws(() => git(clone, 'cat-file', '-e', `${branchCommit}^{commit}`))
  assert.throws(() => git(clone, 'cat-file', '-e', `${reviewedBlob}^{blob}`), 'the reviewed raw blob must genuinely be unavailable')
  const landed = JSON.parse(readFileSync(resolve(clone, recordPath), 'utf8'))
  assert.equal(reviewVerdict({record: landed, revision: landed.reference.revision, surface: referenceSurface(clone, moduleId)})[0], 'PASS')
  assert.equal(verifyIndexProvenance(clone, landed), true, 'the committed record must reconstruct its own reviewed bytes')
})

for (const [name, change] of [
  ['changed render', root => put(root, source, 'export const Thing = () => <div>Not reviewed</div>\n')],
  ['added render', root => put(root, 'projections/react/ui/hlp.ui.thing/src/Extra.tsx', 'export const Extra = () => <hr/>\n')],
  ['deleted render', root => rmSync(resolve(root, source))],
  ['changed catalog', root => put(root, scenario, '{"stories":[{"id":"not-staged"}]}\n')],
]) {
  test(`working versus index ${name} is refused without replacing an existing record`, t => {
    const root = fixture(t)
    stageReview(root)
    const written = record(root)
    const before = readFileSync(resolve(root, recordPath), 'utf8')
    change(root)
    assert.throws(() => record(root), /working render surface differs from the staged index/)
    assert.equal(readFileSync(resolve(root, recordPath), 'utf8'), before)
    assert.equal(verifyIndexProvenance(root, written), true, 'historical provenance is independent of current expiry')
  })
}

test('non-surface edits and render-neutral comments do not block a staged review', t => {
  const root = fixture(t)
  stageReview(root)
  put(root, 'README.md', 'Unstaged documentation\n')
  put(root, 'projections/react/ui/hlp.ui.thing/src/__tests__/Thing.test.tsx', 'not a render input\n')
  put(root, source, '// formatting-only working edit\nexport const Thing = () => <span>Reviewed</span>\n')
  const written = record(root)
  assert.equal(verifyIndexProvenance(root, written), true)
  assert.equal(written.reference.provenance.files.find(file => file.path === source).blob, git(root, 'rev-parse', `:${source}`).trim())
})

test('staged additions and removals are represented by the exact declared surface', t => {
  const root = fixture(t)
  git(root, 'rm', source)
  const added = 'projections/blazor/ui/hlp.ui.thing/HarborlineThing.razor'
  put(root, added, '<span>Reviewed</span>\n')
  git(root, 'add', added)
  const written = record(root)
  assert.equal(verifyIndexProvenance(root, written), true)
  assert.ok(written.reference.provenance.files.some(file => file.path === added))
  assert.ok(!written.reference.provenance.files.some(file => file.path === source))
})

test('tampered digest, blob, path, mode, omissions, and surface hashes are refused', t => {
  const root = fixture(t)
  stageReview(root)
  const written = record(root)
  const mutate = edit => { const changed = structuredClone(written); edit(changed); return changed }
  assert.throws(() => verifyIndexProvenance(root, mutate(value => { value.reference.provenance.digest = `sha256:${'0'.repeat(64)}` })), /digest/)
  const wrongBlob = git(root, 'rev-parse', `HEAD:${source}`).trim()
  const blobChanged = mutate(value => { value.reference.provenance.files.find(file => file.path === source).blob = wrongBlob })
  assert.throws(() => verifyIndexProvenance(root, blobChanged), /digest/)
  assert.throws(() => verifyIndexProvenance(root, reseal(blobChanged)), /surface/)
  const pathChanged = mutate(value => { value.reference.provenance.files.find(file => file.path === source).path = '../outside.tsx' })
  assert.throws(() => verifyIndexProvenance(root, reseal(pathChanged)), /path/)
  const renamed = mutate(value => {
    value.reference.provenance.files.find(file => file.path === source).path = source.replace('Thing.tsx', 'Other.tsx')
    value.reference.provenance.files.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0)
  })
  assert.throws(() => verifyIndexProvenance(root, reseal(renamed)), /surface/)
  assert.throws(() => verifyIndexProvenance(root, reseal(mutate(value => { value.reference.provenance.files.find(file => file.path === source).mode = '120000' }))), /mode/)
  assert.throws(() => verifyIndexProvenance(root, reseal(mutate(value => { value.reference.provenance.files = value.reference.provenance.files.filter(file => file.path !== source) }))), /surface/)
  assert.throws(() => verifyIndexProvenance(root, mutate(value => { value.reference.surface[source] = '0'.repeat(64) })), /surface/)
  assert.throws(() => verifyIndexProvenance(root, mutate(value => { value.reference.revision = '0'.repeat(64) })), /revision/)
  assert.throws(() => verifyIndexProvenance(root, mutate(value => { value.reference.provenance.kind = 'unknown' })), /kind/)
})

test('legal Git modes are bound to archived metadata, not merely to a resealed descriptor', t => {
  const root = fixture(t)
  stageReview(root)
  const written = record(root)
  assert.match(git(root, 'ls-files', '--stage', source), /^100644 /)
  const changed = structuredClone(written)
  changed.reference.provenance.files.find(file => file.path === source).mode = '100755'
  assert.throws(() => verifyIndexProvenance(root, reseal(changed)), /archive path\/mode/)
  git(root, 'update-index', '--chmod=+x', source)
  const executable = record(root)
  assert.equal(executable.reference.provenance.files.find(file => file.path === source).mode, '100755')
  assert.equal(verifyIndexProvenance(root, executable), true, 'a genuinely staged executable mode remains supported')
})

test('self-contained archive bytes are required and authenticated without object-DB fallback', t => {
  const root = fixture(t)
  stageReview(root)
  const written = record(root)
  const withoutArchive = structuredClone(written)
  delete withoutArchive.reference.provenance.archive
  assert.throws(() => verifyIndexProvenance(root, withoutArchive), /self-contained surface archive/)
  const badDigest = structuredClone(written)
  badDigest.reference.provenance.archive.digest = `sha256:${'0'.repeat(64)}`
  assert.throws(() => verifyIndexProvenance(root, reseal(badDigest)), /archive digest/)
  const changed = structuredClone(written)
  const archive = changed.reference.provenance.archive
  const entries = JSON.parse(inflateRawSync(Buffer.from(archive.data, 'base64')).toString('utf8'))
  const original = entries.find(entry => entry.path === source)
  assert.equal(Buffer.from(original.contentBase64, 'base64').toString('utf8'), readFileSync(resolve(root, source), 'utf8'))
  original.contentBase64 = Buffer.from('different archived bytes').toString('base64')
  const bytes = Buffer.from(JSON.stringify(entries))
  archive.data = deflateRawSync(bytes, {level: 9}).toString('base64')
  archive.digest = `sha256:${createHash('sha256').update(bytes).digest('hex')}`
  assert.throws(() => verifyIndexProvenance(root, reseal(changed)), /archive blob does not match/)
})

test('the standalone verdict reader fails closed on malformed modern provenance', t => {
  const root = fixture(t)
  stageReview(root)
  const written = record(root)
  const judge = candidate => reviewVerdict({record: candidate, revision: written.reference.revision, surface: referenceSurface(root, moduleId)})
  assert.equal(judge(written)[0], 'PASS')
  const badDigest = structuredClone(written)
  badDigest.reference.provenance.digest = `sha256:${'0'.repeat(64)}`
  assert.deepEqual(judge(badDigest), ['FAIL', 'invalid design-review provenance: design-review provenance digest does not match its manifest and archive'])
  const badMode = structuredClone(written)
  badMode.reference.provenance.files.find(file => file.path === source).mode = '100755'
  assert.equal(judge(reseal(badMode))[0], 'FAIL')
  const absentArchive = structuredClone(written)
  delete absentArchive.reference.provenance.archive
  assert.equal(judge(absentArchive)[0], 'FAIL')
})

test('noncanonical manifests and unavailable or branch-only bases are refused', t => {
  const root = fixture(t)
  stageReview(root)
  const written = record(root)
  const shuffled = structuredClone(written)
  shuffled.reference.provenance.files.reverse()
  assert.throws(() => verifyIndexProvenance(root, reseal(shuffled)), /canonical/)
  const duplicate = structuredClone(written)
  duplicate.reference.provenance.files.push(duplicate.reference.provenance.files[0])
  assert.throws(() => verifyIndexProvenance(root, reseal(duplicate)), /canonical/)
  const unavailable = structuredClone(written)
  unavailable.reference.provenance.baseHead = '0'.repeat(40)
  assert.throws(() => verifyIndexProvenance(root, reseal(unavailable)), /baseHead/)
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'not on main remote')
  const branchOnly = structuredClone(written)
  branchOnly.reference.provenance.baseHead = git(root, 'rev-parse', 'HEAD').trim()
  assert.throws(() => verifyIndexProvenance(root, reseal(branchOnly)), /baseHead/)
  assert.equal(record(root).reference.provenance.baseHead, written.reference.provenance.baseHead)
})

test('the CLI still refuses missing or automated human attribution before writing', t => {
  const root = fixture(t)
  for (const reviewer of [[], ['--reviewer', 'CI']]) {
    const result = spawnSync(process.execPath, [cli, root, moduleId, '--verdict', 'approved', ...reviewer], {encoding: 'utf8'})
    assert.equal(result.status, 1)
    assert.match(result.stderr, /named human reviewer/)
    assert.equal(existsSync(resolve(root, recordPath)), false)
  }
})
