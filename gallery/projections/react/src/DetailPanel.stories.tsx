import type { Meta, StoryObj } from '@storybook/react-vite'
import { DetailPanel, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId = 'detail-panel.docked' | 'detail-panel.sheet' | 'detail-panel.locale-en' | 'detail-panel.locale-pseudo' | 'detail-panel.locale-ar' | 'detail-panel.theme-light' | 'detail-panel.theme-dark' | 'detail-panel.content'

const copy: Record<ScenarioId, [string, string]> = {
  'detail-panel.docked': ['Docked detail panel', 'Elevator 2 details remain available in one labeled inline-end panel.'],
  'detail-panel.sheet': ['Sheet detail panel', 'Below the rail boundary, the same Elevator 2 content uses a modal end-side sheet.'],
  'detail-panel.locale-en': ['English locale', 'English labels and opaque detail content remain intact.'],
  'detail-panel.locale-pseudo': ['Pseudo locale', 'Expanded labels remain unclipped.'],
  'detail-panel.locale-ar': ['Arabic locale', 'The panel, close control, and padding mirror to logical inline-end.'],
  'detail-panel.theme-light': ['Light theme', 'Panel chrome uses public light tokens.'],
  'detail-panel.theme-dark': ['Dark theme', 'The same Elevator 2 panel on the Harborline dark surface.'],
  'detail-panel.content': ['Content resilience', 'Hostile-but-valid copy remains text in the panel label and required caller-owned content.'],
}

function ElevatorDetails({ pseudo, arabic }: { pseudo: boolean; arabic: boolean }) {
  const heading = (value: string) => arabic ? ({ 'FILL A FORM': 'املأ نموذجًا', PROPERTIES: 'الخصائص', 'CONDITION HISTORY': 'سجل الحالة', 'SUBMITTED FORMS': 'النماذج المقدمة' }[value] ?? value) : pseudo ? `⟦ ${value} ···· ⟧` : value
  return <article><h2>{arabic ? 'المصعد 2' : 'Elevator 2'}</h2><h3>{heading('FILL A FORM')}</h3><p><button type="button">Inspection</button> <button type="button">Maintenance</button> <button type="button">Incident</button></p><h3>{heading('PROPERTIES')}</h3><dl><dt>Manufacturer</dt><dd>Otis</dd><dt>Installed</dt><dd>2008-04-11</dd><dt>Capacity</dt><dd>1,600 kg</dd></dl><h3>{heading('CONDITION HISTORY')}</h3><p>Good · inspected 2026-08-14</p><h3>{heading('SUBMITTED FORMS')}</h3><p>Annual inspection · Complete</p></article>
}

function DetailPanelScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'detail-panel.locale-pseudo'
  const arabic = scenarioId === 'detail-panel.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'detail-panel.theme-dark' ? 'dark' : 'light'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack"><HarborlineLocaleProvider locale={locale}><div style={{ display: 'flex', justifyContent: 'flex-end', minHeight: 480, width: '100%', border: '1px solid var(--hl-color-border, #888)' }}>{scenarioId === 'detail-panel.content' ? <DetailPanel label="Awaiting third-party structural certification review" open railCapable><article><h2>{'Awaiting third-party structural certification review'}</h2><p>{'1,284,905'}</p><p>{'Bay 4 <grid C-7> & 8'}</p><p>{'Ordnance Survey — Niño Ångström'}</p></article></DetailPanel> : <DetailPanel label={arabic ? 'المصعد 2' : 'Elevator 2'} open railCapable={scenarioId !== 'detail-panel.sheet'}><ElevatorDetails pseudo={pseudo} arabic={arabic} /></DetailPanel>}</div></HarborlineLocaleProvider></div>
  </section>
}

const meta = { title: 'Platform/Detail Panel', component: DetailPanelScenario, tags: ['autodocs'], parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof DetailPanelScenario>
export default meta
type Story = StoryObj<typeof meta>
export const DockedDetailPanel: Story = { name: 'Docked detail panel', args: { scenarioId: 'detail-panel.docked' } }
export const SheetDetailPanel: Story = { name: 'Sheet detail panel', args: { scenarioId: 'detail-panel.sheet' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'detail-panel.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'detail-panel.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'detail-panel.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'detail-panel.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'detail-panel.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'detail-panel.content' } }
