// @vitest-environment jsdom
import { connect } from '../../projections/blazor/ui/hlp.ui.popover/wwwroot/popover.js'

const box = (left, top, width, height) => ({ left, top, width, height, right: left + width, bottom: top + height, x: left, y: top })

// T-713: the Blazor lane's connect() follows an anchor that moves after the popover opened (a page
// still settling). Run here, like the placement table, because bUnit cannot execute the module's
// JavaScript. The unfixed connect() placed once and again only on window resize or scroll.
describe('Popover Blazor connect() follows its anchor (T-713)', () => {
  afterEach(() => { vi.useRealTimers(); document.body.replaceChildren() })

  it('re-places an open popover when its anchor moves without a resize or scroll', () => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] })
    let anchorBox = box(600, 110, 112, 38)
    const trigger = document.createElement('button')
    const content = document.createElement('div')
    document.body.append(trigger, content)
    trigger.getBoundingClientRect = () => anchorBox
    content.getBoundingClientRect = () => box(0, 0, 187, 100)
    const connection = connect(trigger, null, content, { side: 'bottom', align: 'start', sideOffset: 6 }, { invokeMethodAsync: () => Promise.resolve() })

    vi.advanceTimersToNextFrame()
    expect([content.style.left, content.style.top]).toEqual(['600px', '154px'])

    anchorBox = box(616, 126, 112, 38)
    vi.advanceTimersToNextFrame()
    expect([content.style.left, content.style.top]).toEqual(['616px', '170px'])
    connection.dispose()
  })
})
