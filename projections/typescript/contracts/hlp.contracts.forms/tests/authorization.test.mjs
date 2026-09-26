import assert from 'node:assert/strict'
import test from 'node:test'

import {RoleVocabulary, roleGateAllows, submitGateIsWellFormed} from '../dist/index.js'

const auditor = {vocabulary: 'sys.platform-roles', name: 'auditor'}
const definitions = [{roleDefinitionId: '1', role: auditor, displayName: 'Auditor', owner: {kind: 'Platform', ownerId: 'harborline-platform'}, isSealed: true}]

test('uses API-qualified roles and fails closed for unknown gates', () => {
  const vocabulary = RoleVocabulary.fromApi(definitions)
  assert.equal(roleGateAllows({requiredRoles: [auditor]}, vocabulary, {roles: [auditor]}), true)
  assert.equal(roleGateAllows({requiredRoles: [{vocabulary: 'tax.roles', name: 'missing'}]}, vocabulary, {roles: [auditor]}), false)
  assert.equal(roleGateAllows({requiredRoles: [{vocabulary: 'sys.roles', name: 'auditor'}]}, vocabulary, {roles: [auditor]}), false)
  assert.throws(() => RoleVocabulary.fromApi([{...definitions[0], role: {vocabulary: 'sys.platform-roles', name: 'Captain'}}]), /invalid-platform-role/)
})

test('role and standing shapes remain distinct', () => {
  assert.deepEqual(JSON.parse(JSON.stringify(auditor)), {vocabulary: 'sys.platform-roles', name: 'auditor'})
  assert.deepEqual(JSON.parse(JSON.stringify({name: 'Compliant'})), {name: 'Compliant'})
})

test('a submit gate names exactly one of role, standing or capability (T-755)', () => {
  assert.equal(submitGateIsWellFormed({role: auditor}), true)
  assert.equal(submitGateIsWellFormed({standing: {name: 'author'}}), true)
  assert.equal(submitGateIsWellFormed({capability: {name: 'records:write'}}), true)
  assert.equal(submitGateIsWellFormed({}), false)
  assert.equal(submitGateIsWellFormed({role: auditor, capability: {name: 'records:write'}}), false)
})
