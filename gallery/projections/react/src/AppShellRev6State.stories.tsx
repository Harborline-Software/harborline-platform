import type { Meta, StoryObj } from '@storybook/react-vite'
// Experimental source seam: intentionally does not replace the packed rev5 AppShell.
import { runScenario, presentation } from '../../../../projections/shared/ui/hlp.ui.app-shell/rev6/tab-identity-state.mjs'
import fixture from '../../../../conformance/hlp.ui.app-shell/rev6/state-scenarios.json'

function StateScenario({ scenarioId }: { scenarioId: string }) {
  const scenario = fixture.scenarios.find(item => item.id === scenarioId)!
  const state = runScenario(scenario.events)
  const view = presentation(state)
  return <main data-revision="6" data-scenario={scenarioId}>
    <h1>{scenario.name}</h1>
    <p>Experimental rev6 state slice. Live App Shell remains rev5.</p>
    <p>Identity: {state.owner ? `${state.owner.person} / ${state.owner.tenant} / ${state.owner.workspace}` : 'Signed out'}</p>
    <p>Destination: {view?.destination ?? 'None'}</p>
    <ul>{view?.tabs.map((tab: { id: string; kind: string; target: string; dirty: boolean; preview: boolean }) => <li key={tab.id}>{tab.kind}: {tab.target}  -  {tab.dirty ? 'Unsaved' : tab.preview ? 'Preview' : 'Permanent'}</li>)}</ul>
    <p>{view?.tabs.length ?? 0} visible tabs</p>
  </main>
}
const meta = { title: 'Experimental/App Shell Rev6 State', component: StateScenario, args: { scenarioId: 'dirty-navigation' } } satisfies Meta<typeof StateScenario>
export default meta
type Story = StoryObj<typeof meta>
export const DirtyNavigation: Story = {}
export const IdentityBoundary: Story = { args: { scenarioId: 'identity-boundary' } }
export const SignOut: Story = { args: { scenarioId: 'sign-out' } }
