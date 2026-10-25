// The element that had focus when a menu opened, per trigger, so every close can return focus there.
// <body> is not recorded: there is nothing to return to.
const openers = new WeakMap()

function remember(trigger) {
  const active = document.activeElement
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

export function focusTrigger(trigger) {
  ;(openers.get(trigger) ?? trigger)?.focus()
  openers.delete(trigger)
}
