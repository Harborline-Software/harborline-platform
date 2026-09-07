// Ticket 138 slice 3, fix 2. The gate installed five sub-projects and never the repository root,
// whose package.json declares the one dependency the tooling gates import (typescript, the parser
// behind tooling/gates/render-digest.mjs). A lane worktree always has a leftover root node_modules,
// so the tooling self-tests passed there and failed only in the receipt's detached tested tree,
// where five test files could not resolve 'typescript' and the failing names are not in the
// receipt's bounded log. This is the check that is red without the root install step.
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {resolve} from 'node:path'
import test from 'node:test'

import {requiredStepIds} from '../gate-contract.mjs'

const root = resolve(import.meta.dirname, '../..')
const gate = readFileSync(resolve(root, 'tooling/run-phase-4-gate.mjs'), 'utf8')

test('the gate clean-installs the repository root, before the tooling self-tests read it', () => {
  const [line] = /^ *run\('root-clean-install', 'npm', \['ci'.*$/m.exec(gate) ?? []
  assert.ok(line, 'no step clean-installs the repository root; tooling imports would resolve only by accident')
  assert.match(line, /, root\)$/,
    "the root install must run in the repository root, not in a sub-project's directory")
  assert.ok(gate.indexOf(line) < gate.indexOf("run('tooling-selftests'"),
    'the root install must precede the step whose tests import the root dependency')
})

test('the root install is part of the gate contract, so a receipt without it is refused', () => {
  assert.ok(requiredStepIds.includes('root-clean-install'))
  assert.ok(requiredStepIds.indexOf('root-clean-install') < requiredStepIds.indexOf('tooling-selftests'))
})

// The reason the step has to exist at all: the root package.json is the only place the tooling
// gates' dependency is declared, and the gate is the only thing that installs it.
test('the tooling gates depend on a package only the root package.json declares', () => {
  const declared = JSON.parse(readFileSync(resolve(root, 'package.json'), 'utf8')).devDependencies ?? {}
  const digest = readFileSync(resolve(root, 'tooling/gates/render-digest.mjs'), 'utf8')
  assert.match(digest, /^import ts from 'typescript'$/m)
  assert.ok(declared.typescript, 'typescript must stay a root devDependency for the root install to provide it')
})
