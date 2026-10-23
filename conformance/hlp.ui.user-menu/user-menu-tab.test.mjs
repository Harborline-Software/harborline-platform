// @vitest-environment jsdom
import { disposeKeys, observeKeys } from '../../projections/blazor/ui/hlp.ui.user-menu/wwwroot/user-menu.js'

// The Blazor lane's Tab-out handling, run here because bUnit cannot execute the module's JavaScript.
// Over a Server circuit the closing render arrives after the browser's default Tab, so the handler must
// keep that Tab from landing on the still-rendered items: the panel is inert during the key, and only
// then. The default action itself is a browser behaviour, checked by the Server smoke probe.
describe('UserMenu Blazor Tab out of the menu', () => {
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  function menu() {
    const trigger = document.createElement('button')
    const panel = document.createElement('div')
    const item = document.createElement('button')
    panel.append(item)
    document.body.append(trigger, panel)
    const id = observeKeys(panel, true, { invokeMethodAsync: () => Promise.resolve() }, trigger)
    return { trigger, panel, item, id }
  }

  it('moves focus to the trigger with the panel inert for the default Tab, then restores it', () => {
    vi.useFakeTimers()
    const { trigger, panel, item, id } = menu()
    item.focus()
    let inertDuringKey = null
    document.addEventListener('keydown', () => { inertDuringKey = panel.inert }, { once: true })
    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })
    item.dispatchEvent(event)
    expect([inertDuringKey, document.activeElement === trigger, event.defaultPrevented]).toEqual([true, true, false])
    vi.runAllTimers()
    expect(panel.inert).toBe(false)
    disposeKeys(id)
  })

  it('leaves the panel alone on Shift+Tab, which stops on the trigger', () => {
    const { trigger, panel, item, id } = menu()
    item.focus()
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true })
    item.dispatchEvent(event)
    expect([panel.inert, document.activeElement === trigger, event.defaultPrevented]).toEqual([false, true, true])
    disposeKeys(id)
  })
})
