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
if (build.status !== 0) throw new Error(`workflow TypeScript build failed\n${build.stdout}\n${build.stderr}`)

const fixture = JSON.parse(readFileSync(path.join(root, 'conformance/hlp.contracts.workflow/fixtures.yaml'), 'utf8'))
const wire = await import(pathToFileURL(path.join(packageRoot, 'dist/index.js')).href)
const authority = wire.createWorkflowAuthorityResolver(fixture.authority)
const roleVocabulary = wire.RoleVocabulary.fromApi(fixture.roleDefinitions)
const clone = value => JSON.parse(JSON.stringify(value))
const input = row => row.input === '$canonical' ? clone(fixture.canonical) : clone(row.input)
const mutate = (value, mutation = {}) => {
  if (mutation.actionTransition) value.actions[0].on = {transition: mutation.actionTransition}
  if (mutation.actionClassification) value.actions[0].classification = mutation.actionClassification
  if (mutation.transitionGuard) value.transitions[0].guard = mutation.transitionGuard
  if (mutation.terminalOutgoing) value.transitions.push({id: 't-terminal', from: 'Posted', on: 'approve', to: 'Rejected'})
  if (mutation.unknownActionRole) value.actions[0].requiredRoles = [{vocabulary: 'tax.roles', name: 'Missing'}]
  if (mutation.unknownTransitionRole) value.transitions[0].requiredRoles = [{vocabulary: 'tax.roles', name: 'Missing'}]
  return value
}

for (const row of fixture.cases) {
  if (row.operation === 'surface.exports') assert.equal(wire.workflowWireSurface.declarations.length, row.expected.declarations)
  else if (row.operation === 'closed.values') assert.deepEqual(wire.workflowWireSurface.closedValues, row.expected)
  else if (row.operation.startsWith('wire.')) {
    if (row.expectedError) assert.throws(() => wire.readWorkflowWire(row.type, input(row)), new RegExp(row.expectedError))
    else assert.deepEqual(wire.readWorkflowWire(row.type, input(row)), row.expected === 'same' ? input(row) : row.expected)
  } else {
    const definition = wire.readWorkflowWire('WorkflowDefinition', mutate(input(row), row.mutation))
    const result = wire.validateWorkflowAdmission(definition, authority, roleVocabulary)
    assert.equal(result.isValid, row.expected.isValid)
    for (const code of row.expected.codes) assert.ok(result.violations.some(violation => violation.code === code), code)
  }
}

process.stdout.write(`${JSON.stringify({schemaVersion: 1, moduleId: fixture.moduleId, status: 'PASS', projection: 'typescript', cases: fixture.cases.length}, null, 2)}\n`)
