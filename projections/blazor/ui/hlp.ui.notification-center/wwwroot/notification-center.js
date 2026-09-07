export function focusFirst(root) { (root?.querySelector('button,[href],[tabindex]:not([tabindex="-1"])') ?? root)?.focus({ preventScroll: true }) }
export function focus(element) { element?.focus({ preventScroll: true }) }
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
