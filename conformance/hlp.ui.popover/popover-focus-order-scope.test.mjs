// @vitest-environment jsdom
import { connect } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'

// T-1128, b31 review follow-up, Blazor lane, on the real popover.js. The content's focus navigation scope
// sits inside its trigger's scope (WHATWG HTML 6.6.2) at any depth; the content's own Tab stops are the
// browser's (a radio group is one stop, tabindex=-1 is not a stop, the focused element located by
// document position); and the trigger is found by its data-hl-popover-trigger marker, which a caller's
// own id or attributes cannot override.
const callback = { invokeMethodAsync: () => Promise.resolve() }
const press = (shiftKey = false) => {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
  ;(document.activeElement ?? document.body).dispatchEvent(event)
  const active = document.activeElement
  return [event.defaultPrevented ? 'handled' : 'native', !active || active === document.body ? 'BODY' : (active.getAttribute('aria-label') || active.textContent)]
}
const connections = []
// Renders `html` into <body>; each [triggerId, contentHtml] pair opens a popover on the button with that
// id, its content rendered right after the trigger as Blazor renders it, then moved by connect().
function build(html, ...popovers) {
  document.body.innerHTML = html
  for (const [id, contentHtml] of popovers) {
    const trigger = document.getElementById(id)
    trigger.dataset.hlPopoverTrigger = `key-${id}`
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: contentHtml })
    content.className = 'hl-popover__content'
    trigger.after(content)
    connections.push(connect(trigger, null, content, { side: 'bottom', align: 'center', sideOffset: 6, triggerId: `key-${id}` }, callback))
    vi.advanceTimersToNextFrame()
  }
}
const focus = selector => document.querySelector(selector).focus()

describe('Popover Blazor focus order: scope, Tab stops and trigger lookup (b31 review)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    globalThis.ResizeObserver ??= class { observe() {} disconnect() {} }
  })
  afterEach(() => { while (connections.length) connections.pop().dispose(); vi.useRealTimers(); document.body.replaceChildren() })

  it('nested: Tab from the inner content, its trigger last in the outer content, continues after the outer trigger', () => {
    build('<button>Before</button><button id=ta>Open A</button><button>After</button>',
      ['ta', '<button>X</button><button id=tb>Open B</button>'], ['tb', '<button id=y>Y</button>'])
    focus('#y')
    expect(press()).toEqual(['handled', 'After'])
    focus('#y')
    expect(press(true)).toEqual(['handled', 'Open B'])
  })

  it('nested inside a modal Dialog: Tab from the inner content goes to the control after the outer trigger', () => {
    document.body.innerHTML = '<button>Page</button>'
    const portal = Object.assign(document.createElement('div'), { className: 'hl-dialog__portal',
      innerHTML: '<section role=dialog aria-modal=true tabindex=-1><button aria-label=Close>x</button><div class=hl-dialog__body tabindex=0><button id=ta>Pick</button><button>Done</button></div></section>' })
    document.body.append(portal)
    connections.push(connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true))
    vi.advanceTimersToNextFrame()
    for (const [id, html] of [['ta', '<button>X</button><button id=tb>Open B</button>'], ['tb', '<button id=y>Y</button>']]) {
      const trigger = document.getElementById(id)
      trigger.dataset.hlPopoverTrigger = `key-${id}`
      const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: html, className: 'hl-popover__content' })
      trigger.after(content)
      connections.push(connect(trigger, null, content, { triggerId: `key-${id}` }, callback))
      vi.advanceTimersToNextFrame()
    }
    focus('#y')
    expect(press()).toEqual(['handled', 'Done'])
  })

  // The Dialog makes its siblings inert, which hid the scope rule; a portal appended after it is not.
  it('a non-inert portal after a modal Dialog does not take Tab from a trigger last in the Dialog: the trap wraps', () => {
    const portal = Object.assign(document.createElement('div'), { className: 'hl-dialog__portal',
      innerHTML: '<section role=dialog aria-modal=true tabindex=-1><button aria-label=Close>x</button><div class=hl-dialog__body tabindex=0><button id=ta>Pick</button></div></section>' })
    document.body.append(portal)
    connections.push(connectDialog(portal, portal.firstChild, portal.querySelector('.hl-dialog__body'), callback, true))
    vi.advanceTimersToNextFrame()
    const trigger = document.getElementById('ta')
    trigger.dataset.hlPopoverTrigger = 'key-ta'
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button id=o>Option</button>', className: 'hl-popover__content' })
    trigger.after(content)
    connections.push(connect(trigger, null, content, { triggerId: 'key-ta' }, callback))
    vi.advanceTimersToNextFrame()
    document.body.append(Object.assign(document.createElement('div'), { innerHTML: '<button>Toast</button>' }))
    focus('#o')
    expect(press()).toEqual(['handled', 'Close'])
  })

  it('Tab from a checked radio before the rest of its group, the group last, leaves the content', () => {
    build('<button>Before</button><button id=t>Open</button><button>After</button>',
      ['t', '<button>Apply</button><input type=radio name=g id=r1 aria-label=R1 checked><input type=radio name=g aria-label=R2>'])
    focus('#r1')
    expect(press()).toEqual(['handled', 'After'])
  })

  it('Shift+Tab from a checked radio after the rest of its group, the group first, leaves the content', () => {
    build('<button>Before</button><button id=t>Open</button><button>After</button>',
      ['t', '<input type=radio name=g aria-label=R1><input type=radio name=g id=r2 aria-label=R2 checked><button>Apply</button>'])
    focus('#r2')
    expect(press(true)).toEqual(['handled', 'Open'])
  })

  it('a radio group with nothing checked is one stop at its first radio: Tab from it to the next control stays native', () => {
    build('<button id=t>Open</button><button>After</button>',
      ['t', '<input type=radio name=g id=r1 aria-label=R1><input type=radio name=g aria-label=R2><button>Apply</button>'])
    focus('#r1')
    expect(press()[0]).toBe('native')
  })

  it('Tab from a focused tabindex=-1 note after the last control leaves the content', () => {
    build('<button>Before</button><button id=t>Open</button><button>After</button>',
      ['t', '<button>Apply</button><p id=note tabindex=-1 aria-label=Note>note</p>'])
    focus('#note')
    expect(press()).toEqual(['handled', 'After'])
  })

  it('Shift+Tab from a focused tabindex=-1 heading before the first control leaves the content', () => {
    build('<button>Before</button><button id=t>Open</button><button>After</button>',
      ['t', '<h2 id=h tabindex=-1 aria-label=Heading>H</h2><button>Apply</button>'])
    focus('#h')
    expect(press(true)).toEqual(['handled', 'Open'])
  })

  it('skips elements after the trigger that are not Tab stops: disabled, tabindex=-1, hidden, inside inert', () => {
    build('<button>Before</button><button id=t>Open</button><button disabled>Off</button><button tabindex=-1>Minus</button><button hidden>Hidden</button><div id=inert><button>Inert</button></div><button>After</button>',
      ['t', '<button id=a>Apply</button>'])
    document.getElementById('inert').inert = true
    focus('#a')
    expect(press()).toEqual(['handled', 'After'])
  })

  it('a key a widget inside the content already handled is left alone, even with a stop after the trigger', () => {
    build('<button id=t>Open</button><button>After</button>', ['t', '<button id=a>Apply</button>'])
    document.getElementById('a').addEventListener('keydown', event => event.preventDefault())
    focus('#a')
    expect(press()).toEqual(['handled', 'Apply'])
  })

  it('the content of another open popover after the trigger is not the trigger\'s next stop', () => {
    build('<button id=t>Open</button><button id=o>Other</button>', ['t', '<button id=a>Apply</button>'], ['o', '<button id=far>Far</button>'])
    focus('#far')
    expect(press()).toEqual(['native', 'Far'])
  })

  it('Shift+Tab out goes to the stop before a trigger that became disabled, rather than trapping', () => {
    build('<button>Before</button><button id=t>Open</button><button>After</button>', ['t', '<button id=a>Apply</button>'])
    document.getElementById('t').disabled = true
    focus('#a')
    expect(press(true)).toEqual(['handled', 'Before'])
  })

  it('with no stop before an unfocusable trigger, Shift+Tab is left to the browser', () => {
    build('<button id=t>Open</button><button>After</button>', ['t', '<button id=a>Apply</button>'])
    document.getElementById('t').hidden = true
    focus('#a')
    expect(press(true)).toEqual(['native', 'Apply'])
  })

  // HarborlinePopoverTrigger splats caller attributes after its own id, so a caller's id wins there; the
  // trigger is found by data-hl-popover-trigger, rendered after the splat.
  it('finds a trigger whose id the caller replaced, by its data-hl-popover-trigger marker', () => {
    document.body.innerHTML = '<button>Before</button><button id=caller-id data-hl-popover-trigger=hl-popover-1-trigger>Open</button><button>After</button>'
    const content = Object.assign(document.createElement('div'), { tabIndex: -1, innerHTML: '<button id=a>Apply</button>', className: 'hl-popover__content' })
    document.getElementById('caller-id').after(content)
    connections.push(connect(null, null, content, { triggerId: 'hl-popover-1-trigger' }, callback))
    vi.advanceTimersToNextFrame()
    focus('#a')
    expect(press(true)).toEqual(['handled', 'Open'])
  })
})
