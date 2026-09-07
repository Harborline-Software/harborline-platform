import { useState } from 'react'
import type { ComponentProps } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { SearchInput, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'search-input.defaults'
  | 'search-input.debounce'
  | 'search-input.sync-clear'
  | 'search-input.locale-en'
  | 'search-input.locale-pseudo'
  | 'search-input.locale-ar'
  | 'search-input.theme-light'
  | 'search-input.theme-dark'
  | 'search-input.content'

const copy: Record<ScenarioId, [string, string]> = {
  'search-input.defaults': ['Defaults', 'A controlled searchbox starts empty with localized zero-configuration chrome.'],
  'search-input.debounce': ['Debounce and busy state', 'Draft edits are immediate while only the latest value publishes after the delay.'],
  'search-input.sync-clear': ['External sync and clear', 'External values cancel pending publication and clear publishes an empty value immediately.'],
  'search-input.locale-en': ['English locale', 'Placeholder and clear labels resolve from the locale catalog.'],
  'search-input.locale-pseudo': ['Pseudo locale', 'Expanded search chrome exposes clipping and fixed-width assumptions.'],
  'search-input.locale-ar': ['Arabic locale', 'Logical icon and clear edges follow RTL without changing the value.'],
  'search-input.theme-light': ['Light theme', 'Default, busy, hover, focus, and clear states use public light-theme tokens.'],
  'search-input.theme-dark': ['Dark theme', 'The same search behavior on the Harborline dark surface.'],
  'search-input.content': ['Content resilience', 'Real content spans an overlong label, a grouped large number, escaped markup characters, and a diacritic-rich name.'],
}

function LiveSearchInput({ initial, ...rest }: Omit<ComponentProps<typeof SearchInput>, 'value' | 'onChange'> & { initial: string }) { const [value, setValue] = useState(initial); return <SearchInput {...rest} value={value} onChange={setValue} /> }

function SearchFixture({ initialValue, placeholder, debounceMs = 200 }: { initialValue: string; placeholder?: string; debounceMs?: number }) {
  const [value, setValue] = useState(initialValue)
  const [publications, setPublications] = useState<string[]>([])
  const publish = (next: string) => {
    setValue(next)
    setPublications(current => [...current, next])
  }
  return <div className="hl-gallery-feedback-stack">
    <SearchInput value={value} onChange={publish} placeholder={placeholder} debounceMs={debounceMs} />
    <output className="hl-gallery-activation" aria-live="polite">Published: {publications.length ? publications.join(' → ') || '(empty)' : 'none'}</output>
  </div>
}

function SearchInputScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'search-input.locale-pseudo'
  const arabic = scenarioId === 'search-input.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'search-input.theme-light' ? 'light' : scenarioId === 'search-input.theme-dark' ? 'dark' : undefined
  const catalog = pseudo
    ? { 'structural.search.placeholder': '⟦ Šëåřçh šţřûçţûřë îñšþëçţîøñš ······ ⟧', 'structural.search.clear': '⟦ Çļëåř šëåřçh ···· ⟧' }
    : arabic
      ? { 'structural.search.placeholder': 'ابحث في فحوصات الهيكل', 'structural.search.clear': 'مسح البحث' }
      : { 'structural.search.placeholder': 'Search inspections…', 'structural.search.clear': 'Clear search' }
  const initialValue = scenarioId === 'search-input.sync-clear' ? 'oak' : arabic ? 'بلوط' : theme ? 'structure' : ''
  const delay = scenarioId === 'search-input.debounce' ? 500 : 200

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage">
      <HarborlineLocaleProvider locale={locale} catalog={catalog}>
        {scenarioId === 'search-input.content' ? <div className="hl-gallery-feedback-grid"><LiveSearchInput accessibleLabel="Long content" initial="Awaiting third-party structural certification review" /><LiveSearchInput accessibleLabel="Large number" initial="1,284,905" /><LiveSearchInput accessibleLabel="Escaped content" initial={'Bay 4 <grid C-7> & 8'} /><LiveSearchInput accessibleLabel="Unusual name" initial="Ordnance Survey — Niño Ångström" /></div> : <SearchFixture initialValue={initialValue} debounceMs={delay} />}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Search Input', component: SearchInputScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SearchInputScenario>
export default meta
type Story = StoryObj<typeof meta>

export const Defaults: Story = { args: { scenarioId: 'search-input.defaults' } }
export const DebounceAndBusyState: Story = { name: 'Debounce and busy state', args: { scenarioId: 'search-input.debounce' } }
export const ExternalSyncAndClear: Story = { name: 'External sync and clear', args: { scenarioId: 'search-input.sync-clear' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'search-input.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'search-input.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'search-input.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'search-input.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'search-input.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'search-input.content' } }
