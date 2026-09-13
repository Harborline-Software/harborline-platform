import type { Meta, StoryObj } from '@storybook/react-vite'
import { ViewRuntime, type ViewRenderPlan, type ViewRuntimeRow } from '@harborline-software/ui-react'

type ScenarioId = 'view-runtime.grid' | 'view-runtime.unknown-kind' | 'view-runtime.empty-rows' | 'view-runtime.long-content'
const gridPlan: ViewRenderPlan = { definitionHash: 'sha256:view-assets', definitionId: 'view-assets', definitionVersion: '1', packKey: 'harborline.platform', packVersion: '1.0.0', definitionKind: 'ViewDefinition', bindings: { viewKind: 'views.entity-list/grid', parameters: { fields: [{ id: 'asset', label: 'Asset' }, { id: 'status', label: 'Status' }, { id: 'owner', label: 'Owner' }] } } }
const unknownPlan: ViewRenderPlan = { ...gridPlan, bindings: { ...gridPlan.bindings, viewKind: 'views.unknown' } }
const gridRows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'Pier', status: 'Open', owner: 'Riley' }, { id: 'a2', asset: 'Pump', status: 'Review', owner: 'Morgan' }]
const longContentRows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'A caller-owned value that is deliberately long enough to exercise the runtime handoff.', status: 'Open', owner: 'Riley' }]
const copy: Record<ScenarioId, readonly [string, string]> = {
  'view-runtime.grid': ['Grid definition', 'A three-field entity-list definition renders two caller-supplied rows through the shared data grid.'],
  'view-runtime.unknown-kind': ['Unknown kind', 'An unsupported definition kind is inert and silent.'],
  'view-runtime.empty-rows': ['Empty rows', 'A known grid definition keeps its declared columns while the caller supplies no rows.'],
  'view-runtime.long-content': ['Long content', 'A long caller-owned field value remains available to the delegated grid without runtime truncation or replacement.'],
}
function ViewRuntimeScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const plan = scenarioId === 'view-runtime.unknown-kind' ? unknownPlan : gridPlan
  const rows = scenarioId === 'view-runtime.empty-rows' ? [] : scenarioId === 'view-runtime.long-content' ? longContentRows : gridRows
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}><header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header><div className="hl-gallery-stage"><ViewRuntime plan={plan} empty={scenarioId === 'view-runtime.empty-rows' ? 'No results.' : undefined} rows={rows} /></div></section>
}
const meta = { title: 'Platform/View Runtime', component: ViewRuntimeScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof ViewRuntimeScenario>
export default meta
type Story = StoryObj<typeof meta>
export const GridDefinition: Story = { name: 'Grid definition', args: { scenarioId: 'view-runtime.grid' } }
export const UnknownKind: Story = { name: 'Unknown kind', args: { scenarioId: 'view-runtime.unknown-kind' } }
export const EmptyRows: Story = { name: 'Empty rows', args: { scenarioId: 'view-runtime.empty-rows' } }
export const LongContent: Story = { name: 'Long content', args: { scenarioId: 'view-runtime.long-content' } }
