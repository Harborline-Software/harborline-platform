export function scrollAndFocus(element) {
  if (!element) return
  element.scrollIntoView({ block: 'start' })
  element.tabIndex = -1
  element.focus({ preventScroll: true })
}

export function focusSubmit(form) {
  form?.querySelector('button[type="submit"]')?.focus()
}
