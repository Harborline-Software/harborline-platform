import { afterEach, expect, it, vi } from 'vitest'
import { connect } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'

// Oracle: HTML tabindex excludes negative values from sequential focus navigation;
// APG modal dialogs wrap Tab and Shift+Tab between their sequential stops.
afterEach(() => { vi.unstubAllGlobals(); document.body.replaceChildren(); document.body.style.overflow = '' })

it('wraps between sequential stops, excluding a negative-tabindex button', () => {
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} })
  vi.stubGlobal('requestAnimationFrame', () => 1)
  document.body.innerHTML = '<div id="portal"><div id="dialog" role="dialog"><button id="first">Close</button><div id="body"><button id="last">Sequential stop</button><button tabindex="-1">Programmatic only</button></div></div></div>'
  const connection = connect(document.getElementById('portal'), document.getElementById('dialog'), document.getElementById('body'), { invokeMethodAsync: vi.fn() }, true)
  try {
    const first = document.getElementById('first')!
    const last = document.getElementById('last')!
    first.focus()
    first.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, bubbles: true, cancelable: true }))
    expect(last).toHaveFocus()
    last.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true }))
    expect(first).toHaveFocus()
  } finally { connection.dispose() }
})
