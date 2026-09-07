import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { GuardedControl, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'guarded-control.covered'
  | 'guarded-control.two-step'
  | 'guarded-control.keyboard-recovery'
  | 'guarded-control.locale-en'
  | 'guarded-control.locale-pseudo'
  | 'guarded-control.locale-ar'
  | 'guarded-control.theme-light'
  | 'guarded-control.theme-dark'
  | 'guarded-control.content'

const copy: Record<ScenarioId, [string, string]> = {
  'guarded-control.covered': ['Covered and unavailable', 'A visible action begins covered; caller-owned availability remains an interaction signal, not authorization.'],
  'guarded-control.two-step': ['Arm and commit', 'One activation arms and a distinct second activation invokes the caller operation at most once.'],
  'guarded-control.keyboard-recovery': ['Keyboard, focus, and recovery', 'Enter or Space arms, Escape recovers, and focus follows the visible action.'],
  'guarded-control.locale-en': ['English locale', 'Whole guard messages include a localized action label, countdown value, and duration unit.'],
  'guarded-control.locale-pseudo': ['Pseudo locale', 'Expanded whole messages expose clipping and fixed-width assumptions.'],
  'guarded-control.locale-ar': ['Arabic locale', 'RTL layout and bidi-isolated action text preserve the two-step order.'],
  'guarded-control.theme-light': ['Light theme', 'Covered, armed, committing, disabled, focus, and recovery states use public semantic tokens.'],
  'guarded-control.theme-dark': ['Dark theme', 'The same guarded-action vocabulary on the Harborline dark surface.'],
  'guarded-control.content': ['Content resilience', 'Guarded actions preserve hostile-but-valid labels as text.'],
}

const contentLabels = ['Awaiting third-party structural certification review', '1,284,905', 'Bay 4 <grid C-7> & 8', 'Ordnance Survey — Niño Ångström']

const guardCatalog = {
  'chrome.guard.covered.hint': 'Guarded — activate to unlock',
  'chrome.guard.armed.hint': 'Armed — activate to {action}. {seconds} seconds remain.',
  'chrome.guard.committing.status': 'Committing {action}',
  'chrome.guard.recovered.announce': 'Guard restored.',
  'chrome.guard.rejected.announce': 'The action was not completed. Guard restored.',
}

function GuardFixture({ label, disabled = false }: { label: string; disabled?: boolean }) {
  const [commits, setCommits] = useState(0)
  return (
    <div className="hl-gallery-feedback-stack">
      <GuardedControl
        action="packages.export"
        classificationId="significant-change"
        label={label}
        disabled={disabled}
        describedBy={disabled ? 'guard-denial' : undefined}
        onCommit={async () => setCommits(value => value + 1)}
      />
      {disabled && <p id="guard-denial">Complete the affirmation before signing.</p>}
      <output className="hl-gallery-activation" aria-live="polite">Commit requests: {commits}</output>
    </div>
  )
}

function GuardedControlScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'guarded-control.locale-pseudo'
  const arabic = scenarioId === 'guarded-control.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const label = arabic
    ? 'توقيع وحفظ الحزمة'
    : pseudo
      ? '⟦ Šîĝñ åñđ šåṽë ţhë þåçķåĝë ······ ⟧'
      : 'Sign and save package'
  const theme = scenarioId === 'guarded-control.theme-light' ? 'light' : scenarioId === 'guarded-control.theme-dark' ? 'dark' : undefined

  return (
    <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
      <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
      <div className="hl-gallery-stage hl-gallery-feedback-grid">
        <HarborlineLocaleProvider locale={locale} catalog={guardCatalog}>
          {scenarioId === 'guarded-control.content'
            ? contentLabels.map(contentLabel => <GuardFixture key={contentLabel} label={contentLabel} />)
            : <><GuardFixture label={label} />{(scenarioId === 'guarded-control.covered' || theme) && <GuardFixture label={label} disabled />}</>}
        </HarborlineLocaleProvider>
      </div>
    </section>
  )
}

const meta = {
  title: 'Platform/Guarded Control',
  component: GuardedControlScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof GuardedControlScenario>

export default meta
type Story = StoryObj<typeof meta>

export const CoveredAndUnavailable: Story = { name: 'Covered and unavailable', args: { scenarioId: 'guarded-control.covered' } }
export const ArmAndCommit: Story = { name: 'Arm and commit', args: { scenarioId: 'guarded-control.two-step' } }
export const KeyboardFocusAndRecovery: Story = { name: 'Keyboard, focus, and recovery', args: { scenarioId: 'guarded-control.keyboard-recovery' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'guarded-control.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'guarded-control.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'guarded-control.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'guarded-control.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'guarded-control.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'guarded-control.content' } }
