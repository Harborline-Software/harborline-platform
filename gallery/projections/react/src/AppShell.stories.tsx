import type * as React from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { AppShell, HarborlineLocaleProvider, type PackNavigationDeclaration, type PackPanelDeclaration, type ShellNavigationState, type ShellNavItem, type ShellScopeOption } from '@harborline-software/ui-react'
import { RoleVocabulary } from '@harborline-software/contracts/authorization'
import chromeFixture from '../../../../conformance/hlp.ui.app-shell/chrome-v1.json'

const roleVocabulary = RoleVocabulary.fromApi([])

type ScenarioId =
  | 'app-shell.structure' | 'app-shell.rail-collapsed' | 'app-shell.switchers' | 'app-shell.pins-threads'
  | 'app-shell.actions-endpanel' | 'app-shell.performance' | 'app-shell.locale-en'
  | 'app-shell.locale-pseudo' | 'app-shell.locale-ar' | 'app-shell.theme-light' | 'app-shell.theme-dark' | 'app-shell.content'

const copy: Record<ScenarioId, [string, string]> = {
  'app-shell.structure': ['Operations shell structure', 'The Operations workspace composes brand, grouped navigation, header actions, and one body landmark.'],
  'app-shell.rail-collapsed': ['Rail collapsed', 'The rail collapsed through defaultCollapsed rather than a click: the shell keeps its header and body and the rail is gone, not narrowed.'],
  'app-shell.switchers': ['Workspace and tenant switchers', 'Workspace and tenant scopes keep the active option first while preserving pinned and directory affordances.'],
  'app-shell.pins-threads': ['Pinned navigation and Pilot threads', 'Pinned Operations routes move to one ordered zone while Pilot session threads remain attached to their owning route.'],
  'app-shell.actions-endpanel': ['Ordered multi-panel dock', 'Every declared tool stays open in order; pairs share a pane and the third starts the next splitter branch.'],
  'app-shell.performance': ['Bounded Operations shell under load', 'A 256-item Operations navigation fixture retains equivalent shell observations across projections.'],
  'app-shell.locale-en': ['English locale', 'English shell chrome frames the Operations workspace and its Portfolio and Work groups.'],
  'app-shell.locale-pseudo': ['Pseudo locale', 'Expanded pseudo-localized labels verify truncation without clipping shell chrome.'],
  'app-shell.locale-ar': ['Arabic locale', 'Arabic direction places the Operations rail, thread indent, and Pilot panel on logical edges.'],
  'app-shell.theme-light': ['Light theme', 'Light public tokens render the complete Operations shell with accessible chrome contrast.'],
  'app-shell.theme-dark': ['Dark theme', 'Dark public tokens preserve the same Operations shell hierarchy and interaction affordances.'],
  'app-shell.content': ['Content resilience', 'Hostile shell navigation carries a label longer than the rail, a grouped large badge, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.'],
}

function Mono({ text, color }: { text: string; color: string }) {
  return <span style={{ inlineSize: 24, blockSize: 24, display: 'inline-flex', alignItems: 'center', justifyContent: 'center', borderRadius: 6, background: color, color: '#fff', fontSize: 11, fontWeight: 600 }}>{text}</span>
}

const tenantOptions: ShellScopeOption[] = [
  { id: 'meridian', label: 'Meridian Hotel Group', icon: <Mono text="MH" color="#0f62fe" /> }, { id: 'metro', label: 'Metro Transit Authority', icon: <Mono text="MT" color="#7c3aed" /> },
  { id: 'northstar', label: 'Northstar Health' }, { id: 'richmond', label: 'Richmond Properties' },
  { id: 'summit', label: 'Summit Logistics' }, { id: 'harbor', label: 'Harbor Retail' },
  { id: 'cedar', label: 'Cedar Schools' }, { id: 'atlas', label: 'Atlas Manufacturing' },
  { id: 'union', label: 'Union Credit' }, { id: 'civic', label: 'Civic Services' },
  { id: 'lakeside', label: 'Lakeside Hospitality' },
]

type GalleryWorkspace = { id: string; label: string; groups: readonly { id: string; label: string; items: readonly ShellNavItem[] }[] }
function workspaces(pseudo: boolean, arabic: boolean, load: boolean, content = false): GalleryWorkspace[] {
  const labels = content
    ? ['Awaiting third-party structural certification review', 'Bay 4 <grid C-7> & 8', 'Ordnance Survey — Niño Ångström', 'Run report', 'Work orders', 'Deficiencies', 'Trip plan']
    : arabic
    ? ['نظرة عامة', 'الأصول', 'عمليات التفتيش', 'تشغيل التقرير', 'أوامر العمل', 'أوجه القصور', 'خطة الرحلة']
    : pseudo
      ? ['⟦ Øṽëřṽîëŵ ···· ⟧', '⟦ Åššëţš ···· ⟧', '⟦ Îñšþëçţîøñš ···· ⟧', '⟦ Řûñ řëþøřţ ···· ⟧', '⟦ Ŵøřķ øřðëřš ···· ⟧', '⟦ Ðëƒîçîëñçîëš ···· ⟧', '⟦ Ţřîþ þļåñ ···· ⟧']
      : ['Overview', 'Assets', 'Inspections', 'Run report', 'Work orders', 'Deficiencies', 'Trip plan']
  const items = [
    { id: 'overview', label: labels[0] }, { id: 'assets', label: labels[1] },
    { id: 'inspections', label: labels[2], count: content ? '1,284,905' : 218, threads: [{ id: 'richmond-retry-audit', label: 'Richmond retry audit', active: true, actions: [{ id: 'rename', label: 'Rename' }, { id: 'archive', label: 'Archive' }, { id: 'delete', label: 'Delete', destructive: true }] }] },
    { id: 'run-report', label: labels[3] },
  ]
  if (load) for (let index = 0; index < 249; index++) items.push({ id: `route-${index}`, label: `Operations route ${index + 8}` })
  return [
    { id: 'operations', label: arabic ? 'العمليات' : pseudo ? '⟦ Øþëřåţîøñš ···· ⟧' : 'Operations', groups: [
      { id: 'portfolio', label: arabic ? 'المحفظة' : pseudo ? '⟦ Þøřţƒøļîø ···· ⟧' : 'Portfolio', items },
      { id: 'work', label: arabic ? 'العمل' : pseudo ? '⟦ Ŵøřķ ···· ⟧' : 'Work', items: [{ id: 'work-orders', label: labels[4], count: 42 }, { id: 'deficiencies', label: labels[5], count: 38 }, { id: 'trip-plan', label: labels[6], count: 3 }] },
    ] },
    { id: 'front-desk', label: 'Front desk', groups: [{ id: 'front-desk-work', label: 'Front desk', items: [{ id: 'arrivals', label: 'Arrivals' }, { id: 'departures', label: 'Departures' }] }] },
  ]
}

// chrome-spec 6 traits: form 2 and the pop-out affordance are EARNED from the declaration, never styled in.
// One authority for both App Shell stories: conformance/hlp.ui.app-shell/chrome-v1.json galleryPanels.
// The rows are already PackPanelDeclaration-shaped, so the story is a reader, not a second table
// (the Blazor story deserialises the same rows). Gallery `both App Shell stories render the fixture's
// panels` asserts what each shell actually rendered against this file.
const panels: PackPanelDeclaration[] = chromeFixture.galleryPanels as unknown as PackPanelDeclaration[]
function galleryNavigation(pseudo: boolean, arabic: boolean, load: boolean, content: boolean): { navigation: PackNavigationDeclaration; navigationState: ShellNavigationState; resolveLabel: (key: string) => string } {
  const sources = workspaces(pseudo, arabic, load, content); const items: Record<string, ShellNavItem> = {}
  for (const workspace of sources) for (const group of workspace.groups) for (const item of group.items) items[item.id] = item
  return { navigation: { seedWorkspaces: sources.map(workspace => ({ id: workspace.id, labelKey: workspace.label, groups: workspace.groups.map(group => ({ id: group.id, labelKey: group.label, itemIds: group.items.map(item => item.id) })) })), panelSet: panels }, navigationState: { items }, resolveLabel: key => key }
}

function ShellFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const pseudo = scenarioId === 'app-shell.locale-pseudo'
  const arabic = scenarioId === 'app-shell.locale-ar'
  const load = scenarioId === 'app-shell.performance'
  const content = scenarioId === 'app-shell.content'
  return <div style={{ height: 560, minWidth: 0, border: '1px solid var(--hl-color-border, #888)' }}>
    <AppShell
      shellId={`operations-${scenarioId.split('.').at(-1)}`}
      {...galleryNavigation(pseudo, arabic, load, content)}
      roleVocabulary={roleVocabulary}
      heldRoles={{roles: []}}
      defaultCollapsed={scenarioId === 'app-shell.rail-collapsed'}
      activeWorkspaceId="operations"
      activeItemId={scenarioId === 'app-shell.pins-threads' ? 'inspections' : 'overview'}
      pinnedItemIds={['inspections']}
      headerSwitcher={{ switcherId: 'tenant', scopeLabel: 'Tenant', options: tenantOptions, activeId: 'meridian', pinnedIds: ['metro'], directory: { label: 'All tenants', invoke: () => undefined } }}
      brand={<span aria-hidden>MH</span>}
      brandText="Meridian Hotel Group"
      headerCenter={<span>Operations · Overview</span>}
      footerIdentity={{ label: 'Chris Wood', role: 'Inspector' }}
      systemItems={[{ id: 'pilot', label: 'Pilot' }, { id: 'settings', label: 'Settings' }, { id: 'help', label: 'Help' }]}
      notificationCount={3}
      body={content ? <article style={{ padding: '16px 24px' }}><h1 style={{ margin: 0, fontSize: 22 }}>Content resilience</h1></article> : <article style={{ padding: '16px 24px' }}>
        <h1 style={{ margin: 0, fontSize: 22 }}>{arabic ? 'نظرة عامة على العمليات' : pseudo ? '⟦ Øþëřåţîøñš øṽëřṽîëŵ ······ ⟧' : 'Operations overview — Meridian'}</h1>
        <p style={{ marginBlock: '4px 16px', color: 'var(--hl-muted-foreground, #666)', fontSize: 13.5 }}>Operations · Live queue health across this workspace</p>
        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          {[['Open inspections', '218', '32 unassigned', 'inherit'], ['SLA breaches', '5', 'all fire safety', 'var(--hl-danger, #b42318)'], ['Work orders open', '42', '9 awaiting parts', 'inherit'], ['Crew utilization', '81%', '+4 pts week over week', 'var(--hl-success, #0a7c3a)']].map(([label, value, note, tone]) => (
            <div key={label} style={{ flex: '1 1 130px', minInlineSize: 130, border: '1px solid var(--hl-border, #e2e2e2)', borderRadius: 10, padding: '12px 14px', background: 'var(--hl-card, #fff)' }}>
              <div style={{ fontSize: 12, color: 'var(--hl-muted-foreground, #666)' }}>{label}</div>
              <div style={{ fontSize: 24, fontWeight: 650, color: tone }}>{value}</div>
              <div style={{ fontSize: 11.5, color: 'var(--hl-muted-foreground, #888)' }}>{note}</div>
            </div>
          ))}
        </div>
      </article>}
      endPanel={<section style={{ padding: 16, fontSize: 13, color: 'var(--hl-muted-foreground, #666)' }}><p style={{ marginBlockStart: 0 }}>Pilot sees what you see — Operations · Overview. Anything it proposes lands in your inbox as a confirm-or-deny decision.</p></section>}
      endPanelOpen={false}
      endPanelLabel="Pilot"
      defaultOpenPanelIds={scenarioId === 'app-shell.actions-endpanel' ? panels.map(panel => panel.id) : []}
      panelContent={panel => <div style={{ padding: 12 }}><strong>{panel.labelKey}</strong><p style={{ marginBlockEnd: 0 }}>One flexible body; this entire region owns scrolling.</p></div>}
      spread={false}
      headerFixed
      railCapable
      data-case="operations-shell"
    />
  </div>
}

function AppShellScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'app-shell.locale-pseudo'
  const arabic = scenarioId === 'app-shell.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'app-shell.theme-dark' ? 'dark' : 'light'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-gallery-full-bleed={scenarioId === 'app-shell.actions-endpanel' ? '' : undefined} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack"><HarborlineLocaleProvider locale={locale}><ShellFixture scenarioId={scenarioId} /></HarborlineLocaleProvider></div>
  </section>
}

const meta = { title: 'Platform/App Shell', component: AppShellScenario, tags: ['autodocs'], parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof AppShellScenario>
export default meta
type Story = StoryObj<typeof meta>
export const OperationsShellStructure: Story = { name: 'Operations shell structure', args: { scenarioId: 'app-shell.structure' } }
export const RailCollapsed: Story = { name: 'Rail collapsed', args: { scenarioId: 'app-shell.rail-collapsed' } }
export const WorkspaceAndTenantSwitchers: Story = { name: 'Workspace and tenant switchers', args: { scenarioId: 'app-shell.switchers' } }
export const PinnedNavigationAndPilotThreads: Story = { name: 'Pinned navigation and Pilot threads', args: { scenarioId: 'app-shell.pins-threads' } }
export const OrderedMultiPanelDock: Story = { name: 'Ordered multi-panel dock', args: { scenarioId: 'app-shell.actions-endpanel' } }
export const BoundedOperationsShellUnderLoad: Story = { name: 'Bounded Operations shell under load', args: { scenarioId: 'app-shell.performance' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'app-shell.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'app-shell.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'app-shell.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'app-shell.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'app-shell.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'app-shell.content' } }
