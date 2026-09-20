// T-674: the control on the commit-time refusal. Red if the scanner stops refusing a baseHead that
// only a lane branch can produce, which is the shape T-568 committed and mac16 could not build.
import assert from 'node:assert/strict'
import test from 'node:test'
import {execFileSync, spawnSync} from 'node:child_process'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'

import {EVIDENCE_PATH, checkEvidenceProvenance} from '../gates/scan-evidence-provenance.mjs'

const cli = resolve(import.meta.dirname, '../gates/scan-evidence-provenance.mjs')
const git = (root, ...args) => execFileSync('git', args, {
  cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
}).trim()
const evidence = subject => JSON.stringify({schemaVersion: 3, phase: 4, status: 'PASS', subject})

function put(root, path, text) {
  mkdirSync(dirname(resolve(root, path)), {recursive: true})
  writeFileSync(resolve(root, path), text)
}

// A repository whose main holds one commit, with a lane branch commit on top that a squash merge
// would discard -- exactly the two commits T-568 could have recorded.
function fixture(t) {
  const root = mkdtempSync(resolve(tmpdir(), 'evidence-provenance-'))
  t.after(() => rmSync(root, {recursive: true, force: true}))
  git(root, 'init', '-b', 'main')
  git(root, 'config', 'user.name', 'Fixture Author')
  git(root, 'config', 'user.email', 'fixture@example.invalid')
  git(root, 'config', 'core.autocrlf', 'false')
  put(root, 'a.txt', 'one\n')
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'base')
  git(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD')
  const onMain = git(root, 'rev-parse', 'HEAD')
  put(root, 'a.txt', 'two\n')
  git(root, 'add', '.')
  git(root, '-c', 'core.hooksPath=', 'commit', '-m', 'lane branch only')
  return {root, onMain, onMainTree: git(root, 'rev-parse', `${onMain}^{tree}`), branchOnly: git(root, 'rev-parse', 'HEAD')}
}

test('a run recorded at a commit main holds is accepted', t => {
  const {root, onMain, onMainTree} = fixture(t)
  assert.equal(checkEvidenceProvenance(root, evidence({baseHead: onMain, testedTree: onMainTree})), null)
})

test('a run recorded on a lane branch is refused by name', t => {
  const {root, branchOnly} = fixture(t)
  const branchTree = git(root, 'rev-parse', `${branchOnly}^{tree}`)
  const refusal = checkEvidenceProvenance(root, evidence({baseHead: branchOnly, testedTree: branchTree}))
  assert.equal(refusal?.field, 'subject.baseHead')
})

test('a tree the recorded commit does not carry is refused by name', t => {
  const {root, onMain, branchOnly} = fixture(t)
  const strayTree = git(root, 'rev-parse', `${branchOnly}^{tree}`)
  const refusal = checkEvidenceProvenance(root, evidence({baseHead: onMain, testedTree: strayTree}))
  assert.equal(refusal?.field, 'subject.testedTree')
})

test('an absent object, an unparseable document and a clone without main all refuse', t => {
  const {root, onMain, onMainTree} = fixture(t)
  assert.equal(checkEvidenceProvenance(root, evidence({baseHead: '0'.repeat(40), testedTree: onMainTree}))?.field, 'subject.baseHead')
  assert.equal(checkEvidenceProvenance(root, evidence({testedTree: onMainTree}))?.field, 'subject.baseHead')
  assert.equal(checkEvidenceProvenance(root, 'not json')?.field, 'subject')
  git(root, 'update-ref', '-d', 'refs/remotes/origin/main')
  git(root, 'branch', '-m', 'main', 'elsewhere')
  assert.match(checkEvidenceProvenance(root, evidence({baseHead: onMain, testedTree: onMainTree}))?.reason, /fetch main/)
})

test('the CLI exits nonzero naming the field, and passes when no evidence is recorded', t => {
  const {root, branchOnly} = fixture(t)
  const absent = spawnSync(process.execPath, [cli, root], {encoding: 'utf8'})
  assert.equal(absent.status, 0)
  put(root, EVIDENCE_PATH, evidence({baseHead: branchOnly, testedTree: git(root, 'rev-parse', `${branchOnly}^{tree}`)}))
  const refused = spawnSync(process.execPath, [cli, root], {encoding: 'utf8'})
  assert.equal(refused.status, 1)
  assert.match(refused.stderr, /subject\.baseHead/)
})
