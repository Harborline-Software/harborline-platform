import type { Meta, StoryObj } from '@storybook/react-vite'
import { SchedulingAuthoringEditor, type SchedulingCatalogue, type SchedulingDiagnosticSet, type SchedulingDraft } from '@harborline-software/ui-react'

type ScenarioId = 'scheduling-authoring.chain' | 'scheduling-authoring.solver' | 'scheduling-authoring.refusals'
// Copies of conformance/hlp.ui.scheduling-authoring/fixtures.yaml (catalogue, the chain and solver cases' expected bodies, the chain's trigger event left unnamed, and the
// refusal-list case), written out identically in SchedulingAuthoringScenario.razor; the gallery's ARIA parity check holds the two lanes together.
const catalogue: SchedulingCatalogue = { types: [{ id: 'types.vehicle', label: 'Vehicle' }, { id: 'types.patient', label: 'Patient' }], forms: [{ id: 'forms.inspection', label: 'Inspection form' }], workflows: [{ id: 'workflows.check', label: 'Post-operative check workflow' }], workItems: ['work.oil-change'], legalityRuleSets: [{ id: 'legality.hours-of-service', label: 'Hours of service' }], backends: [{ id: 'prototype.exhaustive-proof.v2', label: 'Exhaustive proof (prototype)', guarantees: ['feasible_proof'] }, { id: 'prototype.greedy-first-fit.v1', label: 'Greedy first fit (prototype)', guarantees: ['bounded_heuristic'] }] }
const chain: SchedulingDraft = { identity: 'schedule.post-op', versionId: '1.0.0', expectedRevision: '1', body: { name: 'Post-operative checks', on_type_id: 'types.patient', starts: { kind: 'workflow', target_id: 'workflows.check' }, basis: { kind: 'event_anchored_chain', nodes: [{ node_id: 'first-check', offset: 'PT4H', tolerance: { early: 'PT30M', late: 'PT1H' } }, { node_id: 'second-check', after_node_id: 'first-check', offset: 'P1D', tolerance: { early: 'PT2H', late: 'PT2H' } }] }, missed_behaviour: 'record_and_replan' } }
const solver: SchedulingDraft = { identity: 'schedule.workshop', versionId: '1.0.0', expectedRevision: '1', body: { name: 'Workshop plan', starts: { kind: 'work_item', target_id: 'work.oil-change' }, due_date: '2027-01-01T00:00:00Z', problem_class: 'Project', solver: { backend_id: 'prototype.exhaustive-proof.v2', parameters: {}, guarantee: 'feasible_proof', work_budget: 10 }, modules: [{ module: 'precedence', parameters: { constraints: [{ id: 'print-after-layout', beforeActivityId: 'layout', afterActivityId: 'print', minimumGapSlots: 0 }] } }] } }
const refused: SchedulingDraft = { identity: 'schedule.inspection', versionId: '1.0.0', expectedRevision: '2', body: { name: '', starts: { kind: 'form', target_id: 'forms.inspection' }, due_date: '2027-01-01T00:00:00Z', problem_class: 'Project', solver: { backend_id: 'missing', parameters: {}, guarantee: 'feasible_proof', work_budget: 10 }, input_plan_id: 'plan.upstream' } }
const refusals: SchedulingDiagnosticSet = { identity: 'schedule.inspection', expectedRevision: '2', generation: 0, diagnostics: [{ code: 'scheduling.definition.name_required', pointer: '/name' }, { code: 'scheduling.definition.backend_unsupported', pointer: '/solver/backend_id' }, { code: 'scheduling.definition.plan_input_unsupported', pointer: '/input_plan_id' }, { code: 'scheduling.definition.body_invalid', pointer: '' }] }
const copy: Record<ScenarioId, readonly [string, string]> = {
  'scheduling-authoring.chain': ['Event-anchored chain', 'Post-operative checks as a chain whose trigger event the author still names; each node states in words what it follows, and fields stack at a narrow viewport.'],
  'scheduling-authoring.solver': ['Solver and module', 'A Project schedule bound to a registered backend with its declared guarantee, a work budget and one precedence constraint.'],
  'scheduling-authoring.refusals': ['Refusal list', 'Every admission refusal is listed before publish with its pointer and, when shown, its field; a pointer with no field is listed as given.'],
}
const drafts: Record<ScenarioId, SchedulingDraft> = { 'scheduling-authoring.chain': chain, 'scheduling-authoring.solver': solver, 'scheduling-authoring.refusals': refused }
function SchedulingAuthoringScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><SchedulingAuthoringEditor value={drafts[scenarioId]} catalogue={catalogue} diagnostics={scenarioId === 'scheduling-authoring.refusals' ? refusals : undefined} onDraftChange={() => undefined} /></div></section>
}
const meta = { title: 'Platform/Schedule authoring', component: SchedulingAuthoringScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SchedulingAuthoringScenario>
export default meta
type Story = StoryObj<typeof meta>
export const EventAnchoredChain: Story = { name: 'Event-anchored chain', args: { scenarioId: 'scheduling-authoring.chain' } }
export const SolverAndModule: Story = { name: 'Solver and module', args: { scenarioId: 'scheduling-authoring.solver' } }
export const RefusalList: Story = { name: 'Refusal list', args: { scenarioId: 'scheduling-authoring.refusals' } }
