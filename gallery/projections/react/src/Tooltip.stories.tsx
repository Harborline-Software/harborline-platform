import type { Meta, StoryObj } from '@storybook/react-vite'
import { Tooltip } from '@harborline-software/ui-react'

type ScenarioId = 'tooltip.lifecycle' | 'tooltip.interaction' | 'tooltip.locale-theme' | 'tooltip.content'

function TooltipScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const localized = scenarioId === 'tooltip.locale-theme'
  const content = scenarioId === 'tooltip.content'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={localized ? 'dark' : undefined} dir={localized ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{localized ? 'تلميح الأداة' : content ? 'Content resilience' : scenarioId === 'tooltip.interaction' ? 'Focus and dismissal' : 'Delayed lifecycle'}</h2><p>{content ? 'The long trigger label and tooltip copy carry the grouped number 1,284,905, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.' : 'Hover and focus ownership share one accessible description.'}</p></header>
    <div className="hl-gallery-stage">{content ? <Tooltip content={'1,284,905 records for Ordnance Survey — Niño Ångström at Bay 4 <grid C-7> & 8'} delayDuration={0} defaultOpen><button type="button">{'Review affected inspection records and continue import'}</button></Tooltip> : <Tooltip content={localized ? 'عرض تفاصيل الهيكل' : 'View structure details'} side={scenarioId === 'tooltip.lifecycle' ? 'top' : 'right'} delayDuration={0} defaultOpen><button type="button">{localized ? 'التفاصيل' : 'Details'}</button></Tooltip>}</div>
  </section>
}

const meta = { title: 'Platform/Tooltip', component: TooltipScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof TooltipScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Lifecycle: Story = { name: 'Delayed lifecycle', args: { scenarioId: 'tooltip.lifecycle' } }
export const Interaction: Story = { name: 'Focus and dismissal', args: { scenarioId: 'tooltip.interaction' } }
export const LocaleTheme: Story = { name: 'Locale and theme', args: { scenarioId: 'tooltip.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'tooltip.content' } }
