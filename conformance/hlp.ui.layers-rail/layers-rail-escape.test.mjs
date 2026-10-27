// @vitest-environment jsdom
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'
import { connect as connectSheet } from '../../projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.js'
import { observeLensKeys } from '../../projections/blazor/ui/hlp.ui.layers-rail/wwwroot/layers-rail.js'

// The Blazor lane's Escape layering, run with the real dialog.js and sheet.js because bUnit cannot
// execute JavaScript. The lens group clears an active lens on Escape through its Razor @onkeydown,
// which cannot mark the key handled; layers-rail.js marks it, and only while a lens is active, so an
// enclosing Dialog or Sheet stays open then and closes on an Escape the rail has no use for.
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} }

describe('LayersRail Blazor Escape inside a Dialog or Sheet', () => {
  let cleanup = []
  afterEach(() => { cleanup.reverse().forEach(run => run()); cleanup = []; document.body.replaceChildren() })

  function build(host, active) {
    const calls = []
    const recorder = { invokeMethodAsync: method => { calls.push(method); return Promise.resolve() } }
    const portal = document.createElement('div'); portal.className = host === 'dialog' ? 'hl-dialog__portal' : 'hl-sheet__portal'
    const surface = document.createElement('div'); surface.tabIndex = -1; portal.append(surface)
    const group = document.createElement('div'); group.setAttribute('role', 'group'); group.className = 'hl-layers-rail__lenses'
    const lens = document.createElement('button'); lens.className = 'hl-layers-rail__lens-activate'; lens.setAttribute('aria-pressed', String(active)); lens.textContent = 'Layout'
    group.append(lens); surface.append(group); document.body.append(portal)
    const overlay = host === 'dialog' ? connectDialog(portal, surface, surface, recorder, true) : connectSheet(portal, surface, null, true, recorder)
    const observer = observeLensKeys(group)
    cleanup.push(() => overlay.dispose(), () => observer.dispose())
    return { calls, lens, observer }
  }
  const escape = target => { const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }); target.dispatchEvent(event); return event }

  for (const host of ['dialog', 'sheet']) {
    it(`${host}: Escape with an active lens is marked handled and leaves the ${host} open`, () => {
      const { calls, lens } = build(host, true)
      lens.focus()
      expect([escape(lens).defaultPrevented, calls]).toEqual([true, []])
    })

    it(`${host}: Escape with no active lens is left to the ${host}`, () => {
      const { calls, lens } = build(host, false)
      lens.focus()
      expect(calls.length === 0 && escape(lens) && calls).toEqual(['DismissFromJavaScriptAsync'])
    })
  }

  it('stops observing once disposed', () => {
    const { calls, lens, observer } = build('dialog', true)
    observer.dispose()
    escape(lens)
    expect(calls).toEqual(['DismissFromJavaScriptAsync'])
  })
})
