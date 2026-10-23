// Escape on a nonempty search is Clear. The listener sits on the input, so it runs before any document
// listener (an enclosing dialog or sheet), and marks the key handled so those leave it alone. The
// browser's own search clearing is prevented; the component clears and publishes at once instead.
export function observeEscape(input, component) {
  const onKeyDown = event => {
    if (event.key !== 'Escape' || event.defaultPrevented || input.value === '') return
    event.preventDefault()
    component.invokeMethodAsync('ClearFromEscapeAsync')
  }
  input.addEventListener('keydown', onKeyDown)
  return { dispose: () => input.removeEventListener('keydown', onKeyDown) }
}
