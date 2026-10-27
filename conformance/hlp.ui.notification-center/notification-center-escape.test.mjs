// @vitest-environment jsdom
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'
import { connect as connectSheet } from '../../projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.js'
import { observeEscape } from '../../projections/blazor/ui/hlp.ui.notification-center/wwwroot/notification-center.js'

// The Blazor lane's Escape layering, run with the real dialog.js and sheet.js because bUnit cannot
// execute JavaScript. The notification dialog closes on Escape through its Razor @onkeydown, which
// cannot mark the key handled, so both overlays (document listeners that leave a handled Escape
// alone) used to close too. notification-center.js marks it on the dialog element first.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} }

describe('NotificationCenter Blazor Escape inside a Dialog or Sheet', () => {
  let cleanup = []
  afterEach(() => { cleanup.reverse().forEach(run => run()); cleanup = []; document.body.replaceChildren() })

  function build(host) {
    const calls = []
    const recorder = { invokeMethodAsync: method => { calls.push(method); return Promise.resolve() } }
    const portal = document.createElement('div'); portal.className = host === 'dialog' ? 'hl-dialog__portal' : 'hl-sheet__portal'
    const surface = document.createElement('div'); surface.tabIndex = -1; portal.append(surface)
    const bell = document.createElement('button'); bell.textContent = 'Notifications'
    const dialog = document.createElement('section'); dialog.setAttribute('role', 'dialog'); dialog.tabIndex = -1
    const action = document.createElement('button'); action.textContent = 'Mark all read'; dialog.append(action)
    surface.append(bell, dialog); document.body.append(portal)
    const overlay = host === 'dialog' ? connectDialog(portal, surface, surface, recorder, true) : connectSheet(portal, surface, null, true, recorder)
    const observer = observeEscape(dialog)
    cleanup.push(() => overlay.dispose(), () => observer.dispose())
    return { calls, action, bell, observer }
  }
  const escape = target => { const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }); target.dispatchEvent(event); return event }

  for (const host of ['dialog', 'sheet']) {
    it(`${host}: Escape inside the notification dialog is marked handled and leaves the ${host} open`, () => {
      const { calls, action } = build(host)
      action.focus()
      expect([escape(action).defaultPrevented, calls]).toEqual([true, []])
    })
  }

  it('leaves other keys and an Escape from outside the notification dialog alone', () => {
    const { calls, action, bell } = build('dialog')
    const tab = new KeyboardEvent('keydown', { key: 'a', bubbles: true, cancelable: true })
    action.dispatchEvent(tab)
    bell.focus()
    expect([tab.defaultPrevented, escape(bell).defaultPrevented, calls]).toEqual([false, true, ['DismissFromJavaScriptAsync']])
  })

  it('stops observing once disposed', () => {
    const { calls, action, observer } = build('dialog')
    observer.dispose()
    expect([escape(action).defaultPrevented, calls]).toEqual([true, ['DismissFromJavaScriptAsync']])
  })
})
