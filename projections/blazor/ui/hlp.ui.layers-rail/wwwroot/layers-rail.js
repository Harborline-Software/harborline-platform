const escape = id => globalThis.CSS?.escape ? CSS.escape(id) : id.replace(/["\\]/g, '\\$&')

// The treeitem is not itself focusable: focus goes to the node's control inside it. onlyIfLost moves
// focus only when it is on the page or inside the rail, so focus the user moved elsewhere stays put.
export function focusNode(root, id, onlyIfLost = false) {
  if (onlyIfLost) {
    const active = document.activeElement
    if (active && active !== document.body && active.isConnected && !root?.contains(active)) return
  }
  root?.querySelector(`[data-node-id="${escape(id)}"] .hl-layers-rail__node-control`)?.focus({ preventScroll: true })
}

export function focusLens(root, id) {
  root?.querySelector(`.hl-layers-rail__lens-activate[data-lens-id="${escape(id)}"]`)?.focus({ preventScroll: true })
}

// Arrow, Home and End move the roving focus; their default would scroll the rail. Blazor cannot
// prevent the default for some keys only, so the tree does it.
const navigation = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End'])
export function observeTreeKeys(tree) {
  tree?.addEventListener('keydown', event => { if (navigation.has(event.key) && event.target.closest('.hl-layers-rail__node-control')) event.preventDefault() })
}
