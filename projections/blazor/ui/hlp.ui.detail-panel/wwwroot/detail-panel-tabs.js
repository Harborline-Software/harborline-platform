// Blazor-lane per-key default route. `@onkeydown:preventDefault` is a render-time directive and
// would swallow Escape and Tab too, so the handled set is read from the tab bar's
// data-handled-keys attribute and only those keys lose their browser default (document scroll on
// the arrows, jump to top/bottom on Home/End). Everything else reaches the surrounding shell.
export function connect(tablist) {
  const onKeyDown = event => {
    if ((tablist.dataset.handledKeys ?? '').split(' ').includes(event.key)) event.preventDefault()
  }
  tablist.addEventListener('keydown', onKeyDown)
  return {
    dispose() {
      tablist.removeEventListener('keydown', onKeyDown)
    }
  }
}
