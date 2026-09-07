import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { Window, WindowActionsBar, type WindowState } from '@harborline-software/ui-react'

type ScenarioId = 'window.states' | 'window.geometry' | 'window.modal' | 'window.locale-theme' | 'window.content'

function WindowScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const localized = scenarioId === 'window.locale-theme'
  const content = scenarioId === 'window.content'
  const [state, setState] = useState<WindowState>('default')
  const [visible, setVisible] = useState(true)
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={localized ? 'dark' : undefined} dir={localized ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{content ? 'Content resilience' : localized ? 'نافذة الفحص' : scenarioId === 'window.geometry' ? 'Movement and resize' : scenarioId === 'window.modal' ? 'Modal focus and dismissal' : 'Window states'}</h2><p>{content ? 'Hostile window content carries a title longer than its title bar, a grouped large number, escaped angle brackets and an ampersand, and a name with diacritics and an em dash.' : 'State, geometry, actions, focus, and localized controls.'}</p></header>
    {!visible ? <button type="button" onClick={() => setVisible(true)}>Restore window</button> : null}
    {visible ? <Window title={content ? 'Awaiting third-party structural certification review' : localized ? 'فحص الهيكل' : 'Structure inspection'} width={440} height={280} top={120} left={120} modal={scenarioId === 'window.modal'} state={state} onStateChange={setState} onClose={() => setVisible(false)} labels={localized ? { close: 'إغلاق', minimize: 'تصغير', maximize: 'تكبير', restore: 'استعادة', move: 'نقل النافذة', moveDescription: 'استخدم مفاتيح الأسهم', resizeHeight: 'تغيير الارتفاع', resizeWidth: 'تغيير العرض', window: 'نافذة' } : undefined}><WindowActionsBar><button type="button">{content ? '1,284,905' : localized ? 'حفظ' : 'Save'}</button></WindowActionsBar><p>{content ? 'Bay 4 <grid C-7> & 8' : localized ? 'التقط صورة وحدد موضعها في المخطط.' : 'Capture a photo and place it within the blueprint.'}</p><button type="button">{content ? 'Ordnance Survey — Niño Ångström' : localized ? 'بدء الالتقاط' : 'Start capture'}</button></Window> : null}
  </section>
}

const meta = { title: 'Platform/Window', component: WindowScenario, parameters: { layout: 'fullscreen', controls: { disable: true } } } satisfies Meta<typeof WindowScenario>
export default meta
type Story = StoryObj<typeof meta>
export const States: Story = { name: 'Window states', args: { scenarioId: 'window.states' } }
export const Geometry: Story = { name: 'Movement and resize', args: { scenarioId: 'window.geometry' } }
export const Modal: Story = { name: 'Modal focus and dismissal', args: { scenarioId: 'window.modal' } }
export const LocaleTheme: Story = { name: 'Locale and theme', args: { scenarioId: 'window.locale-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'window.content' } }
