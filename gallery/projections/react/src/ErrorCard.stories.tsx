import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { ErrorCard } from '@harborline-software/ui-react'

type ErrorCardScenarioId =
  | 'error-card.default'
  | 'error-card.variants'
  | 'error-card.message'
  | 'error-card.retry'
  | 'error-card.no-retry'
  | 'error-card.host-attributes'
  | 'error-card.locale-en'
  | 'error-card.locale-pseudo'
  | 'error-card.locale-ar'
  | 'error-card.theme-light'
  | 'error-card.theme-dark'
  | 'error-card.content'

const copy: Record<ErrorCardScenarioId, [string, string]> = {
  'error-card.default': ['Default', 'A visible alert with a caller-owned title and no implied action.'],
  'error-card.variants': ['Variants', 'Page, default, and compact presentation preserve their heading semantics.'],
  'error-card.message': ['Message and tone', 'Optional detail follows the title using the destructive and muted token roles.'],
  'error-card.retry': ['Retry action', 'A native button is present only when the caller supplies a retry handler.'],
  'error-card.no-retry': ['Without retry', 'The same failure remains complete when recovery is unavailable.'],
  'error-card.host-attributes': ['Host attributes', 'Consumer class, data, description, and direction reach the alert host.'],
  'error-card.locale-en': ['English locale', 'Caller text and the source-compatible retry fallback render in English.'],
  'error-card.locale-pseudo': ['Pseudo locale', 'Expanded caller text and retry copy expose clipping and reflow defects.'],
  'error-card.locale-ar': ['Arabic locale', 'Caller-localized content follows logical RTL alignment and spacing.'],
  'error-card.theme-light': ['Light theme', 'Surface, border, title, message, retry, hover, and focus roles on light.'],
  'error-card.theme-dark': ['Dark theme', 'The complete feedback state vocabulary on the dark surface.'],
  'error-card.content': ['Content resilience', 'Hostile content carries a long retry label, the grouped number 1,284,905, escaped angle brackets and an ampersand, a name with diacritics and an em dash, and omitted optional fields.'],
}

function RetryableErrorCard(props: { retryLabel?: string; lang?: string; dir?: 'ltr' | 'rtl' }) {
  const [count, setCount] = useState(0)
  return <div className="hl-gallery-feedback-stack">
    <ErrorCard
      title={props.lang === 'ar-SA' ? 'تعذر تحميل عمليات الفحص' : props.lang === 'en-XA' ? '⟦ Ûñåɓļë ţø ļøåđ îñšþëçţîøñš ······ ⟧' : 'Unable to load inspections'}
      message={props.lang === 'ar-SA' ? 'تحقق من اتصالك ثم حاول مرة أخرى.' : props.lang === 'en-XA' ? '⟦ Çhëçķ ÿøûř çøññëçţîøñ åñđ ţřÿ åĝåîñ ········ ⟧' : 'Check your connection and try again.'}
      onRetry={() => setCount(value => value + 1)}
      retryLabel={props.retryLabel}
      lang={props.lang}
      dir={props.dir}
      data-retry-count={count}
    />
    <output className="hl-gallery-activation" aria-live="polite">Retry activations: {count}</output>
  </div>
}

function ErrorCardScenario({ scenarioId }: { scenarioId: ErrorCardScenarioId }) {
  const [title, description] = copy[scenarioId]
  let content

  switch (scenarioId) {
    case 'error-card.content':
      content = <><ErrorCard title={'Ordnance Survey — Niño Ångström'} message={'1,284,905 records affected at Bay 4 <grid C-7> & 8'} onRetry={() => {}} retryLabel={'Review affected inspection records and retry import'} /><ErrorCard title={'Awaiting third-party structural certification review'} /></>
      break
    case 'error-card.variants':
      content = <div className="hl-gallery-feedback-grid">
        <ErrorCard title="Page unavailable" message="Return to the inspection list or try again." variant="page" />
        <ErrorCard title="Unable to load" message="Check your connection." />
        <ErrorCard title="Failed" message="Attachment unavailable." variant="compact" />
      </div>
      break
    case 'error-card.message':
      content = <ErrorCard title="Unable to load" message="Check your connection before trying again." />
      break
    case 'error-card.retry':
      content = <RetryableErrorCard />
      break
    case 'error-card.host-attributes':
      content = <><ErrorCard title="تعذر التحميل" message="راجع تفاصيل الخطأ." dir="rtl" className="consumer" data-case="shared" aria-describedby="error-details" /><span id="error-details" className="hl-gallery-sr-only">Inspection loading error details</span></>
      break
    case 'error-card.locale-en':
      content = <RetryableErrorCard lang="en-US" dir="ltr" />
      break
    case 'error-card.locale-pseudo':
      content = <RetryableErrorCard lang="en-XA" dir="ltr" retryLabel="⟦ Řëţřý ··· ⟧" />
      break
    case 'error-card.locale-ar':
      content = <RetryableErrorCard lang="ar-SA" dir="rtl" retryLabel="إعادة المحاولة" />
      break
    case 'error-card.theme-light':
    case 'error-card.theme-dark':
      content = <ErrorCard title="Page unavailable" message="Check your connection and try again." variant="page" onRetry={() => {}} />
      break
    case 'error-card.default':
    case 'error-card.no-retry':
      content = <ErrorCard title="Unable to load" />
      break
  }

  const theme = scenarioId === 'error-card.theme-light' ? 'light' : scenarioId === 'error-card.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stage">{content}</div>
  </section>
}

const meta = {
  title: 'Platform/Error Card',
  component: ErrorCardScenario,
  tags: ['autodocs'],
  parameters: { controls: { disable: true } },
} satisfies Meta<typeof ErrorCardScenario>

export default meta
type Story = StoryObj<typeof meta>

export const Default: Story = { args: { scenarioId: 'error-card.default' } }
export const Variants: Story = { args: { scenarioId: 'error-card.variants' } }
export const MessageAndTone: Story = { name: 'Message and tone', args: { scenarioId: 'error-card.message' } }
export const RetryAction: Story = { name: 'Retry action', args: { scenarioId: 'error-card.retry' } }
export const WithoutRetry: Story = { name: 'Without retry', args: { scenarioId: 'error-card.no-retry' } }
export const HostAttributes: Story = { name: 'Host attributes', args: { scenarioId: 'error-card.host-attributes' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'error-card.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'error-card.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'error-card.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'error-card.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'error-card.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'error-card.content' } }
