// @vitest-environment jsdom
import { connect } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'
import { connect as connectDialog } from '../../projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.js'

// T-1128, Blazor lane. connect() moves the content to the end of <body>, so native Tab order put it
// after the whole page: Tab out went to the end of the page and Shift+Tab to the last control, with
// the popover still open. As in the HTML popover focus navigation model (WHATWG HTML 6.6.2, a showing
// popover's focus navigation scope is inserted after its invoker), the content sits right after its
// trigger in sequential focus order. Run here, on the real popover.js, because bUnit cannot execute
// the module's JavaScript; jsdom has no native Tab, so each key is dispatched and its handling read.
const callback = { invokeMethodAsync: () => Promise.resolve() }
const button = label => Object.assign(document.createElement('button'), { type: 'button', textContent: label })
const tab = (shiftKey = false) => {
  const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey, bubbles: true, cancelable: true })
  ;(document.activeElement ?? document.body).dispatchEvent(event)
  return event.defaultPrevented ? 'handled' : 'native'
}
const focused = () => document.activeElement === document.body ? 'BODY' : (document.activeElement.getAttribute('aria-label') || document.activeElement.textContent)

// Before / trigger / [After] in the page; the content is rendered after the trigger, as Blazor does.
function page({ after = true, children = [button('Apply')] } = {}) {
  const trigger = button('Open')
  trigger.id = 'hl-popover-t-trigger'
  trigger.dataset.hlPopoverTrigger = trigger.id
  const content = Object.assign(document.createElement('div'), { id: 'hl-popover-t-content', tabIndex: -1 })
  content.className = 'hl-popover__content'
  content.setAttribute('role', 'dialog')
  content.setAttribute('aria-label', 'Details')
  content.append(...children)
  const root = document.createElement('main')
  root.append(button('Before'), trigger, content, ...(after ? [button('After')] : []))
  document.body.append(root)
  const connection = connect(trigger, null, content, { side: 'bottom', align: 'center', sideOffset: 6, triggerId: trigger.id }, callback)
  vi.advanceTimersToNextFrame()
  return { trigger, content, connection }
}

describe('Popover Blazor connect() sequential focus order (T-1128)', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] }))
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  it('Tab from the last control in the content moves to the element after the trigger', () => {
    const { content, connection } = page()
    expect(focused()).toBe('Apply')
    expect([tab(), focused(), content.isConnected]).toEqual(['handled', 'After', true])
    connection.dispose()
  })

  it('Shift+Tab from the first control in the content moves to the trigger', () => {
    const { connection } = page()
    expect([tab(true), focused()]).toEqual(['handled', 'Open'])
    connection.dispose()
  })

  it('Tab and Shift+Tab between controls inside the content stay native', () => {
    const { content, connection } = page({ children: [button('First'), button('Second')] })
    expect([focused(), tab()]).toEqual(['First', 'native'])
    content.querySelectorAll('button')[1].focus()
    expect([tab(true), focused()]).toEqual(['native', 'Second'])
    connection.dispose()
  })

  it('with nothing tabbable in the content, Tab from the content element goes after the trigger and Shift+Tab to it', () => {
    const paragraph = Object.assign(document.createElement('p'), { textContent: 'Read only' })
    const { content, connection } = page({ children: [paragraph] })
    expect(document.activeElement).toBe(content)
    expect([tab(), focused()]).toEqual(['handled', 'After'])
    content.focus()
    expect([tab(true), focused()]).toEqual(['handled', 'Open'])
    connection.dispose()
  })

  // The content is already last in <body>, so the browser's own Tab from it goes past the end of the page,
  // where native Tab from the trigger goes. Moving focus to the trigger first would re-enter the content.
  it('when the trigger is last on the page, Tab out leaves the key to the browser with focus in place', () => {
    const { connection } = page({ after: false })
    expect([tab(), focused()]).toEqual(['native', 'Apply'])
    connection.dispose()
  })

  // Blazor Server renders over a connection: content can arrive after connect(), and the trigger
  // reference can be stale or missing when the content connects. Nothing is captured at connect time.
  it('reads the content live: a control rendered after connect() is the new last one', () => {
    const { content, connection } = page()
    content.append(button('Later'))
    content.querySelector('button').focus()
    expect([tab(), focused()]).toEqual(['native', 'Apply'])
    content.querySelectorAll('button')[1].focus()
    expect([tab(), focused()]).toEqual(['handled', 'After'])
    connection.dispose()
  })

  it('resolves the trigger by its data-hl-popover-trigger marker when the reference it was handed is missing or replaced', () => {
    const trigger = button('Open')
    trigger.id = 'hl-popover-late-trigger'
    trigger.dataset.hlPopoverTrigger = trigger.id
    const content = Object.assign(document.createElement('div'), { tabIndex: -1 })
    content.className = 'hl-popover__content'
    content.append(button('Apply'))
    const root = document.createElement('main')
    root.append(button('Before'), trigger, content, button('After'))
    document.body.append(root)
    const connection = connect(null, null, content, { side: 'bottom', align: 'center', sideOffset: 6, triggerId: trigger.id }, callback)
    vi.advanceTimersToNextFrame()
    expect([tab(true), focused()]).toEqual(['handled', 'Open'])
    const rerendered = button('Open again')
    rerendered.id = trigger.id
    rerendered.dataset.hlPopoverTrigger = trigger.id
    trigger.replaceWith(rerendered)
    content.querySelector('button').focus()
    expect([tab(true), focused()]).toEqual(['handled', 'Open again'])
    connection.dispose()
  })

  it('stops handling keys once disposed', () => {
    const { content, connection } = page()
    connection.dispose()
    document.body.append(content)
    content.querySelector('button').focus()
    expect([tab(), focused()]).toEqual(['native', 'Apply'])
  })
})

// Inside a real modal Dialog (the real dialog.js): the content sits after the Dialog portal in <body>.
describe('Popover Blazor focus order inside a modal Dialog (T-1128)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    globalThis.ResizeObserver ??= class { observe() {} disconnect() {} }
  })
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  function inDialog(done) {
    document.body.append(button('Page before'))
    const portal = document.createElement('div')
    portal.className = 'hl-dialog__portal'
    const dialog = document.createElement('section')
    Object.assign(dialog, { tabIndex: -1 })
    dialog.setAttribute('role', 'dialog')
    dialog.setAttribute('aria-modal', 'true')
    const close = button('')
    close.setAttribute('aria-label', 'Close')
    const body = Object.assign(document.createElement('div'), { tabIndex: 0 })
    body.className = 'hl-dialog__body'
    const trigger = button('Pick')
    trigger.id = 'hl-popover-d-trigger'
    trigger.dataset.hlPopoverTrigger = trigger.id
    const content = Object.assign(document.createElement('div'), { tabIndex: -1 })
    content.className = 'hl-popover__content'
    content.append(button('Option'))
    body.append(trigger, content, ...(done ? [button('Done')] : []))
    dialog.append(close, body)
    portal.append(dialog)
    document.body.append(portal, button('Page after'))
    const dialogConnection = connectDialog(portal, dialog, body, callback, true)
    vi.advanceTimersToNextFrame()
    const connection = connect(trigger, null, content, { side: 'bottom', align: 'center', sideOffset: 6, triggerId: trigger.id }, callback)
    vi.advanceTimersToNextFrame()
    expect(focused()).toBe('Option')
    return () => { connection.dispose(); dialogConnection.dispose() }
  }

  it('Tab out of the content goes to the control after the trigger inside the Dialog', () => {
    const dispose = inDialog(true)
    expect([tab(), focused()]).toEqual(['handled', 'Done'])
    dispose()
  })

  it('Shift+Tab out of the content goes to the trigger', () => {
    const dispose = inDialog(true)
    expect([tab(true), focused()]).toEqual(['handled', 'Pick'])
    dispose()
  })

  it('the Dialog trap still wins at its boundary: from a trigger last in the Dialog, Tab out wraps to its first control', () => {
    const dispose = inDialog(false)
    expect([tab(), focused()]).toEqual(['handled', 'Close'])
    dispose()
  })
})
