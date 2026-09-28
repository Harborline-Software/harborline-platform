export function bind(root, callback) {
  let composing = false
  const outside = event => {
    if (root.dataset.hlOpen === 'true' && !root.contains(event.target)) callback.invokeMethodAsync('OnOutsidePointer')
  }
  const blur = event => {
    if (root.dataset.hlOpen === 'true' && !root.contains(event.relatedTarget)) callback.invokeMethodAsync('OnOutsidePointer')
  }
  const start = () => { composing = true }
  const end = () => { composing = false }
  const key = event => {
    const editable = event.target.tagName === 'INPUT'
    if (editable && (composing || event.isComposing || event.keyCode === 229)) {
      if (event.key === 'Enter') event.preventDefault()
      event.stopPropagation()
      return
    }
    const filter = event.target.getAttribute('role') === 'searchbox'
    const handled = editable ? (filter ? ['Enter', 'ArrowDown', 'Escape'] : ['Enter', 'ArrowDown', 'ArrowUp', 'Escape'])
      : ['Enter', ' ', 'ArrowDown', 'ArrowUp', 'Home', 'End', 'Escape']
    if (handled.includes(event.key)) event.preventDefault()
  }
  document.addEventListener('pointerdown', outside, true)
  root.addEventListener('focusout', blur)
  root.addEventListener('keydown', key)
  root.addEventListener('compositionstart', start)
  root.addEventListener('compositionend', end)
  return { dispose() {
    document.removeEventListener('pointerdown', outside, true)
    root.removeEventListener('focusout', blur)
    root.removeEventListener('keydown', key)
    root.removeEventListener('compositionstart', start)
    root.removeEventListener('compositionend', end)
  } }
}
export function focusTarget(root, target, active) {
  const element = target === 'trigger' ? root.querySelector(':scope > button, :scope > input')
    : target === 'option' ? [...root.querySelectorAll('[role=option]')].find(option => option.dataset.hlOption === active)
    : root.querySelector('[role=' + target + ']')
  element?.focus()
}
