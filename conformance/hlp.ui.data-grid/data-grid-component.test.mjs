import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { connect as connectBlazorGrid } from '../../projections/blazor/ui/hlp.ui.data-grid/wwwroot/data-grid-component.js'

describe('DataGrid Blazor JavaScript conformance', () => {
  it('measures the Blazor outer container while its max-content viewport stays wide', () => {
    let observer
    class TestResizeObserver {
      constructor(callback) { observer = this; this.callback = callback }
      observe(target) { Object.defineProperty(this, 'target', { value: target }) }
      disconnect() {}
      unobserve() {}
      publish() { this.callback([], this) }
    }
    vi.stubGlobal('ResizeObserver', TestResizeObserver)
    const container = document.createElement('div')
    const viewport = document.createElement('div')
    container.append(viewport)
    Object.defineProperty(container, 'clientWidth', { configurable: true, value: 320 })
    Object.defineProperty(viewport, 'clientWidth', { configurable: true, value: 640 })
    const callback = { invokeMethodAsync: vi.fn() }

    const connection = connectBlazorGrid(container, viewport, callback)
    expect(observer?.target).toBe(container)
    expect(callback.invokeMethodAsync).toHaveBeenCalledWith('OnResizedAsync', 320, false)
    Object.defineProperty(container, 'clientWidth', { configurable: true, value: 160 })
    observer?.publish()
    expect(callback.invokeMethodAsync).toHaveBeenLastCalledWith('OnResizedAsync', 160, false)

    connection.dispose()
    vi.unstubAllGlobals()
  })

  it('applies the preserved scroll offset before the first Blazor scroll report', () => {
    class TestResizeObserver { observe() {} disconnect() {} unobserve() {} }
    vi.stubGlobal('ResizeObserver', TestResizeObserver)
    const preserved = JSON.parse(readFileSync(resolve(import.meta.dirname, 'selection-v1.json'), 'utf8')).preservedListState
    const container = document.createElement('div')
    const viewport = document.createElement('div')
    container.append(viewport)
    const reported = []
    const callback = { invokeMethodAsync: (name, value) => { if (name === 'OnScrolledAsync') reported.push(value) } }

    const connection = connectBlazorGrid(container, viewport, callback, preserved.scrollTop)

    expect(viewport.scrollTop).toBe(preserved.scrollTop)
    expect(reported).toEqual([preserved.scrollTop])
    connection.dispose()
    vi.unstubAllGlobals()
  })

  it('focuses the exact Blazor cell by entry and column identity', () => {
    class TestResizeObserver {
      observe() {}
      disconnect() {}
      unobserve() {}
    }
    vi.stubGlobal('ResizeObserver', TestResizeObserver)
    const container = document.createElement('div')
    const viewport = document.createElement('div')
    const wrongColumn = document.createElement('div')
    wrongColumn.tabIndex = -1
    wrongColumn.dataset.gridEntryKey = 'row:a1'
    wrongColumn.dataset.columnId = 'asset'
    const exactCell = document.createElement('div')
    exactCell.tabIndex = -1
    exactCell.dataset.gridEntryKey = 'row:a1'
    exactCell.dataset.columnId = 'due'
    const wrongEntry = document.createElement('div')
    wrongEntry.tabIndex = -1
    wrongEntry.dataset.gridEntryKey = 'row:a2'
    wrongEntry.dataset.columnId = 'due'
    container.append(viewport, wrongColumn, exactCell, wrongEntry)
    document.body.append(container)
    const connection = connectBlazorGrid(container, viewport, { invokeMethodAsync: vi.fn() })

    connection.focus('row:a1', 'due')

    expect(document.activeElement).toBe(exactCell)
    connection.dispose()
    container.remove()
    vi.unstubAllGlobals()
  })
})
