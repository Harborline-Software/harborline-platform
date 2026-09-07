const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

export function connect(portal, dialog, body, callback, _closeOnEscape) {
  const placeholder = document.createComment('hl-dialog-portal')
  const opener = document.activeElement
  portal.before(placeholder)
  document.body.append(portal)
  const siblings = [...document.body.children].filter(element => element !== portal)
  const inert = siblings.map(element => element.inert ?? false)
  const previousOverflow = document.body.style.overflow
  siblings.forEach(element => { element.inert = true })
  document.body.style.overflow = 'hidden'
  const nodes = () => [...dialog.querySelectorAll(focusable)]
  const measure = () => callback.invokeMethodAsync('OnBodyMeasuredAsync', {
    rawPosition: body.scrollTop, scrollSize: body.scrollHeight, clientSize: body.clientHeight, rightToLeft: false
  })
  const onKey = event => {
    if (event.key === 'Escape') { event.preventDefault(); callback.invokeMethodAsync('DismissFromJavaScriptAsync'); return }
    if (event.key !== 'Tab') return
    const items = nodes()
    if (!items.length) { event.preventDefault(); dialog.focus(); return }
    const first = items[0], last = items[items.length - 1]
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }
  const resize = new ResizeObserver(measure)
  document.addEventListener('keydown', onKey)
  body.addEventListener('scroll', measure, { passive: true })
  resize.observe(body)
  requestAnimationFrame(() => { (nodes()[0] ?? dialog).focus(); measure() })
  return {
    scrollBodyTo(top) { body.scrollTo({ top, behavior: 'auto' }) },
    dispose() {
      document.removeEventListener('keydown', onKey)
      body.removeEventListener('scroll', measure)
      resize.disconnect()
      siblings.forEach((element, index) => { element.inert = inert[index] })
      document.body.style.overflow = previousOverflow
      if (portal.isConnected && placeholder.isConnected) placeholder.before(portal)
      placeholder.remove()
      opener?.focus?.()
    }
  }
}
