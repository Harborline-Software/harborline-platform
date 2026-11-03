const escape = id => globalThis.CSS?.escape ? CSS.escape(id) : id.replace(/["\\]/g, '\\$&')

// The treeitem itself takes focus (APG treeview); its buttons are for pointers only. onlyIfLost moves
// focus only when it is on the page or inside the rail, so focus the user moved elsewhere stays put.
export function focusNode(root, id, onlyIfLost = false) {
  if (onlyIfLost) {
    const active = document.activeElement
    if (active && active !== document.body && active.isConnected && !root?.contains(active)) return
  }
  root?.querySelector(`[role="treeitem"][data-node-id="${escape(id)}"]`)?.focus({ preventScroll: true })
}

export function focusLens(root, id) {
  root?.querySelector(`.hl-layers-rail__lens-activate[data-lens-id="${escape(id)}"]`)?.focus({ preventScroll: true })
}

// Arrow, Home and End move the roving focus and Enter and Space select; their default would scroll the
// rail. Blazor cannot prevent the default for some keys only, so the tree does it.
const navigation = new Set(['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter', ' '])
export function observeTreeKeys(tree) {
  tree?.addEventListener('keydown', event => { if (navigation.has(event.key) && !event.altKey && !event.ctrlKey && !event.metaKey && event.target.matches?.('[role="treeitem"]')) event.preventDefault() })
}

// Escape in the lens group clears an active lens (the Razor @onkeydown). Blazor cannot prevent the
// default for some keys or states only, so this marks that Escape handled, before an enclosing Dialog
// or Sheet listening on the document sees it, and only while a lens is active: with none, the rail has
// no use for Escape and leaves it to them.
export function observeLensKeys(group) {
  const onKeyDown = event => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    if (group.querySelector('.hl-layers-rail__lens-activate[aria-pressed="true"]')) event.preventDefault()
  }
  group?.addEventListener('keydown', onKeyDown)
  return { dispose: () => group?.removeEventListener('keydown', onKeyDown) }
}
