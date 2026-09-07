import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { AppLayout, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'app-layout.structure-modes' | 'app-layout.rail-breakpoints' | 'app-layout.drawer-state'
  | 'app-layout.dismissal-scroll' | 'app-layout.performance' | 'app-layout.locale-en'
  | 'app-layout.locale-pseudo' | 'app-layout.locale-ar' | 'app-layout.theme-light' | 'app-layout.theme-dark' | 'app-layout.content'

const copy: Record<ScenarioId, [string, string]> = {
  'app-layout.structure-modes': ['Structure and navigation modes', 'One shell owns one main landmark and at most one opaque header and navigation subtree.'],
  'app-layout.rail-breakpoints': ['Rail and breakpoint boundary', 'Rail capability requires both the frozen width and height boundary while collapsed content remains mounted.'],
  'app-layout.drawer-state': ['Drawer and controlled state', 'Phone and short-landscape viewports use one modal drawer with controlled or uncontrolled state.'],
  'app-layout.dismissal-scroll': ['Dismissal, header, and scroll ownership', 'Focus, fixed chrome, one vertical scroll owner, and wide content remain reachable.'],
  'app-layout.performance': ['Bounded shell under load', 'Large navigation, repeated updates, and viewport churn retain one current subtree without stale callbacks.'],
  'app-layout.locale-en': ['English locale', 'Caller header, navigation, and body content remain opaque.'],
  'app-layout.locale-pseudo': ['Pseudo locale', 'Expanded header and navigation labels remain viewport-clamped.'],
  'app-layout.locale-ar': ['Arabic locale', 'Rail, drawer, trigger, and safe areas use logical edges.'],
  'app-layout.theme-light': ['Light theme', 'Shell, header, rail, drawer, main, and scrim use public light tokens.'],
  'app-layout.theme-dark': ['Dark theme', 'The same responsive application chrome on the Harborline dark surface.'],
  'app-layout.content': ['Content resilience', 'Hostile application content carries a long header label, a grouped large number, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.'],
}

function Navigation({ count, pseudo, arabic }: { count: number; pseudo: boolean; arabic: boolean }) {
  return <div style={{ padding: 12 }}>
    <strong>{arabic ? 'كوميت إكس' : 'Engineering Services'}</strong>
    <ul>{Array.from({ length: count }, (_, index) => <li key={index}><a href={`#section-${index}`}>{arabic ? `قسم ${index + 1}` : pseudo ? `⟦ Structure workspace ${index + 1} ···· ⟧` : `Workspace ${index + 1}`}</a></li>)}</ul>
  </div>
}

function LayoutFixture({ scenarioId, collapsed = false, noNavigation = false }: { scenarioId: ScenarioId; collapsed?: boolean; noNavigation?: boolean }) {
  const pseudo = scenarioId === 'app-layout.locale-pseudo'
  const arabic = scenarioId === 'app-layout.locale-ar'
  const content = scenarioId === 'app-layout.content'
  const [mobileNavOpen, setMobileNavOpen] = useState(scenarioId === 'app-layout.drawer-state')
  const count = scenarioId === 'app-layout.performance' ? 256 : 5
  const sideNav = useMemo(() => noNavigation ? undefined : <Navigation count={count} pseudo={pseudo} arabic={arabic} />, [noNavigation, count, pseudo, arabic])
  const mode = scenarioId === 'app-layout.drawer-state' ? 'overlay' : scenarioId === 'app-layout.structure-modes' && noNavigation ? 'hidden' : 'auto'
  const body = content ? <article style={{ padding: 16, minWidth: 0 }}>
    <h1>{'Ordnance Survey — Niño Ångström'}</h1>
    <p>{'1,284,905'}</p>
    <p>{'Bay 4 <grid C-7> & 8'}</p>
  </article> : <article style={{ padding: 16, minWidth: scenarioId === 'app-layout.dismissal-scroll' ? 720 : 0 }}>
    <h1>{arabic ? 'التقاط صورة مع الوضعية' : 'Photo-with-pose capture'}</h1>
    <p>{pseudo ? '⟦ Place the captured pose within the selected blueprint structure. ········ ⟧' : 'Place the captured pose within the selected blueprint structure.'}</p>
    {Array.from({ length: 8 }, (_, index) => <p key={index}>Capture row {index + 1}: grid C-{index + 1}</p>)}
  </article>
  return <div style={{ height: 520, minWidth: 0, border: '1px solid var(--hl-color-border, #888)' }}>
    <AppLayout
      body={body}
      header={<div style={{ padding: 12 }}><strong>{content ? 'Awaiting third-party structural certification review' : arabic ? 'مساحة فحص الهيكل' : pseudo ? '⟦ Šţřûçţûřë îñšþëçţîøñ ŵøřķšþåçë ······ ⟧' : 'Structure inspection workspace'}</strong></div>}
      sideNav={sideNav}
      sideNavMode={mode}
      sideNavOpen={!collapsed}
      mobileNavOpen={mobileNavOpen}
      onMobileNavOpenChange={setMobileNavOpen}
      mobileNavLabel={arabic ? 'التنقل' : pseudo ? '⟦ Ñåṽîĝåţîøñ ···· ⟧' : 'Navigation'}
      headerFixed={scenarioId === 'app-layout.dismissal-scroll'}
      contentScroll={scenarioId === 'app-layout.structure-modes' ? 'page' : 'main'}
      data-case="engineering-shell"
    />
  </div>
}

function AppLayoutScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'app-layout.locale-pseudo'
  const arabic = scenarioId === 'app-layout.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'app-layout.theme-light' ? 'light' : scenarioId === 'app-layout.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack">
      <HarborlineLocaleProvider locale={locale}>
        <LayoutFixture scenarioId={scenarioId} collapsed={scenarioId === 'app-layout.rail-breakpoints'} noNavigation={scenarioId === 'app-layout.structure-modes'} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/App Layout', component: AppLayoutScenario, tags: ['autodocs'], parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof AppLayoutScenario>
export default meta
type Story = StoryObj<typeof meta>
export const StructureAndNavigationModes: Story = { name: 'Structure and navigation modes', args: { scenarioId: 'app-layout.structure-modes' } }
export const RailAndBreakpointBoundary: Story = { name: 'Rail and breakpoint boundary', args: { scenarioId: 'app-layout.rail-breakpoints' } }
export const DrawerAndControlledState: Story = { name: 'Drawer and controlled state', args: { scenarioId: 'app-layout.drawer-state' } }
export const DismissalHeaderAndScrollOwnership: Story = { name: 'Dismissal, header, and scroll ownership', args: { scenarioId: 'app-layout.dismissal-scroll' } }
export const BoundedShellUnderLoad: Story = { name: 'Bounded shell under load', args: { scenarioId: 'app-layout.performance' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'app-layout.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'app-layout.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'app-layout.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'app-layout.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'app-layout.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'app-layout.content' } }
