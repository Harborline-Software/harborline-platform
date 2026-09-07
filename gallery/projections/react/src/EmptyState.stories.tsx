import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { EmptyState } from '@harborline-software/ui-react'

type EmptyStateScenarioId =
  | 'empty-state.informational'
  | 'empty-state.positive'
  | 'empty-state.actionable'
  | 'empty-state.host-accessibility'
  | 'empty-state.locale-pseudo'
  | 'empty-state.locale-ar'
  | 'empty-state.theme-light'
  | 'empty-state.theme-dark'
  | 'empty-state.content'

const copy: Record<EmptyStateScenarioId, [string, string]> = {
  'empty-state.informational': ['Informational', 'A caller-owned explanation with a decorative information icon and no implied action.'],
  'empty-state.positive': ['Positive', 'A complete state confirms that no outstanding work remains.'],
  'empty-state.actionable': ['Actionable', 'A native, keyboard-operable action helps the user create the first record.'],
  'empty-state.host-accessibility': ['Host accessibility', 'The host supplies region semantics and an accessible label without turning the module into a live region.'],
  'empty-state.locale-pseudo': ['Pseudo locale', 'Expanded caller text exposes clipping and fixed-width assumptions at narrow widths.'],
  'empty-state.locale-ar': ['Arabic locale', 'Caller-localized content preserves right-to-left direction and logical alignment.'],
  'empty-state.theme-light': ['Light theme', 'Informational, positive, and actionable roles on the neutral light fixture.'],
  'empty-state.theme-dark': ['Dark theme', 'The same semantic vocabulary on the Harborline dark surface.'],
  'empty-state.content': ['Content resilience', 'Hostile content carries a long action label, the grouped number 1,284,905, escaped angle brackets and an ampersand, a name with diacritics and an em dash, and omitted optional fields.'],
}

function ActionableEmptyState(props: { variant?: 'informational' | 'positive' | 'actionable'; label?: string }) {
  const [activations, setActivations] = useState(0)
  const label = props.label ?? 'Record the first payment'

  return (
    <div className="hl-gallery-feedback-stack">
      <EmptyState
        variant={props.variant ?? 'actionable'}
        title="No payments yet"
        description="Record a payment to begin tracking this account."
        action={{ label, onClick: () => setActivations(value => value + 1) }}
        data-activation-count={activations}
      />
      <output className="hl-gallery-activation" aria-live="polite">Action activations: {activations}</output>
    </div>
  )
}

function ThemeFixture() {
  return (
    <div className="hl-gallery-feedback-grid">
      <EmptyState variant="informational" title="No search results" description="Adjust the filters and try again." />
      <EmptyState variant="positive" title="All inspections are complete" description="There is no outstanding work for this structure." />
      <EmptyState variant="actionable" title="No saved views" description="Save a view to return to this layout." action={{ label: 'Save this view', onClick: () => {} }} />
    </div>
  )
}

function EmptyStateScenario({ scenarioId }: { scenarioId: EmptyStateScenarioId }) {
  const [title, description] = copy[scenarioId]
  let content

  switch (scenarioId) {
    case 'empty-state.content':
      content = <><EmptyState variant="actionable" title={'Ordnance Survey — Niño Ångström'} description={'1,284,905 records at Bay 4 <grid C-7> & 8'} action={{ label: 'Review affected inspection records and continue', onClick: () => {} }} /><EmptyState variant="informational" title={'Awaiting third-party structural certification review'} /></>
      break
    case 'empty-state.positive':
      content = <EmptyState variant="positive" title="No outstanding receivables" description="Every posted invoice has been paid." />
      break
    case 'empty-state.actionable':
      content = <ActionableEmptyState />
      break
    case 'empty-state.host-accessibility':
      content = <EmptyState variant="informational" title="No saved filters" description="Saved filters will appear here." role="region" aria-label="Saved filters empty state" />
      break
    case 'empty-state.locale-pseudo':
      content = <EmptyState variant="actionable" title="⟦ Ñø šåṽëđ îñšþëçţîøñ ṽîëŵš ······ ⟧" description="⟦ Šåṽë å ṽîëŵ ţø řëţûřñ ţø ţhîš šţřûçţûřë ļåÿøûţ ········ ⟧" action={{ label: '⟦ Šåṽë ţhîš ṽîëŵ ···· ⟧', onClick: () => {} }} lang="en-XA" dir="ltr" />
      break
    case 'empty-state.locale-ar':
      content = <EmptyState variant="actionable" title="لا توجد طرق عرض محفوظة" description="احفظ طريقة عرض للعودة إلى تخطيط هذا المبنى." action={{ label: 'حفظ طريقة العرض', onClick: () => {} }} lang="ar-SA" dir="rtl" />
      break
    case 'empty-state.theme-light':
    case 'empty-state.theme-dark':
      content = <ThemeFixture />
      break
    case 'empty-state.informational':
      content = <EmptyState variant="informational" title="No results found" />
      break
  }

  const theme = scenarioId === 'empty-state.theme-light' ? 'light' : scenarioId === 'empty-state.theme-dark' ? 'dark' : undefined
  return (
    <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme}>
      <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
      <div className="hl-gallery-stage hl-gallery-feedback-stage">{content}</div>
    </section>
  )
}

const meta = {
  title: 'Platform/Empty State',
  component: EmptyStateScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof EmptyStateScenario>

export default meta
type Story = StoryObj<typeof meta>

export const Informational: Story = { args: { scenarioId: 'empty-state.informational' } }
export const Positive: Story = { args: { scenarioId: 'empty-state.positive' } }
export const Actionable: Story = { args: { scenarioId: 'empty-state.actionable' } }
export const HostAccessibility: Story = { name: 'Host accessibility', args: { scenarioId: 'empty-state.host-accessibility' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'empty-state.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'empty-state.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'empty-state.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'empty-state.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'empty-state.content' } }
