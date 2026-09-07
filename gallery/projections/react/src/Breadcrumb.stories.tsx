import type { Meta, StoryObj } from '@storybook/react-vite'
import { Breadcrumb } from '@harborline-software/ui-react'

type ScenarioId = 'breadcrumb.defaults' | 'breadcrumb.navigation' | 'breadcrumb.rtl-theme' | 'breadcrumb.content'

function BreadcrumbScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const rtl = scenarioId === 'breadcrumb.rtl-theme'
  const content = scenarioId === 'breadcrumb.content'
  const title = content
    ? 'Content resilience'
    : scenarioId === 'breadcrumb.navigation'
    ? 'Current items and links'
    : rtl ? 'RTL and theme' : 'Defaults and relationships'
  const contentItems = [{ id: 'review', label: 'Awaiting third-party structural certification review', href: '/review' }, { id: 'count', label: '1,284,905', href: '/records' }, { id: 'grid', label: 'Bay 4 <grid C-7> & 8', href: '/grid' }, { id: 'current', label: 'Ordnance Survey — Niño Ångström' }]
  const items = rtl
    ? [{ id: 'home', label: 'الرئيسية', href: '/' }, { id: 'forms', label: 'النماذج', href: '/forms' }, { id: 'current', label: 'التفاصيل' }]
    : [{ id: 'home', label: 'Home', href: '/' }, { id: 'forms', label: 'Forms', href: '/forms' }, { id: 'current', label: 'Inspection details' }]

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={rtl ? 'dark' : undefined} dir={rtl ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>Current-page semantics, logical separators, and wrapping share one neutral contract.</p></header>
    <div className="hl-gallery-stage">{content ? <Breadcrumb accessibleLabel="Location" items={contentItems} /> : <Breadcrumb accessibleLabel="Location" items={items} />}</div>
  </section>
}

const meta = { title: 'Platform/Breadcrumb', component: BreadcrumbScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof BreadcrumbScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { name: 'Defaults and relationships', args: { scenarioId: 'breadcrumb.defaults' } }
export const Navigation: Story = { name: 'Current items and links', args: { scenarioId: 'breadcrumb.navigation' } }
export const RtlAndTheme: Story = { name: 'RTL and theme', args: { scenarioId: 'breadcrumb.rtl-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'breadcrumb.content' } }
