import { useState } from 'react'
import type { ComponentProps, KeyboardEvent } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { FormField, HarborlineLocaleProvider, TextBox } from '@harborline-software/ui-react'

type ScenarioId =
  | 'text-box.ownership-form'
  | 'text-box.clear-reveal'
  | 'text-box.attributes-keyboard'
  | 'text-box.appearance-reflow'
  | 'text-box.replacement'
  | 'text-box.locale-pseudo'
  | 'text-box.locale-ar'
  | 'text-box.theme-light'
  | 'text-box.theme-dark'
  | 'text-box.content'
  | 'text-box.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'text-box.ownership-form': ['Value ownership and form state', 'Controlled and uncontrolled raw strings preserve Form Field identity, description, required, invalid, and disabled state.'],
  'text-box.clear-reveal': ['Clear, reveal, and composed actions', 'Keyboard-reachable clear and password actions compose after caller suffix content and retain the editing context.'],
  'text-box.attributes-keyboard': ['Input attributes and keyboard handler', 'Identity, autocomplete, ARIA, tab order, and caller keyboard handling target the native input exactly once.'],
  'text-box.appearance-reflow': ['Appearance and narrow reflow', 'Sizes, fills, rounding, prefix, suffix, and actions remain usable at a narrow 200-percent surface.'],
  'text-box.replacement': ['Complete controlled replacement', 'Caller replacement remains authoritative while reveal state never mutates the raw value.'],
  'text-box.locale-pseudo': ['Pseudo-localized actions', 'Expanded placeholder, clear, reveal, and description copy reflow without clipping.'],
  'text-box.locale-ar': ['Arabic right-to-left editor', 'Prefix, input, suffix, and actions follow logical order while explicit direction remains available.'],
  'text-box.theme-light': ['Light theme', 'Control, validation, action, filled, and focus states use Harborline light tokens.'],
  'text-box.theme-dark': ['Dark theme', 'The same raw-string editor states on the Harborline dark surface.'],
  'text-box.content': ['Content resilience', 'Real content spans an overlong label, a grouped large number, escaped markup characters, and a diacritic-rich name.'],
  'text-box.error-state': ['Error state', 'The text box renders its declared invalid state with an accessible name.'],
}

const pseudoCatalog = {
  'common.clear': '⟦ Çļëåř ţhë çûřřëñţ ṽåļûë ······ ⟧',
  'forms.textBox.showPassword': '⟦ Šhøŵ þåššŵøřđ ······ ⟧',
  'forms.textBox.hidePassword': '⟦ Hîđë þåššŵøřđ ······ ⟧',
}
const arabicCatalog = {
  'common.clear': 'مسح القيمة',
  'forms.textBox.showPassword': 'إظهار كلمة المرور',
  'forms.textBox.hidePassword': 'إخفاء كلمة المرور',
}

function LiveTextBox({ initial, ...rest }: Omit<ComponentProps<typeof TextBox>, 'value' | 'onChange'> & { initial: string }) { const [value, setValue] = useState(initial); return <TextBox {...rest} value={value} onChange={setValue} /> }

function OwnershipFixture() {
  const [controlled, setControlled] = useState('Pier 14')
  return <div className="hl-gallery-feedback-grid">
    <FormField name="structureName" label="Structure name" hint="Use the registered structural identifier." required>
      <TextBox name="structureName" value={controlled} onChange={setControlled} required />
    </FormField>
    <FormField name="assetTag" label="Asset tag" error="Asset tag is required.">
      <TextBox name="assetTag" defaultValue="Initial A-104" error />
    </FormField>
    <FormField name="lockedRecord" label="Locked record" disabled>
      <LiveTextBox name="lockedRecord" initial="Read-only source" />
    </FormField>
    <button type="button" onClick={() => setControlled(current => current === 'Pier 14' ? 'Pier 96' : 'Pier 14')}>Replace controlled value</button>
  </div>
}

function TextBoxFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const pseudo = scenarioId === 'text-box.locale-pseudo'
  const arabic = scenarioId === 'text-box.locale-ar'
  const replacement = scenarioId === 'text-box.replacement'
  const [value, setValue] = useState(arabic ? 'الهيكل ألف' : pseudo ? '⟦ Šţřûçţûřë Åļþhå ······ ⟧' : replacement ? 'Revision 1' : 'Blueprint C-14')
  const [keys, setKeys] = useState<string[]>([])
  const label = arabic ? 'اسم الهيكل' : pseudo ? '⟦ Šţřûçţûřë đîšþļåÿ ñåmë ······ ⟧' : 'Structure display name'

  if (scenarioId === 'text-box.ownership-form') return <OwnershipFixture />
  if (scenarioId === 'text-box.content') return <div className="hl-gallery-feedback-grid">
    <TextBox aria-label="Long content" defaultValue="Awaiting third-party structural certification review" />
    <TextBox aria-label="Large number" defaultValue="1,284,905" />
    <TextBox aria-label="Escaped content" defaultValue={'Bay 4 <grid C-7> & 8'} />
    <TextBox aria-label="Unusual name" defaultValue="Ordnance Survey — Niño Ångström" />
  </div>
  if (scenarioId === 'text-box.error-state') return <TextBox aria-label="Vessel name" defaultValue="Invalid berth assignment" error name="vessel-name" />
  if (scenarioId === 'text-box.clear-reveal') return <div className="hl-gallery-feedback-grid">
    <TextBox aria-label="Clearable structure name" clearButton name="clearableStructure" value={value} onChange={setValue} prefix={<span aria-hidden="true">⌖</span>} suffix={<span>Verified</span>} />
    <LiveTextBox aria-label="Structure access code" name="structureCode" showReveal type="password" initial="Harborline-2026" />
    <LiveTextBox aria-label="Disabled structure access code" disabled name="disabledCode" showReveal type="password" initial="Locked" />
  </div>
  if (scenarioId === 'text-box.appearance-reflow') return <div className="hl-gallery-feedback-stack" style={{ maxWidth: 320 }}>
    <LiveTextBox aria-label="Small flat value" clearButton fillMode="flat" rounded="small" size="sm" initial="Grid C-14" />
    <LiveTextBox aria-label="Medium solid value" clearButton fillMode="solid" rounded="medium" size="md" initial="Grid C-14" />
    <LiveTextBox aria-label="Large outline value" clearButton fillMode="outline" rounded="full" size="lg" initial="Grid C-14" />
  </div>

  return <div className="hl-gallery-feedback-stack" style={{ maxWidth: pseudo ? 420 : 520 }}>
    <label>{label}
      <TextBox
        aria-label={label}
        autoComplete="organization"
        clearButton
        data-owner="app"
        dir={arabic ? 'rtl' : undefined}
        id="structure-display-name"
        name="structureDisplayName"
        onChange={(next: string) => { if (!replacement) setValue(next) }}
        onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => setKeys(current => [...current.slice(-3), event.key])}
        placeholder={pseudo ? '⟦ Ëñţëř å ļøñĝ šţřûçţûřë ñåmë ········ ⟧' : arabic ? 'أدخل اسم الهيكل' : 'Enter structure name'}
        prefix={<span aria-hidden="true">⌂</span>}
        showReveal={replacement}
        suffix={<span>{arabic ? 'معتمد' : pseudo ? '⟦ Ṽëřîƒîëđ ··· ⟧' : 'Verified'}</span>}
        tabIndex={0}
        type={replacement ? 'password' : 'text'}
        value={value}
      />
    </label>
    <output aria-live="polite">Keys: {keys.length ? keys.join(', ') : 'none'}; value: {value}</output>
    {replacement ? <button type="button" onClick={() => setValue(current => current === 'Revision 1' ? 'Revision 96' : 'Revision 1')}>Replace caller value</button> : null}
  </div>
}

function TextBoxScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'text-box.locale-pseudo'
  const arabic = scenarioId === 'text-box.locale-ar'
  const theme = scenarioId === 'text-box.theme-light' ? 'light' : scenarioId === 'text-box.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">
      <HarborlineLocaleProvider catalog={pseudo ? pseudoCatalog : arabic ? arabicCatalog : undefined} direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        <TextBoxFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Text Box', component: TextBoxScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof TextBoxScenario>
export default meta
type Story = StoryObj<typeof meta>

export const ValueOwnershipAndFormState: Story = { name: 'Value ownership and form state', args: { scenarioId: 'text-box.ownership-form' } }
export const ClearRevealAndComposedActions: Story = { name: 'Clear, reveal, and composed actions', args: { scenarioId: 'text-box.clear-reveal' } }
export const InputAttributesAndKeyboardHandler: Story = { name: 'Input attributes and keyboard handler', args: { scenarioId: 'text-box.attributes-keyboard' } }
export const AppearanceAndNarrowReflow: Story = { name: 'Appearance and narrow reflow', args: { scenarioId: 'text-box.appearance-reflow' } }
export const CompleteControlledReplacement: Story = { name: 'Complete controlled replacement', args: { scenarioId: 'text-box.replacement' } }
export const PseudoLocalizedActions: Story = { name: 'Pseudo-localized actions', args: { scenarioId: 'text-box.locale-pseudo' } }
export const ArabicRightToLeftEditor: Story = { name: 'Arabic right-to-left editor', args: { scenarioId: 'text-box.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'text-box.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'text-box.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'text-box.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'text-box.error-state' } }
