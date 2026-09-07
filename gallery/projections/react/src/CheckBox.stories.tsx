import type { Meta, StoryObj } from '@storybook/react-vite'
import { CheckBox } from '@harborline-software/ui-react'

type ScenarioId = 'check-box.defaults' | 'check-box.states' | 'check-box.rtl-theme' | 'check-box.content' | 'check-box.error-state'

function CheckBoxScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const states = scenarioId === 'check-box.states'
  const rtl = scenarioId === 'check-box.rtl-theme'
  const content = scenarioId === 'check-box.content'
  const errorState = scenarioId === 'check-box.error-state'
  const title = errorState ? 'Error state' : states ? 'States and sizes' : rtl ? 'RTL and theme' : content ? 'Content resilience' : 'Defaults and labels'

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={rtl ? 'dark' : undefined} dir={rtl ? 'rtl' : undefined}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>Native state, labels, validation, and logical placement remain equivalent.</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid">
      {errorState ? <CheckBox error required label="Accept inspection terms" /> : content ? <>
        <CheckBox label={'Awaiting third-party structural certification review'} />
        <CheckBox label={'1,284,905'} />
        <CheckBox label={'Bay 4 <grid C-7> & 8'} />
        <CheckBox label={'Ordnance Survey — Niño Ångström'} />
      </> : states ? <>
        <CheckBox checked={false} label="Unchecked" size="sm" />
        <CheckBox checked label="Checked" size="md" />
        <CheckBox checked="mixed" label="Mixed" size="lg" />
        <CheckBox checked disabled label="Disabled" />
        <CheckBox error label="Required with error" required />
      </> : <>
        <CheckBox defaultChecked label={rtl ? 'تضمين السجل' : 'Include record'} />
        <CheckBox label={rtl ? 'إرسال إشعار' : 'Send notification'} labelPlacement="before" />
      </>}
    </div>
  </section>
}

const meta = { title: 'Platform/Check Box', component: CheckBoxScenario, parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof CheckBoxScenario>
export default meta
type Story = StoryObj<typeof meta>
export const Defaults: Story = { name: 'Defaults and labels', args: { scenarioId: 'check-box.defaults' } }
export const States: Story = { name: 'States and sizes', args: { scenarioId: 'check-box.states' } }
export const RtlAndTheme: Story = { name: 'RTL and theme', args: { scenarioId: 'check-box.rtl-theme' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'check-box.content' } }
;export const ErrorState: Story = { name: 'Error state', args: { scenarioId: 'check-box.error-state' } }
