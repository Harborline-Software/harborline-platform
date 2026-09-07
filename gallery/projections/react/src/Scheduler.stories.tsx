import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  Scheduler,
  HarborlineLocaleProvider,
  expandRecurrence,
  parseRRule,
  type SchedulerEvent,
  type SchedulerViewType,
} from '@harborline-software/ui-react'

type ScenarioId =
  | 'scheduler.views-state'
  | 'scheduler.navigation-layout'
  | 'scheduler.month-agenda'
  | 'scheduler.narrow-reflow'
  | 'scheduler.now-readonly'
  | 'scheduler.editing-recurrence'
  | 'scheduler.large-data'
  | 'scheduler.locale-pseudo'
  | 'scheduler.locale-ar'
  | 'scheduler.theme-light'
  | 'scheduler.theme-dark'

const copy: Record<ScenarioId, [string, string]> = {
  'scheduler.views-state': ['Views and controlled state', 'Four views, caller-selected subsets, controlled view, and controlled date retain one request per action.'],
  'scheduler.navigation-layout': ['Navigation and calendar layout', 'Day and week navigation preserve all-day bands, overlaps, work-hour shading, and stable event identity.'],
  'scheduler.month-agenda': ['Month drilldown and agenda', 'Month overflow drills into a day while agenda orders future events and names its empty state.'],
  'scheduler.narrow-reflow': ['Narrow responsive schedule', 'Uncontrolled narrow mode defaults to agenda while an explicit week remains horizontally keyboard-scrollable.'],
  'scheduler.now-readonly': ['Current time and read-only policy', 'A frozen current-time line anchors deterministically and read-only mode removes every mutation path.'],
  'scheduler.editing-recurrence': ['Editing and recurrence workflow', 'Provider-neutral CRUD requests and bounded occurrence-or-series decisions preserve recurrence identity.'],
  'scheduler.large-data': ['Bounded large schedule', 'Two hundred fifty-six visible events and dense overlap retain unique chips, bounded chrome, and deterministic replacement.'],
  'scheduler.locale-pseudo': ['Pseudo-localized schedule', 'Expanded toolbar, view, empty, recurrence, and overflow copy remains usable in every view.'],
  'scheduler.locale-ar': ['Arabic date, time, and direction', 'Locale formatting, chevrons, grid placement, and overlays follow right-to-left direction.'],
  'scheduler.theme-light': ['Light theme', 'Calendar surface, work hours, events, now line, overlay, and focus use Harborline light tokens.'],
  'scheduler.theme-dark': ['Dark theme', 'The same deep scheduling seam on the Harborline dark surface.'],
}

const selectedDate = new Date(2026, 7, 11, 12, 0, 0)
const frozenNow = new Date(2026, 7, 11, 10, 15, 0)

function at(dayOffset: number, hour: number, minute = 0) {
  return new Date(2026, 7, 11 + dayOffset, hour, minute, 0)
}

const baseEvents: SchedulerEvent[] = [
  { id: 'all-day', title: 'Structure safety review', start: at(0, 0), end: at(1, 0), allDay: true, color: '#2563eb' },
  { id: 'capture', title: 'Photo-with-pose capture', start: at(0, 9), end: at(0, 10, 30), description: 'Capture grid C-14 and record camera pose.', color: '#0f766e' },
  { id: 'blueprint', title: 'Blueprint placement', start: at(0, 9, 30), end: at(0, 11), description: 'Place the captured pose within the primary structure.', color: '#7c3aed' },
  { id: 'handoff', title: 'Inspection handoff', start: at(0, 13), end: at(0, 14), color: '#b45309' },
  { id: 'field', title: 'Field verification', start: at(1, 8, 30), end: at(1, 10), color: '#be123c' },
]

const recurringMaster: SchedulerEvent = {
  id: 'recurring-review',
  title: 'Weekly spatial review',
  start: at(0, 15),
  end: at(0, 16),
  description: 'Review capture placement with the structural team.',
  recurrenceRule: 'FREQ=WEEKLY;BYDAY=TU,TH;COUNT=8',
  recurrenceExceptions: [at(7, 15)],
  color: '#0f766e',
}

const pseudoCatalog = {
  'scheduler.day': '⟦ Đåÿ ··· ⟧',
  'scheduler.week': '⟦ Ŵëëķ ··· ⟧',
  'scheduler.month': '⟦ Møñţh ···· ⟧',
  'scheduler.agenda': '⟦ Åĝëñđå ······ ⟧',
  'scheduler.today': '⟦ Ţøđåÿ ···· ⟧',
  'scheduler.previous': '⟦ Þřëṽîøûš ······ ⟧',
  'scheduler.next': '⟦ Ñëxţ ··· ⟧',
  'scheduler.now': '⟦ Ñøŵ ··· ⟧',
  'scheduler.allDay': '⟦ Åļļ đåÿ ······ ⟧',
  'scheduler.noUpcomingEvents': '⟦ Ñø ûþçømîñĝ šçhëđûļëđ ëṽëñţš ········ ⟧',
  'scheduler.more': '⟦ +{count} møřë šçhëđûļëđ ëṽëñţš ······ ⟧',
  'scheduler.editRecurringEvent': '⟦ Ëđîţ řëçûřřîñĝ ëṽëñţ ······ ⟧',
  'scheduler.deleteRecurringEvent': '⟦ Đëļëţë řëçûřřîñĝ ëṽëñţ ······ ⟧',
}

const arabicCatalog = {
  'scheduler.day': 'اليوم',
  'scheduler.week': 'الأسبوع',
  'scheduler.month': 'الشهر',
  'scheduler.agenda': 'جدول الأعمال',
  'scheduler.today': 'اليوم الحالي',
  'scheduler.previous': 'السابق',
  'scheduler.next': 'التالي',
  'scheduler.now': 'الآن',
  'scheduler.allDay': 'طوال اليوم',
  'scheduler.noUpcomingEvents': 'لا توجد أحداث قادمة',
  'scheduler.more': '+{count} إضافية',
}

function LargeDataScheduler() {
  const [revision, setRevision] = useState(1)
  const events = useMemo<SchedulerEvent[]>(() => Array.from({ length: 256 }, (_, index) => {
    const day = index % 14
    const lane = index % 64
    const hour = 8 + (lane % 8)
    return {
      id: `revision-${revision}-event-${index}`,
      title: `Structure ${String(index + 1).padStart(3, '0')} · revision ${revision}`,
      start: at(day, hour, (lane % 2) * 30),
      end: at(day, Math.min(23, hour + 1), (lane % 2) * 30),
      color: ['#0f766e', '#2563eb', '#7c3aed', '#b45309'][index % 4],
    }
  }), [revision])
  return <div className="hl-gallery-feedback-stack">
    <div style={{ height: 560 }}><Scheduler data={events} defaultDate={selectedDate} now={frozenNow} readOnly view="agenda" /></div>
    <div className="hl-gallery-button-row">
      <button type="button" onClick={() => setRevision(current => current === 1 ? 96 : 1)}>Replace 256 events</button>
      <output aria-live="polite">Revision {revision} · {events.length} unique events</output>
    </div>
  </div>
}

function EditingScheduler() {
  const [events, setEvents] = useState<SchedulerEvent[]>([...baseEvents, recurringMaster])
  const [requests, setRequests] = useState<string[]>([])
  const parsed = parseRRule(recurringMaster.recurrenceRule ?? '')
  const occurrences = expandRecurrence(recurringMaster, at(0, 0), at(60, 0))
  const remember = (request: string) => setRequests(current => [...current.slice(-3), request])
  return <div className="hl-gallery-feedback-stack">
    <div style={{ height: 560 }}><Scheduler
      data={events}
      defaultDate={selectedDate}
      defaultView="week"
      now={frozenNow}
      onEventAdd={(event: SchedulerEvent) => { setEvents(current => [...current, event]); remember(`add:${event.id}`) }}
      onEventDelete={(event: SchedulerEvent) => { setEvents(current => current.filter(candidate => candidate.id !== event.id)); remember(`delete:${event.id}`) }}
      onEventUpdate={(event: SchedulerEvent) => { setEvents(current => current.map(candidate => candidate.id === event.id ? event : candidate)); remember(`update:${event.id}`) }}
    /></div>
    <output aria-live="polite">RRULE {parsed ? 'parsed' : 'invalid'} · {occurrences.length} bounded occurrences · requests {requests.length ? requests.join(', ') : 'none'}</output>
  </div>
}

function ControlledScheduler({ scenarioId }: { scenarioId: ScenarioId }) {
  const [view, setView] = useState<SchedulerViewType>(scenarioId === 'scheduler.navigation-layout' ? 'week' : 'day')
  const [date, setDate] = useState(selectedDate)
  const [requests, setRequests] = useState<string[]>([])
  const remember = (request: string) => setRequests(current => [...current.slice(-3), request])
  return <div className="hl-gallery-feedback-stack">
    <div style={{ height: 560 }}><Scheduler
      data={baseEvents}
      now={frozenNow}
      onDateChange={(next: Date) => { setDate(next); remember(`date:${next.toISOString().slice(0, 10)}`) }}
      onViewChange={(next: SchedulerViewType) => { setView(next); remember(`view:${next}`) }}
      selectedDate={date}
      view={view}
      views={scenarioId === 'scheduler.views-state' ? [{ type: 'day', title: 'Day' }, { type: 'agenda', title: 'Agenda' }] : undefined}
      workDayStart={8}
      workDayEnd={18}
      workDays={[1, 2, 3, 4, 5]}
      workingHours={scenarioId === 'scheduler.navigation-layout' ? { start: 9, end: 17 } : undefined}
    /></div>
    <output aria-live="polite">Controlled {view} · {date.toLocaleDateString()} · requests {requests.length ? requests.join(', ') : 'none'}</output>
  </div>
}

function MonthAgendaSchedulers() {
  return <div className="hl-gallery-feedback-stack">
    <div style={{ height: 460 }}><Scheduler accessibleName="Month schedule" data={[...baseEvents, ...Array.from({ length: 4 }, (_, index) => ({ id: `month-${index}`, title: `Additional review ${index + 1}`, start: at(0, 14 + index), end: at(0, 15 + index) }))]} defaultDate={selectedDate} now={frozenNow} readOnly view="month" /></div>
    <div style={{ height: 260 }}><Scheduler accessibleName="Agenda schedule" data={[]} defaultDate={selectedDate} now={frozenNow} readOnly view="agenda" /></div>
  </div>
}

function NarrowSchedulers() {
  return <div className="hl-gallery-feedback-stack" style={{ maxWidth: 320 }}>
    <div style={{ height: 340 }}><Scheduler accessibleName="Responsive agenda schedule" data={baseEvents} defaultDate={selectedDate} now={frozenNow} readOnly /></div>
    <div style={{ height: 460 }}><Scheduler accessibleName="Responsive week schedule" data={baseEvents} now={frozenNow} readOnly selectedDate={selectedDate} view="week" /></div>
  </div>
}

function StandardScheduler({ scenarioId }: { scenarioId: ScenarioId }) {
  const arabic = scenarioId === 'scheduler.locale-ar'
  const pseudo = scenarioId === 'scheduler.locale-pseudo'
  const readonly = scenarioId === 'scheduler.now-readonly' || Boolean(arabic || pseudo || scenarioId.startsWith('scheduler.theme-'))
  const view: SchedulerViewType = pseudo ? 'month' : arabic ? 'agenda' : 'day'
  const events = pseudo
    ? baseEvents.map(event => ({ ...event, title: `⟦ ${event.title} ······ ⟧`, description: event.description ? `⟦ ${event.description} ······ ⟧` : undefined }))
    : arabic
      ? baseEvents.map((event, index) => ({ ...event, title: ['مراجعة سلامة الهيكل', 'التقاط صورة مع الوضعية', 'موضع المخطط', 'تسليم الفحص', 'التحقق الميداني'][index] }))
      : baseEvents
  return <div style={{ height: 560, inlineSize: '100%' }}><Scheduler data={events} now={frozenNow} readOnly={readonly} selectedDate={selectedDate} view={view} /></div>
}

function SchedulerFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  if (scenarioId === 'scheduler.views-state' || scenarioId === 'scheduler.navigation-layout') return <ControlledScheduler scenarioId={scenarioId} />
  if (scenarioId === 'scheduler.month-agenda') return <MonthAgendaSchedulers />
  if (scenarioId === 'scheduler.narrow-reflow') return <NarrowSchedulers />
  if (scenarioId === 'scheduler.editing-recurrence') return <EditingScheduler />
  if (scenarioId === 'scheduler.large-data') return <LargeDataScheduler />
  return <StandardScheduler scenarioId={scenarioId} />
}

function SchedulerScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'scheduler.locale-pseudo'
  const arabic = scenarioId === 'scheduler.locale-ar'
  const theme = scenarioId === 'scheduler.theme-light' ? 'light' : scenarioId === 'scheduler.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ overflow: 'auto' }}>
      <HarborlineLocaleProvider catalog={pseudo ? pseudoCatalog : arabic ? arabicCatalog : undefined} direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        <SchedulerFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Scheduler', component: SchedulerScenario, tags: ['autodocs'], parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof SchedulerScenario>
export default meta
type Story = StoryObj<typeof meta>

export const ViewsAndControlledState: Story = { name: 'Views and controlled state', args: { scenarioId: 'scheduler.views-state' } }
export const NavigationAndCalendarLayout: Story = { name: 'Navigation and calendar layout', args: { scenarioId: 'scheduler.navigation-layout' } }
export const MonthDrilldownAndAgenda: Story = { name: 'Month drilldown and agenda', args: { scenarioId: 'scheduler.month-agenda' } }
export const NarrowResponsiveSchedule: Story = { name: 'Narrow responsive schedule', args: { scenarioId: 'scheduler.narrow-reflow' } }
export const CurrentTimeAndReadOnlyPolicy: Story = { name: 'Current time and read-only policy', args: { scenarioId: 'scheduler.now-readonly' } }
export const EditingAndRecurrenceWorkflow: Story = { name: 'Editing and recurrence workflow', args: { scenarioId: 'scheduler.editing-recurrence' } }
export const BoundedLargeSchedule: Story = { name: 'Bounded large schedule', args: { scenarioId: 'scheduler.large-data' } }
export const PseudoLocalizedSchedule: Story = { name: 'Pseudo-localized schedule', args: { scenarioId: 'scheduler.locale-pseudo' } }
export const ArabicDateTimeAndDirection: Story = { name: 'Arabic date, time, and direction', args: { scenarioId: 'scheduler.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'scheduler.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'scheduler.theme-dark' } }
