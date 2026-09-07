import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { connect } from '../../projections/blazor/ui/hlp.ui.detail-panel/wwwroot/detail-panel-tabs.js'

const fixture = JSON.parse(readFileSync(resolve(import.meta.dirname, 'drilldown-v1.json'), 'utf8'))

// The Blazor lane's per-key default route, replaying the SAME fixture key sets as the two
// component suites. `@onkeydown:preventDefault` is render-time and would swallow the pass-through
// keys too, so this module is what keeps the two shells identical here.
describe('DetailPanel Blazor JavaScript conformance (conformance/hlp.ui.detail-panel/drilldown-v1.json)', () => {
  function mount() {
    const tablist = document.createElement('div')
    tablist.setAttribute('role', 'tablist')
    // Rendered by HarborlineDetailPanel.razor from its declared TabKeys.
    tablist.dataset.handledKeys = fixture.handledKeys.join(' ')
    const tab = document.createElement('button')
    tab.setAttribute('role', 'tab')
    tablist.append(tab)
    document.body.append(tablist)
    // jsdom implements no scrolling default, so the page stands in for it: it moves only for an
    // event still default-allowed when it reaches the document.
    const page = { scrollTop: 0, passedThrough: [] }
    const listener = event => {
      if (event.defaultPrevented) return
      page.scrollTop += 1
      page.passedThrough.push(event.key)
    }
    document.addEventListener('keydown', listener)
    const connection = connect(tablist)
    return {
      tab,
      page,
      press: key => tab.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })),
      dispose: () => { connection.dispose(); document.removeEventListener('keydown', listener); tablist.remove() },
    }
  }

  it('leaves the document scroll position unchanged for every declared handled key', () => {
    const mounted = mount()
    try {
      for (const key of fixture.handledKeys) {
        mounted.press(key)
        expect(mounted.page.scrollTop, key).toBe(0)
      }
    } finally {
      mounted.dispose()
    }
  })

  it('lets every declared pass-through key reach the page with its default intact', () => {
    const mounted = mount()
    try {
      for (const key of fixture.passThroughKeys) mounted.press(key)
      expect(mounted.page.passedThrough).toEqual(fixture.passThroughKeys)
    } finally {
      mounted.dispose()
    }
  })

  it('stops preventing a key the tab bar no longer declares', () => {
    const mounted = mount()
    try {
      mounted.tab.parentElement.dataset.handledKeys = ''
      mounted.press(fixture.handledKeys[0])
      expect(mounted.page.passedThrough).toEqual([fixture.handledKeys[0]])
    } finally {
      mounted.dispose()
    }
  })
})
