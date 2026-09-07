import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { HarborlineLocaleProvider, SideNav } from '@harborline-software/ui-react'

type ScenarioId =
  | 'side-nav.structure-active'
  | 'side-nav.activation-states'
  | 'side-nav.collapsed-badges'
  | 'side-nav.replacement'
  | 'side-nav.locale-pseudo'
  | 'side-nav.locale-ar'
  | 'side-nav.theme-light'
  | 'side-nav.theme-dark'
  | 'side-nav.content'

interface ActivatedItem {
  readonly id: string
  readonly label: string
}

const copy: Record<ScenarioId, [string, string]> = {
  'side-nav.structure-active': ['Groups, links, and active route', 'Ordered groups and recursive items expose one current leaf without owning the route table.'],
  'side-nav.activation-states': ['Activation, disclosure, and disabled state', 'Buttons, links, disclosure, disabled policy, and visible focus retain native keyboard behavior.'],
  'side-nav.collapsed-badges': ['Collapsed rail, badges, and targets', 'The icon rail preserves names and tooltips while noninteractive badges remain outside activation targets.'],
  'side-nav.replacement': ['Complete model replacement', 'The newest groups, active identity, labels, and callbacks replace the previous model without stale rows.'],
  'side-nav.locale-pseudo': ['Pseudo-localized navigation', 'Expanded group, item, tooltip, and badge copy reflows without clipping.'],
  'side-nav.locale-ar': ['Arabic right-to-left navigation', 'Indentation, disclosure, icons, badges, and tooltips follow logical direction.'],
  'side-nav.theme-light': ['Light theme', 'Navigation surface, active route, hover, disabled, badge, and focus use Harborline light tokens.'],
  'side-nav.theme-dark': ['Dark theme', 'The same recursive navigation model on the Harborline dark surface.'],
  'side-nav.content': ['Content resilience', 'Hostile-but-valid labels remain text across a real navigation collection.'],
}

function icon(glyph: string) {
  return <span aria-hidden="true" style={{ display: 'inline-grid', minWidth: 20, placeItems: 'center' }}>{glyph}</span>
}

function SideNavFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const [revision, setRevision] = useState(1)
  const [activeId, setActiveId] = useState('calendar')
  const [lastActivation, setLastActivation] = useState('none')
  const pseudo = scenarioId === 'side-nav.locale-pseudo'
  const arabic = scenarioId === 'side-nav.locale-ar'
  const collapsed = scenarioId === 'side-nav.collapsed-badges'
  const contentItems = [{id: 'content', label: 'Content resilience', items: [
    {id: 'long', label: 'Awaiting third-party structural certification review'},
    {id: 'number', label: '1,284,905'},
    {id: 'hostile', label: 'Bay 4 <grid C-7> & 8'},
    {id: 'name', label: 'Ordnance Survey — Niño Ångström'},
  ]}]
  const label = (english: string, rtl: string) => arabic ? rtl : pseudo ? `⟦ ${english} ······ ⟧` : english
  const items = useMemo(() => revision === 1 ? [
    {
      id: 'operations',
      label: label('Operations', 'العمليات'),
      items: [
        { id: 'overview', label: label('Overview', 'نظرة عامة'), icon: icon('⌂'), badge: <span>{label('New', 'جديد')}</span>, href: '#overview' },
        { id: 'calendar', label: label('Schedule', 'الجدول'), icon: icon('▦'), badge: <span>3</span> },
        { id: 'assets', label: label('Structures', 'الهياكل'), icon: icon('◇'), children: [
          { id: 'asset-map', label: label('Structure map', 'خريطة الهيكل'), icon: icon('⌖') },
          { id: 'asset-archive', label: label('Archived structures', 'الهياكل المؤرشفة'), icon: icon('□'), disabled: true },
        ] },
      ],
    },
    {
      id: 'administration',
      label: label('Administration', 'الإدارة'),
      items: [{ id: 'team', label: label('Team access', 'وصول الفريق'), icon: icon('◉') }],
    },
  ] : [{
    id: 'spatial',
    label: label('Spatial workspace', 'مساحة العمل المكانية'),
    items: [
      { id: 'capture', label: label('Photo with pose', 'صورة مع الوضعية'), icon: icon('◎'), badge: <span>96</span> },
      { id: 'blueprints', label: label('Blueprint placement', 'موضع المخطط'), icon: icon('▤') },
    ],
  }], [arabic, pseudo, revision])

  return <div className="hl-gallery-feedback-stack">
    <div style={{ border: '1px solid var(--hl-color-border, #888)', maxWidth: collapsed ? 96 : 320, minHeight: 420 }}>
      {scenarioId === 'side-nav.content' ? <SideNav items={contentItems} activeItemId="long" navigationLabel="Content resilience navigation" /> : <SideNav
        activeItemId={activeId}
        className="gallery-side-nav"
        collapsed={collapsed}
        items={items}
        onItemActivate={(item: ActivatedItem) => {
          setActiveId(item.id)
          setLastActivation(item.label)
        }}
      />}
    </div>
    <output aria-live="polite">Last activation: {lastActivation}</output>
    {scenarioId === 'side-nav.replacement' ? <button type="button" onClick={() => { setRevision(current => current === 1 ? 96 : 1); setActiveId('capture') }}>Replace navigation model</button> : null}
  </div>
}

function SideNavScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'side-nav.locale-pseudo'
  const arabic = scenarioId === 'side-nav.locale-ar'
  const theme = scenarioId === 'side-nav.theme-light' ? 'light' : scenarioId === 'side-nav.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">
      <HarborlineLocaleProvider direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        <SideNavFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Side Nav', component: SideNavScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SideNavScenario>
export default meta
type Story = StoryObj<typeof meta>

export const GroupsLinksAndActiveRoute: Story = { name: 'Groups, links, and active route', args: { scenarioId: 'side-nav.structure-active' } }
export const ActivationDisclosureAndDisabledState: Story = { name: 'Activation, disclosure, and disabled state', args: { scenarioId: 'side-nav.activation-states' } }
export const CollapsedRailBadgesAndTargets: Story = { name: 'Collapsed rail, badges, and targets', args: { scenarioId: 'side-nav.collapsed-badges' } }
export const CompleteModelReplacement: Story = { name: 'Complete model replacement', args: { scenarioId: 'side-nav.replacement' } }
export const PseudoLocalizedNavigation: Story = { name: 'Pseudo-localized navigation', args: { scenarioId: 'side-nav.locale-pseudo' } }
export const ArabicRightToLeftNavigation: Story = { name: 'Arabic right-to-left navigation', args: { scenarioId: 'side-nav.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'side-nav.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'side-nav.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'side-nav.content' } }
