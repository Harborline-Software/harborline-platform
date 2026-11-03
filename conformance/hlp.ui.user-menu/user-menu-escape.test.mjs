// @vitest-environment jsdom
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'
import { connect as connectSheet } from '../../projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.js'
import { disposeKeys, observeKeys } from '../../projections/blazor/ui/hlp.ui.user-menu/wwwroot/user-menu.js'

// The Blazor lane's Escape layering, run with the real dialog.js and sheet.js because bUnit cannot
// execute JavaScript. Both overlays listen on the document and leave a handled Escape (default
// prevented) alone, so the menu must mark the Escape it uses before the key reaches the document.
// A stand-in for Blazor's own document listener, registered first as Blazor's is, calls
// preventDefault for an element carrying @onkeydown:preventDefault (here data-blazor-prevent).
globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} }
const blazor = event => { if (event.target instanceof Element && event.target.closest('[data-blazor-prevent]')) event.preventDefault() }

describe('UserMenu Blazor Escape inside a Dialog or Sheet', () => {
  let cleanup = []
  beforeAll(() => document.addEventListener('keydown', blazor))
  afterAll(() => document.removeEventListener('keydown', blazor))
  afterEach(() => { cleanup.reverse().forEach(run => run()); cleanup = []; document.body.replaceChildren() })

  function build(host, { pattern = true, inner = null } = {}) {
    const calls = []
    const recorder = name => ({ invokeMethodAsync: method => { calls.push(`${name}.${method}`); return Promise.resolve() } })
    const portal = document.createElement('div'); portal.className = host === 'dialog' ? 'hl-dialog__portal' : 'hl-sheet__portal'
    const surface = document.createElement('div'); surface.tabIndex = -1; portal.append(surface)
    const trigger = document.createElement('button'); trigger.textContent = 'Account'
    const panel = document.createElement('div'); panel.setAttribute('role', pattern ? 'menu' : 'dialog')
    const item = document.createElement('button'); item.setAttribute('role', 'menuitem'); item.textContent = 'Profile'; panel.append(item)
    if (inner) panel.append(inner)
    surface.append(trigger, panel); document.body.append(portal)
    const overlay = host === 'dialog' ? connectDialog(portal, surface, surface, recorder('Overlay'), true) : connectSheet(portal, surface, null, true, recorder('Overlay'))
    const id = observeKeys(panel, pattern, recorder('UserMenu'), trigger)
    cleanup.push(() => overlay.dispose(), () => disposeKeys(id))
    return { calls, item, trigger }
  }
  const escape = target => { const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }); target.dispatchEvent(event); return event }

  for (const host of ['dialog', 'sheet']) {
    it(`${host}: Escape on a menu item closes only the menu and is marked handled`, () => {
      const { calls, item } = build(host)
      item.focus()
      const event = escape(item)
      expect([calls, event.defaultPrevented]).toEqual([['UserMenu.OnEscapeKey'], true])
    })

    it(`${host}: an Escape a native control in custom content handled closes neither`, () => {
      const input = document.createElement('input')
      input.addEventListener('keydown', event => { if (event.key === 'Escape') event.preventDefault() })
      const { calls } = build(host, { pattern: false, inner: input })
      input.focus()
      expect([calls, escape(input).defaultPrevented]).toEqual([[], true])
    })

    it(`${host}: an Escape a Blazor handler in custom content prevented closes neither`, () => {
      const input = document.createElement('input'); input.dataset.blazorPrevent = ''
      const { calls } = build(host, { pattern: false, inner: input })
      input.focus()
      expect([calls, escape(input).defaultPrevented]).toEqual([[], true])
    })
  }

  it('leaves an Escape from outside the panel to the overlay', () => {
    const { calls, trigger } = build('dialog')
    trigger.focus()
    expect([calls, escape(trigger).defaultPrevented]).toEqual([['Overlay.DismissFromJavaScriptAsync'], true])
  })
})
