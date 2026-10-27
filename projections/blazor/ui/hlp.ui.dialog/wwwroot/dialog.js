const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

// Dialogs and sheets are both modal overlays; the topmost is whichever was portaled last.
const isTopmost = portal => [...document.body.children].filter(element => element.matches('.hl-dialog__portal, .hl-sheet__portal')).at(-1) === portal

// T-1128: Tab on the trigger of an OPEN popover enters its content; popover.js routes it on window, after
// Blazor's delegated handlers, which is after this document listener. popover.js marks the connected
// content data-hl-popover-open="<trigger key>" from the end of connect() until dispose(), so the trap
// hands that one key over only while the popover is really open: the marker present, the content
// connected, and the trigger inside this overlay. Rendered ARIA (aria-expanded) is not trusted: a
// disclosure, an accordion header or a stale trigger is still wrapped. Same rule in dialog.js and sheet.js.
const opensPopover = target => {
  const key = target instanceof Element ? target.getAttribute('data-hl-popover-trigger') : null
  return !!key && [...document.querySelectorAll('[data-hl-popover-open]')].some(content => content.isConnected && content.getAttribute('data-hl-popover-open') === key)
}

export function connect(portal, dialog, body, callback, _closeOnEscape) {
  const placeholder = document.createComment('hl-dialog-portal')
  const opener = document.activeElement
  portal.before(placeholder)
  document.body.append(portal)
  const siblings = [...document.body.children].filter(element => element !== portal)
  const inert = siblings.map(element => element.inert ?? false)
  const previousOverflow = document.body.style.overflow
  siblings.forEach(element => { element.inert = true })
  document.body.style.overflow = 'hidden'
  const nodes = () => [...dialog.querySelectorAll(focusable)]
  const measure = () => callback.invokeMethodAsync('OnBodyMeasuredAsync', {
    rawPosition: body.scrollTop, scrollSize: body.scrollHeight, clientSize: body.clientHeight, rightToLeft: false
  })
  // An Escape a widget already handled (prevented the default) is left alone, and only the dialog
  // the key came from acts, or the topmost one for a key from the page itself; without this, one
  // Escape dismissed every open dialog. Tab stays with the topmost dialog wherever focus is.
  const onKey = event => {
    if (event.defaultPrevented) return
    const target = event.target
    const fromThis = portal.contains(target)
    const fromPage = target === document || target === document.body || target === document.documentElement
    if (event.key === 'Escape' && !fromThis && !(fromPage && isTopmost(portal))) return
    if (event.key === 'Tab' && !fromThis && !isTopmost(portal)) return
    if (event.key === 'Escape') { event.preventDefault(); callback.invokeMethodAsync('DismissFromJavaScriptAsync'); return }
    if (event.key !== 'Tab') return
    // Forward Tab on an open popover's trigger in this dialog is the popover's (see opensPopover).
    if (!event.shiftKey && dialog.contains(target) && opensPopover(target)) return
    const items = nodes()
    if (!items.length) { event.preventDefault(); dialog.focus(); return }
    const first = items[0], last = items[items.length - 1]
    // Focus outside the dialog (content portaled to body after it opened) is brought back in, as in React.
    if (!dialog.contains(document.activeElement)) { event.preventDefault(); (event.shiftKey ? last : first).focus(); return }
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }
  const resize = new ResizeObserver(measure)
  document.addEventListener('keydown', onKey)
  body.addEventListener('scroll', measure, { passive: true })
  resize.observe(body)
  requestAnimationFrame(() => { (nodes()[0] ?? dialog).focus(); measure() })
  return {
    scrollBodyTo(top) { body.scrollTo({ top, behavior: 'auto' }) },
    dispose() {
      document.removeEventListener('keydown', onKey)
      body.removeEventListener('scroll', measure)
      resize.disconnect()
      siblings.forEach((element, index) => { element.inert = inert[index] })
      document.body.style.overflow = previousOverflow
      if (portal.isConnected && placeholder.isConnected) placeholder.before(portal)
      placeholder.remove()
      opener?.focus?.()
    }
  }
}
