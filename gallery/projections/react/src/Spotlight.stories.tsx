import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Spotlight, type SpotlightSection } from '@harborline-software/ui-react'

type ScenarioId = 'spotlight.results' | 'spotlight.interaction' | 'spotlight.locale-theme' | 'spotlight.content' | 'spotlight.empty-state'

function SpotlightScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const localized = scenarioId === 'spotlight.locale-theme'
  const content = scenarioId === 'spotlight.content'
  const empty = scenarioId === 'spotlight.empty-state'
  const [open, setOpen] = useState(true)
  const [query, setQuery] = useState('')
  const sections: SpotlightSection[] = empty ? [] : content ? [{ id: 'content', label: 'Content resilience', items: [{ id: 'long', label: 'Awaiting third-party structural certification review', onSelect: () => {} }, { id: 'number', label: '1,284,905', onSelect: () => {} }, { id: 'hostile', label: 'Bay 4 <grid C-7> & 8', onSelect: () => {} }, { id: 'name', label: 'Ordnance Survey — Niño Ångström', onSelect: () => {} }] }] : [{ id: 'structures', label: localized ? 'الهياكل' : 'Structures', items: [{ id: 'north-pier', label: localized ? 'الرصيف الشمالي' : 'North Pier', description: localized ? 'آخر فحص اليوم' : 'Inspected today', badge: localized ? 'نشط' : 'Active', shortcut: '↵', onSelect: () => {}, keepOpen: scenarioId === 'spotlight.interaction' }, { id: 'pump-house', label: localized ? 'بيت المضخة' : 'Pump House', description: localized ? 'مراجعة مطلوبة' : 'Review required', onSelect: () => {} }] }]
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={localized ? 'dark' : undefined} dir={localized ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{localized ? 'البحث السريع' : empty ? 'Empty state' : content ? 'Content resilience' : scenarioId === 'spotlight.interaction' ? 'Keyboard and selection' : 'Ranked results'}</h2><p>Grouped results, keyboard selection, announcements, and dismissal.</p></header>
    <button type="button" onClick={() => setOpen(true)}>{localized ? 'بحث' : 'Open search'}</button>
    {empty ? <Spotlight open={open} onOpenChange={setOpen} query={query} onQueryChange={setQuery} sections={sections} ariaLabel="Search structures" placeholder="Type to search" empty="No structures found." resultsCountLabel={count => `${count} results`} /> : content ? <Spotlight open={open} onOpenChange={setOpen} query={query} onQueryChange={setQuery} sections={sections} ariaLabel="Search resilient content" placeholder="Type to search" empty="No results" resultsCountLabel={count => `${count} results`} /> : <Spotlight open={open} onOpenChange={setOpen} query={query} onQueryChange={setQuery} sections={sections} pinnedAction={{ id: 'new', label: localized ? 'هيكل جديد' : 'Create structure', onSelect: () => {}, keepOpen: true }} ariaLabel={localized ? 'البحث في الهياكل' : 'Search structures'} placeholder={localized ? 'اكتب للبحث' : 'Type to search'} empty={localized ? 'لا نتائج' : 'No results'} resultsCountLabel={count => localized ? `${count} نتيجة` : `${count} results`} footer={<span>{localized ? 'استخدم الأسهم للتنقل' : 'Use arrow keys to navigate'}</span>} />}
  </section>
}

const meta = { title: 'Platform/Spotlight', component: SpotlightScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SpotlightScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Results: Story = { name: 'Ranked results', args: { scenarioId: 'spotlight.results' } }
export const Interaction: Story = { name: 'Keyboard and selection', args: { scenarioId: 'spotlight.interaction' } }
export const LocaleTheme: Story = { name: 'Locale and theme', args: { scenarioId: 'spotlight.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'spotlight.content' } }
;export const EmptyState: Story = { name: 'Empty state', args: { scenarioId: 'spotlight.empty-state' } }
