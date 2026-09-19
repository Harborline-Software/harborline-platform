export function scrollAndFocus(element) {
  if (!element) return
  element.scrollIntoView({ block: 'start' })
  element.tabIndex = -1
  element.focus({ preventScroll: true })
}

export function bindDomainPicker(element) {
  const preventNavigationDefault = event => {
    if (['Enter', 'ArrowDown', 'ArrowUp', 'Escape'].includes(event.key)) event.preventDefault()
  }
  element.addEventListener('keydown', preventNavigationDefault)
  return { dispose() { element.removeEventListener('keydown', preventNavigationDefault) } }
}
