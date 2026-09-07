const focusable = 'input,button:not([disabled]),a[href],[tabindex]:not([tabindex="-1"])'
export function connect(portal, dialog, input) {
  const previous = document.activeElement
  const placeholder = document.createComment('hl-spotlight-portal')
  portal.before(placeholder)
  document.body.append(portal)
  const containFocus = event => { if (!dialog.contains(event.target)) input?.focus() }
  const onKey = event => {
    if (event.key !== 'Tab') return
    const nodes = [...dialog.querySelectorAll(focusable)]
    if (nodes.length === 0) { event.preventDefault(); dialog.focus(); return }
    const first = nodes[0], last = nodes[nodes.length - 1]
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }
  document.addEventListener('keydown', onKey)
  document.addEventListener('focusin', containFocus)
  requestAnimationFrame(() => input?.focus())
  return { dispose() { document.removeEventListener('keydown', onKey); document.removeEventListener('focusin', containFocus); if(portal.isConnected&&placeholder.isConnected)placeholder.before(portal); placeholder.remove(); previous?.focus?.() } }
}
