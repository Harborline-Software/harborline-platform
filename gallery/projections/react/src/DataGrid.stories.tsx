import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  DataGrid,
  HarborlineLocaleProvider,
  type DataGridColumnDef,
} from '@harborline-software/ui-react'

type ScenarioId =
  | 'data-grid.app-grid-dump'
  | 'data-grid.app-grouped-report'
  | 'data-grid.replacement-state'
  | 'data-grid.keyboard-focus'
  | 'data-grid.large-data'
  | 'data-grid.locale-pseudo'
  | 'data-grid.locale-ar'
  | 'data-grid.theme-light'
  | 'data-grid.theme-dark'
  | 'data-grid.empty-state'

interface AssetRow {
  readonly id: string
  readonly structure: string
  readonly area: string
  readonly inspection: string
  readonly status: 'Active' | 'Review' | 'Paused'
  readonly owner: string
  readonly updated: string
}

interface ReportRow {
  readonly id: string
  readonly property: string
  readonly category: string
  readonly status: string
  readonly amount: number
}

interface LargeRow {
  readonly id: string
  readonly values: readonly string[]
}

const copy: Record<ScenarioId, readonly [string, string]> = {
  'data-grid.app-grid-dump': ['Harborline grid dump', 'Sixty ordered assets retain stable identity, caller-owned status content, and leaf-only zebra presentation.'],
  'data-grid.app-grouped-report': ['Harborline grouped report', 'Three first-seen property groups retain every visible column and name mixed child values.'],
  'data-grid.replacement-state': ['Complete replacement', 'Rows, columns, groups, focus, and expansion follow the latest caller state without stale content.'],
  'data-grid.keyboard-focus': ['Keyboard and focus', 'One tab stop supports arrows, Home, End, corner navigation, and group disclosure.'],
  'data-grid.large-data': ['Bounded large data', 'Ten thousand rows and twenty columns preserve total-row semantics through a bounded rendered window.'],
  'data-grid.locale-pseudo': ['Pseudo locale', 'Expanded caller labels remain readable and reachable without changing source order.'],
  'data-grid.locale-ar': ['Arabic RTL', 'Direction and scrolling use logical edges while row, column, and grouping order remain caller-owned.'],
  'data-grid.theme-light': ['Light theme', 'Semantic surface, border, grouping, status, and focus tokens on the Harborline light theme.'],
  'data-grid.theme-dark': ['Dark theme', 'The same provider-neutral grid semantics on the Harborline dark theme.'],
  'data-grid.empty-state': ['Empty state', 'No assets are available for this inventory.'],
}

const statusCell: NonNullable<DataGridColumnDef<AssetRow>['renderCell']> = ({ value }) => {
  const status = String(value)
  const tone = status === 'Active' ? 'success' : status === 'Review' ? 'warning' : 'neutral'
  return <span data-tone={tone}>{status}</span>
}

const assetColumns: readonly DataGridColumnDef<AssetRow>[] = [
  { id: 'structure', field: 'structure', header: 'Structure', removalPriority: 60 },
  { id: 'area', field: 'area', header: 'Area', removalPriority: 50 },
  { id: 'inspection', field: 'inspection', header: 'Inspection', removalPriority: 40 },
  { id: 'status', field: 'status', header: 'Status', removalPriority: 30, renderCell: statusCell },
  { id: 'owner', field: 'owner', header: 'Owner', removalPriority: 20 },
  { id: 'updated', field: 'updated', header: 'Updated', removalPriority: 10 },
]

const reportColumns: readonly DataGridColumnDef<ReportRow>[] = [
  { id: 'property', field: 'property', header: 'Property', removalPriority: 40 },
  { id: 'category', field: 'category', header: 'Category', removalPriority: 30 },
  { id: 'status', field: 'status', header: 'Status', removalPriority: 20 },
  { id: 'amount', field: 'amount', header: 'Replacement cost', removalPriority: 10, valueKind: 'number' },
]

const compactColumns: readonly DataGridColumnDef<AssetRow>[] = assetColumns.slice(0, 3)

function createAssets(count: number, revision = 0): readonly AssetRow[] {
  const structures = ['North Pier', 'Pump House', 'Main Span', 'Service Gallery']
  const statuses: readonly AssetRow['status'][] = ['Active', 'Review', 'Paused']
  return Array.from({ length: count }, (_, index) => ({
    id: `asset-${revision}-${index + 1}`,
    structure: structures[index % structures.length],
    area: `Bay ${String.fromCharCode(65 + (index % 6))}`,
    inspection: `INS-${String(index + 1).padStart(4, '0')}`,
    status: statuses[index % statuses.length],
    owner: ['A. Rivera', 'M. Chen', 'S. Patel'][index % 3],
    updated: `2026-08-${String((index % 28) + 1).padStart(2, '0')}`,
  }))
}

const groupedReportRows: readonly ReportRow[] = [
  { id: 'report-1', property: 'Harbor Tower', category: 'Envelope', status: 'Active', amount: 120000 },
  { id: 'report-2', property: 'Harbor Tower', category: 'Mechanical', status: 'Review', amount: 76000 },
  { id: 'report-3', property: 'North Pier', category: 'Structure', status: 'Active', amount: 185000 },
  { id: 'report-4', property: 'North Pier', category: 'Electrical', status: 'Paused', amount: 43000 },
  { id: 'report-5', property: 'North Pier', category: 'Safety', status: 'Active', amount: 18000 },
  { id: 'report-6', property: 'Pump House', category: 'Mechanical', status: 'Review', amount: 92000 },
  { id: 'report-7', property: 'Pump House', category: 'Envelope', status: 'Active', amount: 51000 },
  { id: 'report-8', property: 'Pump House', category: 'Safety', status: 'Active', amount: 24000 },
]

function DataGridFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const [replacementRevision, setReplacementRevision] = useState(0)
  const isReplacement = scenarioId === 'data-grid.replacement-state'
  const isGrouped = scenarioId === 'data-grid.app-grouped-report'
  const isKeyboard = scenarioId === 'data-grid.keyboard-focus'
  const isLarge = scenarioId === 'data-grid.large-data'
  const isPseudo = scenarioId === 'data-grid.locale-pseudo'
  const isArabic = scenarioId === 'data-grid.locale-ar'
  const isEmpty = scenarioId === 'data-grid.empty-state'

  const largeColumns = useMemo<readonly DataGridColumnDef<LargeRow>[]>(() => Array.from({ length: 20 }, (_, index) => ({
    id: `column-${index + 1}`,
    field: row => row.values[index],
    header: `Column ${index + 1}`,
    removalPriority: 20 - index,
  })), [])
  const largeRows = useMemo<readonly LargeRow[]>(() => Array.from({ length: 10_000 }, (_, rowIndex) => ({
    id: `large-${rowIndex + 1}`,
    values: Array.from({ length: 20 }, (_, columnIndex) => `R${rowIndex + 1} C${columnIndex + 1}`),
  })), [])

  if (isLarge) {
    return <DataGrid accessibleName="Large asset inventory" columns={largeColumns} getRowId={row => row.id} rows={largeRows} zebra />
  }

  if (isGrouped) {
    return <DataGrid accessibleName="Grouped analytical report" columns={reportColumns} getRowId={row => row.id} grouping={['property']} rows={groupedReportRows} zebra />
  }

  if (isEmpty) {
    return <DataGrid accessibleName="Empty asset inventory" columns={compactColumns} empty="No assets found." getRowId={row => row.id} rows={[]} />
  }

  const localeColumns: readonly DataGridColumnDef<AssetRow>[] = isArabic
    ? [
        { ...assetColumns[0], header: 'الهيكل' },
        { ...assetColumns[1], header: 'المنطقة' },
        { ...assetColumns[3], header: 'الحالة' },
      ]
    : isPseudo
      ? [
          { ...assetColumns[0], header: '⟦ Šţřûçţûřë ···· ⟧' },
          { ...assetColumns[1], header: '⟦ Îñšþëçţîøñ åřëå ······ ⟧' },
          { ...assetColumns[3], header: '⟦ Çûřřëñţ šţåţûš ······ ⟧' },
        ]
      : compactColumns
  const columns = isReplacement
    ? replacementRevision % 2 === 0 ? compactColumns : [compactColumns[2], compactColumns[0]]
    : localeColumns
  const rows = createAssets(isKeyboard ? 3 : scenarioId === 'data-grid.app-grid-dump' ? 60 : 12, replacementRevision)

  return <>
    {isReplacement && <button type="button" onClick={() => setReplacementRevision(value => value + 1)}>Replace rows and columns</button>}
    <DataGrid
      accessibleName={isArabic ? 'مخزون الأصول' : isPseudo ? '⟦ Åššëţ îñṽëñţøřÿ ······ ⟧' : 'Asset inventory'}
      columns={columns}
      empty={isArabic ? 'لا توجد نتائج.' : undefined}
      getRowId={row => row.id}
      grouping={isKeyboard ? ['structure'] : undefined}
      rows={rows}
      zebra
    />
  </>
}

function DataGridScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const isPseudo = scenarioId === 'data-grid.locale-pseudo'
  const isArabic = scenarioId === 'data-grid.locale-ar'
  const theme = scenarioId === 'data-grid.theme-dark' ? 'dark' : scenarioId === 'data-grid.theme-light' ? 'light' : undefined
  const locale = isArabic ? 'ar-SA' : isPseudo ? 'en-XA' : 'en-US'

  return <section
    className="hl-gallery-scene"
    data-gallery-probe
    data-gallery-scenario={scenarioId}
    data-theme={theme}
    dir={isArabic ? 'rtl' : 'ltr'}
  >
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack">
      <HarborlineLocaleProvider locale={locale}>
        <DataGridFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = {
  title: 'Platform/Data Grid',
  component: DataGridScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof DataGridScenario>

export default meta
type Story = StoryObj<typeof meta>

export const AppGridDump: Story = { name: 'Harborline grid dump', args: { scenarioId: 'data-grid.app-grid-dump' } }
export const AppGroupedReport: Story = { name: 'Harborline grouped report', args: { scenarioId: 'data-grid.app-grouped-report' } }
export const CompleteReplacement: Story = { name: 'Complete replacement', args: { scenarioId: 'data-grid.replacement-state' } }
export const KeyboardAndFocus: Story = { name: 'Keyboard and focus', args: { scenarioId: 'data-grid.keyboard-focus' } }
export const BoundedLargeData: Story = { name: 'Bounded large data', args: { scenarioId: 'data-grid.large-data' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'data-grid.locale-pseudo' } }
export const ArabicRtl: Story = { name: 'Arabic RTL', args: { scenarioId: 'data-grid.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'data-grid.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'data-grid.theme-dark' } }
;export const EmptyState: Story = { name: 'Empty state', args: { scenarioId: 'data-grid.empty-state' } }
