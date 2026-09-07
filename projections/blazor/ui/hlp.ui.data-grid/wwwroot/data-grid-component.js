export function connect(container, viewport, callback, initialScrollTop = 0) {
  // L1701: a remount is handed the offset the host preserved. It is applied before the first
  // scroll report so the grid never publishes a 0 that would overwrite the preserved state.
  if (initialScrollTop > 0) viewport.scrollTop = initialScrollTop
  const update = () => callback.invokeMethodAsync('OnScrolledAsync', viewport.scrollTop, container.contains(document.activeElement))
  const publishWidth = () => callback.invokeMethodAsync('OnResizedAsync', container.clientWidth, container.contains(document.activeElement))
  const resize = new ResizeObserver(publishWidth)
  viewport.addEventListener('scroll', update, { passive: true })
  resize.observe(container)
  update()
  publishWidth()
  return {
    focus(entryKey, columnId) {
      const cells = container.querySelectorAll('[data-grid-entry-key][data-column-id]')
      for (const cell of cells) {
        if (cell.dataset.gridEntryKey === entryKey && cell.dataset.columnId === columnId) {
          cell.focus()
          return
        }
      }
    },
    dispose() {
      viewport.removeEventListener('scroll', update)
      resize.disconnect()
    }
  }
}
