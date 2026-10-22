// The input is rendered by hlp.ui.input, so it is found by its id.

// The clear action disappears with the value, so focus returns to the native input to keep keyboard
// focus in the text-entry group.
export function focusInput(id) { document.getElementById(id)?.focus({ preventScroll: true }) }

// Reveal toggles the rendered type, which can reset the selection; read it before and restore it after.
export function readSelection(id) {
  const input = document.getElementById(id)
  return input ? [input.selectionStart ?? -1, input.selectionEnd ?? -1] : null
}

export function restoreSelection(id, start, end) {
  const input = document.getElementById(id)
  if (!input) return
  input.focus({ preventScroll: true })
  if (start < 0 || end < 0) return
  try { input.setSelectionRange(start, end) } catch { /* some input types expose no selection range */ }
}
