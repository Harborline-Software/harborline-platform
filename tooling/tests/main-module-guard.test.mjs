import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {mkdtempSync, readFileSync, readdirSync, rmSync, symlinkSync} from 'node:fs'
import {tmpdir} from 'node:os'
import path from 'node:path'
import test from 'node:test'

import {evaluateStepStdout} from '../gate-step-evidence.mjs'

const repositoryRoot = path.resolve(import.meta.dirname, '../..')

// Node realpaths the entry module, so `import.meta.url` is the RESOLVED path while `process.argv[1]`
// is the path as typed. A main-module guard that compares the two is FALSE whenever the CLI is
// reached through a symlink or junction: the script body never runs, and it exits 0 in silence.
// These tests invoke real CLIs through a linked path; they are red against the argv[1] guard.
function linkedRepositoryRoot(t) {
  const scratch = mkdtempSync(path.join(tmpdir(), 'main-module-guard-'))
  const link = path.join(scratch, 'repo-link')
  try {
    symlinkSync(repositoryRoot, link, process.platform === 'win32' ? 'junction' : 'dir')
  } catch (error) {
    rmSync(scratch, {recursive: true, force: true})
    if (error.code === 'EPERM' || error.code === 'EACCES') return undefined
    throw error
  }
  t.after(() => rmSync(scratch, {recursive: true, force: true}))
  return link
}

test('a CLI invoked through a linked path still runs its main body', t => {
  const link = linkedRepositoryRoot(t)
  if (!link) return t.skip('symlink creation not permitted on this host')
  const result = spawnSync(process.execPath, [path.join(link, 'tooling/resolve-dotnet.mjs')], {encoding: 'utf8'})
  assert.equal(result.status, 0, result.stderr)
  assert.ok(result.stdout.trim(), 'CLI produced no stdout through the linked path')
  assert.equal(typeof JSON.parse(result.stdout).version, 'string')
})

test('the prop-vocabulary gate step is a FAIL, not a silent pass, when the scan produces no output', t => {
  const link = linkedRepositoryRoot(t)
  if (!link) return t.skip('symlink creation not permitted on this host')
  const result = spawnSync(process.execPath,
    [path.join(link, 'tooling/gates/scan-prop-vocabulary.mjs'), '--json', repositoryRoot], {encoding: 'utf8'})
  const outcome = evaluateStepStdout({json: true, status: result.status, stdout: result.stdout})
  assert.equal(outcome.status, 0, `scan step is not green through a linked path: ${outcome.failure ?? result.stderr}`)
  assert.ok(outcome.report.checked > 0, 'the scan checked nothing')
  // The class: had the guard skipped the body, exit 0 with empty stdout would now be recorded RED.
  assert.equal(evaluateStepStdout({json: true, status: 0, stdout: ''}).status, 1)
})

test('no tooling entry point reintroduces the argv[1] main-module guard', () => {
  const offenders = []
  const walk = directory => {
    for (const entry of readdirSync(directory, {withFileTypes: true})) {
      if (entry.name === 'node_modules' || entry.name === 'tests') continue
      const full = path.join(directory, entry.name)
      if (entry.isDirectory()) walk(full)
      else if (entry.name.endsWith('.mjs') && readFileSync(full, 'utf8').includes('process.argv[1]')) {
        offenders.push(path.relative(repositoryRoot, full))
      }
    }
  }
  walk(path.join(repositoryRoot, 'tooling'))
  assert.deepEqual(offenders, [], 'use `import.meta.main`; an argv[1] guard is false under a linked path')
})
