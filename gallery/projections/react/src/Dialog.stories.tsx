import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Dialog, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'dialog.lifecycle-content'
  | 'dialog.dismissal-focus'
  | 'dialog.independent-dismissal'
  | 'dialog.scroll-reflow'
  | 'dialog.replacement'
  | 'dialog.locale-pseudo'
  | 'dialog.locale-ar'
  | 'dialog.theme-light'
  | 'dialog.theme-dark'
  | 'dialog.content'

const copy: Record<ScenarioId, [string, string]> = {
  'dialog.lifecycle-content': ['Controlled lifecycle and content', 'Title, description, body, footer, and nested controls share one controlled modal lifecycle.'],
  'dialog.dismissal-focus': ['Dismissal and focus lifecycle', 'Close, Escape, and overlay dismissal each request closure once before focus returns to the opener.'],
  'dialog.independent-dismissal': ['Independent dismissal controls', 'The close button and overlay are suppressed while Escape remains independently enabled.'],
  'dialog.scroll-reflow': ['Scrollable body and narrow reflow', 'Pinned chrome surrounds one keyboard-scrollable body at narrow widths and 200 percent zoom.'],
  'dialog.replacement': ['Complete controlled replacement', 'Title, description, body, footer, and policy follow the latest caller revision.'],
  'dialog.locale-pseudo': ['Pseudo-localized modal', 'Expanded modal copy and close chrome wrap without clipping or hiding controls.'],
  'dialog.locale-ar': ['Arabic right-to-left modal', 'Direction, logical spacing, focus order, and localized close chrome mirror under Arabic.'],
  'dialog.theme-light': ['Light theme', 'Overlay, surface, boundary, text, controls, and focus use Harborline light tokens.'],
  'dialog.theme-dark': ['Dark theme', 'The same controlled modal surface on the Harborline dark theme.'],
  'dialog.content': ['Content resilience', 'Hostile-but-valid copy remains text across the dialog title, description, body, and footer.'],
}

const pseudoCatalog = { 'common.close': '⟦ Çļøšë đîåļøĝ ······ ⟧' }
const arabicCatalog = { 'common.close': 'إغلاق الحوار' }

function DialogFixture({ scenarioId }: { scenarioId: ScenarioId }) {
  const [open, setOpen] = useState(true)
  const [requests, setRequests] = useState(0)
  const [revision, setRevision] = useState(1)
  const pseudo = scenarioId === 'dialog.locale-pseudo'
  const arabic = scenarioId === 'dialog.locale-ar'
  const scrolling = scenarioId === 'dialog.scroll-reflow'
  const independent = scenarioId === 'dialog.independent-dismissal'
  const replacement = scenarioId === 'dialog.replacement'
  const content = scenarioId === 'dialog.content'
  const theme = scenarioId === 'dialog.theme-dark' ? 'dark' : 'light'
  const title = content ? 'Awaiting third-party structural certification review' : arabic ? 'تعديل موضع الالتقاط داخل الهيكل' : pseudo ? '⟦ Ëđîţ þhøţø-ŵîţh-þøšë þļåçëmëñţ ········ ⟧' : replacement ? `Edit structure placement · revision ${revision}` : 'Edit structure placement'
  const description = content ? 'Bay 4 <grid C-7> & 8' : arabic ? 'راجع المخطط وموضع الصورة قبل الحفظ.' : pseudo ? '⟦ Řëṽîëŵ ţhë ƀļûëþřîñţ åñđ çåþţûřëđ þøšë ƀëƒøřë šåṽîñĝ. ········ ⟧' : 'Review the blueprint and captured pose before saving.'
  const requestOpen = (next: boolean) => {
    setRequests(current => current + 1)
    setOpen(next)
  }

  return <div className="hl-gallery-feedback-stack" style={{ minHeight: 420 }}>
    <button type="button" onClick={() => setOpen(true)}>Open structure dialog</button>
    <output aria-live="polite">Open-change requests: {requests}</output>
    <Dialog
      open={open}
      onOpenChange={requestOpen}
      {...{ theme }}
      title={title}
      description={description}
      closeIcon={!independent}
      closeOnEscape
      closeOnOverlayClick={!independent}
      footer={content ? <button type="button">Ordnance Survey — Niño Ångström</button> : <div className="hl-gallery-button-row">
        <button type="button" onClick={() => requestOpen(false)}>{arabic ? 'إلغاء' : pseudo ? '⟦ Çåñçëļ ··· ⟧' : 'Cancel'}</button>
        <button type="button" onClick={() => requestOpen(false)}>{arabic ? 'حفظ الموضع' : pseudo ? '⟦ Šåṽë þļåçëmëñţ ······ ⟧' : 'Save placement'}</button>
      </div>}
    >
      <div className="hl-gallery-feedback-stack">
        {content ? <p>1,284,905</p> : <><label>{arabic ? 'مرجع الشبكة' : pseudo ? '⟦ ßļûëþřîñţ ĝřîđ řëƒëřëñçë ······ ⟧' : 'Blueprint grid reference'}<input defaultValue={replacement ? `C-${revision}` : 'C-14'} /></label>
        <button type="button">{arabic ? 'معاينة الوضعية' : pseudo ? '⟦ Þřëṽîëŵ þøšë ······ ⟧' : 'Preview pose'}</button>
        {scrolling ? Array.from({ length: 18 }, (_, index) => <p key={index}>Inspection note {index + 1}: preserve alignment with the primary structural axis.</p>) : null}
        {replacement ? <button type="button" onClick={() => setRevision(current => current === 1 ? 96 : 1)}>Replace dialog content</button> : null}</>}
      </div>
    </Dialog>
  </div>
}

function DialogScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'dialog.locale-pseudo'
  const arabic = scenarioId === 'dialog.locale-ar'
  const theme = scenarioId === 'dialog.theme-light' ? 'light' : scenarioId === 'dialog.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ maxWidth: scenarioId === 'dialog.scroll-reflow' ? 360 : 720 }}>
      <HarborlineLocaleProvider catalog={pseudo ? pseudoCatalog : arabic ? arabicCatalog : undefined} direction={arabic ? 'rtl' : 'ltr'} locale={arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'}>
        <DialogFixture scenarioId={scenarioId} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Dialog', component: DialogScenario, tags: ['autodocs'], parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof DialogScenario>
export default meta
type Story = StoryObj<typeof meta>

export const ControlledLifecycleAndContent: Story = { name: 'Controlled lifecycle and content', args: { scenarioId: 'dialog.lifecycle-content' } }
export const DismissalAndFocusLifecycle: Story = { name: 'Dismissal and focus lifecycle', args: { scenarioId: 'dialog.dismissal-focus' } }
export const IndependentDismissalControls: Story = { name: 'Independent dismissal controls', args: { scenarioId: 'dialog.independent-dismissal' } }
export const ScrollableBodyAndNarrowReflow: Story = { name: 'Scrollable body and narrow reflow', args: { scenarioId: 'dialog.scroll-reflow' } }
export const CompleteControlledReplacement: Story = { name: 'Complete controlled replacement', args: { scenarioId: 'dialog.replacement' } }
export const PseudoLocalizedModal: Story = { name: 'Pseudo-localized modal', args: { scenarioId: 'dialog.locale-pseudo' } }
export const ArabicRightToLeftModal: Story = { name: 'Arabic right-to-left modal', args: { scenarioId: 'dialog.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'dialog.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'dialog.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'dialog.content' } }
