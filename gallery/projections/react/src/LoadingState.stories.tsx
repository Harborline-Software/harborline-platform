import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { LoadingState } from '@harborline-software/ui-react'

type LoadingStateScenarioId =
  | 'loading-state.default'
  | 'loading-state.inline'
  | 'loading-state.label-update'
  | 'loading-state.host-attributes'
  | 'loading-state.locale-en'
  | 'loading-state.locale-pseudo'
  | 'loading-state.locale-ar'
  | 'loading-state.theme-light'
  | 'loading-state.theme-dark'
  | 'loading-state.content'

const copy: Record<LoadingStateScenarioId, [string, string]> = {
  'loading-state.default': ['Page', 'A centered polite status with caller-owned visible text.'],
  'loading-state.inline': ['Inline', 'The compact status participates in surrounding content flow.'],
  'loading-state.label-update': ['Label update', 'The caller updates one persistent polite status region as work advances.'],
  'loading-state.host-attributes': ['Host attributes', 'Consumer class, data, and atomic announcement attributes reach the status host.'],
  'loading-state.locale-en': ['English locale', 'Caller-localized visible text and direction propagate to the status.'],
  'loading-state.locale-pseudo': ['Pseudo locale', 'Expanded status text exposes clipping and fixed-width assumptions.'],
  'loading-state.locale-ar': ['Arabic locale', 'RTL status text keeps its order and logical alignment.'],
  'loading-state.theme-light': ['Light theme', 'Page, inline, text, and surface roles on the neutral light fixture.'],
  'loading-state.theme-dark': ['Dark theme', 'The same non-interactive status vocabulary on the Harborline dark surface.'],
  'loading-state.content': ['Content resilience', 'The visible status label carries long copy, the grouped number 1,284,905, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.'],
}

function UpdatingLoadingState() {
  const [label, setLabel] = useState('Loading inspections')
  return <div className="hl-gallery-feedback-stack">
    <LoadingState label={label} variant="inline" data-update-region />
    <button className="hl-gallery-test-control" type="button" onClick={() => setLabel('Loading attachments')} data-update-control>
      Advance caller state
    </button>
  </div>
}

function LoadingStateScenario({ scenarioId }: { scenarioId: LoadingStateScenarioId }) {
  const [title, description] = copy[scenarioId]
  let content

  switch (scenarioId) {
    case 'loading-state.content':
      content = <LoadingState label={'Loading 1,284,905 records for Ordnance Survey — Niño Ångström at Bay 4 <grid C-7> & 8'} />
      break
    case 'loading-state.inline':
      content = <LoadingState label="Loading results" variant="inline" />
      break
    case 'loading-state.label-update':
      content = <UpdatingLoadingState />
      break
    case 'loading-state.host-attributes':
      content = <LoadingState label="Loading inspections" variant="inline" className="consumer" data-case="shared" aria-atomic="true" />
      break
    case 'loading-state.locale-en':
      content = <LoadingState label="Loading inspections" lang="en-US" dir="ltr" />
      break
    case 'loading-state.locale-pseudo':
      content = <LoadingState label="⟦ Ļøåđîñĝ îñšþëçţîøñš ······ ⟧" lang="en-XA" dir="ltr" />
      break
    case 'loading-state.locale-ar':
      content = <LoadingState label="جارٍ تحميل عمليات الفحص" lang="ar-SA" dir="rtl" />
      break
    case 'loading-state.theme-light':
    case 'loading-state.theme-dark':
      content = <div className="hl-gallery-feedback-grid"><LoadingState label="Loading inspections" /><LoadingState label="Loading attachments" variant="inline" /></div>
      break
    case 'loading-state.default':
      content = <LoadingState label="Loading inspections" />
      break
  }

  const theme = scenarioId === 'loading-state.theme-light' ? 'light' : scenarioId === 'loading-state.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stage">{content}</div>
  </section>
}

const meta = {
  title: 'Platform/Loading State',
  component: LoadingStateScenario,
  tags: ['autodocs'],
  parameters: { controls: { disable: true } },
} satisfies Meta<typeof LoadingStateScenario>

export default meta
type Story = StoryObj<typeof meta>

export const Page: Story = { args: { scenarioId: 'loading-state.default' } }
export const Inline: Story = { args: { scenarioId: 'loading-state.inline' } }
export const LabelUpdate: Story = { name: 'Label update', args: { scenarioId: 'loading-state.label-update' } }
export const HostAttributes: Story = { name: 'Host attributes', args: { scenarioId: 'loading-state.host-attributes' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'loading-state.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'loading-state.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'loading-state.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'loading-state.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'loading-state.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'loading-state.content' } }
