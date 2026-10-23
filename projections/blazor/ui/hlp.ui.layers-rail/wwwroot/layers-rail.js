const escape = id => globalThis.CSS?.escape ? CSS.escape(id) : id.replace(/["\\]/g, '\\$&')

// The treeitem is not itself focusable: focus goes to the node's control inside it.
export function focusNode(root, id) {
  root?.querySelector(`[data-node-id="${escape(id)}"] .hl-layers-rail__node-control`)?.focus({ preventScroll: true })
}

export function focusLens(root, id) {
  root?.querySelector(`.hl-layers-rail__lens-activate[data-lens-id="${escape(id)}"]`)?.focus({ preventScroll: true })
}
