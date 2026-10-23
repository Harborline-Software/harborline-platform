export function focusFirst(root) { (root?.querySelector('button,[href],[tabindex]:not([tabindex="-1"])') ?? root)?.focus({ preventScroll: true }) }
export function focus(element) { element?.focus({ preventScroll: true }) }

// After an action removed its own control: if focus fell out of the dialog, settle it on the row now at
// the same position (else the last row), else the first control in the dialog. Returns whether the
// pending settle is done (focus moved, or the dialog is gone); false means focus is still on the
// control, waiting for the host to remove it.
export function settleFocus(dialog, row) {
  if (!dialog?.isConnected) return true
  const active = document.activeElement
  if (active && active !== document.body && active.isConnected) return false
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
