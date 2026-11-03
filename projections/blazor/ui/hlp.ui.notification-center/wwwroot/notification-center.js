export function focusFirst(root) { (root?.querySelector('button,[href],[tabindex]:not([tabindex="-1"])') ?? root)?.focus({ preventScroll: true }) }
export function focus(element) { element?.focus({ preventScroll: true }) }

// After an action that removes its own control: the first call for a settle (a new token) records the
// control focus is on. While focus stays on that control, the settle waits for the host to remove it
// (returns false). Focus anywhere else cancels the settle (returns true) so focus the user moved is
// never taken back. Focus lost to the page settles on the row now at the same position (else the last
// row), else the first control in the dialog.
const origins = new WeakMap()
export function settleFocus(dialog, row, token) {
  if (!dialog?.isConnected) return true
  const active = document.activeElement
  const connected = active && active !== document.body && active.isConnected
  const recorded = origins.get(dialog)
  if (!recorded || recorded.token !== token) origins.set(dialog, { token, origin: connected ? active : null })
  if (connected) {
    if (active === origins.get(dialog).origin) return false
    origins.delete(dialog)
    return true
  }
  origins.delete(dialog)
  const rows = dialog.querySelectorAll('.hl-notification-center__item')
  const target = row >= 0 && rows.length ? rows[Math.min(row, rows.length - 1)] : null
  ;(target?.querySelector('button') ?? dialog.querySelector('button') ?? dialog).focus({ preventScroll: true })
  return true
}
export function positionDialog(trigger, dialog) {
  if (!trigger || !dialog) return
  const anchor = trigger.getBoundingClientRect()
  const content = dialog.getBoundingClientRect()
  const viewportWidth = document.documentElement.clientWidth
  const viewportHeight = document.documentElement.clientHeight
  const margin = 8
  const offset = 6
  const rtl = getComputedStyle(trigger).direction === 'rtl'
  const preferredLeft = rtl ? anchor.left : anchor.right - content.width
  const left = Math.min(Math.max(preferredLeft, margin), Math.max(margin, viewportWidth - content.width - margin))
  const preferredTop = anchor.bottom + offset
  const top = Math.min(Math.max(preferredTop, margin), Math.max(margin, viewportHeight - content.height - margin))
  Object.assign(dialog.style, { position: 'fixed', left: `${left}px`, top: `${top}px` })
}

// Escape inside the notification dialog closes it (its Razor @onkeydown). Blazor cannot mark the key
// handled, so an enclosing Dialog or Sheet, which listens on the document and leaves a handled Escape
// alone, closed as well. This listener sits on the dialog element, before any document listener, and
// marks the Escape the dialog uses, as search-input.js does for Clear.
export function observeEscape(dialog) {
  const onKeyDown = event => { if (event.key === 'Escape' && !event.defaultPrevented) event.preventDefault() }
  dialog?.addEventListener('keydown', onKeyDown)
  return { dispose: () => dialog?.removeEventListener('keydown', onKeyDown) }
}
