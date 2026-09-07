const observers = new WeakMap()
export function measure(element, vertical) {
  const rect = element.parentElement.getBoundingClientRect()
  return vertical ? rect.width : rect.height
}
export function direction(element) { return getComputedStyle(element).direction === 'rtl' ? -1 : 1 }
export function observe(element, vertical, reference) {
  const observer = new ResizeObserver(() => reference.invokeMethodAsync('Measure', measure(element, vertical)))
  let activePointer
  const lost = event => { if (event.pointerId === activePointer) activePointer = undefined }
  const down = event => { if (event.button !== 0 || activePointer !== undefined) return; activePointer = event.pointerId; event.preventDefault(); element.focus(); element.setPointerCapture(event.pointerId) }
  // Bare keys only: a Mod chord belongs to the shell, so the divider suppresses no browser default for it.
  const key = event => { if (event.ctrlKey || event.metaKey || event.altKey) return; if (['Home', 'End', 'Enter', 'Escape', '0', ...(vertical ? ['ArrowLeft', 'ArrowRight'] : ['ArrowUp', 'ArrowDown'])].includes(event.key)) event.preventDefault() }
  element.addEventListener('pointerdown', down)
  element.addEventListener('keydown', key)
  element.addEventListener('lostpointercapture', lost)
  observer.observe(element.parentElement)
  observers.set(element, { observer, down, key, lost })
}
// The shell's own inline size, published to the component. One measurement seam for both lanes: React observes
// the same box with a ResizeObserver, so the width the dock clamps against is the same number in both.
const inlineObservers = new WeakMap()
export function measureInlineSize(element) { return element.getBoundingClientRect().width }
export function observeInlineSize(element, reference) {
  const observer = new ResizeObserver(() => reference.invokeMethodAsync('ShellMeasured', measureInlineSize(element)))
  observer.observe(element)
  inlineObservers.set(element, observer)
}
export function unobserveInlineSize(element) {
  const observer = inlineObservers.get(element)
  if (!observer) return
  observer.disconnect()
  inlineObservers.delete(element)
}

// The pop-out chord's browser default. Blazor's :preventDefault directive is a render-time flag and cannot
// vary per key, so an unconditional one would swallow Enter and Space on every header button; the chord is
// suppressed here instead, exactly where React calls event.preventDefault. Scope is the panel HEADER that
// carries a pop-out affordance - the same condition the .NET handler applies - so Shift+Enter typed into a
// panel body, and every other key anywhere, keeps its default.
const chordListeners = new WeakMap()
export function observePanelChord(element) {
  const key = event => {
    if (event.ctrlKey || event.metaKey || event.altKey || !event.shiftKey || event.key !== 'Enter') return
    const header = event.target && event.target.closest ? event.target.closest('.hl-app-shell__end-panel-header') : null
    if (!header || !header.querySelector('[data-panel-pop-out]')) return
    event.preventDefault()
  }
  element.addEventListener('keydown', key)
  chordListeners.set(element, key)
}
export function unobservePanelChord(element) {
  const key = chordListeners.get(element)
  if (!key) return
  element.removeEventListener('keydown', key)
  chordListeners.delete(element)
}

export function unobserve(element) {
  const state = observers.get(element)
  if (!state) return
  state.observer.disconnect()
  element.removeEventListener('pointerdown', state.down)
  element.removeEventListener('keydown', state.key)
  element.removeEventListener('lostpointercapture', state.lost)
  observers.delete(element)
}

// The dock's outer separator is a 24px box and a real drag leaves it immediately, so the element must own the
// pointer for the rest of the gesture - exactly what the pane divider above does with setPointerCapture. Without
// it pointermove stops firing at the element edge and the dock silently stops following the pointer.
export function capturePointer(element, pointerId) { element.setPointerCapture(pointerId) }
