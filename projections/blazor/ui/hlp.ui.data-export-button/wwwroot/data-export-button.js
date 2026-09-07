export function connect(root, trigger, callback) {
  const dismiss = event => {
    if (!root?.contains(event.target)) callback.invokeMethodAsync('DismissFromJavaScriptAsync')
  }
  document.addEventListener('pointerdown', dismiss, true)
  return { dispose() { document.removeEventListener('pointerdown', dismiss, true) } }
}

export function focusActive(root) {
  root?.querySelector('[role="menuitem"][tabindex="0"]')?.focus()
}

export function focusTrigger(trigger) {
  trigger?.focus()
}
