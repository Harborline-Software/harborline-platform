export function focusNode(root, id) {
  const escaped = globalThis.CSS?.escape ? CSS.escape(id) : id.replace(/["\\]/g, '\\$&')
  root?.querySelector(`[data-node-id="${escaped}"]`)?.focus({ preventScroll: true })
}
