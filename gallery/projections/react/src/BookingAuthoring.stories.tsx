import type { Meta, StoryObj } from '@storybook/react-vite'
import { BookingAuthoringEditor, type BookingCatalogue, type BookingDiagnosticSet, type BookingDraft } from '@harborline-software/ui-react'

type ScenarioId = 'booking-authoring.resource' | 'booking-authoring.refusals'
// Copies of conformance/hlp.ui.booking-authoring/fixtures.yaml (catalogue, the Resource case's expected body and the refusals case),
// written out identically in BookingAuthoringScenario.razor; the gallery's ARIA parity check holds the two lanes together.
const envelope = { cascade_layer: 'domain_package', provenance: { kind: 'package', package: 'training' }, retention_class: 'definition', legal_hold: false, requires: [], contract: { major: 1, minor: 0 } }
const catalogue: BookingCatalogue = { resourceTypes: [{ id: 'type.room', label: 'Room' }, 'type.nurse'], bookableTypes: [{ id: 'type.learner', label: 'Learner' }, 'type.note'], resources: [{ id: 'room', label: 'Training room' }, 'instructor'], maintenanceSources: [{ id: 'field.out_of_service', label: 'Out of service' }, 'field.under_repair'] }
const resource: BookingDraft = { identity: 'resource.training-room', versionId: '1.0.0', expectedRevision: '1', body: { envelope, kind: 'resource', name: 'Training room', from_type_id: 'type.room', capacity_kind: 'pool', pool_size: 2, setup_minutes: 10, cleanup_minutes: 15, maintenance_windows: ['field.out_of_service'], availability_from: 'supply.base-hours' } }
const refused: BookingDraft = { identity: 'resource.room', versionId: '1.0.0', expectedRevision: '3', body: { envelope, kind: 'resource', name: '', from_type_id: 'type.room', capacity_kind: 'pool', pool_size: 0, availability_from: 'supply.base-hours', extra: true } }
const refusals: BookingDiagnosticSet = { identity: 'resource.room', expectedRevision: '3', generation: 0, diagnostics: [{ code: 'booking.definition.member_unknown', pointer: '/extra' }, { code: 'booking.definition.name_required', pointer: '/name' }, { code: 'booking.resource.pool_size_invalid', pointer: '/pool_size' }] }
const copy: Record<ScenarioId, readonly [string, string]> = {
  'booking-authoring.resource': ['Pooled Resource', 'A pooled training room with buffers, a maintenance source and an availability source; fields stack at a narrow viewport.'],
  'booking-authoring.refusals': ['Refusals by field', 'Admission refusals render beside the field they name; a member the editor shows no field for is listed with its pointer.'],
}
function BookingAuthoringScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const refusing = scenarioId === 'booking-authoring.refusals'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><BookingAuthoringEditor value={refusing ? refused : resource} catalogue={catalogue} diagnostics={refusing ? refusals : undefined} onDraftChange={() => undefined} /></div></section>
}
const meta = { title: 'Platform/Booking authoring', component: BookingAuthoringScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof BookingAuthoringScenario>
export default meta
type Story = StoryObj<typeof meta>
export const PooledResource: Story = { name: 'Pooled Resource', args: { scenarioId: 'booking-authoring.resource' } }
export const RefusalsByField: Story = { name: 'Refusals by field', args: { scenarioId: 'booking-authoring.refusals' } }
