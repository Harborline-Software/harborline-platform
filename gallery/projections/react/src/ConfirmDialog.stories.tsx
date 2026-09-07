import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { ConfirmDialog, HarborlineLocaleProvider } from '@harborline-software/ui-react'

// One story per scenario id in gallery/scenarios/hlp.ui.confirm-dialog.json. The gallery gate
// matches them by id, so an id here that is absent there (or the reverse) fails the run.
type ScenarioId =
  | 'confirm-dialog.action-pair'
  | 'confirm-dialog.labels'
  | 'confirm-dialog.destructive'
  | 'confirm-dialog.reflow-rtl'
  | 'confirm-dialog.dismissal'
  | 'confirm-dialog.content'

const copy: Record<ScenarioId, [string, string]> = {
  'confirm-dialog.action-pair': ['Action pair and confirm ordering', 'Cancel precedes confirm; confirming invokes the caller once and then requests closure.'],
  'confirm-dialog.labels': ['Localized and overridden labels', 'Both actions resolve from the catalog, and explicit labels win over it.'],
  'confirm-dialog.destructive': ['Destructive emphasis', 'The destructive variant changes emphasis only — order and semantics are unchanged.'],
  'confirm-dialog.reflow-rtl': ['Long content reflow and RTL mirroring', 'Expanded confirmation copy wraps, and the action pair mirrors under Arabic.'],
  'confirm-dialog.dismissal': ['Dismissal policy passthrough', 'Overlay and Escape dismissal are suppressed by policy passed to the composed shell.'],
  'confirm-dialog.content': ['Content resilience', 'Hostile-but-valid copy remains text across the confirmation title, description, and action labels.'],
}

// A catalog that echoes its keys is the observed key-echo defect. The labels scenario renders it
// deliberately so the gallery captures the fallback rather than a raw "common.confirm".
const echoCatalog = { 'common.confirm': 'common.confirm', 'common.cancel': 'common.cancel' }
const arabicCatalog = { 'common.confirm': 'تأكيد', 'common.cancel': 'إلغاء' }
const longLabel = 'Permanently delete this structure and every captured pose attached to it'

function ConfirmFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const [open, setOpen] = useState(true)
  const [confirms, setConfirms] = useState(0)
  const [requests, setRequests] = useState(0)

  const arabic = scenarioId === 'confirm-dialog.reflow-rtl'
  const destructive = scenarioId === 'confirm-dialog.destructive'
  const suppressed = scenarioId === 'confirm-dialog.dismissal'
  const echo = scenarioId === 'confirm-dialog.labels'

  const title = arabic
    ? 'حذف الهيكل نهائيًا؟'
    : destructive
      ? 'Delete this structure permanently?'
      : 'Discard unsaved changes?'
  const description = arabic
    ? 'لا يمكن التراجع عن هذا الإجراء بعد التأكيد.'
    : 'This action cannot be undone once confirmed.'

  return (
    // The probe is the hl-gallery-scene section, matching ConfirmDialogScenario.razor element for
    // element. Visual parity compares the probe's own box, so a differently placed probe fails on
    // canvas dimensions even when the rendered component is identical.
    <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}>
      <header className="hl-gallery-heading">
        <h2>{copy[scenarioId][0]}</h2>
        <p>{copy[scenarioId][1]}</p>
      </header>
      <div className="hl-gallery-stage">
    <HarborlineLocaleProvider
      catalog={arabic ? arabicCatalog : echo ? echoCatalog : {}}
      direction={arabic ? 'rtl' : 'ltr'}
      locale={arabic ? 'ar-SA' : 'en-US'}
    >
      <div className="hl-gallery-feedback-stack" style={{ minBlockSize: 420 }}>
        <button type="button" onClick={() => setOpen(true)}>Open confirmation</button>
        {/* Counters make the ordering contract observable in the gallery, not just in unit tests. */}
        <output aria-live="polite">Confirms: {confirms} · Open-change requests: {requests}</output>
        {scenarioId === 'confirm-dialog.content' ? <ConfirmDialog
          open={open}
          onOpenChange={(next: boolean) => { setRequests(current => current + 1); setOpen(next) }}
          onConfirm={() => setConfirms(current => current + 1)}
          title={'Awaiting third-party structural certification review'}
          description={'Bay 4 <grid C-7> & 8'}
          cancelLabel={'1,284,905'}
          confirmLabel={'Ordnance Survey — Niño Ångström'}
        /> : <ConfirmDialog
          open={open}
          onOpenChange={(next: boolean) => { setRequests(current => current + 1); setOpen(next) }}
          onConfirm={() => setConfirms(current => current + 1)}
          title={title}
          description={description}
          confirmLabel={arabic ? undefined : destructive ? longLabel : undefined}
          variant={destructive ? 'destructive' : 'default'}
          closeOnOverlayClick={!suppressed}
          closeOnEscape={!suppressed}
        />}
      </div>
    </HarborlineLocaleProvider>
      </div>
    </section>
  )
}

const meta = {
  title: 'UI/ConfirmDialog',
  component: ConfirmFixture,
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof ConfirmFixture>

export default meta

type Story = StoryObj<typeof meta>

// The story name must be a literal. Storybook's indexer reads this file statically, so a name
// computed from `copy` is not resolved and the entry falls back to its export name — which the
// gallery gate then cannot match against the scenario name.
const story = (scenarioId: ScenarioId): Omit<Story, 'name'> => ({
  args: { scenarioId },
  parameters: { docs: { description: { story: copy[scenarioId][1] } } },
})

export const ActionPair: Story = { ...story('confirm-dialog.action-pair'), name: 'Action pair and confirm ordering' }
export const Labels: Story = { ...story('confirm-dialog.labels'), name: 'Localized and overridden labels' }
export const Destructive: Story = { ...story('confirm-dialog.destructive'), name: 'Destructive emphasis' }
export const ReflowRtl: Story = { ...story('confirm-dialog.reflow-rtl'), name: 'Long content reflow and RTL mirroring' }
export const Dismissal: Story = { ...story('confirm-dialog.dismissal'), name: 'Dismissal policy passthrough' }
;export const Content: Story = { ...story('confirm-dialog.content'), name: 'Content resilience' }
