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

function page() {
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
    { invokeMethodAsync: name => { dismissals.push(name); return callback.invokeMethodAsync(name) } })
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
