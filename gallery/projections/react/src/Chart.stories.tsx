import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  Chart,
  HarborlineLocaleProvider,
  type ChartDefinition,
  type LineChartDefinition,
} from '@harborline-software/ui-react'

type ScenarioId =
  | 'chart.line-series'
  | 'chart.donut-missing'
  | 'chart.empty'
  | 'chart.validation'
  | 'chart.replacement'
  | 'chart.accessible-data'
  | 'chart.large-data'
  | 'chart.locale-en'
  | 'chart.locale-pseudo'
  | 'chart.locale-ar'
  | 'chart.theme-light'
  | 'chart.theme-dark'

const copy: Record<ScenarioId, [string, string]> = {
  'chart.line-series': ['Line series and missing values', 'Ordered categories and series retain missing values without substituting zero.'],
  'chart.donut-missing': ['Donut and missing slices', 'Missing slices stay in the accessible data while the visual omits them.'],
  'chart.empty': ['Empty series and slices', 'A definition with nothing to plot says so instead of drawing axes over no data.'],
  'chart.validation': ['Validation and provider boundary', 'Stable errors protect the neutral definition from renderer-specific options.'],
  'chart.replacement': ['Replacement and visibility intent', 'Complete updates remove stale data while legend and tooltip intent remain explicit.'],
  'chart.accessible-data': ['Accessible data path', 'The named figure includes a keyboard-reachable ordered data table.'],
  'chart.large-data': ['Bounded large data', 'A dense definition preserves all semantic data while point decoration remains bounded.'],
  'chart.locale-en': ['English locale and number formatting', 'Labels stay caller-owned and values use the active locale scope.'],
  'chart.locale-pseudo': ['Pseudo-expanded labels', 'Expanded copy probes title, legend, summary, and table reflow.'],
  'chart.locale-ar': ['Arabic RTL chart', 'Direction changes logically without reversing caller data order.'],
  'chart.theme-light': ['Light theme', 'Semantic chart tokens preserve visible series and non-color distinctions.'],
  'chart.theme-dark': ['Dark theme and reduced motion', 'The same definition remains readable with dark tokens and motion disabled.'],
}

const occupancy: LineChartDefinition = {
  kind: 'line',
  accessibleName: 'Monthly occupancy trend',
  title: 'Monthly occupancy',
  categories: ['January', 'February', 'March', 'April', 'May', 'June'],
  series: [
    { name: 'North Pier', values: [91, 92, null, 94, 95, 96] },
    { name: 'South Pier', values: [84, 86, 88, 87, 90, 92] },
  ],
}

const emptyOccupancy: LineChartDefinition = {
  kind: 'line',
  accessibleName: 'Monthly occupancy trend',
  title: 'Monthly occupancy',
  categories: [],
  series: [],
}

const replacement: LineChartDefinition = {
  kind: 'line',
  accessibleName: 'Quarterly inspection completion',
  title: 'Inspection completion',
  legend: 'hidden',
  tooltip: 'disabled',
  categories: ['Q3'],
  series: [{ name: 'Completed', values: [97] }],
}

const donut: ChartDefinition = {
  kind: 'donut',
  accessibleName: 'Inspection findings by status',
  title: 'Findings by status',
  slices: [
    { label: 'Verified', value: 34 },
    { label: 'Needs review', value: 11 },
    { label: 'Awaiting measurement', value: null },
    { label: 'Blocked', value: 4 },
  ],
}

function ValidationBoundary() {
  const errors = [
    ['category-value-count-mismatch', 'Each line series has one value per category.'],
    ['duplicate-series-name', 'Line series names are unique.'],
    ['label-required', 'Series and slice labels are nonblank.'],
    ['non-finite-value', 'Values are finite numbers or null.'],
  ]
  return <div style={{ display: 'grid', gap: 16, gridTemplateColumns: 'minmax(0, 1.4fr) minmax(15rem, 1fr)' }}>
    <Chart definition={occupancy} />
    <aside aria-label="Chart validation boundary" style={{ border: '1px solid var(--hl-border, #64748b)', borderRadius: 12, padding: 16 }}>
      <h3 style={{ marginBlockStart: 0 }}>Neutral validation</h3>
      <dl>{errors.map(([code, meaning]) => <div key={code} style={{ marginBlockEnd: 12 }}><dt><code>{code}</code></dt><dd style={{ marginInlineStart: 0 }}>{meaning}</dd></div>)}</dl>
      <p style={{ marginBlockEnd: 0 }}>Renderer options and provider identities remain private to each projection.</p>
    </aside>
  </div>
}

function ReplacementChart() {
  const [updated, setUpdated] = useState(false)
  return <div style={{ display: 'grid', gap: 12 }}>
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
      <button type="button" onClick={() => setUpdated(false)}>Original definition</button>
      <button type="button" onClick={() => setUpdated(true)}>Replace definition</button>
    </div>
    <Chart definition={updated ? replacement : occupancy} />
  </div>
}

function LargeDataChart() {
  const definition = useMemo<LineChartDefinition>(() => ({
    kind: 'line',
    accessibleName: 'Hourly structural sensor history',
    title: 'Structural sensor history · 256 samples',
    categories: Array.from({ length: 256 }, (_, index) => `Sample ${index + 1}`),
    series: [
      { name: 'Displacement', values: Array.from({ length: 256 }, (_, index) => 18 + Math.sin(index / 9) * 4) },
      { name: 'Vibration', values: Array.from({ length: 256 }, (_, index) => index % 41 === 0 ? null : 8 + Math.cos(index / 7) * 2) },
      { name: 'Temperature', values: Array.from({ length: 256 }, (_, index) => 68 + Math.sin(index / 19) * 3) },
      { name: 'Humidity', values: Array.from({ length: 256 }, (_, index) => 45 + Math.cos(index / 23) * 5) },
    ],
  }), [])
  return <Chart definition={definition} />
}

function localizedDefinition(scenarioId: ScenarioId): LineChartDefinition {
  if (scenarioId === 'chart.locale-ar') return {
    kind: 'line',
    accessibleName: 'اتجاه اكتمال الفحص',
    title: 'اكتمال الفحص الشهري',
    categories: ['يناير', 'فبراير', 'مارس'],
    series: [{ name: 'مكتمل', values: [81.5, 88.25, 94.75] }],
  }
  if (scenarioId === 'chart.locale-pseudo') return {
    kind: 'line',
    accessibleName: '⟦ Monthly structure inspection trend ········ ⟧',
    title: '⟦ Monthly structure inspection completion by operational region ············ ⟧',
    categories: ['⟦ January ··· ⟧', '⟦ February ···· ⟧', '⟦ March ··· ⟧'],
    series: [{ name: '⟦ Completed inspections ······ ⟧', values: [81.5, 88.25, 94.75] }],
  }
  return {
    kind: 'line',
    accessibleName: 'Inspection completion trend',
    title: 'Inspection completion',
    categories: ['January', 'February', 'March'],
    series: [{ name: 'Completed', values: [81.5, 88.25, 94.75] }],
  }
}

function ChartScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const arabic = scenarioId === 'chart.locale-ar'
  const pseudo = scenarioId === 'chart.locale-pseudo'
  const theme = scenarioId === 'chart.theme-light' ? 'light' : scenarioId === 'chart.theme-dark' ? 'dark' : undefined
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const catalog = pseudo ? {
    'charts.chart': '⟦ Çĥåřţ ··· ⟧',
    'charts.value': '⟦ Ṽåļûë ··· ⟧',
    'charts.series': '⟦ Šëřîëš ··· ⟧',
    'charts.item': '⟦ Îţëm ·· ⟧',
  } : arabic ? {
    'charts.chart': 'مخطط',
    'charts.value': 'القيمة',
    'charts.series': 'السلسلة',
    'charts.item': 'العنصر',
  } : undefined

  let content
  if (scenarioId === 'chart.donut-missing') content = <Chart definition={donut} dataSummaryLabel="Ordered findings data" />
  else if (scenarioId === 'chart.empty') content = <Chart definition={emptyOccupancy} empty="No occupancy has been recorded for this period yet." />
  else if (scenarioId === 'chart.validation') content = <ValidationBoundary />
  else if (scenarioId === 'chart.replacement') content = <ReplacementChart />
  else if (scenarioId === 'chart.accessible-data') content = <Chart definition={{ ...occupancy, legend: 'hidden', tooltip: 'disabled' }} dataSummaryLabel="Ordered monthly occupancy data" />
  else if (scenarioId === 'chart.large-data') content = <LargeDataChart />
  else if (scenarioId.startsWith('chart.locale-')) content = <Chart definition={localizedDefinition(scenarioId)} />
  else content = <Chart definition={{ ...occupancy, motion: scenarioId === 'chart.theme-dark' ? 'disabled' : 'host' }} />

  return <section
    className="hl-gallery-scene"
    data-gallery-probe
    data-gallery-scenario={scenarioId}
    data-theme={theme}
    dir={arabic ? 'rtl' : 'ltr'}
  >
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ inlineSize: 'min(100%, 900px)', minInlineSize: 0 }}>
      <HarborlineLocaleProvider catalog={catalog} locale={locale}>{content}</HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = {
  title: 'Platform/Chart',
  component: ChartScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof ChartScenario>

export default meta
type Story = StoryObj<typeof meta>

export const LineSeries: Story = { name: 'Line series and missing values', args: { scenarioId: 'chart.line-series' } }
export const DonutMissing: Story = { name: 'Donut and missing slices', args: { scenarioId: 'chart.donut-missing' } }
export const Empty: Story = { name: 'Empty series and slices', args: { scenarioId: 'chart.empty' } }
export const Validation: Story = { name: 'Validation and provider boundary', args: { scenarioId: 'chart.validation' } }
export const Replacement: Story = { name: 'Replacement and visibility intent', args: { scenarioId: 'chart.replacement' } }
export const AccessibleData: Story = { name: 'Accessible data path', args: { scenarioId: 'chart.accessible-data' } }
export const LargeData: Story = { name: 'Bounded large data', args: { scenarioId: 'chart.large-data' } }
export const EnglishLocale: Story = { name: 'English locale and number formatting', args: { scenarioId: 'chart.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo-expanded labels', args: { scenarioId: 'chart.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic RTL chart', args: { scenarioId: 'chart.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'chart.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme and reduced motion', args: { scenarioId: 'chart.theme-dark' } }
