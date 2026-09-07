import type { Meta, StoryObj } from '@storybook/react-vite'
import { Collapsible } from '@harborline-software/ui-react'

type ScenarioId = 'collapsible.defaults' | 'collapsible.states' | 'collapsible.rtl-theme' | 'collapsible.content'

function CollapsibleScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const states = scenarioId === 'collapsible.states'
  const rtl = scenarioId === 'collapsible.rtl-theme'
  const heading = states ? 'States and interactions' : rtl ? 'RTL and theme' : 'Defaults and relationships'

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={rtl ? 'dark' : undefined} dir={rtl ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{heading}</h2><p>Titled disclosure state and independent header actions use one bounded Harborline contract.</p></header>
    <div className="hl-gallery-stage">
      {scenarioId === 'collapsible.content' ? <Collapsible
        defaultOpen
        headerActions={<button className="hl-gallery-button" type="button">{'1,284,905'}</button>}
        subtitle={'Bay 4 <grid C-7> & 8'}
        title="Awaiting third-party structural certification review"
      >
        <p>{'Ordnance Survey — Niño Ångström'}</p>
      </Collapsible> : <Collapsible
        defaultOpen={!states}
        disabled={states}
        headerActions={<button className="hl-gallery-button" type="button">{rtl ? 'تحرير' : 'Edit'}</button>}
        subtitle={rtl ? 'تكوين اختياري' : 'Optional configuration'}
        title={rtl ? 'إعدادات التطبيق' : 'Application settings'}
      >
        <p>Caller-owned panel content remains available when the disclosure is open.</p>
      </Collapsible>}
    </div>
  </section>
}

const meta = { title: 'Platform/Collapsible', component: CollapsibleScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof CollapsibleScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { name: 'Defaults and relationships', args: { scenarioId: 'collapsible.defaults' } }
export const States: Story = { name: 'States and interactions', args: { scenarioId: 'collapsible.states' } }
export const RtlAndTheme: Story = { name: 'RTL and theme', args: { scenarioId: 'collapsible.rtl-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'collapsible.content' } }
