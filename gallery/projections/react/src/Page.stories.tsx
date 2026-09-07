import type { Meta, StoryObj } from '@storybook/react-vite'
import { Page } from '@harborline-software/ui-react'

type ScenarioId =
  | 'page.structure'
  | 'page.options'
  | 'page.keyboard-responsive'
  | 'page.locale-en'
  | 'page.locale-pseudo'
  | 'page.locale-ar'
  | 'page.theme-light'
  | 'page.theme-dark'
  | 'page.content'

const copy: Record<ScenarioId, [string, string]> = {
  'page.structure': ['Header and body structure', 'One header landmark sits above a keyboard-scrollable section; the application shell retains the main landmark.'],
  'page.options': ['Padding, sticky, and heading levels', 'Closed heading, padding, and sticky options change chrome without changing body ownership.'],
  'page.keyboard-responsive': ['Keyboard scroll and responsive actions', 'The body accepts focus and actions wrap below the title at phone width.'],
  'page.locale-en': ['English locale', 'Caller-owned title, subtitle, actions, and body content are preserved.'],
  'page.locale-pseudo': ['Pseudo locale', 'Expanded title and action copy wrap without clipping.'],
  'page.locale-ar': ['Arabic locale', 'Logical action placement follows RTL while preserving content order.'],
  'page.theme-light': ['Light theme', 'Header, body, boundary, and focus states use public light-theme tokens.'],
  'page.theme-dark': ['Dark theme', 'The same route shell on the Harborline dark surface.'],
  'page.content': ['Content resilience', 'Hostile page content carries a long action label, the grouped number 1,284,905, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.'],
}

function ContentFixture() {
  return <div style={{ height: 360, minWidth: 0 }}>
    <Page
      title={'Awaiting third-party structural certification review'}
      subtitle={'Ordnance Survey — Niño Ångström'}
      actions={<button type="button">{'Review affected inspection records and continue'}</button>}
    >
      <p>{'1,284,905 records at Bay 4 <grid C-7> & 8'}</p>
    </Page>
  </div>
}

function PageFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const pseudo = scenarioId === 'page.locale-pseudo'
  const arabic = scenarioId === 'page.locale-ar'
  const options = scenarioId === 'page.options'
  const title = arabic ? 'صحة الهيكل' : pseudo ? '⟦ Šţřûçţûřë hëåļţh åñđ îñšþëçţîøñ šûmmåřÿ ········ ⟧' : 'Structure health'
  const subtitle = arabic ? 'مراجعة عمليات الفحص الأخيرة' : pseudo ? '⟦ Řëṽîëŵ řëçëñţ îñšþëçţîøñ åçţîṽîţÿ ······ ⟧' : 'Review recent inspection activity'
  const actionLabel = arabic ? 'تحديث التقرير' : pseudo ? '⟦ Řëƒřëšh ţhë šţřûçţûřë řëþøřţ ······ ⟧' : 'Refresh report'
  return <div style={{ height: 360, minWidth: 0 }}>
    <Page
      id="structure-health-page"
      className="hl-gallery-record"
      bodyClassName="hl-gallery-feedback-stack"
      title={title}
      subtitle={subtitle}
      actions={<button type="button">{actionLabel}</button>}
      titleAs={options ? 'h2' : 'h1'}
      sticky={!options}
      bodyPadding={options ? 'lg' : 'md'}
    >
      <p>Harborline route content remains independent from the application shell landmark.</p>
      {Array.from({ length: 8 }, (_, index) => <p key={index}>Inspection row {index + 1}: placement and pose captured.</p>)}
    </Page>
  </div>
}

function PageScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const arabic = scenarioId === 'page.locale-ar'
  const theme = scenarioId === 'page.theme-light' ? 'light' : scenarioId === 'page.theme-dark' ? 'dark' : undefined
  const page = scenarioId === 'page.content' ? <ContentFixture /> : <PageFixture scenarioId={scenarioId} />
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">{page}</div>
  </section>
}

const meta = { title: 'Platform/Page', component: PageScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof PageScenario>
export default meta
type Story = StoryObj<typeof meta>

export const HeaderAndBodyStructure: Story = { name: 'Header and body structure', args: { scenarioId: 'page.structure' } }
export const PaddingStickyAndHeadingLevels: Story = { name: 'Padding, sticky, and heading levels', args: { scenarioId: 'page.options' } }
export const KeyboardScrollAndResponsiveActions: Story = { name: 'Keyboard scroll and responsive actions', args: { scenarioId: 'page.keyboard-responsive' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'page.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'page.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'page.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'page.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'page.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'page.content' } }
