import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { AppShellRev6TabHost, type AppShellRev6Presentation } from '@harborline-software/ui-react'
import { presentation, runScenario, transition } from '../../../../projections/shared/ui/hlp.ui.app-shell/rev6/tab-identity-state.mjs'
import fixture from '../../../../conformance/hlp.ui.app-shell/rev6/state-scenarios.json'

function NativeScenario({ scenarioId }: { scenarioId: string }) {
  const scenario = fixture.scenarios.find(item => item.id === scenarioId)!
  const [state, setState] = useState(() => runScenario(scenario.events))
  const dispatch = (event: object) => setState(current => transition(current, event))
  const view = presentation(state)
  const identity = (person: string, tenant = 'north', workspace = 'work') => dispatch({ type: 'identity', owner: { person, tenant, workspace } })
  return <div>
    <h2>{scenario.name}</h2><p>Experimental rev6 tab-host slice. The host owns identity, drafts and acknowledgments.</p>
    <div aria-label="Host controls">
      <button onClick={() => identity('alice')}>Alice</button><button onClick={() => identity('bob')}>Bob</button>
      <button onClick={() => identity('alice', 'south')}>Other tenant</button><button onClick={() => identity('alice', 'north', 'other')}>Other workspace</button>
      <button onClick={() => dispatch({ type: 'sign-out' })}>Sign out</button>
      <button onClick={() => setState(runScenario(scenario.events))}>Reset scenario</button>
      <button disabled={!state.owner} onClick={() => dispatch({ type: 'navigate', destination: view?.destination === 'records' ? 'home' : 'records' })}>Change destination</button>
      <button disabled={!state.owner} onClick={() => dispatch({ type: 'open', kind: 'Details', target: 'record-a', preview: true })}>Preview record A</button>
      <button disabled={!state.owner} onClick={() => dispatch({ type: 'open', kind: 'Details', target: 'record-b', preview: true })}>Preview record B</button>
      <button disabled={!state.owner} onClick={() => dispatch({ type: 'open', kind: 'Assistant', target: 'conversation-a' })}>Assistant A</button>
      <button disabled={!state.owner} onClick={() => dispatch({ type: 'open', kind: 'Assistant', target: 'conversation-b' })}>Assistant B</button>
      <button disabled={!view?.saveRequest} onClick={() => { if (view?.saveRequest) dispatch({ type: 'saved', ...view.saveRequest }) }}>Acknowledge successful save</button>
    </div>
    <p>Current owner: {state.owner ? `${state.owner.person} / ${state.owner.tenant} / ${state.owner.workspace}` : 'Signed out'}</p>
    <AppShellRev6TabHost shellId="rev6-native" owner={state.owner} presentationOwner={state.owner} presentation={view as AppShellRev6Presentation | null} onIntent={dispatch} />
  </div>
}
const meta = { title: 'Experimental/App Shell Rev6 Native Tab Host', component: NativeScenario, args: { scenarioId: 'dirty-navigation' }, render: args => <NativeScenario key={args.scenarioId} {...args} /> } satisfies Meta<typeof NativeScenario>
export default meta
type Story = StoryObj<typeof meta>
export const DirtyNavigation: Story = {}
export const IdentityBoundary: Story = { args: { scenarioId: 'identity-boundary' } }
export const SignOut: Story = { args: { scenarioId: 'sign-out' } }
