import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { measure, observe, unobserve, direction, observePanelChord, unobservePanelChord } from '../../projections/blazor/ui/hlp.ui.app-shell/wwwroot/dock-divider.js'
const fixture = JSON.parse(readFileSync(new URL('./dividers-v1.json', import.meta.url)))
const chrome = JSON.parse(readFileSync(new URL('./chrome-v1.json', import.meta.url)))
for (const name of ['chordScope', 'passThroughKeys']) if (chrome[name] === undefined) throw new Error(`chrome-v1.json is missing "${name}"`)

test('Blazor divider browser boundary: measurement, capture, key suppression and disposal', () => {
  for (const row of fixture.cases) {
    let notify, disconnected = false, focused = 0, captured
    globalThis.ResizeObserver = class { constructor(callback) { notify = callback } observe(parent) { assert.ok(parent) } disconnect() { disconnected = true } }
    globalThis.getComputedStyle = () => ({ direction: 'rtl' })
    const element = new EventTarget()
    element.parentElement = { getBoundingClientRect: () => ({ width: row.extent, height: row.extent }) }
    element.focus = () => focused++
    element.setPointerCapture = pointer => { captured = pointer }
    const calls = []
    const vertical = row.orientation === 'vertical'
    assert.equal(measure(element, vertical), row.extent)
    assert.equal(direction(element), -1)
    observe(element, vertical, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve() } })
    notify()
    assert.deepEqual(calls, [['Measure', row.extent]])
    const down = Object.assign(new Event('pointerdown', { cancelable: true }), { button: 0, pointerId: 17 })
    element.dispatchEvent(down)
    assert.equal(captured, 17); assert.equal(focused, 1); assert.equal(down.defaultPrevented, true)
    // The handled set is the fixture's, not this file's, so both shells and the browser boundary read one list.
    const handled = fixture.handledKeys[row.orientation]
    for (const step of [...row.steps.filter(step => step.action === 'key'), ...handled.map(key => ({ key }))]) {
      const event = Object.assign(new Event('keydown', { cancelable: true }), { key: step.key })
      element.dispatchEvent(event)
      assert.equal(event.defaultPrevented, handled.includes(step.key))
    }
    for (const pass of fixture.shellPassThroughKeys) {
      const event = Object.assign(new Event('keydown', { cancelable: true }), { key: pass.key, ctrlKey: true })
      element.dispatchEvent(event)
      assert.equal(event.defaultPrevented, false, `${pass.token} must pass through the divider`)
    }
    const tab = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Tab' })
    element.dispatchEvent(tab); assert.equal(tab.defaultPrevented, false)
    unobserve(element); assert.equal(disconnected, true)
    element.dispatchEvent(Object.assign(new Event('pointerdown'), { button: 0, pointerId: 19 }))
    assert.equal(captured, 17)
  }
})

// The Blazor half of chrome-v1.json's chordScope: React prevents the chord's default in its own handler, and
// this lane does it here, so both shells suppress the same default from the same fixture rows. The dispatch
// target is the listener's own element, so each row gives that element the `closest` the real target would have.
const header = { querySelector: selector => selector === '[data-panel-pop-out]' ? {} : null }
const closests = {
  header: selector => selector === '.hl-app-shell__end-panel-header' ? header : null,
  body: () => null,
  bare: () => ({ querySelector: () => null }),
}
function chord(from, key, shiftKey = true) {
  const element = Object.assign(new EventTarget(), { closest: closests[from] })
  observePanelChord(element)
  const event = Object.assign(new Event('keydown', { cancelable: true }), { key, shiftKey })
  element.dispatchEvent(event)
  return { element, event }
}

test('Blazor pop-out chord browser boundary: the header route suppresses the default, the body does not', () => {
  for (const row of chrome.chordScope.rows) assert.equal(chord(row.from, row.key, row.shiftKey).event.defaultPrevented, row.expectedDefaultPrevented, `${row.from} chord`)
  // A header with no pop-out affordance has no chord - the same condition the .NET handler applies.
  assert.equal(chord('bare', 'Enter').event.defaultPrevented, false)
  // Every BARE pass-through key keeps its default, in the header too: only the Shift chord is suppressed.
  for (const key of chrome.passThroughKeys) assert.equal(chord('header', key, false).event.defaultPrevented, false, `${key} must pass through the header`)
  const { element } = chord('header', 'Enter')
  unobservePanelChord(element)
  const after = Object.assign(new Event('keydown', { cancelable: true }), { key: 'Enter', shiftKey: true })
  element.dispatchEvent(after)
  assert.equal(after.defaultPrevented, false)
})
