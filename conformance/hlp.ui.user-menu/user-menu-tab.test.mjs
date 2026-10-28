// @vitest-environment jsdom
import { disposeKeys, observeKeys } from '../../projections/blazor/ui/hlp.ui.user-menu/wwwroot/user-menu.js'

// The Blazor lane's Tab-out handling, run here because bUnit cannot execute the module's JavaScript.
// Over a Server circuit the closing render arrives after the browser's default Tab, so the handler must
// keep that Tab from landing on the still-rendered items: the panel is inert during the key, and only
// then. jsdom does not implement inert, so this pins the handler's writes; the default action itself is
// a browser behaviour, checked by the Server smoke probe.
describe('UserMenu Blazor Tab out of the menu', () => {
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  function menu(pattern = true) {
    const trigger = document.createElement('button')
    const panel = document.createElement('div')
    const item = document.createElement('button')
    const outside = document.createElement('button')
    panel.append(item)
    document.body.append(trigger, panel, outside)
    const calls = []
    const id = observeKeys(panel, pattern, { invokeMethodAsync: name => { calls.push(name); return Promise.resolve() } }, trigger)
    return { trigger, panel, item, outside, calls, id }
  }
  const tab = (target, init = {}) => { const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true, ...init }); target.dispatchEvent(event); return event }

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
    expect([Boolean(panel.inert), document.activeElement === trigger, event.defaultPrevented]).toEqual([false, true, true])
    disposeKeys(id)
  })

  it('closes the menu on Tab from the page body, where no Blazor handler sees the key', () => {
    vi.useFakeTimers()
    const { trigger, panel, calls, id } = menu()
    document.activeElement?.blur()
    tab(document.body)
    expect([document.activeElement === trigger, calls]).toEqual([true, ['OnTabOut']])
    vi.runAllTimers()
    expect(Boolean(panel.inert)).toBe(false)
    disposeKeys(id)
  })

  it('ignores Tab from outside the panel and modified Tabs', () => {
    const { panel, item, outside, calls, id } = menu()
    outside.focus()
    tab(outside)
    item.focus()
    tab(item, { ctrlKey: true })
    expect([Boolean(panel.inert), document.activeElement === item, calls]).toEqual([false, true, []])
    disposeKeys(id)
  })

  it('leaves the custom-content dialog pattern to native Tab', () => {
    const { panel, item, calls, id } = menu(false)
    item.focus()
    const event = tab(item)
    expect([Boolean(panel.inert), document.activeElement === item, event.defaultPrevented, calls]).toEqual([false, true, false, []])
    disposeKeys(id)
  })
})
