import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { TextArea } from '@harborline-software/ui-react'

type ScenarioId = 'text-area.editing' | 'text-area.variants' | 'text-area.locale-theme' | 'text-area.content' | 'text-area.error-state'

function TextAreaScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const localized = scenarioId === 'text-area.locale-theme'
  const content = scenarioId === 'text-area.content'
  const errorState = scenarioId === 'text-area.error-state'
  const [value, setValue] = useState(localized ? 'ملاحظات فحص الهيكل' : 'Observed surface corrosion near the north joint.')
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={localized ? 'dark' : undefined} dir={localized ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{errorState ? 'Error state' : content ? 'Content resilience' : localized ? 'ملاحظات الفحص' : scenarioId === 'text-area.variants' ? 'Variants and counter' : 'Editing and metadata'}</h2><p>{errorState ? 'The text area renders its declared invalid state with an accessible name.' : content ? 'Real content spans an overlong label, a grouped large number, escaped markup characters, and a diacritic-rich name.' : 'Controlled editing, field metadata, resizing, validation, and counting.'}</p></header>
    <div className="hl-gallery-stage">{errorState ? <TextArea aria-label="Inspection notes" defaultValue="Invalid inspection notes" error rows={4} /> : content ? <div className="hl-gallery-feedback-grid"><TextArea aria-label="Long content" defaultValue="Awaiting third-party structural certification review" rows={2} /><TextArea aria-label="Large number" defaultValue="1,284,905" rows={2} /><TextArea aria-label="Escaped content" defaultValue={'Bay 4 <grid C-7> & 8'} rows={2} /><TextArea aria-label="Unusual name" defaultValue="Ordnance Survey — Niño Ångström" rows={2} /></div> : <><label htmlFor={`notes-${scenarioId}`}>{localized ? 'ملاحظات' : 'Inspection notes'}</label><TextArea id={`notes-${scenarioId}`} value={value} onChange={setValue} rows={4} resize={scenarioId === 'text-area.variants' ? 'both' : 'vertical'} showCounter maxLength={160} error={scenarioId === 'text-area.variants'} fillMode={scenarioId === 'text-area.variants' ? 'outline' : 'solid'} aria-describedby={`help-${scenarioId}`} /><p id={`help-${scenarioId}`}>{localized ? 'صف حالة الهيكل.' : 'Describe the condition of the structure.'}</p></>}</div>
  </section>
}

const meta = { title: 'Platform/Text Area', component: TextAreaScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof TextAreaScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Editing: Story = { name: 'Editing and metadata', args: { scenarioId: 'text-area.editing' } }
export const Variants: Story = { name: 'Variants and counter', args: { scenarioId: 'text-area.variants' } }
export const LocaleTheme: Story = { name: 'Locale and theme', args: { scenarioId: 'text-area.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'text-area.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'text-area.error-state' } }
