import { useMemo, useRef, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { ScrollAffordance, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'scroll-affordance.axes-overflow'
  | 'scroll-affordance.keyboard-status'
  | 'scroll-affordance.snap-fade'
  | 'scroll-affordance.replacement-host'
  | 'scroll-affordance.locale-pseudo'
  | 'scroll-affordance.locale-ar'
  | 'scroll-affordance.theme-light'
  | 'scroll-affordance.theme-dark'

const copy: Record<ScenarioId, [string, string]> = {
  'scroll-affordance.axes-overflow': ['Axes and overflow state', 'Only overflowing regions enter the tab order; horizontal and vertical axes remain independent.'],
  'scroll-affordance.keyboard-status': ['Keyboard and live position', 'Arrow, Home, and End move the active axis while a polite status describes the visible range.'],
  'scroll-affordance.snap-fade': ['Snap and logical fades', 'Mandatory snapping and edge masks expose hidden content without theme-specific colors.'],
  'scroll-affordance.replacement-host': ['Host attributes and replacement', 'The latest child set, caller attribute, class, and scroll-host reference remain intact.'],
  'scroll-affordance.locale-pseudo': ['Pseudo-localized announcements', 'Expanded position messages and labels remain available without changing content order.'],
  'scroll-affordance.locale-ar': ['Arabic logical scrolling', 'Logical start, end, fades, and keyboard movement mirror under right-to-left direction.'],
  'scroll-affordance.theme-light': ['Light theme', 'Overflow, focus, snap, and fade states use the Harborline light tokens.'],
  'scroll-affordance.theme-dark': ['Dark theme', 'The same labelled scroll region on the Harborline dark surface.'],
}

const pseudoCatalog = {
  'scrollAffordance.showingOne': '⟦ Šçřøļļåƀļë, šhøŵîñĝ {index} øƒ {total} ······ ⟧',
  'scrollAffordance.showingRange': '⟦ Šçřøļļåƀļë, šhøŵîñĝ {start} ţø {end} øƒ {total} ······ ⟧',
  'scrollAffordance.endReached': '⟦ Šçřøļļåƀļë řëĝîøñ, ëñđ řëåçhëđ ······ ⟧',
  'scrollAffordance.moreAvailable': '⟦ Møřë çøñţëñţ åṽåîļåƀļë; ûšë åřřøŵ ķëÿš ········ ⟧',
  'scrollAffordance.percentScrolled': '⟦ Šçřøļļåƀļë řëĝîøñ, {percent}% šçřøļļëđ ······ ⟧',
}

function Tiles({ count, revision, vertical = false }: { count: number; revision: number; vertical?: boolean }) {
  return <div style={{ display: 'flex', flexDirection: vertical ? 'column' : 'row', gap: 12, minWidth: vertical ? 0 : count * 176 }}>
    {Array.from({ length: count }, (_, index) => <article
      key={`${revision}-${index}`}
      style={{ border: '1px solid var(--hl-border, CanvasText)', borderRadius: 8, flex: '0 0 164px', padding: 12, scrollSnapAlign: 'start' }}
    >
      <strong>Structure {revision}-{index + 1}</strong>
      <p style={{ marginBlockEnd: 0 }}>Grid {String.fromCharCode(65 + (index % 6))}-{index + 1}</p>
    </article>)}
  </div>
}

function ScrollFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const [revision, setRevision] = useState(1)
  const regionRef = useRef<HTMLDivElement>(null)
  const rtl = scenarioId === 'scroll-affordance.locale-ar'
  const pseudo = scenarioId === 'scroll-affordance.locale-pseudo'
  const axisDemo = scenarioId === 'scroll-affordance.axes-overflow'
  const label = rtl ? 'نتائج فحص الهيكل' : pseudo ? '⟦ Šţřûçţûřë îñšþëçţîøñ řëšûļţš ······ ⟧' : 'Structure inspection results'
  const count = scenarioId === 'scroll-affordance.replacement-host' ? (revision === 1 ? 7 : 4) : 8
  const tiles = useMemo(() => <Tiles count={count} revision={revision} />, [count, revision])

  if (axisDemo) return <div className="hl-gallery-feedback-stack">
    <ScrollAffordance ariaLabel="Fitting summary"><Tiles count={2} revision={1} /></ScrollAffordance>
    <ScrollAffordance ariaLabel="Vertical findings" orientation="vertical" style={{ maxHeight: 220 }}>
      <Tiles count={7} revision={1} vertical />
    </ScrollAffordance>
  </div>

  return <div className="hl-gallery-feedback-stack">
    <ScrollAffordance
      ref={regionRef}
      ariaLabel={label}
      className="gallery-scroll-affordance"
      data-owner="app"
      fadeSize={scenarioId === 'scroll-affordance.snap-fade' ? 32 : 24}
      itemCount={count}
      snap={scenarioId === 'scroll-affordance.snap-fade'}
    >
      {tiles}
    </ScrollAffordance>
    {scenarioId === 'scroll-affordance.replacement-host' ? <>
      <button type="button" onClick={() => setRevision(current => current === 1 ? 96 : 1)}>Replace children</button>
      <output aria-live="polite">Revision {revision}; host ref {regionRef.current ? 'connected' : 'pending'}.</output>
    </> : null}
  </div>
}

function ScrollAffordanceScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'scroll-affordance.locale-pseudo'
  const arabic = scenarioId === 'scroll-affordance.locale-ar'
  const theme = scenarioId === 'scroll-affordance.theme-light' ? 'light' : scenarioId === 'scroll-affordance.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ maxWidth: 720 }}>
      <HarborlineLocaleProvider catalog={pseudo ? pseudoCatalog : undefined} direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        <ScrollFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Scroll Affordance', component: ScrollAffordanceScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof ScrollAffordanceScenario>
export default meta
type Story = StoryObj<typeof meta>

export const AxesAndOverflowState: Story = { name: 'Axes and overflow state', args: { scenarioId: 'scroll-affordance.axes-overflow' } }
export const KeyboardAndLivePosition: Story = { name: 'Keyboard and live position', args: { scenarioId: 'scroll-affordance.keyboard-status' } }
export const SnapAndLogicalFades: Story = { name: 'Snap and logical fades', args: { scenarioId: 'scroll-affordance.snap-fade' } }
export const HostAttributesAndReplacement: Story = { name: 'Host attributes and replacement', args: { scenarioId: 'scroll-affordance.replacement-host' } }
export const PseudoLocalizedAnnouncements: Story = { name: 'Pseudo-localized announcements', args: { scenarioId: 'scroll-affordance.locale-pseudo' } }
export const ArabicLogicalScrolling: Story = { name: 'Arabic logical scrolling', args: { scenarioId: 'scroll-affordance.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'scroll-affordance.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'scroll-affordance.theme-dark' } }
