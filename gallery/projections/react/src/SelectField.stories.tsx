import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { SelectField, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'select-field.states' | 'select-field.selection' | 'select-field.keyboard-dismissal'
  | 'select-field.locale-en' | 'select-field.locale-pseudo' | 'select-field.locale-ar'
  | 'select-field.theme-light' | 'select-field.theme-dark'
  | 'select-field.content'
  | 'select-field.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'select-field.states': ['Value, placeholder, and validation', 'Controlled value, identity, placeholder, density, and validation metadata remain explicit.'],
  'select-field.selection': ['Open and select', 'The trigger opens one listbox and selection requests one value before closing.'],
  'select-field.keyboard-dismissal': ['Keyboard, typeahead, and dismissal', 'Keyboard navigation, typeahead, Escape, Tab, and outside pointer follow one deterministic lifecycle.'],
  'select-field.locale-en': ['English locale', 'The locale catalog supplies only absent chrome while caller option labels remain unchanged.'],
  'select-field.locale-pseudo': ['Pseudo locale', 'Expanded trigger and option copy remain viewport-clamped.'],
  'select-field.locale-ar': ['Arabic locale', 'Trigger icon, checkmark, and popup alignment follow logical direction.'],
  'select-field.theme-light': ['Light theme', 'Default, open, selected, invalid, disabled, and focus states use public tokens.'],
  'select-field.theme-dark': ['Dark theme', 'The same controlled selection surface on the Harborline dark theme.'],
  'select-field.content': ['Content resilience', 'Real option content spans an overlong label, a grouped large number, escaped markup characters, and a diacritic-rich name.'],
  'select-field.error-state': ['Error state', 'The select field renders its declared invalid state with an accessible name.'],
}

const contentOptions = [
  { value: 'long', label: 'Awaiting third-party structural certification review' },
  { value: 'number', label: '1,284,905' },
  { value: 'escaped', label: 'Bay 4 <grid C-7> & 8' },
  { value: 'name', label: 'Ordnance Survey — Niño Ångström' },
]

const defaultOptions = [
  { value: 'active', label: 'Active' },
  { value: 'pending', label: 'Pending review' },
  { value: 'archived', label: 'Archived' },
]

function SelectFixture({ scenarioId, size = 'md', disabled = false, error = false }: { scenarioId: ScenarioId; size?: 'sm' | 'md' | 'lg'; disabled?: boolean; error?: boolean }) {
  const pseudo = scenarioId === 'select-field.locale-pseudo'
  const arabic = scenarioId === 'select-field.locale-ar'
  const [value, setValue] = useState(scenarioId === 'select-field.states' && !error && !disabled ? '' : 'active')
  const options = arabic
    ? [{ value: 'active', label: 'نشط' }, { value: 'pending', label: 'في انتظار المراجعة' }, { value: 'archived', label: 'مؤرشف' }]
    : pseudo
      ? defaultOptions.map(option => ({ ...option, label: `⟦ ${option.label} ······ ⟧` }))
      : defaultOptions
  return <SelectField
    name={`status-${size}-${disabled ? 'disabled' : error ? 'invalid' : 'default'}`}
    value={value}
    onValueChange={setValue}
    options={options}
    placeholder={pseudo ? '⟦ Çhøøšë å šţřûçţûřë šţåţûš ······ ⟧' : arabic ? 'اختر الحالة' : undefined}
    disabled={disabled}
    error={error}
    required={error}
    size={size}
    aria-label={arabic ? 'حالة الهيكل' : 'Structure status'}
    data-case="structure-status"
  />
}

function SelectFieldScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'select-field.locale-pseudo'
  const arabic = scenarioId === 'select-field.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'select-field.theme-light' ? 'light' : scenarioId === 'select-field.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid">
      <HarborlineLocaleProvider locale={locale}>
        {scenarioId === 'select-field.error-state' ? <SelectField aria-label="Inspection status" error name="inspection-status" onValueChange={() => {}} options={defaultOptions} value="pending" /> : scenarioId === 'select-field.content' ? <SelectField name="content-resilience" value="long" onValueChange={() => {}} open options={contentOptions} aria-label="Content resilience options" /> : <SelectFixture scenarioId={scenarioId} size={scenarioId === 'select-field.states' || theme ? 'sm' : 'md'} />}
        {scenarioId !== 'select-field.content' && scenarioId !== 'select-field.error-state' && (scenarioId === 'select-field.states' || theme) && <>
          <SelectFixture scenarioId={scenarioId} size="md" error />
          <SelectFixture scenarioId={scenarioId} size="lg" disabled />
        </>}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Select Field', component: SelectFieldScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SelectFieldScenario>
export default meta
type Story = StoryObj<typeof meta>
export const ValuePlaceholderAndValidation: Story = { name: 'Value, placeholder, and validation', args: { scenarioId: 'select-field.states' } }
export const OpenAndSelect: Story = { name: 'Open and select', args: { scenarioId: 'select-field.selection' } }
export const KeyboardTypeaheadAndDismissal: Story = { name: 'Keyboard, typeahead, and dismissal', args: { scenarioId: 'select-field.keyboard-dismissal' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'select-field.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'select-field.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'select-field.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'select-field.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'select-field.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'select-field.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'select-field.error-state' } }
