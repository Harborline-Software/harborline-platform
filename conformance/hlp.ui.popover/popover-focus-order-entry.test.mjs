// @vitest-environment jsdom
import { connect } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'

// T-1128, b31 re-review, Blazor lane, on the real popover.js. The way into the content mirrors the way
// out (WHATWG HTML 6.6.2, the content's scope right after its trigger): while open, Tab on the trigger
// goes to the content's first Tab stop (or the content element when it has none), and Shift+Tab from the
// next stop after the trigger in the trigger's own scope goes to the content's last stop. Disposed
// (closed), nothing is handled. Also the radio-group rules the earlier tests did not pin down.
const callback = { invokeMethodAsync: () => Promise.resolve() }
const press = (shiftKey = false) => {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
  ;(document.activeElement ?? document.body).dispatchEvent(event)
  const active = document.activeElement
  return [event.defaultPrevented ? 'handled' : 'native', !active || active === document.body ? 'BODY' : (active.getAttribute('aria-label') || active.textContent)]
}
const connections = []
function build(html, id, contentHtml) {
  document.body.innerHTML = html
  const trigger = document.getElementById(id)
  trigger.dataset.hlPopoverTrigger = `key-${id}`
  const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: contentHtml, className: 'hl-popover__content' })
  content.setAttribute('aria-label', 'Details')
  trigger.after(content)
  const connection = connect(trigger, null, content, { triggerId: `key-${id}` }, callback)
  connections.push(connection)
  vi.advanceTimersToNextFrame()
  return connection
}
const focus = selector => document.querySelector(selector).focus()

describe('Popover Blazor focus order: the way into the content (b31 re-review)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    globalThis.ResizeObserver ??= class { observe() {} disconnect() {} }
  })
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })

  const page = '<button>Before</button><button id=t>Open</button><button id=after>After</button><button id=further>Further</button>'

  it('Tab on the trigger while open goes to the content\'s first stop', () => {
    build(page, 't', '<button>Apply</button><button>Second</button>')
    focus('#t')
    expect(press()).toEqual(['handled', 'Apply'])
  })

  it('with no stop in the content, Tab on the trigger goes to the content element', () => {
    build(page, 't', '<p>Read only</p>')
    focus('#t')
    expect(press()).toEqual(['handled', 'Details'])
  })

  it('Shift+Tab out to the trigger, then Tab, re-enters the content', () => {
    build(page, 't', '<button id=a>Apply</button>')
    focus('#a')
    expect(press(true)).toEqual(['handled', 'Open'])
    expect(press()).toEqual(['handled', 'Apply'])
  })

  it('Shift+Tab from the next stop after the trigger goes to the content\'s last stop', () => {
    build(page, 't', '<button>Apply</button><button>Last</button>')
    focus('#after')
    expect(press(true)).toEqual(['handled', 'Last'])
  })

  it('Shift+Tab from a later stop than the next one stays native', () => {
    build(page, 't', '<button>Apply</button>')
    focus('#further')
    expect(press(true)).toEqual(['native', 'Further'])
  })

  it('a Shift+Tab the next stop already handled is left alone', () => {
    build(page, 't', '<button>Apply</button>')
    document.getElementById('after').addEventListener('keydown', event => event.preventDefault())
    focus('#after')
    expect(press(true)).toEqual(['handled', 'After'])
  })

  it('disposed (closed), Tab on the trigger and Shift+Tab after it stay native', () => {
    build(page, 't', '<button>Apply</button>')
    connections.pop().dispose()
    focus('#t')
    expect(press()).toEqual(['native', 'Open'])
    focus('#after')
    expect(press(true)).toEqual(['native', 'After'])
  })

  // The trigger carries aria-expanded=true while open, as HarborlinePopoverTrigger renders it.
  it('inside a modal Dialog: Tab on a trigger last in it enters the content; Shift+Tab from the control after a trigger enters at the last stop', () => {
    for (const done of [false, true]) {
      document.body.replaceChildren()
      const portal = Object.assign(document.createElement('div'), { className: 'hl-dialog__portal',
        innerHTML: `<section role=dialog aria-modal=true tabindex=-1><button aria-label=Close>x</button><div class=hl-dialog__body tabindex=0><button id=t aria-expanded=true>Pick</button>${done ? '<button id=done>Done</button>' : ''}</div></section>` })
      document.body.append(portal)
      const dialog = connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true)
      vi.advanceTimersToNextFrame()
      const trigger = document.getElementById('t')
      trigger.dataset.hlPopoverTrigger = 'key-t'
      const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button>Option</button>', className: 'hl-popover__content' })
      trigger.after(content)
      const connection = connect(trigger, null, content, { triggerId: 'key-t' }, callback)
      vi.advanceTimersToNextFrame()
      if (done) { focus('#done'); expect(press(true)).toEqual(['handled', 'Option']) }
      else { focus('#t'); expect(press()).toEqual(['handled', 'Option']) }
      connection.dispose(); dialog.dispose()
    }
  })
})

describe('Popover Blazor focus order: radio groups as one stop (b31 re-review test gap)', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] }))
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })

  it('the next stop after the trigger is a radio group\'s checked radio, not its first', () => {
    build('<button id=t>Open</button><input type=radio name=page aria-label=P1><input type=radio name=page aria-label=P2 checked>', 't', '<button id=a>Apply</button>')
    focus('#a')
    expect(press()).toEqual(['handled', 'P2'])
  })

  it('radios with one name in different forms are different groups: Tab between them stays native', () => {
    build('<button id=t>Open</button><button>After</button>', 't', '<form><input type=radio name=g id=r1 aria-label=R1 checked></form><form><input type=radio name=g aria-label=R2 checked></form>')
    focus('#r1')
    expect(press()[0]).toBe('native')
  })
})

// b31 final check: a caller's own handler on the trigger runs first and can take Tab, whether bound on the
// trigger or delegated from the document as Blazor's event handlers are; the popover's entry runs after
// them (on window, after Blazor's dispatch), finds the trigger by its marker when the key arrives, and
// still beats a modal Dialog's trap, measured with the real dialog.js and sheet.js.
import { connect as connectSheet } from '../../projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.js'

describe('Popover Blazor focus order: a caller\'s trigger handler runs first (b31 final check)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    globalThis.ResizeObserver ??= class { observe() {} disconnect() {} }
  })
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })
  const page = '<button>Before</button><button id=t>Open</button><button id=cu>Custom</button>'
  const takeTab = event => { if (event.key === 'Tab' && !event.defaultPrevented) { event.preventDefault(); document.getElementById('cu').focus() } }

  it('a handler bound on the trigger after open that takes Tab wins', () => {
    build(page, 't', '<button>Apply</button>')
    document.getElementById('t').addEventListener('keydown', takeTab)
    focus('#t')
    expect(press()).toEqual(['handled', 'Custom'])
  })

  it('a handler delegated from the document (as Blazor dispatches @onkeydown) that takes Tab wins', () => {
    build(page, 't', '<button>Apply</button>')
    const delegated = event => { if (event.target.id === 't') takeTab(event) }
    document.addEventListener('keydown', delegated)
    try {
      focus('#t')
      expect(press()).toEqual(['handled', 'Custom'])
    } finally { document.removeEventListener('keydown', delegated) }
  })

  it('a caller handler that leaves Tab alone keeps the popover\'s entry', () => {
    build(page, 't', '<button>Apply</button>')
    const seen = []
    document.getElementById('t').addEventListener('keydown', event => seen.push(event.key))
    focus('#t')
    expect([...press(), seen.join()]).toEqual(['handled', 'Apply', 'Tab'])
  })

  it('a trigger replaced while open is found by its marker: Tab on the new element enters the content', () => {
    build(page, 't', '<button>Apply</button>')
    const old = document.getElementById('t')
    old.replaceWith(old.cloneNode(true))
    focus('#t')
    expect(press()).toEqual(['handled', 'Apply'])
  })

  it('the swap repro inside a modal Dialog (Close | Pick, Pick replaced while open): Tab enters the content, not the trap', () => {
    const portal = Object.assign(document.createElement('div'), { className: 'hl-dialog__portal',
      innerHTML: '<section role=dialog aria-modal=true tabindex=-1><button aria-label=Close>x</button><div class=hl-dialog__body><button id=t aria-expanded=true>Pick</button></div></section>' })
    document.body.append(portal)
    const dialog = connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true)
    vi.advanceTimersToNextFrame()
    const trigger = document.getElementById('t')
    trigger.dataset.hlPopoverTrigger = 'key-t'
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button>Option</button>', className: 'hl-popover__content' })
    trigger.after(content)
    connections.push(connect(trigger, null, content, { triggerId: 'key-t' }, callback))
    vi.advanceTimersToNextFrame()
    trigger.replaceWith(trigger.cloneNode(true))
    focus('#t')
    try { expect(press()).toEqual(['handled', 'Option']) } finally { dialog.dispose() }
  })

  it('inside a modal Dialog, Shift+Tab on an open trigger that is the Dialog\'s first stop is still the trap\'s', () => {
    const portal = Object.assign(document.createElement('div'), { className: 'hl-dialog__portal',
      innerHTML: '<section role=dialog aria-modal=true tabindex=-1><div class=hl-dialog__body><button id=t aria-expanded=true>Pick</button><button>Done</button></div></section>' })
    document.body.append(portal)
    const dialog = connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true)
    vi.advanceTimersToNextFrame()
    const trigger = document.getElementById('t')
    trigger.dataset.hlPopoverTrigger = 'key-t'
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button>Option</button>', className: 'hl-popover__content' })
    trigger.after(content)
    connections.push(connect(trigger, null, content, { triggerId: 'key-t' }, callback))
    vi.advanceTimersToNextFrame()
    focus('#t')
    try { expect(press(true)).toEqual(['handled', 'Done']) } finally { dialog.dispose() }
  })

  it('inside a modal Sheet (real sheet.js), Tab on the open trigger enters the content', () => {
    const portal = Object.assign(document.createElement('div'), { className: 'hl-sheet__portal',
      innerHTML: '<div role=dialog aria-modal=true tabindex=-1 id=sc><button id=t aria-expanded=true>Pick</button><button aria-label="Close panel">x</button></div>' })
    document.body.append(portal)
    const sheet = connectSheet(portal, portal.querySelector('#sc'), null, true, callback)
    vi.advanceTimersToNextFrame()
    const trigger = document.getElementById('t')
    trigger.dataset.hlPopoverTrigger = 'key-t'
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button>Option</button>', className: 'hl-popover__content' })
    trigger.after(content)
    connections.push(connect(trigger, null, content, { triggerId: 'key-t' }, callback))
    vi.advanceTimersToNextFrame()
    focus('#t')
    try { expect(press()).toEqual(['handled', 'Option']) } finally { sheet.dispose() }
  })
})

describe('Popover Blazor focus order: the way in, nested (b31 final check)', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] }))
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })
  function nested() {
    document.body.innerHTML = '<button>Before</button><button id=ta>Open A</button><button id=after>After</button>'
    for (const [id, html] of [['ta', '<button>X</button><button id=tb>Open B</button>'], ['tb', '<button id=y>Y</button>']]) {
      const trigger = document.getElementById(id)
      trigger.dataset.hlPopoverTrigger = `key-${id}`
      const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: html, className: 'hl-popover__content' })
      trigger.after(content)
      connections.push(connect(trigger, null, content, { triggerId: `key-${id}` }, callback))
      vi.advanceTimersToNextFrame()
    }
  }

  it('Tab on an open inner trigger that is the outer content\'s last stop enters the inner content', () => {
    nested()
    focus('#tb')
    expect(press()).toEqual(['handled', 'Y'])
  })

  it('Shift+Tab from the stop after the outer trigger walks into the open inner content', () => {
    nested()
    focus('#after')
    expect(press(true)).toEqual(['handled', 'Y'])
  })
})

// b31 final check 2: the Dialog's and Sheet's traps hand forward Tab on a trigger to the popover only while
// popover.js has that popover open (a marker connect() sets on the connected content and every dispose
// removes), never on rendered ARIA alone. Measured with the real dialog.js and sheet.js, trigger last.
describe('Popover Blazor focus order: the overlay traps defer only to an open popover (b31 final check 2)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    globalThis.ResizeObserver ??= class { observe() {} disconnect() {} }
  })
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })

  // A Dialog or Sheet whose last control is `lastHtml`; returns its dispose.
  function overlay(kind, lastHtml) {
    const portal = document.createElement('div')
    if (kind === 'dialog') {
      portal.className = 'hl-dialog__portal'
      portal.innerHTML = `<section role=dialog aria-modal=true tabindex=-1><button aria-label=Close>x</button><div class=hl-dialog__body>${lastHtml}</div></section>`
      document.body.append(portal)
      const connection = connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true)
      vi.advanceTimersToNextFrame()
      return () => connection.dispose()
    }
    portal.className = 'hl-sheet__portal'
    portal.innerHTML = `<div role=dialog aria-modal=true tabindex=-1 id=sc><button aria-label=Close>x</button>${lastHtml}</div>`
    document.body.append(portal)
    const connection = connectSheet(portal, portal.querySelector('#sc'), null, true, callback)
    vi.advanceTimersToNextFrame()
    return () => connection.dispose()
  }
  function openPopover() {
    const trigger = document.getElementById('t')
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button>Option</button>', className: 'hl-popover__content' })
    trigger.after(content)
    const connection = connect(trigger, null, content, { triggerId: 'key-t' }, callback)
    vi.advanceTimersToNextFrame()
    return connection
  }
  const pick = '<button id=t data-hl-popover-trigger=key-t aria-expanded=true>Pick</button>'

  for (const kind of ['dialog', 'sheet']) {
    it(`${kind}: Tab on an open popover trigger last in it enters the content`, () => {
      const close = overlay(kind, pick)
      connections.push(openPopover())
      focus('#t')
      try { expect(press()).toEqual(['handled', 'Option']) } finally { close() }
    })

    it(`${kind}: a trigger with a stale aria-expanded and no connected content wraps (the trap keeps it)`, () => {
      const close = overlay(kind, pick)
      focus('#t')
      try { expect(press()).toEqual(['handled', 'Close']) } finally { close() }
    })

    it(`${kind}: a popover disposed while its trigger keeps aria-expanded=true wraps`, () => {
      const close = overlay(kind, pick)
      openPopover().dispose()
      // Left behind and still connected (a failed close), outside the overlay: only the marker says closed.
      document.body.append(document.querySelector('.hl-popover__content'))
      focus('#t')
      try { expect(press()).toEqual(['handled', 'Close']) } finally { close() }
    })

    for (const [name, html] of [
      ['an expanded disclosure', '<button id=t aria-expanded=true aria-controls=panel>More</button><div id=panel></div>'],
      ['an accordion header', '<h3><button id=t aria-expanded=true>Section</button></h3>'],
      ['a combobox', '<input id=t role=combobox aria-expanded=true aria-label=Search>'],
    ]) {
      it(`${kind}: ${name} last in it still wraps`, () => {
        const close = overlay(kind, html)
        focus('#t')
        try { expect(press()).toEqual(['handled', 'Close']) } finally { close() }
      })
    }
  }
})
