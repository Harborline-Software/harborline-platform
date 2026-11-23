import type { Meta, StoryObj } from '@storybook/react'
import { useState } from 'react'
import { SchedulingAuthoringEditor, type ScheduleBody, type ScheduleCatalogue } from '@harborline-software/ui-react'
import '@harborline-software/ui-react/style.css'
const catalogue: ScheduleCatalogue = { types: [{id:'types.asset',label:'Asset'}], legality: [{id:'legality.safety',label:'Safety rules'}], backends: [{id:'prototype.exhaustive-proof.v2',label:'Prototype',guarantees:['feasible_proof']}], modules:[{id:'precedence',label:'Precedence',scope:'chain'}] }
const bound: ScheduleBody = {name:'Inspection',starts:{kind:'form',target_id:'forms.inspection'},due_date:'2027-01-01T00:00:00Z',problem_class:'Project',solver:{backend_id:'prototype.exhaustive-proof.v2',parameters:{seed:7},guarantee:'feasible_proof',work_budget:10},modules:[{module:'precedence',parameters:{constraints:[{id:'after',beforeActivityId:'a',afterActivityId:'b',minimumGapSlots:1}]}}]}
const copy: Record<string, readonly [string,string]> = {"scheduling-authoring.from-empty": ["Author from empty", "Create a Schedule from nothing using named native controls."], "scheduling-authoring.bound": ["Bound solver and module", "Choose a registered backend and explicitly opt into an admitted module."], "scheduling-authoring.refusals": ["Refusal list", "Server refusals appear before publish in a live region."]}
function SchedulingAuthoringScenario({ scenarioId }: { scenarioId: string }) {
 const [body, setBody] = useState<ScheduleBody>(scenarioId === 'scheduling-authoring.bound' ? bound : {})
 const [title,description] = copy[scenarioId]
 return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><SchedulingAuthoringEditor value={body} onChange={setBody} onCreate={setBody} catalogue={catalogue} refusals={scenarioId === 'scheduling-authoring.refusals' ? [{code:'scheduling.definition.name_required',pointer:'/name'}] : []} /></div></section>
}
const meta = {title:'Platform/Scheduling authoring',component:SchedulingAuthoringScenario,tags:['autodocs'],parameters:{layout:'padded',controls:{disable:true}}} satisfies Meta<typeof SchedulingAuthoringScenario>
export default meta
type Story = StoryObj<typeof meta>
export const AuthorFromEmpty: Story = {name:'Author from empty',args:{scenarioId:'scheduling-authoring.from-empty'}}
export const BoundSolverAndModule: Story = {name:'Bound solver and module',args:{scenarioId:'scheduling-authoring.bound'}}
export const RefusalList: Story = {name:'Refusal list',args:{scenarioId:'scheduling-authoring.refusals'}}
