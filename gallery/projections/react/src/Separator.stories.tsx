import type { Meta, StoryObj } from '@storybook/react-vite'
import { Separator } from '@harborline-software/ui-react'

type ScenarioId = 'separator.decorative' | 'separator.semantic' | 'separator.labeled-theme' | 'separator.content'
function SeparatorScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const semantic = scenarioId === 'separator.semantic'
  const labeled = scenarioId === 'separator.labeled-theme'
  const content = scenarioId === 'separator.content'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={labeled ? 'dark' : undefined} dir={labeled ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{content ? 'Content resilience' : semantic ? 'Semantic orientations' : labeled ? 'Labeled RTL and theme' : 'Decorative'}</h2><p>Rules remain decorative by default and expose orientation only when semantic.</p></header>
    <div className="hl-gallery-stage">{content ? <Separator decorative={false} label={'Awaiting third-party structural certification review — 1,284,905 — Bay 4 <grid C-7> & 8 — Ordnance Survey — Niño Ångström'} /> : <div style={{ minHeight: semantic ? 120 : undefined, display: semantic ? 'flex' : 'block', gap: '2rem' }}><Separator decorative={!semantic && !labeled} label={labeled ? 'أو' : undefined} />{semantic && <Separator decorative={false} orientation="vertical" />}</div>}</div>
  </section>
}
const meta = { title: 'Platform/Separator', component: SeparatorScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SeparatorScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Decorative: Story = { args: { scenarioId: 'separator.decorative' } }
export const SemanticOrientations: Story = { name: 'Semantic orientations', args: { scenarioId: 'separator.semantic' } }
export const LabeledRtlAndTheme: Story = { name: 'Labeled RTL and theme', args: { scenarioId: 'separator.labeled-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'separator.content' } }
