const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

export function connect(portal, content, trigger, modal, callback) {
  const placeholder = document.createComment('hl-sheet-portal')
  portal.before(placeholder)
  document.body.append(portal)
  const siblings = modal ? [...document.body.children].filter(element => element !== portal) : []
  const previousInert = siblings.map(element => element.inert ?? false)
  const previousOverflow = document.body.style.overflow
  if (modal) {
    siblings.forEach(element => { element.inert = true })
    document.body.style.overflow = 'hidden'
  }
  const nodes = () => [...content.querySelectorAll(focusable)]
  const onKey = event => {
    if (event.key === 'Escape') { event.preventDefault(); callback.invokeMethodAsync('DismissFromJavaScriptAsync'); return }
    if (!modal || event.key !== 'Tab') return
    const items = nodes()
    if (items.length === 0) { event.preventDefault(); content.focus(); return }
    const first = items[0]
    const last = items[items.length - 1]
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }
  document.addEventListener('keydown', onKey)
  requestAnimationFrame(() => (nodes()[0] ?? content).focus())
  return { dispose() {
    document.removeEventListener('keydown', onKey)
    siblings.forEach((element, index) => { element.inert = previousInert[index] })
    if (modal) document.body.style.overflow = previousOverflow
    if (portal.isConnected && placeholder.isConnected) placeholder.before(portal)
    placeholder.remove()
    trigger?.focus()
  } }
}
