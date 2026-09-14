const observers = new WeakMap()
export function measure(element, vertical) {
  if (!element?.isConnected || !element.parentElement?.isConnected) return 0
  const rect = element.parentElement.getBoundingClientRect()
  return vertical ? rect.width : rect.height
}
export function direction(element) { return element?.isConnected && getComputedStyle(element).direction === 'rtl' ? -1 : 1 }
export function observe(element, vertical, reference) {
  if (!element?.isConnected || !element.parentElement?.isConnected) return { dispose() {} }
  unobserve(element)
  let disposed = false
  const observer = new ResizeObserver(() => { if (!disposed && element.isConnected) reference.invokeMethodAsync('Measure', measure(element, vertical)) })
  let activePointer
  const lost = event => { if (event.pointerId === activePointer) activePointer = undefined }
  const down = event => { if (disposed || !element.isConnected || event.button !== 0 || activePointer !== undefined) return; activePointer = event.pointerId; event.preventDefault(); element.focus(); element.setPointerCapture(event.pointerId) }
  // Bare keys only: a Mod chord belongs to the shell, so the divider suppresses no browser default for it.
  const key = event => { if (disposed || !element.isConnected || event.ctrlKey || event.metaKey || event.altKey) return; if (['Home', 'End', 'Enter', 'Escape', '0', ...(vertical ? ['ArrowLeft', 'ArrowRight'] : ['ArrowUp', 'ArrowDown'])].includes(event.key)) event.preventDefault() }
  element.addEventListener('pointerdown', down)
  element.addEventListener('keydown', key)
  element.addEventListener('lostpointercapture', lost)
  observer.observe(element.parentElement)
  const connection = { dispose() {
    if (disposed) return
    disposed = true
    observer.disconnect()
    element.removeEventListener('pointerdown', down)
    element.removeEventListener('keydown', key)
    element.removeEventListener('lostpointercapture', lost)
    observers.delete(element)
  } }
  observers.set(element, connection)
  return connection
}
// The shell's own inline size, published to the component. One measurement seam for both lanes: React observes
// the same box with a ResizeObserver, so the width the dock clamps against is the same number in both.
const inlineObservers = new WeakMap()
export function measureInlineSize(element) { return element?.isConnected ? element.getBoundingClientRect().width : 0 }
export function observeInlineSize(element, reference) {
  if (!element?.isConnected) return { dispose() {} }
  unobserveInlineSize(element)
  let disposed = false
  const observer = new ResizeObserver(() => { if (!disposed && element.isConnected) reference.invokeMethodAsync('ShellMeasured', measureInlineSize(element)) })
  observer.observe(element)
  const connection = { dispose() {
    if (disposed) return
    disposed = true
    observer.disconnect()
    inlineObservers.delete(element)
  } }
  inlineObservers.set(element, connection)
  return connection
}
export function unobserveInlineSize(element) {
  inlineObservers.get(element)?.dispose()
}

// The pop-out chord's browser default. Blazor's :preventDefault directive is a render-time flag and cannot
// vary per key, so an unconditional one would swallow Enter and Space on every header button; the chord is
// suppressed here instead, exactly where React calls event.preventDefault. Scope is the panel HEADER that
// carries a pop-out affordance - the same condition the .NET handler applies - so Shift+Enter typed into a
// panel body, and every other key anywhere, keeps its default.
const chordListeners = new WeakMap()
export function observePanelChord(element) {
  if (!element?.isConnected) return { dispose() {} }
  unobservePanelChord(element)
  let disposed = false
  const key = event => {
    if (disposed || !element.isConnected || event.ctrlKey || event.metaKey || event.altKey || !event.shiftKey || event.key !== 'Enter') return
    const header = event.target && event.target.closest ? event.target.closest('.hl-app-shell__end-panel-header') : null
    if (!header || !header.querySelector('[data-panel-pop-out]')) return
    event.preventDefault()
  }
  element.addEventListener('keydown', key)
  const connection = { dispose() {
    if (disposed) return
    disposed = true
    element.removeEventListener('keydown', key)
    chordListeners.delete(element)
  } }
  chordListeners.set(element, connection)
  return connection
}
export function unobservePanelChord(element) {
  chordListeners.get(element)?.dispose()
}

export function unobserve(element) {
  observers.get(element)?.dispose()
}

// The dock's outer separator is a 24px box and a real drag leaves it immediately, so the element must own the
// pointer for the rest of the gesture - exactly what the pane divider above does with setPointerCapture. Without
// it pointermove stops firing at the element edge and the dock silently stops following the pointer.
export function capturePointer(element, pointerId) { if (element?.isConnected) element.setPointerCapture(pointerId) }
