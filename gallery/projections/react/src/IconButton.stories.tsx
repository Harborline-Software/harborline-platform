import type { Meta, StoryObj } from '@storybook/react-vite'
import { IconButton } from '@harborline-software/ui-react'

type ScenarioId = 'icon-button.defaults' | 'icon-button.states' | 'icon-button.rtl-theme' | 'icon-button.content'
const icon = <span aria-hidden="true">+</span>
function IconButtonScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const states = scenarioId === 'icon-button.states'
  const rtl = scenarioId === 'icon-button.rtl-theme'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={rtl ? 'dark' : undefined} dir={rtl ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{scenarioId === 'icon-button.content' ? 'Content resilience' : states ? 'States and sizes' : rtl ? 'RTL and theme' : 'Defaults'}</h2><p>Accessible icon-only commands preserve native button behavior and hit geometry.</p></header>
    <div className="hl-gallery-stage"><div className="hl-gallery-button-row">{scenarioId === 'icon-button.content' ? <><IconButton aria-label="Awaiting third-party structural certification review">{icon}</IconButton><IconButton aria-label="1,284,905">{icon}</IconButton><IconButton aria-label="Bay 4 <grid C-7> & 8">{icon}</IconButton><IconButton aria-label="Ordnance Survey — Niño Ångström">{icon}</IconButton></> : states ? <><IconButton aria-label="Default action">{icon}</IconButton><IconButton aria-label="Ghost action" variant="ghost" size="sm">{icon}</IconButton><IconButton aria-label="Outline action" variant="outline" size="lg">{icon}</IconButton><IconButton aria-label="Destructive action" variant="destructive" size="touch">{icon}</IconButton><IconButton aria-label="Loading action" loading>{icon}</IconButton><IconButton aria-label="Disabled action" disabled>{icon}</IconButton></> : <IconButton aria-label={rtl ? 'إضافة عنصر' : 'Add item'}>{icon}</IconButton>}</div></div>
  </section>
}
const meta = { title: 'Platform/Icon Button', component: IconButtonScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof IconButtonScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { args: { scenarioId: 'icon-button.defaults' } }
export const StatesAndSizes: Story = { name: 'States and sizes', args: { scenarioId: 'icon-button.states' } }
export const RtlAndTheme: Story = { name: 'RTL and theme', args: { scenarioId: 'icon-button.rtl-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'icon-button.content' } }
