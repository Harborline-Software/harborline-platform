import { useEffect, useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { createToastService, HarborlineLocaleProvider, Toaster, type ToastService } from '@harborline-software/ui-react'

type ScenarioId =
  | 'toaster.variants-roles' | 'toaster.queue-lifecycle' | 'toaster.action-promise' | 'toaster.positions'
  | 'toaster.locale-en' | 'toaster.locale-pseudo' | 'toaster.locale-ar'
  | 'toaster.theme-light' | 'toaster.theme-dark'
  | 'toaster.content'
  | 'toaster.error-state'

const copy: Record<ScenarioId, [string, string]> = {
  'toaster.variants-roles': ['Variants and live-region roles', 'Error uses alert while every other variant uses status without redundant live-region metadata.'],
  'toaster.queue-lifecycle': ['Queue, timing, and dismissal', 'Newest entries remain visible while persistence, close policy, and bounded dismissal remain deterministic.'],
  'toaster.action-promise': ['Action and tracked operation', 'Actions invoke once without closing and one identity transitions from loading to its result.'],
  'toaster.positions': ['Physical positions', 'Six source-compatible physical positions remain distinct while motion follows logical direction.'],
  'toaster.locale-en': ['English locale', 'Caller messages and a localized dismiss label remain separate.'],
  'toaster.locale-pseudo': ['Pseudo locale', 'Expanded messages, descriptions, and actions remain viewport-clamped.'],
  'toaster.locale-ar': ['Arabic locale', 'Physical placement is retained while entry and exit motion mirror.'],
  'toaster.theme-light': ['Light theme', 'Every toast variant uses public light-theme tokens.'],
  'toaster.theme-dark': ['Dark theme', 'The same host-scoped queue on the Harborline dark surface.'],
  'toaster.content': ['Content resilience', 'A dense queue preserves hostile-but-valid messages as text.'],
  'toaster.error-state': ['Error state', 'A failed save is announced as an error alert.'],
}

function seedToasts(service: ToastService, scenarioId: ScenarioId) {
  const pseudo = scenarioId === 'toaster.locale-pseudo'
  const arabic = scenarioId === 'toaster.locale-ar'
  const message = (value: string) => arabic ? `تم ${value}` : pseudo ? `⟦ ${value} ······ ⟧` : value
  if (scenarioId === 'toaster.content') {
    service.show('Awaiting third-party structural certification review')
    service.success('1,284,905')
    service.error('Bay 4 <grid C-7> & 8')
    service.information('Ordnance Survey — Niño Ångström')
    return
  }
  if (scenarioId === 'toaster.error-state') {
    service.error('Unable to save changes')
    return
  }
  if (scenarioId === 'toaster.action-promise') {
    service.show(message('Blueprint placement needs review'), { description: message('Bay 4, grid C-7'), action: { label: arabic ? 'إعادة المحاولة' : 'Retry', onActivate: () => {} } })
    void service.trackAsync(Promise.resolve('indexed'), { loading: message('Indexing blueprint'), success: message('Blueprint indexed'), error: message('Indexing failed') })
    return
  }
  if (scenarioId === 'toaster.queue-lifecycle') {
    service.show(message('Capture queued'))
    service.success(message('Pose saved'))
    service.error(message('Placement failed'), { description: message('Select a different blueprint region') })
    service.loading(message('Recalculating orientation'))
    return
  }
  service.show(message('Capture ready'))
  service.success(message('Pose saved'))
  service.error(message('Placement failed'))
  service.warning(message('Blueprint scale is unverified'))
  service.information(message('Three collaborators are viewing'))
  service.loading(message('Calculating orientation'))
}

const positions = ['top-left', 'top-center', 'top-right', 'bottom-left', 'bottom-center', 'bottom-right'] as const

function ToastFixture({ scenarioId, position = 'bottom-right', onPositionChange }: { scenarioId: ScenarioId; position?: typeof positions[number]; onPositionChange?: (position: typeof positions[number]) => void }) {
  const [seed, setSeed] = useState(0)
  const service = useMemo(() => createToastService(), [])
  useEffect(() => {
    service.clear()
    seedToasts(service, scenarioId)
  }, [scenarioId, seed, service])
  useEffect(() => () => service.dispose(), [service])
  const arabic = scenarioId === 'toaster.locale-ar'
  const pseudo = scenarioId === 'toaster.locale-pseudo'
  return <div className="hl-gallery-feedback-stack">
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
      <button type="button" onClick={() => setSeed(value => value + 1)}>{arabic ? 'إظهار الإشعارات' : pseudo ? '⟦ Šhøŵ ţøåšţš ··· ⟧' : 'Show toasts'}</button>
      <button type="button" onClick={() => service.dismiss()}>{arabic ? 'إغلاق الكل' : 'Dismiss all'}</button>
      {onPositionChange ? positions.map(candidate => <button type="button" key={candidate} onClick={() => onPositionChange(candidate)}>{candidate}</button>) : null}
    </div>
    <Toaster
      service={service}
      position={position}
      duration={null}
      maximumVisible={scenarioId === 'toaster.queue-lifecycle' ? 2 : 6}
      showCloseButton={scenarioId === 'toaster.queue-lifecycle'}
      keepErrorsPersistent
      dismissLabel={arabic ? 'إغلاق الإشعار' : pseudo ? '⟦ Đîšmîšš ñøţîƒîçåţîøñ ···· ⟧' : 'Dismiss notification'}
      direction={arabic ? 'rtl' : 'ltr'}
    />
  </div>
}

function ToasterScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [position, setPosition] = useState<typeof positions[number]>('bottom-right')
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'toaster.locale-pseudo'
  const arabic = scenarioId === 'toaster.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'toaster.theme-light' ? 'light' : scenarioId === 'toaster.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-stack">
      <HarborlineLocaleProvider locale={locale}>
        <ToastFixture scenarioId={scenarioId} position={position} onPositionChange={scenarioId === 'toaster.positions' ? setPosition : undefined} />
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Toaster', component: ToasterScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof ToasterScenario>
export default meta
type Story = StoryObj<typeof meta>
export const VariantsAndLiveRegionRoles: Story = { name: 'Variants and live-region roles', args: { scenarioId: 'toaster.variants-roles' } }
export const QueueTimingAndDismissal: Story = { name: 'Queue, timing, and dismissal', args: { scenarioId: 'toaster.queue-lifecycle' } }
export const ActionAndTrackedOperation: Story = { name: 'Action and tracked operation', args: { scenarioId: 'toaster.action-promise' } }
export const PhysicalPositions: Story = { name: 'Physical positions', args: { scenarioId: 'toaster.positions' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'toaster.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'toaster.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'toaster.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'toaster.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'toaster.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'toaster.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'toaster.error-state' } }
