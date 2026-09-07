import { useState, type ReactNode } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { SegmentedControl, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'segmented-control.structure' | 'segmented-control.activation' | 'segmented-control.keyboard'
  | 'segmented-control.sizing' | 'segmented-control.locale-en' | 'segmented-control.locale-pseudo'
  | 'segmented-control.locale-ar' | 'segmented-control.theme-light' | 'segmented-control.theme-dark'
  | 'segmented-control.content'

const copy: Record<ScenarioId, [string, string]> = {
  'segmented-control.structure': ['Radio structure and state', 'One named radiogroup preserves rich option content and one controlled selection.'],
  'segmented-control.activation': ['Activation and disabled options', 'Pointer activation requests one value while disabled options remain visible and inert.'],
  'segmented-control.keyboard': ['Keyboard selection', 'Arrow, Home, and End keys wrap, skip disabled options, and move focus with selection.'],
  'segmented-control.sizing': ['Sizes, touch, and full width', 'Closed densities and responsive touch targets retain the same radio semantics.'],
  'segmented-control.locale-en': ['English locale', 'Caller-owned option content is preserved in the default direction.'],
  'segmented-control.locale-pseudo': ['Pseudo locale', 'Expanded option labels expose clipping and reflow defects.'],
  'segmented-control.locale-ar': ['Arabic locale', 'Horizontal arrow progression mirrors while DOM order remains stable.'],
  'segmented-control.theme-light': ['Light theme', 'Checked, disabled, hover, and focus states use public light-theme tokens.'],
  'segmented-control.theme-dark': ['Dark theme', 'The same mutually exclusive choices on the Harborline dark surface.'],
  'segmented-control.content': ['Content resilience', 'Four segmented options carry hostile-but-valid labels.'],
}

function ContentSegmentFixture() {
  const [value, setValue] = useState('large-number')
  return <SegmentedControl
    accessibleName="Content cases"
    options={[
      { value: 'long-label', label: 'Awaiting third-party structural certification review' },
      { value: 'large-number', label: '1,284,905' },
      { value: 'hostile-text', label: 'Bay 4 <grid C-7> & 8' },
      { value: 'unusual-name', label: 'Ordnance Survey — Niño Ångström' },
    ]}
    value={value}
    onValueChange={setValue}
  />
}

function SegmentFixture({ scenarioId, size = 'md', fullWidth = false }: { scenarioId: ScenarioId; size?: 'sm' | 'md' | 'lg' | 'touch'; fullWidth?: boolean }) {
  const pseudo = scenarioId === 'segmented-control.locale-pseudo'
  const arabic = scenarioId === 'segmented-control.locale-ar'
  const [value, setValue] = useState('week')
  const labels: Record<string, ReactNode> = arabic
    ? { day: 'اليوم', week: 'الأسبوع', month: 'الشهر' }
    : pseudo
      ? { day: '⟦ Đåÿ ··· ⟧', week: '⟦ Wëëķļÿ ṽîëŵ ······ ⟧', month: '⟦ Møñţhļÿ øṽëřṽîëŵ ······ ⟧' }
      : { day: 'Day', week: <><span aria-hidden="true">▦</span> Week</>, month: 'Month' }
  return <SegmentedControl
    accessibleName={arabic ? 'نطاق الجدول' : 'Schedule range'}
    options={[
      { value: 'day', label: labels.day, automationId: 'segment-day' },
      { value: 'week', label: labels.week, accessibleLabel: arabic ? 'عرض الأسبوع' : 'Week view', automationId: 'segment-week' },
      { value: 'month', label: labels.month, disabled: scenarioId === 'segmented-control.activation' || scenarioId === 'segmented-control.keyboard' },
    ]}
    value={value}
    onValueChange={setValue}
    size={size}
    fullWidth={fullWidth}
    data-case="schedule-range"
  />
}

function SegmentedControlScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'segmented-control.locale-pseudo'
  const arabic = scenarioId === 'segmented-control.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'segmented-control.theme-light' ? 'light' : scenarioId === 'segmented-control.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack">
      <HarborlineLocaleProvider locale={locale}>
        {scenarioId === 'segmented-control.content' ? <ContentSegmentFixture /> : scenarioId === 'segmented-control.sizing' || theme ? <>
          <SegmentFixture scenarioId={scenarioId} size="sm" />
          <SegmentFixture scenarioId={scenarioId} size="md" />
          <SegmentFixture scenarioId={scenarioId} size="lg" />
          <SegmentFixture scenarioId={scenarioId} size="touch" fullWidth />
        </> : <SegmentFixture scenarioId={scenarioId} fullWidth={scenarioId === 'segmented-control.locale-pseudo'} />}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Segmented Control', component: SegmentedControlScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SegmentedControlScenario>
export default meta
type Story = StoryObj<typeof meta>
export const RadioStructureAndState: Story = { name: 'Radio structure and state', args: { scenarioId: 'segmented-control.structure' } }
export const ActivationAndDisabledOptions: Story = { name: 'Activation and disabled options', args: { scenarioId: 'segmented-control.activation' } }
export const KeyboardSelection: Story = { name: 'Keyboard selection', args: { scenarioId: 'segmented-control.keyboard' } }
export const SizesTouchAndFullWidth: Story = { name: 'Sizes, touch, and full width', args: { scenarioId: 'segmented-control.sizing' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'segmented-control.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'segmented-control.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'segmented-control.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'segmented-control.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'segmented-control.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'segmented-control.content' } }
