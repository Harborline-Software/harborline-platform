import type { Meta, StoryObj } from '@storybook/react-vite'
import { Card, CardContent, CardHeader, CardTitle } from '@harborline-software/ui-react'

type ScenarioId = 'card.defaults' | 'card.horizontal' | 'card.themes' | 'card.content'
function Surface({ variant = 'outlined', horizontal = false }: { variant?: 'flat' | 'raised' | 'outlined' | 'elevated'; horizontal?: boolean }) {
  return <Card variant={variant} orientation={horizontal ? 'horizontal' : 'vertical'} separators={horizontal}>
    <CardHeader><CardTitle as="h3">Inspection summary</CardTitle></CardHeader>
    <CardContent>Three rooms remain ready for review.</CardContent>
  </Card>
}
function CardScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const themes = scenarioId === 'card.themes'
  const content = scenarioId === 'card.content'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={themes ? 'dark' : undefined}>
    <header className="hl-gallery-heading"><h2>{content ? 'Content resilience' : themes ? 'Appearances and theme' : scenarioId === 'card.horizontal' ? 'Horizontal separators' : 'Defaults'}</h2><p>Caller-owned regions retain semantic headings and logical separators.</p></header>
    <div className="hl-gallery-stage">{content ? <Card><CardHeader><CardTitle as="h3">{'Awaiting third-party structural certification review'}</CardTitle></CardHeader><CardContent>{'1,284,905 · Bay 4 <grid C-7> & 8 · Ordnance Survey — Niño Ångström'}</CardContent></Card> : themes ? <div className="hl-gallery-feedback-grid"><Surface variant="flat" /><Surface variant="raised" /><Surface variant="outlined" /><Surface variant="elevated" /></div> : <Surface horizontal={scenarioId === 'card.horizontal'} />}</div>
  </section>
}
const meta = { title: 'Platform/Card', component: CardScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof CardScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { args: { scenarioId: 'card.defaults' } }
export const HorizontalSeparators: Story = { name: 'Horizontal separators', args: { scenarioId: 'card.horizontal' } }
export const AppearancesAndTheme: Story = { name: 'Appearances and theme', args: { scenarioId: 'card.themes' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'card.content' } }
