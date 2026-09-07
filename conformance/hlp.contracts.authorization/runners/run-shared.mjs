#!/usr/bin/env node

import assert from 'node:assert/strict'
import {spawnSync} from 'node:child_process'
import {readFileSync} from 'node:fs'
import path from 'node:path'
import {fileURLToPath, pathToFileURL} from 'node:url'

import {resolveCommand} from '../../../tooling/resolve-command.mjs'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..')
const packageRoot = path.join(root, 'projections/typescript/contracts/hlp.contracts.forms')
const npmBuild = resolveCommand('npm', ['run', 'build'])
const build = spawnSync(npmBuild.executable, npmBuild.args, {cwd: packageRoot, encoding: 'utf8', env: {...process.env, CI: '1', NO_COLOR: '1'}})
if (build.status !== 0) throw new Error(`authorization TypeScript build failed\n${build.stdout}\n${build.stderr}`)

const fixture = JSON.parse(readFileSync(path.join(root, 'conformance/hlp.contracts.authorization/fixtures.yaml'), 'utf8'))
const contracts = await import(pathToFileURL(path.join(packageRoot, 'dist/index.js')).href)
const clone = value => JSON.parse(JSON.stringify(value))

for (const row of fixture.cases) {
  const definitions = clone(fixture.definitions)
  if (row.operation === 'invalid') {
    if (row.mutation === 'duplicate') definitions.push(clone(definitions[0]))
    if (row.mutation === 'unknown-vocabulary') definitions[0].role.vocabulary = 'sys.roles'
    if (row.mutation === 'platform-taxonomy') definitions[0].role.vocabulary = 'tax.roles'
    if (row.mutation === 'domain-platform') { definitions[2].role.vocabulary = 'sys.platform-roles'; definitions[2].isSealed = true }
    if (row.mutation === 'unsealed-platform') definitions[0].isSealed = false
    if (row.mutation === 'sealed-domain') definitions[2].isSealed = true
    if (row.mutation === 'invented-platform-role') definitions[0].role.name = 'Captain'
    assert.throws(() => contracts.RoleVocabulary.fromApi(definitions), new RegExp(row.expectedError))
    continue
  }
  const vocabulary = contracts.RoleVocabulary.fromApi(definitions)
  if (row.operation === 'resolve') assert.equal(vocabulary.resolve(row.role)?.displayName ?? null, row.expected)
  else if (row.operation === 'require') {
    if (row.expectedError) assert.throws(() => vocabulary.require(row.role), new RegExp(row.expectedError))
    else assert.equal(vocabulary.require(row.role).displayName, row.expected)
  } else if (row.operation === 'allows') assert.equal(contracts.roleGateAllows(row.gate, vocabulary, row.held), row.expected)
}

process.stdout.write(`${JSON.stringify({schemaVersion: 1, moduleId: fixture.moduleId, status: 'PASS', projection: 'typescript', cases: fixture.cases.length}, null, 2)}\n`)
