import { useState, type ChangeEvent, type ComponentProps } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Input } from '@harborline-software/ui-react'

type ScenarioId =
  | 'input.defaults'
  | 'input.appearance'
  | 'input.states'
  | 'input.adornments'
  | 'input.locale-en'
  | 'input.locale-pseudo'
  | 'input.locale-ar'
  | 'input.theme-light'
  | 'input.theme-dark'
  | 'input.content'
  | 'input.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'input.defaults': ['Defaults and host attributes', 'Native value, type, name, labels, descriptions, and host attributes remain caller-owned.'],
  'input.appearance': ['Size, fill, and rounding', 'Independent closed appearance axes normalize Harborline compatibility aliases.'],
  'input.states': ['Disabled, readonly, and invalid', 'Native interaction states and visual invalid intent preserve host accessibility semantics.'],
  'input.adornments': ['Prefix and suffix', 'Decorative content surrounds one native input and uses logical padding.'],
  'input.locale-en': ['English locale', 'Caller text and native input behavior remain unchanged.'],
  'input.locale-pseudo': ['Pseudo locale', 'Expanded placeholders and adornments reflow without clipping.'],
  'input.locale-ar': ['Arabic locale', 'Logical adornment edges follow RTL without changing the value.'],
  'input.theme-light': ['Light theme', 'Every appearance axis uses public light-theme tokens.'],
  'input.theme-dark': ['Dark theme', 'The same input vocabulary on the Harborline dark surface.'],
  'input.content': ['Content resilience', 'Real content spans an overlong label, a grouped large number, escaped markup characters, and a diacritic-rich name.'],
  'input.error-state': ['Error state', 'The input renders its declared invalid state with an accessible name.'],
}

function ControlledInput(props: ComponentProps<typeof Input>) {
  const [value, setValue] = useState(String(props.value ?? ''))
  return <Input {...props} value={value} onChange={(event: ChangeEvent<HTMLInputElement>) => setValue(event.currentTarget.value)} />
}

function AppearanceGrid() {
  return (
    <div className="hl-gallery-feedback-grid">
      <ControlledInput aria-label="Small solid input" placeholder="Small solid" size="sm" fillMode="solid" rounded="small" />
      <ControlledInput aria-label="Medium outline input" placeholder="Medium outline" size="md" fillMode="outline" rounded="medium" />
      <ControlledInput aria-label="Large flat input" placeholder="Large flat" size="lg" fillMode="flat" rounded="large" />
      <ControlledInput aria-label="Full rounded input" placeholder="Full rounded" size="medium" fillMode="outline" rounded="full" />
    </div>
  )
}

function InputScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'input.locale-pseudo'
  const arabic = scenarioId === 'input.locale-ar'
  const theme = scenarioId === 'input.theme-light' ? 'light' : scenarioId === 'input.theme-dark' ? 'dark' : undefined
  let content

  if (scenarioId === 'input.content') content = <div className="hl-gallery-feedback-grid">
    <Input aria-label="Long content" value="Awaiting third-party structural certification review" readOnly />
    <Input aria-label="Large number" value="1,284,905" readOnly />
    <Input aria-label="Escaped content" value={'Bay 4 <grid C-7> & 8'} readOnly />
    <Input aria-label="Unusual name" value="Ordnance Survey — Niño Ångström" readOnly />
  </div>
  else if (scenarioId === 'input.error-state') content = <Input aria-label="Structure identifier" aria-invalid="true" defaultValue="Unrecognized structure" invalid />
  else if (scenarioId === 'input.appearance' || theme) content = <AppearanceGrid />
  else if (scenarioId === 'input.states') content = <div className="hl-gallery-feedback-grid">
    <Input aria-label="Disabled input" value="Disabled" disabled readOnly />
    <Input aria-label="Readonly input" value="Read only" readOnly />
    <Input aria-label="Invalid input" defaultValue="Needs review" invalid aria-invalid="true" aria-describedby="input-error" />
    <p id="input-error">Enter a recognized structure identifier.</p>
  </div>
  else if (scenarioId === 'input.adornments') content = <div className="hl-gallery-feedback-grid">
    <ControlledInput aria-label="Cost" value="125" prefix="$" suffix="USD" inputMode="decimal" />
    <ControlledInput aria-label="Weight" value="40" suffix="kg" inputMode="decimal" />
  </div>
  else content = <ControlledInput
    id="structure-name"
    name="structureName"
    type="text"
    aria-label={arabic ? 'اسم الهيكل' : 'Structure name'}
    placeholder={pseudo ? '⟦ Ëñţëř ţhë šţřûçţûřë ñåmë ······ ⟧' : arabic ? 'أدخل اسم الهيكل' : 'Enter structure name'}
    value={arabic ? 'المبنى أ' : ''}
    prefix={arabic ? '₪' : undefined}
    suffix={arabic ? 'ILS' : undefined}
    dir={arabic ? 'rtl' : 'ltr'}
    data-case="structure-intake"
  />

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">{content}</div>
  </section>
}

const meta = { title: 'Platform/Input', component: InputScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof InputScenario>
export default meta
type Story = StoryObj<typeof meta>

export const DefaultsAndHostAttributes: Story = { name: 'Defaults and host attributes', args: { scenarioId: 'input.defaults' } }
export const SizeFillAndRounding: Story = { name: 'Size, fill, and rounding', args: { scenarioId: 'input.appearance' } }
export const DisabledReadonlyAndInvalid: Story = { name: 'Disabled, readonly, and invalid', args: { scenarioId: 'input.states' } }
export const PrefixAndSuffix: Story = { name: 'Prefix and suffix', args: { scenarioId: 'input.adornments' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'input.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'input.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'input.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'input.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'input.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'input.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'input.error-state' } }
