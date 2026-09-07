import type { Meta, StoryObj } from '@storybook/react-vite'
import { Sheet, SheetClose, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle, SheetTrigger } from '@harborline-software/ui-react'

type ScenarioId = 'sheet.lifecycle' | 'sheet.dismissal' | 'sheet.locale-theme' | 'sheet.content'

function SheetScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const localized = scenarioId === 'sheet.locale-theme'
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={localized ? 'dark' : undefined} dir={localized ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{scenarioId === 'sheet.content' ? 'Content resilience' : localized ? 'المرشحات' : scenarioId === 'sheet.dismissal' ? 'Dismissal and focus' : 'Lifecycle and sides'}</h2><p>Modal state, focus restoration, placement, and caller-owned copy.</p></header>
    <div className="hl-gallery-stage">{scenarioId === 'sheet.content' ? <Sheet defaultOpen><SheetTrigger>{'Awaiting third-party structural certification review'}</SheetTrigger><SheetContent closeLabel="Close content sheet" side="right"><SheetHeader><SheetTitle>{'Ordnance Survey — Niño Ångström'}</SheetTitle><SheetDescription>{'Bay 4 <grid C-7> & 8'}</SheetDescription></SheetHeader><p>{'1,284,905'}</p><SheetFooter><SheetClose>Close</SheetClose></SheetFooter></SheetContent></Sheet> : <Sheet defaultOpen><SheetTrigger>{localized ? 'فتح' : 'Open filters'}</SheetTrigger><SheetContent closeLabel={localized ? 'إغلاق' : 'Close filters'} side={scenarioId === 'sheet.lifecycle' ? 'right' : 'left'}><SheetHeader><SheetTitle>{localized ? 'مرشحات البحث' : 'Search filters'}</SheetTitle><SheetDescription>{localized ? 'ضيّق نتائج الهياكل.' : 'Narrow the structure results.'}</SheetDescription></SheetHeader><p>{localized ? 'حالة الفحص والموقع.' : 'Inspection state and location.'}</p><SheetFooter><SheetClose>{localized ? 'تم' : 'Apply filters'}</SheetClose></SheetFooter></SheetContent></Sheet>}</div>
  </section>
}

const meta = { title: 'Platform/Sheet', component: SheetScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof SheetScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Lifecycle: Story = { name: 'Lifecycle and sides', args: { scenarioId: 'sheet.lifecycle' } }
export const Dismissal: Story = { name: 'Dismissal and focus', args: { scenarioId: 'sheet.dismissal' } }
export const LocaleTheme: Story = { name: 'Locale and theme', args: { scenarioId: 'sheet.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'sheet.content' } }
