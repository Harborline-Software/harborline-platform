#!/usr/bin/env node
// Proves reusableStepInputs covers every script a reusable step actually runs.
//
// hashStepInputs only hashes what it is told to hash. A declaration that omits a script the step
// executes does not fail loudly -- it produces an unchanged hash for a changed step, and the gate
// reuses a recorded PASS the tested tree no longer justifies. That is a green gate that proved
// nothing, so the declaration is checked here rather than trusted.

import assert from 'node:assert/strict'
import {readFileSync, writeFileSync, mkdirSync, mkdtempSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {dirname, posix, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'
import {test} from 'node:test'

import {reusableStepEntryScript, reusableStepInputs} from '../gate-step-evidence.mjs'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
// `from './x.mjs'` and bare `import './x.mjs'` reach a script through the module graph; a
// 'tooling/x.mjs' literal reaches one through spawn or a required-file assertion. Both make that
// file an input, and neither is visible to the other's pattern.
const RELATIVE_IMPORT = /(?:from|import)\s*\(?\s*['"](\.[^'"]+)['"]/g
const TOOLING_PATH = /['"](tooling\/[A-Za-z0-9._-]+\.mjs)['"]/g

function scriptClosure(repositoryRoot, entry) {
  const seen = new Set()
  const queue = [entry]
  while (queue.length > 0) {
    const relativePath = queue.pop()
    if (seen.has(relativePath)) continue
    seen.add(relativePath)
    const source = readFileSync(resolve(repositoryRoot, relativePath), 'utf8')
    for (const match of source.matchAll(RELATIVE_IMPORT)) queue.push(posix.join(posix.dirname(relativePath), match[1]))
    for (const match of source.matchAll(TOOLING_PATH)) queue.push(match[1])
  }
  return [...seen].sort()
}

function uncovered(closure, pathspecs) {
  const declared = new Set(pathspecs)
  return closure.filter(path => !declared.has(path) && !pathspecs.some(spec => path.startsWith(`${spec}/`)))
}

test('every reusable step declares the scripts it runs', () => {
  for (const [stepId, entry] of Object.entries(reusableStepEntryScript)) {
    const pathspecs = reusableStepInputs[stepId]
    assert.ok(pathspecs, `${stepId} has an entry script but no declared inputs`)
    const closure = scriptClosure(root, entry)
    assert.ok(closure.includes(entry), `${stepId} closure lost its own entry script`)
    assert.deepEqual(uncovered(closure, pathspecs), [], `${stepId} runs scripts its declaration does not cover`)
  }
})

test('every declared step has an entry script', () => {
  assert.deepEqual(
    Object.keys(reusableStepInputs).sort(),
    Object.keys(reusableStepEntryScript).sort(),
    'a step declaring inputs with no entry script cannot have those inputs checked',
  )
})

test('an undeclared import is caught', () => {
  // Positive control. Without it this file passes just as happily when scriptClosure silently
  // returns nothing but the entry -- the exact failure it exists to prevent.
  const scratch = mkdtempSync(resolve(tmpdir(), 'hlp-step-inputs-'))
  mkdirSync(resolve(scratch, 'tooling'), {recursive: true})
  writeFileSync(resolve(scratch, 'tooling/entry.mjs'), "import {x} from './helper.mjs'\nimport './spawned.mjs'\nconst s = 'tooling/spawned-by-path.mjs'\n")
  writeFileSync(resolve(scratch, 'tooling/helper.mjs'), 'export const x = 1\n')
  writeFileSync(resolve(scratch, 'tooling/spawned.mjs'), '\n')
  writeFileSync(resolve(scratch, 'tooling/spawned-by-path.mjs'), '\n')
  const closure = scriptClosure(scratch, 'tooling/entry.mjs')
  assert.deepEqual(closure, [
    'tooling/entry.mjs', 'tooling/helper.mjs', 'tooling/spawned-by-path.mjs', 'tooling/spawned.mjs',
  ], 'the walker must follow static imports, bare imports, and spawned tooling paths alike')
  assert.deepEqual(uncovered(closure, ['tooling/entry.mjs']), [
    'tooling/helper.mjs', 'tooling/spawned-by-path.mjs', 'tooling/spawned.mjs',
  ], 'a declaration naming only the entry must report the three scripts it omits')
  assert.deepEqual(uncovered(closure, ['tooling']), [], 'a directory pathspec covers the files beneath it')
})
