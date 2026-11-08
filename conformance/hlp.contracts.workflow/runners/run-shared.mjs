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
  if (mutation.envelope) value.envelope = clone(fixture.envelope)
  if (Object.hasOwn(mutation, 'envelopeContract')) { if (mutation.envelopeContract === null) delete value.envelope.contract; else value.envelope.contract = mutation.envelopeContract }
  if (mutation.envelopeIdentity) value.envelope.identity = mutation.envelopeIdentity
  if (mutation.envelopeRetentionClass) value.envelope.retentionClass = mutation.envelopeRetentionClass
  if (mutation.envelopeRequirement) value.envelope.requires = [{capability: mutation.envelopeRequirement}]
  if (mutation.transitionRole) value.transitions[0].requiredRoles = [mutation.transitionRole]
  if (mutation.actionRole) value.actions[0].requiredRoles = [mutation.actionRole]
  if (mutation.transitionStanding) value.transitions[0].requiredStandings = [{name: mutation.transitionStanding}]
  if (mutation.actionStanding) value.actions[0].requiredStandings = [{name: mutation.actionStanding}]
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
    const result = wire.validateWorkflowAdmission(definition, authority, roleVocabulary, row.window ? fixture.contractWindow : undefined)
    assert.equal(result.isValid, row.expected.isValid, row.id)
    assert.deepEqual([...new Set(result.violations.map(violation => violation.code))].sort(), [...row.expected.codes].sort(), row.id)
  }
}

process.stdout.write(`${JSON.stringify({schemaVersion: 1, moduleId: fixture.moduleId, status: 'PASS', projection: 'typescript', cases: fixture.cases.length}, null, 2)}\n`)
