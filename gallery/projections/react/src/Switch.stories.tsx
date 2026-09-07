import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { HarborlineLocaleProvider, Switch } from '@harborline-software/ui-react'

type ScenarioId =
  | 'switch.states' | 'switch.activation-validation' | 'switch.sizing'
  | 'switch.locale-en' | 'switch.locale-pseudo' | 'switch.locale-ar'
  | 'switch.theme-light' | 'switch.theme-dark' | 'switch.content' | 'switch.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'switch.states': ['Controlled state and stable naming', 'Visible labels and descriptions name one switch while state copy changes independently.'],
  'switch.activation-validation': ['Activation, validation, and form value', 'Pointer, Space, and Enter toggle once; disabled and invalid states remain explicit.'],
  'switch.sizing': ['Sizes and touch target', 'Canonical sizes retain semantics while coarse pointers receive a 44-pixel target.'],
  'switch.locale-en': ['English locale', 'Catalog-backed state copy and caller labels remain separate.'],
  'switch.locale-pseudo': ['Pseudo locale', 'Expanded labels and descriptions reflow without clipping.'],
  'switch.locale-ar': ['Arabic locale', 'The thumb moves toward logical inline-end while content order remains stable.'],
  'switch.theme-light': ['Light theme', 'Off, on, invalid, disabled, and focus states use public light-theme tokens.'],
  'switch.theme-dark': ['Dark theme', 'The same binary control on the Harborline dark surface.'],
  'switch.content': ['Content resilience', 'Two switches carry hostile-but-valid labels, descriptions, and state copy.'],
  'switch.error-state': ['Error state', 'The required switch exposes its invalid state while retaining a stable accessible name.'],
}

function SwitchFixture({ scenarioId, size = 'md', disabled = false, error = false }: { scenarioId: ScenarioId; size?: 'sm' | 'md' | 'lg'; disabled?: boolean; error?: boolean }) {
  const pseudo = scenarioId === 'switch.locale-pseudo'
  const arabic = scenarioId === 'switch.locale-ar'
  const [checked, setChecked] = useState(size !== 'sm')
  return <Switch
    id={`inspection-alerts-${size}`}
    name={`inspectionAlerts${size}`}
    checked={checked}
    onCheckedChange={setChecked}
    label={arabic ? 'تنبيهات الفحص' : pseudo ? '⟦ Îñšþëçţîøñ åļëřţš åñđ řëmîñđëřš ······ ⟧' : 'Inspection alerts'}
    accessibleName={arabic ? 'تنبيهات الفحص' : 'Inspection alerts'}
    description={arabic ? 'إعلام جميع مالكي الهيكل' : pseudo ? '⟦ Ñøţîƒÿ ëṽëřÿ šţřûçţûřë øŵñëř ······ ⟧' : 'Notify every structure owner'}
    onText={arabic ? 'مفعل' : 'Enabled'}
    offText={arabic ? 'متوقف' : 'Disabled'}
    size={size}
    disabled={disabled}
    error={error}
    required={error}
    data-case="inspection-alerts"
  />
}

function SwitchScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'switch.locale-pseudo'
  const arabic = scenarioId === 'switch.locale-ar'
  const content = scenarioId === 'switch.content'
  const errorState = scenarioId === 'switch.error-state'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'switch.theme-light' ? 'light' : scenarioId === 'switch.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid">
      <HarborlineLocaleProvider locale={locale}>
        {errorState ? <Switch checked={false} label="Inspection alerts" accessibleName="Inspection alerts" description="Resolve the validation error" offText="Off" error required /> : content ? <>
          <Switch checked label={'Awaiting third-party structural certification review'} accessibleName={'Awaiting third-party structural certification review'} description={'Bay 4 <grid C-7> & 8'} onText={'1,284,905'} />
          <Switch checked={false} label={'Ordnance Survey — Niño Ångström'} accessibleName={'Ordnance Survey — Niño Ångström'} />
        </> : <>
          <SwitchFixture scenarioId={scenarioId} size="sm" />
          {(scenarioId === 'switch.sizing' || scenarioId === 'switch.activation-validation' || scenarioId === 'switch.states' || theme) && <>
            <SwitchFixture scenarioId={scenarioId} size="md" error={scenarioId === 'switch.activation-validation'} />
            <SwitchFixture scenarioId={scenarioId} size="lg" disabled={scenarioId === 'switch.activation-validation' || scenarioId === 'switch.states'} />
          </>}
        </>}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Switch', component: SwitchScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SwitchScenario>
export default meta
type Story = StoryObj<typeof meta>
export const ControlledStateAndStableNaming: Story = { name: 'Controlled state and stable naming', args: { scenarioId: 'switch.states' } }
export const ActivationValidationAndFormValue: Story = { name: 'Activation, validation, and form value', args: { scenarioId: 'switch.activation-validation' } }
export const SizesAndTouchTarget: Story = { name: 'Sizes and touch target', args: { scenarioId: 'switch.sizing' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'switch.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'switch.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'switch.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'switch.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'switch.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'switch.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'switch.error-state' } }
