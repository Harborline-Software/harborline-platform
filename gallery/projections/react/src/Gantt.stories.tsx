import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  Gantt,
  HarborlineLocaleProvider,
  type GanttColumn,
  type GanttDependency,
  type GanttTask,
  type GanttZoom,
} from '@harborline-software/ui-react'

type ScenarioId =
  | 'gantt.overview'
  | 'gantt.empty-columns'
  | 'gantt.zoom-navigation'
  | 'gantt.replacement-validation'
  | 'gantt.large-data'
  | 'gantt.locale-fr'
  | 'gantt.locale-pseudo'
  | 'gantt.locale-ar'
  | 'gantt.theme-light'
  | 'gantt.theme-dark'

const copy: Record<ScenarioId, [string, string]> = {
  'gantt.overview': ['Read-only schedule', 'Ordered work, progress, and finish-to-start dependencies remain available without editing controls.'],
  'gantt.empty-columns': ['Empty and column states', 'Localized empty content and caller-selected columns replace the defaults without stale headers.'],
  'gantt.zoom-navigation': ['Controlled zoom and keyboard navigation', 'The picker requests a scale while the supplied scale and keyboard-reachable task grid remain authoritative.'],
  'gantt.replacement-validation': ['Replacement and validation boundaries', 'Only the latest valid task graph is visible; malformed inputs are rejected by stable contract codes.'],
  'gantt.large-data': ['Large schedule', 'The deterministic Tier-C fixture exposes 256 ordered tasks and 320 valid dependencies.'],
  'gantt.locale-fr': ['French calendar dates', 'Calendar dates are formatted in French without timezone drift.'],
  'gantt.locale-pseudo': ['Pseudo-localized reflow', 'Expanded headers and labels remain reachable in a narrow, magnified surface.'],
  'gantt.locale-ar': ['Arabic right-to-left schedule', 'Logical layout follows right-to-left direction while source task order remains unchanged.'],
  'gantt.theme-light': ['Light theme', 'Semantic timeline, progress, focus, and dependency tokens on the Harborline light surface.'],
  'gantt.theme-dark': ['Dark theme', 'The same provider-neutral schedule on the Harborline dark surface.'],
}

const baseTasks: readonly GanttTask[] = [
  { id: 'survey', title: 'Survey structure', start: '2026-08-11', end: '2026-08-12', progress: 100, color: 'var(--hl-color-success)' },
  { id: 'capture', title: 'Capture photo with pose', start: '2026-08-13', end: '2026-08-15', progress: 45, color: 'var(--hl-color-info)' },
  { id: 'place', title: 'Place capture in blueprint', start: '2026-08-16', end: '2026-08-19', progress: 10, color: 'var(--hl-color-warning)' },
]

const baseDependencies: readonly GanttDependency[] = [
  { fromId: 'survey', toId: 'capture' },
  { fromId: 'capture', toId: 'place' },
]

const pseudoCatalog = {
  'gantt.title': '⟦ Šçĥëđûļë ţîmëļîñë ······ ⟧',
  'gantt.name': '⟦ Ţîmëļîñë ···· ⟧',
  'gantt.start': '⟦ Šţåřţ đåţë ···· ⟧',
  'gantt.end': '⟦ Ëñđ đåţë ···· ⟧',
  'gantt.progress': '⟦ Þřøĝřëšš ···· ⟧',
  'gantt.zoom': '⟦ Ţîmë šçåļë ···· ⟧',
  'gantt.day': '⟦ Đåÿ ·· ⟧',
  'gantt.week': '⟦ Ŵëëķ ··· ⟧',
  'gantt.month': '⟦ Møñţĥ ··· ⟧',
  'dataGrid.noResults': '⟦ Ñø šçĥëđûļëđ ŵøřķ ········ ⟧',
}

const frenchCatalog = {
  'gantt.title': 'Calendrier des travaux',
  'gantt.name': 'Chronologie',
  'gantt.start': 'Début',
  'gantt.end': 'Fin',
  'gantt.progress': 'Avancement',
  'gantt.zoom': 'Échelle',
  'gantt.day': 'Jour',
  'gantt.week': 'Semaine',
  'gantt.month': 'Mois',
  'dataGrid.noResults': 'Aucun travail planifié.',
}

const arabicCatalog = {
  'gantt.title': 'الجدول الزمني للعمل',
  'gantt.name': 'الخط الزمني',
  'gantt.start': 'البداية',
  'gantt.end': 'النهاية',
  'gantt.progress': 'التقدم',
  'gantt.zoom': 'المقياس الزمني',
  'gantt.day': 'يوم',
  'gantt.week': 'أسبوع',
  'gantt.month': 'شهر',
  'dataGrid.noResults': 'لا يوجد عمل مجدول.',
}

// Eight parallel workstreams of thirty-two tasks each. Every task occupies one three-day slot and
// hands off to the next position, so each dependency is genuinely finish-to-start and its connector
// points the way the schedule runs. The fixture this replaced started all 256 tasks on the same day
// and wrapped task 255 back to task 0, so 320 connectors were drawn between coincident bar edges and
// one of them ran the full height of the chart -- the diagonal that made this scenario unreadable.
// Counts stay at 256 and 320 because the scenario asserts both.
const LARGE_STREAMS = 8
const LARGE_STREAM_LENGTH = 32
const LARGE_DEPENDENCY_COUNT = 320
const LARGE_ORIGIN = Date.UTC(2026, 7, 11)
const LARGE_SLOT_DAYS = 3

// GanttTask dates are a template-literal type, so the ISO slice is narrowed rather than widened.
function largeDay(offset: number): GanttTask['start'] {
  return new Date(LARGE_ORIGIN + offset * 86_400_000).toISOString().slice(0, 10) as GanttTask['start']
}

function largeTaskSet(): readonly GanttTask[] {
  return Array.from({ length: LARGE_STREAMS * LARGE_STREAM_LENGTH }, (_, index) => {
    const slot = index % LARGE_STREAM_LENGTH * LARGE_SLOT_DAYS
    return {
      id: `large-task-${index}`,
      title: `Structure task ${index + 1}`,
      start: largeDay(slot),
      end: largeDay(slot + LARGE_SLOT_DAYS - 1),
      progress: index % 101,
    } as const
  })
}

function largeDependencySet(): readonly GanttDependency[] {
  const identifier = (stream: number, position: number) => `large-task-${stream * LARGE_STREAM_LENGTH + position}`
  const edges: GanttDependency[] = []
  for (let stream = 0; stream < LARGE_STREAMS; stream += 1) {
    for (let position = 0; position + 1 < LARGE_STREAM_LENGTH; position += 1) {
      edges.push({ fromId: identifier(stream, position), toId: identifier(stream, position + 1) })
    }
  }
  // Skip-ahead edges make up the balance: a task can gate more than its immediate successor. Landing
  // two positions later keeps every edge pointing at a strictly later slot, so the set stays acyclic
  // and duplicate-free -- and no connector spans more than two rows, which is what keeps the chart
  // free of the long diagonals that crossed it before.
  for (let position = 0; edges.length < LARGE_DEPENDENCY_COUNT; position += 1) {
    for (let stream = 0; stream < LARGE_STREAMS && edges.length < LARGE_DEPENDENCY_COUNT; stream += 1) {
      edges.push({ fromId: identifier(stream, position), toId: identifier(stream, position + 2) })
    }
  }
  return edges
}

function ScenarioGantt({ scenarioId }: { scenarioId: ScenarioId }) {
  const [requestedZoom, setRequestedZoom] = useState<GanttZoom | null>(null)
  const largeTasks = useMemo(() => scenarioId === 'gantt.large-data' ? largeTaskSet() : undefined, [scenarioId])
  const largeDependencies = useMemo(() => scenarioId === 'gantt.large-data' ? largeDependencySet() : undefined, [scenarioId])
  const isEmpty = scenarioId === 'gantt.empty-columns'
  const isReplacement = scenarioId === 'gantt.replacement-validation'
  const isZoom = scenarioId === 'gantt.zoom-navigation'
  const isPseudo = scenarioId === 'gantt.locale-pseudo'
  const isArabic = scenarioId === 'gantt.locale-ar'
  const tasks = largeTasks ?? (isEmpty ? [] : isReplacement
    ? [{ id: 'revision-96', title: 'Latest replacement only', start: '2026-08-19', end: '2026-08-20', progress: 96 } satisfies GanttTask]
    : isArabic
      ? [
          { id: 'survey', title: 'مسح الهيكل', start: '2026-08-11', end: '2026-08-12', progress: 100 },
          { id: 'capture', title: 'التقاط صورة مع الوضعية', start: '2026-08-13', end: '2026-08-15', progress: 45 },
          { id: 'place', title: 'وضع الالتقاط في المخطط', start: '2026-08-16', end: '2026-08-19', progress: 10 },
        ] satisfies readonly GanttTask[]
      : baseTasks)
  const dependencies = largeDependencies ?? (isEmpty || isReplacement ? [] : baseDependencies)
  const columns: readonly GanttColumn[] | undefined = isEmpty
    ? [{ field: 'title', title: 'Work package', width: 240 }]
    : isPseudo
      ? [
          { field: 'title', title: '⟦ Šţřûçţûřë ŵøřķ þåçķåĝë ········ ⟧', width: 260 },
          { field: 'start' },
          { field: 'end' },
          { field: 'progress' },
        ]
      : undefined

  return <div style={{ inlineSize: '100%', minInlineSize: 0, maxInlineSize: isPseudo ? 640 : '100%', zoom: isPseudo ? 2 : undefined }}>
    <Gantt
      accessibleName={isArabic ? 'الجدول الزمني للعمل' : isPseudo ? pseudoCatalog['gantt.title'] : 'Structure work schedule'}
      className="engineering-schedule"
      columns={columns}
      data-owner="engineering-services"
      dependencies={dependencies}
      empty={isEmpty ? 'No scheduled structure work.' : undefined}
      onZoomChange={isZoom ? setRequestedZoom : undefined}
      rowHeight={isPseudo ? 44 : 36}
      showZoomPicker={isZoom || isPseudo}
      tasks={tasks}
      zoom={isZoom ? 'day' : scenarioId === 'gantt.locale-fr' ? 'week' : 'month'}
    />
    {isZoom ? <output aria-live="polite" style={{ display: 'block', marginBlockStart: 12 }}>
      {requestedZoom ? `Requested ${requestedZoom}; supplied zoom remains day.` : 'Choose a scale to request a controlled zoom change.'}
    </output> : null}
    {isReplacement ? <p role="status">Revision 96 is current. Earlier rows and connectors have been removed.</p> : null}
    {scenarioId === 'gantt.large-data' ? <p role="status">256 tasks · 320 dependencies</p> : null}
  </div>
}

function GanttScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'gantt.locale-pseudo'
  const arabic = scenarioId === 'gantt.locale-ar'
  const locale = arabic ? 'ar-SA' : scenarioId === 'gantt.locale-fr' ? 'fr-FR' : pseudo ? 'en-XA' : 'en-US'
  const catalog = arabic ? arabicCatalog : scenarioId === 'gantt.locale-fr' ? frenchCatalog : pseudo ? pseudoCatalog : undefined
  const theme = scenarioId === 'gantt.theme-light' ? 'light' : scenarioId === 'gantt.theme-dark' ? 'dark' : undefined

  return <section
    className="hl-gallery-scene"
    data-gallery-probe
    data-gallery-scenario={scenarioId}
    data-theme={theme}
    dir={arabic ? 'rtl' : 'ltr'}
  >
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div aria-label="Schedule gallery viewport" className="hl-gallery-stage" style={{ overflow: 'auto' }} tabIndex={0}>
      <HarborlineLocaleProvider catalog={catalog} direction={arabic ? 'rtl' : 'ltr'} locale={locale}>
        <ScenarioGantt scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = {
  title: 'Platform/Gantt',
  component: GanttScenario,
  tags: ['autodocs'],
  parameters: { layout: 'fullscreen', controls: { disable: true } },
} satisfies Meta<typeof GanttScenario>

export default meta
type Story = StoryObj<typeof meta>

export const ReadOnlySchedule: Story = { name: 'Read-only schedule', args: { scenarioId: 'gantt.overview' } }
export const EmptyAndColumnStates: Story = { name: 'Empty and column states', args: { scenarioId: 'gantt.empty-columns' } }
export const ControlledZoomAndKeyboardNavigation: Story = { name: 'Controlled zoom and keyboard navigation', args: { scenarioId: 'gantt.zoom-navigation' } }
export const ReplacementAndValidationBoundaries: Story = { name: 'Replacement and validation boundaries', args: { scenarioId: 'gantt.replacement-validation' } }
export const LargeSchedule: Story = { name: 'Large schedule', args: { scenarioId: 'gantt.large-data' } }
export const FrenchCalendarDates: Story = { name: 'French calendar dates', args: { scenarioId: 'gantt.locale-fr' } }
export const PseudoLocalizedReflow: Story = { name: 'Pseudo-localized reflow', args: { scenarioId: 'gantt.locale-pseudo' } }
export const ArabicRightToLeftSchedule: Story = { name: 'Arabic right-to-left schedule', args: { scenarioId: 'gantt.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'gantt.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'gantt.theme-dark' } }
