export function focusOption(root, value) {
  const option = [...root.querySelectorAll('[data-hl-option]')].find(node => node.dataset.hlOption === value)
  option?.focus()
}
