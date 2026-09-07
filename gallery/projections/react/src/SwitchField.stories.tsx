import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { FormField, HarborlineLocaleProvider, SwitchField } from '@harborline-software/ui-react'

type ScenarioId =
  | 'switch-field.controlled-form'
  | 'switch-field.activation-states'
  | 'switch-field.appearance-replacement'
  | 'switch-field.locale-pseudo'
  | 'switch-field.locale-ar'
  | 'switch-field.theme-light'
  | 'switch-field.theme-dark'
  | 'switch-field.content'

const copy: Record<ScenarioId, [string, string]> = {
  'switch-field.controlled-form': ['Controlled form compatibility', 'The deprecated field seam delegates controlled value, identity, labels, descriptions, and form metadata to Switch.'],
  'switch-field.activation-states': ['Activation and validation states', 'Pointer, Enter, and Space request one change while disabled state suppresses every request.'],
  'switch-field.appearance-replacement': ['Appearance and replacement', 'Canonical sizes and caller replacements remain visually and behaviorally equivalent to Switch.'],
  'switch-field.locale-pseudo': ['Pseudo-localized compatibility', 'Expanded label, description, and state copy reflow without creating adapter-specific chrome.'],
  'switch-field.locale-ar': ['Arabic right-to-left switch', 'Track, thumb, label, description, and state copy follow logical direction.'],
  'switch-field.theme-light': ['Light theme', 'The compatibility wrapper uses only authoritative Switch light-theme tokens.'],
  'switch-field.theme-dark': ['Dark theme', 'The same controlled compatibility seam on the Harborline dark surface.'],
  'switch-field.content': ['Content resilience', 'Two controlled switch fields carry hostile-but-valid labels, descriptions, and state copy.'],
}

function SwitchFieldFixture({ scenarioId, size = 'md', disabled = false, invalid = false, inForm = false }: { scenarioId: ScenarioId; size?: 'sm' | 'md' | 'lg'; disabled?: boolean; invalid?: boolean; inForm?: boolean }) {
  const pseudo = scenarioId === 'switch-field.locale-pseudo'
  const arabic = scenarioId === 'switch-field.locale-ar'
  const replacement = scenarioId === 'switch-field.appearance-replacement'
  const [checked, setChecked] = useState(size !== 'sm')
  const [requests, setRequests] = useState(0)
  const label = arabic ? 'إظهار الهياكل المؤرشفة' : pseudo ? '⟦ Šhøŵ åřçhîṽëđ šţřûçţûřëš ········ ⟧' : `Show archived structures · ${size}`
  const description = arabic ? 'تضمين السجلات غير النشطة في النتائج' : pseudo ? '⟦ Îñçļûđë îñåçţîṽë řëçøřđš îñ šëåřçh řëšûļţš ········ ⟧' : 'Include inactive records in search results'
  const control = <SwitchField
    accessibleName={label}
    checked={checked}
    description={inForm ? undefined : description}
    disabled={disabled}
    error={invalid}
    label={inForm ? undefined : label}
    name={`showArchived-${size}`}
    onCheckedChange={(next: boolean) => {
      setRequests(current => current + 1)
      if (!replacement) setChecked(next)
    }}
    required={invalid}
    size={size}
  />

  return <div className="hl-gallery-feedback-stack">
    {inForm ? <FormField name={`showArchived-${size}`} label={label} hint={description} required={invalid} error={invalid ? 'Choose whether archived structures are included.' : undefined}>{control}</FormField> : control}
    <output aria-live="polite">{replacement ? `Controlled value ${checked ? 'on' : 'off'}; requests logged without replacement: ${requests}` : `Requests: ${requests}`}</output>
    {replacement ? <button type="button" onClick={() => setChecked(current => !current)}>Replace caller value</button> : null}
  </div>
}

function SwitchFieldScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'switch-field.locale-pseudo'
  const arabic = scenarioId === 'switch-field.locale-ar'
  const content = scenarioId === 'switch-field.content'
  const theme = scenarioId === 'switch-field.theme-light' ? 'light' : scenarioId === 'switch-field.theme-dark' ? 'dark' : undefined
  const stateScenario = scenarioId === 'switch-field.activation-states'
  const appearance = scenarioId === 'switch-field.appearance-replacement' || Boolean(theme)
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid">
      <HarborlineLocaleProvider direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        {content ? <>
          <SwitchField name="content-long" checked label={'Awaiting third-party structural certification review'} accessibleName={'Awaiting third-party structural certification review'} description={'Bay 4 <grid C-7> & 8'} onText={'1,284,905'} onCheckedChange={()=>{}} />
          <SwitchField name="content-name" checked={false} label={'Ordnance Survey — Niño Ångström'} accessibleName={'Ordnance Survey — Niño Ångström'} onCheckedChange={()=>{}} />
        </> : <>
          <SwitchFieldFixture inForm={scenarioId === 'switch-field.controlled-form'} scenarioId={scenarioId} size={appearance ? 'sm' : 'md'} />
          {(stateScenario || appearance) ? <SwitchFieldFixture invalid={stateScenario} scenarioId={scenarioId} size="md" /> : null}
          {(stateScenario || appearance) ? <SwitchFieldFixture disabled={stateScenario} scenarioId={scenarioId} size="lg" /> : null}
        </>}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Switch Field', component: SwitchFieldScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SwitchFieldScenario>
export default meta
type Story = StoryObj<typeof meta>

export const ControlledFormCompatibility: Story = { name: 'Controlled form compatibility', args: { scenarioId: 'switch-field.controlled-form' } }
export const ActivationAndValidationStates: Story = { name: 'Activation and validation states', args: { scenarioId: 'switch-field.activation-states' } }
export const AppearanceAndReplacement: Story = { name: 'Appearance and replacement', args: { scenarioId: 'switch-field.appearance-replacement' } }
export const PseudoLocalizedCompatibility: Story = { name: 'Pseudo-localized compatibility', args: { scenarioId: 'switch-field.locale-pseudo' } }
export const ArabicRightToLeftSwitch: Story = { name: 'Arabic right-to-left switch', args: { scenarioId: 'switch-field.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'switch-field.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'switch-field.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'switch-field.content' } }
