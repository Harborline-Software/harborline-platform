#!/usr/bin/env node
// T-1135: the Blazor ScrollAffordanceObserver decides announcements in C# from successive measurements
// (ScrollAffordanceAnnouncer), so the JS module must deliver the load baseline before a user scroll can
// replace it. bUnit cannot run the module; these cases drive it against a minimal element and window.
// Oracle: the revision-2 invariant of specs/modules/ui/hlp.ui.use-scroll-affordance/interface.yaml
// (the first measurement is the load baseline; a later scroll is compared against it).
import assert from 'node:assert/strict'
import {resolve} from 'node:path'
import {pathToFileURL} from 'node:url'
import {test, mock} from 'node:test'

const root = resolve(import.meta.dirname, '../..')
const windowListeners = new Map()
globalThis.window = {
  addEventListener: (name, handler) => windowListeners.set(name, handler),
  removeEventListener: name => windowListeners.delete(name),
}
globalThis.getComputedStyle = () => ({direction: 'ltr'})
globalThis.ResizeObserver = class { observe() {} disconnect() {} }

const {observe, dispose} = await import(pathToFileURL(resolve(root, 'projections/blazor/ui/hlp.ui.use-scroll-affordance/wwwroot/scroll-affordance.js')).href)

function scroller() {
  const listeners = new Map()
  return {
    scrollLeft: 0, scrollTop: 0, scrollWidth: 700, clientWidth: 300, scrollHeight: 0, clientHeight: 0,
    addEventListener: (name, handler) => listeners.set(name, handler),
    removeEventListener: name => listeners.delete(name),
    fire: name => listeners.get(name)?.(),
  }
}

test('the load baseline is sent at once, so a scroll inside the first debounce window is measured against it', () => {
  mock.timers.enable({apis: ['setTimeout']})
  try {
    const sent = []
    const callback = {invokeMethodAsync: (method, measurement) => { sent.push({method, ...measurement}) }}
    const element = scroller()
    const id = observe(element, callback, {orientation: 0, announceDebounceMilliseconds: 300})
    assert.deepEqual(sent, [{method: 'OnMeasured', rawPosition: 0, scrollSize: 700, clientSize: 300, rightToLeft: false}])

    mock.timers.tick(100)
    element.scrollLeft = 200
    element.fire('scroll')
    assert.equal(sent.length, 1, 'scroll measurements stay debounced')
    mock.timers.tick(300)
    assert.deepEqual(sent.map(entry => entry.rawPosition), [0, 200])
    dispose(id)
  } finally {
    mock.timers.reset()
  }
})
