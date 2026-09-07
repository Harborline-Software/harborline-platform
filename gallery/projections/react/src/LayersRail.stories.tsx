import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { LayersRail, useLayers } from '@harborline-software/ui-react'

type ScenarioId =
  | 'layers-rail.structure'
  | 'layers-rail.lenses'
  | 'layers-rail.outline'
  | 'layers-rail.provenance'
  | 'layers-rail.locale-en'
  | 'layers-rail.locale-pseudo'
  | 'layers-rail.locale-ar'
  | 'layers-rail.theme-light'
  | 'layers-rail.theme-dark'
  | 'layers-rail.content'

const copy: Record<ScenarioId, [string, string]> = {
  'layers-rail.structure': ['Rail structure', 'A named complementary rail orders the lens group above one outline tree.'],
  'layers-rail.lenses': ['Lens states and shortcuts', 'Active and passive lenses preserve visible state while Escape returns to neutral.'],
  'layers-rail.outline': ['Outline keyboard and focus', 'Roving tree focus, collapse fallback, and selection follow the APG keyboard model.'],
  'layers-rail.provenance': ['Provenance, insert, and empty', 'Source, lock, insert, and empty teaching signals remain explicit and host-owned.'],
  'layers-rail.locale-en': ['English locale', 'All rail chrome is supplied through the frozen labels interface.'],
  'layers-rail.locale-pseudo': ['Pseudo locale', 'Expanded rail labels and compact mode expose clipping and reflow defects.'],
  'layers-rail.locale-ar': ['Arabic locale', 'Logical indentation and alignment follow RTL while tree keys retain their meaning.'],
  'layers-rail.theme-light': ['Light theme', 'Lens, selection, provenance, focus, and outline states use public light-theme tokens.'],
  'layers-rail.theme-dark': ['Dark theme', 'The same structural rail on the Harborline dark surface.'],
  'layers-rail.content': ['Content resilience', 'Rail nodes and lens projections carry a long label, grouped large number, escaped hostile text, and an unusual name with diacritics.'],
}

const baseNodes = [
  { id: 'structure', kind: 'section', label: 'Structure', depth: 0, parentId: null, hasChildren: true },
  { id: 'location', kind: 'field', label: 'Placement', detail: 'Blueprint coordinate', depth: 1, parentId: 'structure' },
  { id: 'pose', kind: 'field', label: 'Photo pose', detail: 'Position and orientation', depth: 1, parentId: 'structure' },
]

const contentNodes = [
  { id: 'structure', kind: 'section', label: 'Awaiting third-party structural certification review', depth: 0, parentId: null, hasChildren: true },
  { id: 'location', kind: 'field', label: 'Bay 4 <grid C-7> & 8', detail: 'Blueprint coordinate', depth: 1, parentId: 'structure' },
  { id: 'pose', kind: 'field', label: 'Ordnance Survey — Niño Ångström', detail: 'Position and orientation', depth: 1, parentId: 'structure' },
]

const lenses = [
  { id: 'layout', label: 'Layout', tone: 'muted' as const, kind: 'colorize' as const, project: () => ({ active: true, badge: 'base' }) },
  { id: 'rules', label: 'Rules', tone: 'warning' as const, kind: 'filter' as const, empty: 'Add a rule to a selected field.', project: (id: string) => ({ active: id === 'pose', badge: id === 'pose' ? '1 rule' : undefined }) },
  { id: 'access', label: 'Access', tone: 'danger' as const, kind: 'colorize' as const, project: (id: string) => ({ active: true, badge: id === 'structure' ? 'tenant' : undefined }) },
]

const contentLenses = [
  { id: 'layout', label: 'Layout', tone: 'muted' as const, kind: 'colorize' as const, project: (id: string) => ({ active: true, badge: id === 'location' ? '1,284,905' : undefined }) },
  { id: 'rules', label: 'Rules', tone: 'warning' as const, kind: 'filter' as const, empty: 'Add a rule to a selected field.', project: (id: string) => ({ active: id === 'pose', badge: id === 'pose' ? '1 rule' : undefined }) },
  { id: 'access', label: 'Access', tone: 'danger' as const, kind: 'colorize' as const, project: (id: string) => ({ active: true, badge: id === 'structure' ? 'tenant' : undefined }) },
]

function labelsFor(locale: 'en' | 'pseudo' | 'ar') {
  if (locale === 'ar') return {
    lensesHeading: 'العدسات', outlineHeading: 'المخطط', insert: 'إدراج', railRegion: 'الطبقات',
    toggleLens: (lens: string) => `تبديل عدسة ${lens}`, activateLens: (lens: string) => `عرض عدسة ${lens}`,
    passiveCount: (count: number) => `+${count}`, source: (tier: string) => tier, locked: 'مقفل', unresolvedSource: 'المصدر غير معروف',
    empty: 'أضف الحقل الأول.', collapseNode: 'طي', expandNode: 'توسيع', viewingLens: (lens: string) => `العرض: ${lens}`,
    exitLens: 'الخروج من العدسة', lensShortcutHint: (number: number) => `اضغط ${number}`,
  }
  if (locale === 'pseudo') return {
    lensesHeading: '⟦ Ļëñšëš ··· ⟧', outlineHeading: '⟦ Øûţļîñë ··· ⟧', insert: '⟦ Îñšëřţ ··· ⟧', railRegion: '⟦ Šţřûçţûřë ļåÿëřš ······ ⟧',
    toggleLens: (lens: string) => `⟦ Ţøĝĝļë ${lens} ļëñš ···· ⟧`, activateLens: (lens: string) => `⟦ Šhøŵ ${lens} øñ çåñṽåš ······ ⟧`,
    passiveCount: (count: number) => `+${count}`, source: (tier: string) => `⟦ ${tier} šøûřçë ··· ⟧`, locked: '⟦ Ļøçķëđ ··· ⟧', unresolvedSource: '⟦ šøûřçë ûñķñøŵñ ···· ⟧',
    empty: '⟦ Åđđ ÿøûř ƒîřšţ šţřûçţûřë ƒîëļđ ······ ⟧', collapseNode: '⟦ Çøļļåþšë ··· ⟧', expandNode: '⟦ Ëxþåñđ ··· ⟧',
    viewingLens: (lens: string) => `⟦ Ṽîëŵîñĝ: ${lens} ··· ⟧`, exitLens: '⟦ Ëxîţ ļëñš ··· ⟧', lensShortcutHint: (number: number) => `⟦ þřëšš ${number} ··· ⟧`,
  }
  return undefined
}

function RailFixture({ scenarioId, compact = false }: { scenarioId: ScenarioId; compact?: boolean }) {
  const [selectedId, setSelectedId] = useState<string | null>('location')
  const fixtureLenses = scenarioId === 'layers-rail.content' ? contentLenses : lenses
  const fixtureNodes = scenarioId === 'layers-rail.content' ? contentNodes : baseNodes
  const layers = useLayers(fixtureLenses, scenarioId === 'layers-rail.provenance' ? null : 'layout')
  const pseudo = scenarioId === 'layers-rail.locale-pseudo'
  const arabic = scenarioId === 'layers-rail.locale-ar'
  const model = useMemo(() => ({ nodes: fixtureNodes, selectedId, select: setSelectedId }), [fixtureNodes, selectedId])
  const provenance = useMemo(() => ({ resolve: (id: string) => id === 'pose'
    ? { source: 'unknown' as const, chain: [], overridden: false, locked: true, resolved: false }
    : { source: 'tenant' as const, chain: ['base' as const, 'tenant' as const], overridden: true, locked: false, resolved: true } }), [])

  const baseLabels = labelsFor(arabic ? 'ar' : pseudo ? 'pseudo' : 'en')
  const labels = compact && baseLabels ? { ...baseLabels, railRegion: `${baseLabels.railRegion} — compact` } : baseLabels
  return <LayersRail
    model={model}
    lenses={fixtureLenses}
    layers={layers}
    provenance={provenance}
    labels={labels}
    compact={compact}
    showProvenance
    onInsert={() => {}}
  />
}

function LayersRailScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const arabic = scenarioId === 'layers-rail.locale-ar'
  const theme = scenarioId === 'layers-rail.theme-light' ? 'light' : scenarioId === 'layers-rail.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ minHeight: 420, display: 'flex', gap: 16 }}>
      <RailFixture scenarioId={scenarioId} />
      {scenarioId === 'layers-rail.locale-pseudo' && <RailFixture scenarioId={scenarioId} compact />}
    </div>
  </section>
}

const meta = { title: 'Platform/Layers Rail', component: LayersRailScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof LayersRailScenario>
export default meta
type Story = StoryObj<typeof meta>

export const RailStructure: Story = { name: 'Rail structure', args: { scenarioId: 'layers-rail.structure' } }
export const LensStatesAndShortcuts: Story = { name: 'Lens states and shortcuts', args: { scenarioId: 'layers-rail.lenses' } }
export const OutlineKeyboardAndFocus: Story = { name: 'Outline keyboard and focus', args: { scenarioId: 'layers-rail.outline' } }
export const ProvenanceInsertAndEmpty: Story = { name: 'Provenance, insert, and empty', args: { scenarioId: 'layers-rail.provenance' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'layers-rail.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'layers-rail.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'layers-rail.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'layers-rail.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'layers-rail.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'layers-rail.content' } }
