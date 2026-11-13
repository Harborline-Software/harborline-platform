import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import test from 'node:test'
import {createWorkflowAuthorityResolver,readWorkflowWire,RoleVocabulary,validateWorkflowAdmission,workflowWireSurface} from '../dist/index.js'
const fixture=JSON.parse(readFileSync(new URL('../../../../../conformance/hlp.contracts.workflow/fixtures.yaml',import.meta.url),'utf8'))
const authority=createWorkflowAuthorityResolver(fixture.authority)
const roleVocabulary=RoleVocabulary.fromApi(fixture.roleDefinitions)
const clone=value=>JSON.parse(JSON.stringify(value))
function input(row){return row.input==='$canonical'?clone(fixture.canonical):clone(row.input)}
// T-1176 Q1: role and standing mutations target the first HumanAction transition; autonomous* keys target transitions[0].
function gated(value){const human=new Set(value.triggers.filter(t=>t.kind==='HumanAction').map(t=>t.id));return value.transitions.find(t=>human.has(t.on))}
function mutate(value,mutation){if(mutation.stateDeclarations)Object.assign(value.states.find(state=>state.kind==='Normal'),{fieldRequirements:[{field:'invoice.amount',condition:'required.amount'}],locks:'dates only',escalation:{schedule:'review-overdue'}});if(mutation.actionTransition)value.actions[0].on={transition:mutation.actionTransition};if(mutation.actionClassification)value.actions[0].classification=mutation.actionClassification;if(mutation.transitionGuard)value.transitions[0].guard=mutation.transitionGuard;if(mutation.terminalOutgoing)value.transitions.push({id:'t-terminal',from:'Posted',on:'approve',to:'Rejected'});if(mutation.unknownActionRole)value.actions[0].requiredRoles=[{vocabulary:'tax.roles',name:'Missing'}];if(mutation.unknownTransitionRole)gated(value).requiredRoles=[{vocabulary:'tax.roles',name:'Missing'}];if(mutation.envelope)value.envelope=clone(fixture.envelope);if(Object.hasOwn(mutation,'envelopeContract')){if(mutation.envelopeContract===null)delete value.envelope.contract;else value.envelope.contract=mutation.envelopeContract}if(mutation.envelopeIdentity)value.envelope.identity=mutation.envelopeIdentity;if(mutation.envelopeRetentionClass)value.envelope.retentionClass=mutation.envelopeRetentionClass;if(mutation.envelopeRequirement)value.envelope.requires=[{capability:mutation.envelopeRequirement}];if(mutation.transitionRole)gated(value).requiredRoles=[mutation.transitionRole];if(mutation.actionRole)value.actions[0].requiredRoles=[mutation.actionRole];if(mutation.transitionStanding)gated(value).requiredStandings=[{name:mutation.transitionStanding}];if(mutation.actionState)value.actions[0].on={state:mutation.actionState};if(mutation.autonomousEdgeInto){const auto=value.triggers.find(t=>t.kind!=='HumanAction').id;value.transitions.push({id:'t-autonomous-into',from:gated(value).from,on:auto,to:mutation.autonomousEdgeInto})}if(mutation.autonomousTransitionRole)value.transitions[0].requiredRoles=[mutation.autonomousTransitionRole];if(mutation.autonomousTransitionStanding)value.transitions[0].requiredStandings=[{name:mutation.autonomousTransitionStanding}];if(mutation.actionStanding)value.actions[0].requiredStandings=[{name:mutation.actionStanding}];return value}
for(const row of fixture.cases){test(row.id,()=>{if(row.operation==='surface.exports'){assert.equal(workflowWireSurface.declarations.length,row.expected.declarations);return}if(row.operation==='closed.values'){assert.deepEqual(workflowWireSurface.closedValues,row.expected);return}if(row.operation.startsWith('wire.')){if(row.expectedError){assert.throws(()=>readWorkflowWire(row.type,input(row)),new RegExp(row.expectedError));return}assert.deepEqual(readWorkflowWire(row.type,input(row)),row.expected==='same'?input(row):row.expected);return}const value=readWorkflowWire('WorkflowDefinition',mutate(input(row),row.mutation??{}));const result=validateWorkflowAdmission(value,authority,roleVocabulary,row.window?fixture.contractWindow:undefined);assert.equal(result.isValid,row.expected.isValid);assert.deepEqual([...new Set(result.violations.map(v=>v.code))].sort(),[...row.expected.codes].sort())})}

test('workflows-ck-22 admission refuses an invalid directly constructed notification',()=>{
  const value=clone(fixture.canonical)
  value.actions[0].notification={eventKey:'',tier:'decision',batchingWindow:'PT10M'}
  const result=validateWorkflowAdmission(value,authority,roleVocabulary)
  assert.ok(result.violations.some(v=>v.code==='workflow.invalid_notification'))
})

test('workflows-ck-22 admission still reports an unclassified action without throwing',()=>{
  const value=clone(fixture.canonical)
  delete value.actions[0].classification
  const result=validateWorkflowAdmission(value,authority,roleVocabulary)
  assert.ok(result.violations.some(v=>v.code==='workflow.admission.action_unclassified'))
})

test('workflows-ck-20 admission reports malformed directly constructed field requirements',()=>{
  const value=clone(fixture.canonical)
  value.states[0].fieldRequirements=[{field:'invoice.amount'}]
  const result=validateWorkflowAdmission(value,authority,roleVocabulary)
  assert.ok(result.violations.some(v=>v.code==='missing-required-property'))
})
