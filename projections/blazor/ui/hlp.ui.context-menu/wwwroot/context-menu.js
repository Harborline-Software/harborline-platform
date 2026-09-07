const keyboardOpeners = new WeakMap()

export function triggerAnchor(trigger) {
  keyboardOpeners.set(trigger, document.activeElement)
  const bounds = trigger.getBoundingClientRect()
  return {left: bounds.left, bottom: bounds.bottom}
}

export function focusActive(menu) {
  ;(menu?.querySelector('[data-active="true"]') ?? menu)?.focus()
}

export function focusTrigger(trigger) {
  ;(keyboardOpeners.get(trigger) ?? trigger)?.focus()
  keyboardOpeners.delete(trigger)
}
