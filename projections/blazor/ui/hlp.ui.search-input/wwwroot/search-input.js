// Escape on a nonempty search is Clear. The listener sits on the input, so it runs before any document
// listener (an enclosing dialog or sheet), and marks the key handled so those leave it alone. The
// browser's own search clearing is prevented; the component clears and publishes at once instead.
// During IME composition Escape cancels the composition only, so it is left alone.
export function observeEscape(input, component) {
  const onKeyDown = event => {
    if (event.key !== 'Escape' || event.defaultPrevented || input.value === '') return
    if (event.isComposing || event.keyCode === 229) return
    event.preventDefault()
    component.invokeMethodAsync('ClearFromEscapeAsync').catch(() => { /* the component was disposed or the host's handler failed */ })
  }
  input.addEventListener('keydown', onKeyDown)
  return { dispose: () => input.removeEventListener('keydown', onKeyDown) }
}
