export function connect(element, callback, horizontal, initialAnchor) {
  const measure = () => callback.invokeMethodAsync('OnMeasuredAsync', {
    rawPosition: horizontal ? element.scrollLeft : element.scrollTop,
    scrollSize: horizontal ? element.scrollWidth : element.scrollHeight,
    clientSize: horizontal ? element.clientWidth : element.clientHeight,
    rightToLeft: horizontal && getComputedStyle(element).direction === 'rtl'
  })
  const resize = new ResizeObserver(measure)
  if (!horizontal && Number.isFinite(initialAnchor)) element.scrollTop = Math.max(0, initialAnchor - element.clientHeight / 3)
  element.addEventListener('scroll', measure, { passive: true })
  window.addEventListener('resize', measure)
  resize.observe(element)
  measure()
  return {
    scrollTo(rawTarget) {
      if (horizontal) element.scrollTo({ left: rawTarget, behavior: 'auto' })
      else element.scrollTo({ top: rawTarget, behavior: 'auto' })
    },
    dispose() {
      element.removeEventListener('scroll', measure)
      window.removeEventListener('resize', measure)
      resize.disconnect()
    }
  }
}
