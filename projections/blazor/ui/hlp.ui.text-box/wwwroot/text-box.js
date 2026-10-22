// The clear action disappears with the value, so focus returns to the native input (by its id, since
// the input is rendered by hlp.ui.input) to keep keyboard focus in the text-entry group.
export function focusInput(id) { document.getElementById(id)?.focus({ preventScroll: true }) }
