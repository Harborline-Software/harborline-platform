// The element that had focus when a menu opened, per trigger, so a close can return focus there.
// <body> is not recorded: there is nothing to return to. Nor is anything inside an open menu: a
// re-open while the menu is open must not replace the real opener with one of its own items.
const openers = new WeakMap()
const focusable = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function remember(trigger) {
  const active = document.activeElement
  if (active?.closest?.('[role="menu"]')) return
  if (active && active !== document.body) openers.set(trigger, active)
  else openers.delete(trigger)
}

export function triggerAnchor(trigger) {
  remember(trigger)
  const bounds = trigger.getBoundingClientRect()
  return {left: bounds.left, bottom: bounds.bottom}
}

export function rememberOpener(trigger) {
  remember(trigger)
}

export function focusActive(menu) {
  ;(menu?.querySelector('[data-active="true"]') ?? menu)?.focus()
}

// Called after the closed menu has left the DOM. Focus goes back only if it is still where the menu
// left it, which by now is <body>: an item whose action moved focus elsewhere (an inline editor, a
// dialog) keeps it there, as React Aria's FocusScope restoreFocus does. The opener is used while it
// is still in the document; otherwise the trigger's first focusable element.
export function focusTrigger(trigger) {
  const opener = openers.get(trigger)
  openers.delete(trigger)
  const active = document.activeElement
  if (active && active !== document.body) return
  const target = opener?.isConnected ? opener : trigger?.matches?.(focusable) ? trigger : trigger?.querySelector?.(focusable)
  target?.focus()
}
