export function setIndeterminate(element, indeterminate) {
  if (element instanceof HTMLInputElement) {
    element.indeterminate = Boolean(indeterminate)
  }
}
