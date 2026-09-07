import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import test from 'node:test'
import {createWorkflowAuthorityResolver,readWorkflowWire,RoleVocabulary,validateWorkflowAdmission,workflowWireSurface} from '../dist/index.js'
const fixture=JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.contracts.workflow/fixtures.yaml',import.meta.url),'utf8'))
const authority=createWorkflowAuthorityResolver(fixture.authority)
const roleVocabulary=RoleVocabulary.fromApi(fixture.roleDefinitions)
const clone=value=>JSON.parse(JSON.stringify(value))
function input(row){return row.input==='$canonical'?clone(fixture.canonical):clone(row.input)}
function mutate(value,mutation){if(mutation.actionTransition)value.actions[0].on={transition:mutation.actionTransition};if(mutation.actionClassification)value.actions[0].classification=mutation.actionClassification;if(mutation.transitionGuard)value.transitions[0].guard=mutation.transitionGuard;if(mutation.terminalOutgoing)value.transitions.push({id:'t-terminal',from:'Posted',on:'approve',to:'Rejected'});if(mutation.unknownActionRole)value.actions[0].requiredRoles=[{vocabulary:'tax.roles',name:'Missing'}];if(mutation.unknownTransitionRole)value.transitions[0].requiredRoles=[{vocabulary:'tax.roles',name:'Missing'}];return value}
for(const row of fixture.cases){test(row.id,()=>{if(row.operation==='surface.exports'){assert.equal(workflowWireSurface.declarations.length,row.expected.declarations);return}if(row.operation==='closed.values'){assert.deepEqual(workflowWireSurface.closedValues,row.expected);return}if(row.operation.startsWith('wire.')){if(row.expectedError){assert.throws(()=>readWorkflowWire(row.type,input(row)),new RegExp(row.expectedError));return}assert.deepEqual(readWorkflowWire(row.type,input(row)),row.expected==='same'?input(row):row.expected);return}const value=readWorkflowWire('WorkflowDefinition',mutate(input(row),row.mutation??{}));const result=validateWorkflowAdmission(value,authority,roleVocabulary);assert.equal(result.isValid,row.expected.isValid);for(const code of row.expected.codes)assert.ok(result.violations.some(v=>v.code===code),code)})}
