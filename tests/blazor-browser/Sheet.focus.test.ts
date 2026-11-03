import { afterEach, expect, it, vi } from 'vitest'
import { connect } from '../../projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.js'

// Oracle: Sheet revision 1 restores the invoking trigger; for a controlled sheet without one,
// Dialog's existing adapter restores the element active before opening (APG dialog focus return).
afterEach(() => { vi.unstubAllGlobals(); document.body.replaceChildren(); document.body.style.overflow = '' })

it.each([null, undefined, {}])('closes a controlled sheet with unregistered trigger %j without throwing and restores its opener', trigger => {
  document.body.innerHTML = '<button id="opener">Open settings</button><div id="host"><div id="portal"><div id="content"><button>Close settings</button></div></div></div>'
  const opener = document.getElementById('opener')!
  opener.focus()
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { callback(0); return 1 })
  const connection = connect(document.getElementById('portal'), document.getElementById('content'), trigger, true, { invokeMethodAsync: vi.fn() })
  expect(document.querySelector('#content button')).toHaveFocus()
  expect(() => connection.dispose()).not.toThrow()
  expect(opener).toHaveFocus()
})

it('restores a registered trigger in preference to the previously focused element', () => {
  document.body.innerHTML = '<button id="opener">Previous</button><button id="trigger">Open settings</button><div><div id="portal"><div id="content"><button>Close settings</button></div></div></div>'
  document.getElementById('opener')!.focus()
  vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => { callback(0); return 1 })
  const connection = connect(document.getElementById('portal'), document.getElementById('content'), document.getElementById('trigger'), true, { invokeMethodAsync: vi.fn() })
  connection.dispose()
  expect(document.getElementById('trigger')).toHaveFocus()
})
