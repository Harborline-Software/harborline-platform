import type { Meta, StoryObj } from '@storybook/react-vite'
import { Accordion } from '@harborline-software/ui-react'

type ScenarioId = 'accordion.defaults' | 'accordion.states' | 'accordion.empty' | 'accordion.rtl-theme' | 'accordion.content'
const emptyContent = 'No sections yet. Add one to see it here.'
const items = [
  { value: 'overview', title: 'Overview', children: 'Structure summary and current inspection status.' },
  { value: 'details', title: 'Details', children: 'Harborline-owned detail content remains composable.' },
  { value: 'disabled', title: 'Unavailable', children: 'This content cannot be opened.', disabled: true },
]
const contentItems = [
  { value: 'review', title: 'Awaiting third-party structural certification review', children: 'Review queue' },
  { value: 'count', title: '1,284,905', children: 'Record count' },
  { value: 'grid', title: 'Bay 4 <grid C-7> & 8', children: 'Survey location' },
  { value: 'name', title: 'Ordnance Survey — Niño Ångström', children: 'Survey provider' },
]
function AccordionScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const states = scenarioId === 'accordion.states'
  const rtl = scenarioId === 'accordion.rtl-theme'
  const content = scenarioId === 'accordion.content'
  const isEmpty = scenarioId === 'accordion.empty'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={rtl ? 'dark' : undefined} dir={rtl ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{content ? 'Content resilience' : isEmpty ? 'Empty collection' : states ? 'States and keyboard' : rtl ? 'RTL and theme' : 'Defaults'}</h2><p>{isEmpty ? 'Handed no items, the group renders the caller-owned empty content instead of an empty bordered box.' : 'Disclosure state, relationships, and logical layout use one neutral contract.'}</p></header>
    <div className="hl-gallery-stage">{isEmpty ? <Accordion empty={emptyContent} items={[]} /> : content ? <Accordion items={contentItems} type="multiple" defaultValue={['review', 'count', 'grid', 'name']} /> : <Accordion items={items} type={states ? 'multiple' : 'single'} defaultValue={states ? ['overview', 'details'] : ['overview']} collapsible={!states} />}</div>
  </section>
}
const meta = { title: 'Platform/Accordion', component: AccordionScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof AccordionScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { args: { scenarioId: 'accordion.defaults' } }
export const StatesAndKeyboard: Story = { name: 'States and keyboard', args: { scenarioId: 'accordion.states' } }
export const Empty: Story = { name: 'Empty collection', args: { scenarioId: 'accordion.empty' } }
export const RtlAndTheme: Story = { name: 'RTL and theme', args: { scenarioId: 'accordion.rtl-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'accordion.content' } }
