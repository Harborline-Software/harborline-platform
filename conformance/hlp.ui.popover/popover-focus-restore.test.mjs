// @vitest-environment jsdom
import { connect } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'

// G5 (R-0125, owner ruling 2026-10-27), Blazor lane. dispose() used to focus the trigger on every close.
// Over a Server circuit it runs one round trip after an outside pointer press, after the browser has
// already focused the pressed field and the user has started typing, so the trigger took focus away.
// As in WHATWG HTML's hide popover algorithm (light dismiss passes focusPreviousElement false; other
// hides restore only when focus is inside the popover, without scrolling): an outside pointer dismissal
// never moves focus, and every other close restores the trigger with preventScroll only when focus is
// inside the closing content or was lost with it to the body. Run here, on the real popover.js, because
// bUnit cannot execute the module's JavaScript. Each test replays Blazor's order: the dismissal request,
// what the browser or user does meanwhile, the render that removes the content, then dispose().
const callback = { invokeMethodAsync: () => Promise.resolve() }
const button = label => Object.assign(document.createElement('button'), { type: 'button', textContent: label })
const focused = () => document.activeElement === document.body ? 'BODY' : (document.activeElement.getAttribute('aria-label') || document.activeElement.textContent || document.activeElement.tagName)
const pointerDown = target => target.dispatchEvent(new (globalThis.PointerEvent ?? MouseEvent)('pointerdown', { bubbles: true, cancelable: true }))
const escape = target => target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }))

function page({ invoke } = {}) {
  const trigger = button('Open')
  trigger.id = 'hl-popover-r-trigger'
  const content = Object.assign(document.createElement('div'), { id: 'hl-popover-r-content', tabIndex: -1 })
  content.className = 'hl-popover__content'
  content.setAttribute('role', 'dialog')
  content.setAttribute('aria-label', 'Details')
  content.append(button('Apply'))
  const input = Object.assign(document.createElement('input'), { type: 'text' })
  input.setAttribute('aria-label', 'Outside field')
  const plain = Object.assign(document.createElement('div'), { textContent: 'Plain text' })
  const root = document.createElement('main')
  root.append(button('Before'), trigger, content, input, plain)
  document.body.append(root)
  const dismissals = []
  const connection = connect(trigger, null, content, { side: 'bottom', align: 'center', sideOffset: 6, triggerId: trigger.id },
    { invokeMethodAsync: name => { dismissals.push(name); return (invoke ?? callback.invokeMethodAsync)(name) } })
  vi.advanceTimersToNextFrame()
  const triggerFocus = vi.spyOn(trigger, 'focus')
  return { trigger, content, input, plain, before: root.firstChild, connection, dismissals, triggerFocus }
}

describe('Popover Blazor dispose() focus restoration (G5)', () => {
  beforeEach(() => vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] }))
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  it('an outside pointer press on a field leaves focus in that field when the close arrives later', () => {
    const { content, input, connection, dismissals, triggerFocus } = page()
    expect(focused()).toBe('Apply')
    pointerDown(input)
    expect(dismissals).toEqual(['DismissFromJavaScriptAsync'])
    input.focus() // the browser's mousedown default, before the server's render arrives
    input.value = 'abc' // the user types at once
    content.remove() // the closing render
    connection.dispose()
    expect([focused(), input.value]).toEqual(['Outside field', 'abc'])
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an outside pointer press on a non-focusable node leaves focus on the body, never on the trigger', () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur() // the browser's default for a non-focusable target
    content.remove()
    connection.dispose()
    expect(focused()).toBe('BODY')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an outside press on a non-focusable node also leaves focus alone when the content was not removed first', () => {
    const { plain, connection, triggerFocus } = page()
    pointerDown(plain)
    connection.dispose()
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('Escape from inside the content restores the trigger without scrolling once the content is removed', () => {
    const { content, connection, dismissals, triggerFocus } = page()
    escape(document.activeElement)
    expect(dismissals).toEqual(['DismissFromJavaScriptAsync'])
    content.remove()
    expect(focused()).toBe('BODY') // lost with the content
    connection.dispose()
    expect(focused()).toBe('Open')
    expect(triggerFocus).toHaveBeenCalledOnce()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('a close while focus is still inside the connected content (component disposal) restores the trigger without scrolling', () => {
    const { connection, triggerFocus } = page()
    expect(focused()).toBe('Apply')
    connection.dispose()
    expect(focused()).toBe('Open')
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('Escape, then focus moved elsewhere by keyboard before the delayed render, leaves focus where the user put it', () => {
    const { content, before, connection, triggerFocus } = page()
    escape(document.activeElement)
    before.focus()
    content.remove()
    connection.dispose()
    expect(focused()).toBe('Before')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a host-driven close while focus is outside the content does not move focus', () => {
    const { content, input, connection, triggerFocus } = page()
    input.focus()
    content.remove()
    connection.dispose()
    expect(focused()).toBe('Outside field')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  // A controlled host may refuse the dismissal: the request completes and the content is still there. The
  // outside press did not close the popover, so a later host-driven close follows the ordinary rule. Over a
  // Server circuit the refusal is only known when the request completes, a round trip after the press.
  const settle = async () => { for (let i = 0; i < 3; i++) await Promise.resolve() }

  it('an outside press the host refuses, then focus back in the content, then a host close: the trigger is restored', async () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    await settle() // the request completes; the host kept it open
    expect(content.isConnected).toBe(true)
    content.querySelector('button').focus()
    content.remove() // the host's own close, later
    connection.dispose()
    expect(focused()).toBe('Open')
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('an outside press the host refuses, focus left on the outside field, then a host close: focus stays on the field', async () => {
    const { content, input, connection, triggerFocus } = page()
    pointerDown(input)
    input.focus()
    await settle()
    content.remove()
    connection.dispose()
    expect(focused()).toBe('Outside field')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an accepted outside press whose closing render arrives before the request completes still leaves focus alone', async () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    content.remove() // Blazor Server sends the render batch before the call's completion
    await settle()
    connection.dispose()
    expect(focused()).toBe('BODY')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  // b32 review: refusal is not inferred from timing. The flag set by an outside press stays until focus comes
  // back into the content, a press lands in the content or on the trigger, or an Escape close starts.
  it('two presses in one round trip, the first refused and the second accepted on a non-focusable node: the trigger is not focused', async () => {
    const pending = []
    const { content, plain, connection, triggerFocus } = page({ invoke: () => new Promise(resolve => pending.push(resolve)) })
    pointerDown(plain) // press 1: the host will refuse it
    document.activeElement.blur()
    pointerDown(plain) // press 2, before press 1 completes: the host accepts it
    pending[0]() // press 1's completion arrives; the content is still there
    await settle()
    content.remove() // press 2's closing render
    pending[1]()
    await settle()
    connection.dispose()
    expect(focused()).toBe('BODY')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('an accepted press whose completion arrives before the closing render (render backpressure) leaves focus alone', async () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    await settle() // the completion first
    content.remove() // the closing render after it
    connection.dispose()
    expect(focused()).toBe('BODY')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a refused press on a non-focusable node, focus left on the body, then a host close: focus stays on the body (ruling point 1)', async () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    await settle()
    content.remove()
    connection.dispose()
    expect(focused()).toBe('BODY')
    expect(triggerFocus).not.toHaveBeenCalled()
  })

  it('a refused press, then a press inside the content that does not focus (WebKit buttons), then a host close: the trigger is restored', async () => {
    const { content, plain, connection, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    await settle()
    pointerDown(content.querySelector('button'))
    content.remove()
    connection.dispose()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('a refused press, then Escape with focus on the body: the Escape close restores the trigger', async () => {
    const { content, plain, connection, dismissals, triggerFocus } = page()
    pointerDown(plain)
    document.activeElement.blur()
    await settle()
    escape(document.body)
    expect(dismissals).toEqual(['DismissFromJavaScriptAsync', 'DismissFromJavaScriptAsync'])
    content.remove()
    connection.dispose()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  // Step order: dispose() reads the active element before the content goes back next to its placeholder.
  // Moving it can run code that moves focus (here a custom element's connectedCallback), so a read after
  // the move could see focus outside and skip the restore of focus that was inside the closing content.
  it('reads the active element before the content moves back to its placeholder', () => {
    if (!customElements.get('hl-test-refocus')) {
      customElements.define('hl-test-refocus', class extends HTMLElement {
        connectedCallback() { if (this.dataset.armed) document.querySelector('input')?.focus() }
      })
    }
    const { content, connection, triggerFocus } = page()
    const probe = document.createElement('hl-test-refocus')
    content.append(probe)
    probe.dataset.armed = 'true'
    expect(focused()).toBe('Apply') // focus is inside the connected content
    connection.dispose()
    expect(triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })

  it('each connection starts without the outside-pointer decision of an earlier opening', () => {
    const first = page()
    pointerDown(first.plain)
    first.content.remove()
    first.connection.dispose()
    document.body.replaceChildren()
    const second = page()
    escape(document.activeElement)
    second.content.remove()
    second.connection.dispose()
    expect(focused()).toBe('Open')
    expect(second.triggerFocus).toHaveBeenCalledWith({ preventScroll: true })
  })
})
