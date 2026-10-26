import assert from 'node:assert/strict'
import {execFileSync, spawnSync} from 'node:child_process'
import {copyFileSync, lstatSync, mkdirSync, mkdtempSync, readlinkSync, rmSync, statSync, symlinkSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, resolve} from 'node:path'
import test from 'node:test'
import {pathToFileURL} from 'node:url'

const root = resolve(import.meta.dirname, '../..')
const validator = resolve(root, 'tooling/validate-repository.mjs')
const preload = resolve(import.meta.dirname, 'fixtures/validate-repository-preload.mjs')
const controlError = 't684-validator-files/ordinary-control.txt: local package protocol'
// This literal policy violation proves that the added ordinary file was inspected,
// independently of the count. Split its spelling so this test's own source is benign.
const readable = {kind: 'file', text: `export const specimen = ${JSON.stringify('workspace:*')}\n`}

function sourceSnapshot(t) {
  const cwd = mkdtempSync(resolve(tmpdir(), 'validate-repository-files-'))
  t.after(() => rmSync(cwd, {recursive: true, force: true}))
  const paths = execFileSync('git', ['ls-files', '-z'], {cwd: root, encoding: 'utf8'}).split('\0').filter(Boolean)
  for (const path of paths) {
    const source = resolve(root, path), target = resolve(cwd, path)
    mkdirSync(dirname(target), {recursive: true})
    if (lstatSync(source).isSymbolicLink()) {
      symlinkSync(readlinkSync(source), target, statSync(source, {throwIfNoEntry: false})?.isDirectory() ? 'dir' : 'file')
    } else copyFileSync(source, target)
  }
  execFileSync('git', ['init', '--quiet'], {cwd})
  return cwd
}

function runTree(cwd, entries) {
  const run = spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, validator, '--allow-stale-gate'], {
    cwd,
    encoding: 'utf8',
    timeout: 60_000,
    maxBuffer: 4 * 1024 * 1024,
    env: {
      ...process.env,
      HARBORLINE_VALIDATE_REPOSITORY_FIXTURE: '',
      HARBORLINE_VALIDATE_REPOSITORY_FILES: JSON.stringify(entries),
    },
  })
  assert.ifError(run.error)
  assert.equal(run.signal, null, run.stderr)
  const report = JSON.parse(run.stdout)
  assert.equal(report.repository, 'harborline-platform')
  assert.ok(Array.isArray(report.errors))
  assert.ok(Number.isSafeInteger(report.counts.sourceFiles) && report.counts.sourceFiles > 0)
  return {run, report, sourceErrors: report.errors.filter(error => error.includes('t684-validator-files/'))}
}

test('actual validator CLI counts and inspects ordinary source while excluding local scaffolding', async t => {
  // Copy tracked source once: concurrent fixture output must not change this delta oracle.
  const cwd = sourceSnapshot(t)
  const baseline = runTree(cwd, {})
  assert.deepEqual(baseline.sourceErrors, [])
  const control = runTree(cwd, {'ordinary-control.txt': readable})
  assert.equal(control.report.counts.sourceFiles, baseline.report.counts.sourceFiles + 1)
  assert.deepEqual(control.sourceErrors, [controlError])
  assert.equal(control.report.status, 'FAIL')
  assert.equal(control.run.status, 1)

  const worktree = {kind: 'directory', entries: {
    worktrees: {kind: 'directory', entries: {
      'other-branch': {kind: 'directory', entries: {
        node_modules: {kind: 'symlink'},
        'branch-source.txt': readable,
      }},
    }},
  }}
  // Literal inputs from T684's accepted boundary; no production skip predicate is imported.
  const excluded = [
    ['a skipped name is excluded even when its dirent is not a directory', {node_modules: readable}],
    ['a skipped-name symlink is excluded', {node_modules: {kind: 'symlink'}}],
    ['an ordinary-name symlink is excluded', {'linked-dependencies': {kind: 'symlink'}}],
    ['.claude worktree sources and linked dependencies are excluded', {'.claude': worktree}],
    ['.codex worktree sources and linked dependencies are excluded', {'.codex': worktree}],
    ['a nested checkout with a .git file is excluded', {'nested-worktree': {kind: 'directory', entries: {
      '.git': {kind: 'file', text: 'gitdir: another-checkout\n'}, 'branch-source.txt': readable,
    }}}],
    ['a nested checkout with a .git directory is excluded', {'nested-clone': {kind: 'directory', entries: {
      '.git': {kind: 'directory', entries: {}}, 'branch-source.txt': readable,
    }}}],
  ]
  for (const [name, entries] of excluded) {
    await t.test(name, () => {
      const observed = runTree(cwd, {'ordinary-control.txt': readable, ...entries})
      assert.equal(observed.report.counts.sourceFiles, control.report.counts.sourceFiles)
      assert.deepEqual(observed.sourceErrors, [controlError])
      assert.equal(observed.report.status, 'FAIL')
      assert.equal(observed.run.status, 1)
    })
  }

  await t.test('an unreadable source produces a named JSON finding and inspection continues', () => {
    // Object insertion order puts the fault before the readable control in the walk.
    const observed = runTree(cwd, {
      'unreadable-source.txt': {kind: 'file', error: 'EACCES'},
      'ordinary-control.txt': readable,
    })
    assert.equal(observed.report.counts.sourceFiles, baseline.report.counts.sourceFiles + 2)
    assert.deepEqual(observed.sourceErrors, [
      'unreadable source path t684-validator-files/unreadable-source.txt: EACCES',
      controlError,
    ])
    assert.equal(observed.report.status, 'FAIL')
    assert.equal(observed.run.status, 1)
  })
})
