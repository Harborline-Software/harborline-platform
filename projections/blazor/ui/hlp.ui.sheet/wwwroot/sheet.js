const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

const overlayPortals = '.hl-dialog__portal, .hl-sheet__portal'
const isTopmostOverlay = portal => [...document.body.children].filter(element => element.matches(overlayPortals)).at(-1) === portal

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
  // A handled Escape (default prevented) is left alone, as is any key from inside another dialog or
  // sheet portal; otherwise only the topmost overlay acts. An Escape from ordinary page content
  // still dismisses the topmost (for example nonmodal) sheet. Without this, an Escape aimed at a
  // dialog opened above the sheet dismissed the sheet as well.
  const onKey = event => {
    if (event.defaultPrevented) return
    const target = event.target
    const fromThis = portal.contains(target)
    const otherOverlay = target instanceof Element ? target.closest(overlayPortals) : null
    if (!fromThis && ((otherOverlay && otherOverlay !== portal) || !isTopmostOverlay(portal))) return
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
