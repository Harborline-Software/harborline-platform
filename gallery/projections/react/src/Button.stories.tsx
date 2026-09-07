import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  Button,
  HarborlineLocaleProvider,
  type ButtonFillMode,
  type ButtonIntent,
  type ButtonRounded,
  type ButtonSize,
} from '@harborline-software/ui-react'

type GalleryScenarioId =
  | 'button.defaults'
  | 'button.variants'
  | 'button.sizes'
  | 'button.fills'
  | 'button.radii'
  | 'button.loading'
  | 'button.disabled'
  | 'button.icons'
  | 'button.accessibility'
  | 'button.host-attributes'
  | 'button.rtl'
  | 'button.theme-light'
  | 'button.theme-dark'
  | 'button.locale-en'
  | 'button.locale-pseudo'
  | 'button.locale-ar'
  | 'button.content'
  | 'button.error-state'

const intents: ButtonIntent[] = [
  'primary', 'secondary', 'danger', 'warning', 'info',
  'success', 'light', 'dark', 'subtle', 'transparent',
]
const sizes: Array<[string, ButtonSize]> = [['Small', 'sm'], ['Medium', 'md'], ['Large', 'lg']]
const fills: ButtonFillMode[] = ['solid', 'outline', 'flat', 'link', 'clear']
const radii: ButtonRounded[] = ['none', 'small', 'medium', 'large', 'full']

const titleByScenario: Record<GalleryScenarioId, [string, string]> = {
  'button.defaults': ['Defaults', 'The neutral action and its preserved type=button default.'],
  'button.variants': ['Variants', 'The complete canonical intent vocabulary.'],
  'button.sizes': ['Sizes', 'Compact, standard, and prominent control heights.'],
  'button.fills': ['Fill modes', 'One semantic color across the five supported treatments.'],
  'button.radii': ['Radii', 'Square through fully rounded geometry.'],
  'button.loading': ['Loading', 'Focusable and inert while keeping its visible label.'],
  'button.disabled': ['Disabled', 'Native disabled semantics with reduced emphasis.'],
  'button.icons': ['Icons', 'Leading, trailing, and named icon-only content.'],
  'button.accessibility': ['Accessibility', 'Explicit naming and busy-state semantics.'],
  'button.host-attributes': ['Host attributes', 'Consumer ARIA, data, class, and form attributes survive.'],
  'button.rtl': ['Right to left', 'Logical content order under an Arabic locale.'],
  'button.theme-light': ['Light theme', 'Interaction, status, icon, border, text, and background states on the neutral light fixture.'],
  'button.theme-dark': ['Dark theme', 'Interaction, status, icon, border, text, and background states on the neutral dark fixture.'],
  'button.locale-en': ['English locale', 'Source-language label and component-owned loading status.'],
  'button.locale-pseudo': ['Pseudo locale', 'Expanded development-only copy exposes extraction and reflow defects.'],
  'button.locale-ar': ['Arabic locale', 'Translated status, RTL flow, and an isolated LTR identifier.'],
  'button.content': ['Content resilience', 'Hostile-but-valid labels remain visible text across ordinary semantic button treatments.'],
  'button.error-state': ['Error state', 'A destructive action uses the component error appearance.'],
}

function GalleryIcon({ children }: { children: string }) {
  return <span className="hl-gallery-icon" aria-hidden="true">{children}</span>
}

function ButtonScenario({ scenarioId }: { scenarioId: GalleryScenarioId }) {
  const [title, description] = titleByScenario[scenarioId]
  let content

  switch (scenarioId) {
    case 'button.defaults':
      content = <Button>Save changes</Button>
      break
    case 'button.variants':
      content = intents.map(intent => <Button key={intent} intent={intent}>{intent}</Button>)
      break
    case 'button.sizes':
      content = sizes.map(([label, size]) => (
        <div className="hl-gallery-stack" key={size}>
          <span className="hl-gallery-label">{label}</span>
          <Button intent="primary" size={size}>{label}</Button>
        </div>
      ))
      break
    case 'button.fills':
      content = fills.map(fillMode => <Button key={fillMode} fillMode={fillMode} themeColor="primary">{fillMode}</Button>)
      break
    case 'button.radii':
      content = radii.map(rounded => <Button key={rounded} intent="primary" rounded={rounded}>{rounded}</Button>)
      break
    case 'button.loading':
      content = <Button intent="primary" loading>Saving changes</Button>
      break
    case 'button.disabled':
      content = <><Button intent="primary" disabled>Primary</Button><Button disabled>Secondary</Button></>
      break
    case 'button.icons':
      content = <>
        <Button intent="primary" leadingIcon={<GalleryIcon>+</GalleryIcon>}>Create</Button>
        <Button trailingIcon={<GalleryIcon>→</GalleryIcon>}>Continue</Button>
        <Button size="icon" intent="subtle" aria-label="Add item"><GalleryIcon>+</GalleryIcon></Button>
      </>
      break
    case 'button.accessibility':
      content = <>
        <Button size="icon" intent="primary" aria-label="Add item"><GalleryIcon>+</GalleryIcon></Button>
        <Button loading aria-describedby="saving-help">Saving</Button>
        <span id="saving-help" className="hl-gallery-sr-only">Your changes are being saved.</span>
      </>
      break
    case 'button.host-attributes':
      content = <>
        <Button aria-controls="settings-panel" data-case="shared" className="consumer" form="profile">Open settings</Button>
        <div id="settings-panel" hidden>Settings</div><form id="profile" hidden />
      </>
      break
    case 'button.rtl':
      content = (
        <HarborlineLocaleProvider locale="ar-SA">
          <Button intent="primary" leadingIcon={<GalleryIcon>←</GalleryIcon>} trailingIcon={<GalleryIcon>✓</GalleryIcon>}>حفظ التغييرات</Button>
        </HarborlineLocaleProvider>
      )
      break
    case 'button.theme-light':
    case 'button.theme-dark':
      content = <>
        <Button intent="primary" data-theme-state="interactive">Primary</Button>
        <Button data-theme-state="secondary">Secondary</Button>
        <Button intent="primary" disabled data-theme-state="disabled">Disabled</Button>
        <Button intent="primary" loading data-theme-state="loading">Loading</Button>
        <Button size="icon" intent="primary" aria-label="Add item" data-theme-state="icon"><GalleryIcon>+</GalleryIcon></Button>
      </>
      break
    case 'button.locale-en':
      content = (
        <HarborlineLocaleProvider locale="en-US" catalog={{ 'common.loading': 'Loading' }}>
          <Button intent="primary" loading>Saving changes</Button>
        </HarborlineLocaleProvider>
      )
      break
    case 'button.locale-pseudo':
      content = (
        <HarborlineLocaleProvider locale="en-XA" catalog={{ 'common.loading': '⟦ Ļøåđîñĝ ···· ⟧' }}>
          <Button intent="primary" loading>⟦ Šåṽîñĝ çhåñĝéš ······ ⟧</Button>
        </HarborlineLocaleProvider>
      )
      break
    case 'button.locale-ar':
      content = (
        <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'common.loading': 'جارٍ التحميل' }}>
          <Button intent="primary" loading>جارٍ حفظ التغييرات <bdi dir="ltr">INV-2048</bdi></Button>
        </HarborlineLocaleProvider>
      )
      break
    case 'button.content':
      content = <>
        <Button>{'Awaiting third-party structural certification review'}</Button>
        <Button intent="primary">{'1,284,905'}</Button>
        <Button intent="danger">{'Bay 4 <grid C-7> & 8'}</Button>
        <Button intent="success">{'Ordnance Survey — Niño Ångström'}</Button>
      </>
      break
    case 'button.error-state':
      content = <Button themeColor="error">Delete vessel record</Button>
      break
  }

  return (
    <section
      className="hl-gallery-scene"
      data-gallery-probe
      data-gallery-scenario={scenarioId}
      data-theme={scenarioId === 'button.theme-light' ? 'light' : scenarioId === 'button.theme-dark' ? 'dark' : undefined}
    >
      <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
      <div className="hl-gallery-stage">{content}</div>
    </section>
  )
}

const meta = {
  title: 'Platform/Button',
  component: ButtonScenario,
  tags: ['autodocs'],
  parameters: { controls: { disable: true } },
} satisfies Meta<typeof ButtonScenario>

export default meta
type Story = StoryObj<typeof meta>

export const Defaults: Story = { name: 'Defaults', args: { scenarioId: 'button.defaults' } }
export const Variants: Story = { name: 'Variants', args: { scenarioId: 'button.variants' } }
export const Sizes: Story = { name: 'Sizes', args: { scenarioId: 'button.sizes' } }
export const FillModes: Story = { name: 'Fill modes', args: { scenarioId: 'button.fills' } }
export const Radii: Story = { name: 'Radii', args: { scenarioId: 'button.radii' } }
export const Loading: Story = { name: 'Loading', args: { scenarioId: 'button.loading' } }
export const Disabled: Story = { name: 'Disabled', args: { scenarioId: 'button.disabled' } }
export const Icons: Story = { name: 'Icons', args: { scenarioId: 'button.icons' } }
export const Accessibility: Story = { name: 'Accessibility', args: { scenarioId: 'button.accessibility' } }
export const HostAttributes: Story = { name: 'Host attributes', args: { scenarioId: 'button.host-attributes' } }
export const RightToLeft: Story = { name: 'Right to left', args: { scenarioId: 'button.rtl' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'button.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'button.theme-dark' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'button.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'button.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'button.locale-ar' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'button.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'button.error-state' } }
