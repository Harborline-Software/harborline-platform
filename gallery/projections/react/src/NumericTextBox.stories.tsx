import { useState } from 'react'
import type { ReactNode } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { NumericTextBox, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'numeric-text-box.defaults'
  | 'numeric-text-box.editing-commit'
  | 'numeric-text-box.stepping-bounds'
  | 'numeric-text-box.formats'
  | 'numeric-text-box.controlled-replacement'
  | 'numeric-text-box.disabled-readonly'
  | 'numeric-text-box.appearance-reflow'
  | 'numeric-text-box.locale-pseudo'
  | 'numeric-text-box.locale-ar'
  | 'numeric-text-box.theme-light'
  | 'numeric-text-box.theme-dark'
  | 'numeric-text-box.content'
  | 'numeric-text-box.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'numeric-text-box.defaults': ['Defaults and value ownership', 'Nullable controlled and uncontrolled values retain text-box semantics and stable form identity.'],
  'numeric-text-box.editing-commit': ['Editing and commit order', 'Focus reveals the raw value; Enter or blur strictly parses and commits once.'],
  'numeric-text-box.stepping-bounds': ['Stepping and bounds', 'Buttons and Arrow Up or Arrow Down apply the step and clamp at active bounds.'],
  'numeric-text-box.formats': ['Plain, currency, and percentage', 'Display formatting follows locale and preserves explicit zero fraction digits.'],
  'numeric-text-box.controlled-replacement': ['Controlled replacement', 'Caller state remains authoritative across requests and complete value replacement.'],
  'numeric-text-box.disabled-readonly': ['Disabled and readonly', 'Disabled controls remain perceivable; readonly values remove spinner actions and never emit.'],
  'numeric-text-box.appearance-reflow': ['Appearance and narrow reflow', 'Sizes, fills, rounding, focus, and 44px actions remain usable in a narrow surface.'],
  'numeric-text-box.locale-pseudo': ['Pseudo locale', 'Expanded placeholder and spinner labels remain visible without clipping.'],
  'numeric-text-box.locale-ar': ['Arabic direction with format override', 'Direction follows Arabic context while French remains the explicit number-format locale.'],
  'numeric-text-box.theme-light': ['Light theme', 'Semantic control, border, state, and focus tokens on the Harborline light surface.'],
  'numeric-text-box.theme-dark': ['Dark theme', 'The same numeric editor states on the Harborline dark surface.'],
  'numeric-text-box.content': ['Content resilience', 'Carries a long certification label, a grouped large number, escaped hostile copy, and a name with diacritics and an em dash.'],
  'numeric-text-box.error-state': ['Error state', 'An invalid amount exposes the component visual and semantic error state.'],
}

function LabeledValue({ children, label, note }: { children: ReactNode; label: string; note?: string }) {
  return <div style={{ display: 'grid', gap: 6 }}>
    <strong>{label}</strong>
    {children}
    {note ? <small>{note}</small> : null}
  </div>
}

function ControlledExample({ scenarioId }: { scenarioId: ScenarioId }) {
  const [value, setValue] = useState<number | null>(scenarioId === 'numeric-text-box.stepping-bounds' ? 8 : 1234.5)
  const [requests, setRequests] = useState<Array<number | null>>([])
  const requestValue = (next: number | null) => {
    setRequests(current => [...current.slice(-3), next])
    if (scenarioId !== 'numeric-text-box.controlled-replacement') setValue(next)
  }

  if (scenarioId === 'numeric-text-box.content') {
    return <div className="hl-gallery-feedback-grid">
      <LabeledValue label="Awaiting third-party structural certification review"><NumericTextBox aria-label="Awaiting third-party structural certification review" decimals={0} value={1284905} /></LabeledValue>
      <LabeledValue label="Ordnance Survey — Niño Ångström" note={'Bay 4 <grid C-7> & 8'}><NumericTextBox aria-label="Ordnance Survey — Niño Ångström" decimals={0} value={8} /></LabeledValue>
    </div>
  }

  if (scenarioId === 'numeric-text-box.error-state') {
    return <LabeledValue label="Total amount"><NumericTextBox aria-label="Total amount" error value={125.5} /></LabeledValue>
  }

  if (scenarioId === 'numeric-text-box.formats') {
    return <div className="hl-gallery-feedback-grid">
      <LabeledValue label="Plain"><NumericTextBox aria-label="Plain amount" locale="en-US" value={1234.5} /></LabeledValue>
      <LabeledValue label="Currency, zero decimals"><NumericTextBox aria-label="Replacement cost" currency="USD" format="c0" locale="en-US" value={1234.5} /></LabeledValue>
      <LabeledValue label="Percentage"><NumericTextBox aria-label="Completion" format="p2" locale="en-US" value={0.125} /></LabeledValue>
    </div>
  }

  if (scenarioId === 'numeric-text-box.disabled-readonly') {
    return <div className="hl-gallery-feedback-grid">
      <LabeledValue label="Disabled"><NumericTextBox aria-label="Disabled amount" disabled value={42} /></LabeledValue>
      <LabeledValue label="Readonly"><NumericTextBox aria-label="Readonly amount" readOnly value={42} /></LabeledValue>
    </div>
  }

  if (scenarioId === 'numeric-text-box.appearance-reflow') {
    return <div className="hl-gallery-feedback-stack" style={{ maxWidth: 320 }}>
      <LabeledValue label="Small · flat"><NumericTextBox aria-label="Small amount" fillMode="flat" rounded="small" size="sm" value={12.5} /></LabeledValue>
      <LabeledValue label="Medium · solid"><NumericTextBox aria-label="Medium amount" fillMode="solid" rounded="medium" size="md" value={12.5} /></LabeledValue>
      <LabeledValue label="Large · outline"><NumericTextBox aria-label="Large amount" fillMode="outline" rounded="full" size="lg" value={12.5} /></LabeledValue>
    </div>
  }

  if (scenarioId === 'numeric-text-box.locale-pseudo') {
    return <LabeledValue label="⟦ Řëþļåçëmëñţ çøšţ ······ ⟧">
      <NumericTextBox
        aria-label="⟦ Řëþļåçëmëñţ çøšţ ······ ⟧"
        decrementLabel="⟦ Đëçřëåšë ṽåļûë ···· ⟧"
        incrementLabel="⟦ Îñçřëåšë ṽåļûë ···· ⟧"
        placeholder="⟦ Ëñţëř åñ åmøûñţ ········ ⟧"
        value={null}
      />
    </LabeledValue>
  }

  if (scenarioId === 'numeric-text-box.locale-ar') {
    return <LabeledValue label="تكلفة الاستبدال" note="French formatting locale inside Arabic layout direction">
      <NumericTextBox aria-label="تكلفة الاستبدال" currency="EUR" format="c2" locale="fr-FR" value={1234.5} />
    </LabeledValue>
  }

  if (scenarioId === 'numeric-text-box.defaults') {
    return <div className="hl-gallery-feedback-grid">
      <LabeledValue label="Controlled value"><NumericTextBox aria-label="Controlled amount" id="controlled-amount" name="controlledAmount" required value={42} /></LabeledValue>
      <LabeledValue label="Controlled null"><NumericTextBox aria-label="Nullable amount" placeholder="Amount" value={null} /></LabeledValue>
      <LabeledValue label="Uncontrolled default"><NumericTextBox aria-label="Initial amount" defaultValue={99} /></LabeledValue>
    </div>
  }

  const stepping = scenarioId === 'numeric-text-box.stepping-bounds'
  const replacement = scenarioId === 'numeric-text-box.controlled-replacement'
  return <div className="hl-gallery-feedback-stack">
    <LabeledValue
      label={stepping ? 'Useful life' : replacement ? 'Caller-owned replacement cost' : 'Replacement cost'}
      note={replacement ? 'Requests are logged without changing the controlled value.' : 'Focus to edit; press Enter or move focus to commit.'}
    >
      <NumericTextBox
        aria-label="Replacement cost"
        currency={stepping ? undefined : 'USD'}
        format={stepping ? undefined : 'c2'}
        max={stepping ? 10 : undefined}
        min={stepping ? 2 : 0}
        onChange={requestValue}
        step={stepping ? 2 : 1}
        value={value}
      />
    </LabeledValue>
    <output aria-live="polite">Requests: {requests.length ? requests.map(item => item ?? 'null').join(', ') : 'none'}</output>
    {replacement ? <button type="button" onClick={() => setValue(current => current === 25 ? 1234.5 : 25)}>Replace caller value</button> : null}
  </div>
}

function NumericTextBoxScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const arabic = scenarioId === 'numeric-text-box.locale-ar'
  const pseudo = scenarioId === 'numeric-text-box.locale-pseudo'
  const theme = scenarioId === 'numeric-text-box.theme-light' ? 'light' : scenarioId === 'numeric-text-box.theme-dark' ? 'dark' : undefined
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">
      <HarborlineLocaleProvider locale={locale}>
        <ControlledExample scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = {
  title: 'Platform/Numeric Text Box',
  component: NumericTextBoxScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof NumericTextBoxScenario>

export default meta
type Story = StoryObj<typeof meta>

export const Defaults: Story = { name: 'Defaults and value ownership', args: { scenarioId: 'numeric-text-box.defaults' } }
export const EditingAndCommit: Story = { name: 'Editing and commit order', args: { scenarioId: 'numeric-text-box.editing-commit' } }
export const SteppingAndBounds: Story = { name: 'Stepping and bounds', args: { scenarioId: 'numeric-text-box.stepping-bounds' } }
export const Formats: Story = { name: 'Plain, currency, and percentage', args: { scenarioId: 'numeric-text-box.formats' } }
export const ControlledReplacement: Story = { name: 'Controlled replacement', args: { scenarioId: 'numeric-text-box.controlled-replacement' } }
export const DisabledAndReadonly: Story = { name: 'Disabled and readonly', args: { scenarioId: 'numeric-text-box.disabled-readonly' } }
export const AppearanceAndReflow: Story = { name: 'Appearance and narrow reflow', args: { scenarioId: 'numeric-text-box.appearance-reflow' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'numeric-text-box.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic direction with format override', args: { scenarioId: 'numeric-text-box.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'numeric-text-box.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'numeric-text-box.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'numeric-text-box.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'numeric-text-box.error-state' } }
