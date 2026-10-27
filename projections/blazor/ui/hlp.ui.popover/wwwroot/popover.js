const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

// Sequential focus order (T-1128). connect() moves the content to the end of <body>, so native Tab order
// put it after the whole page. As in the HTML popover focus navigation model (WHATWG HTML 6.6.2: a
// showing popover's focus navigation scope is inserted after its invoker, inside the invoker's own
// scope), the content sits right after its trigger. Tab leaves the content only when it has no later
// Tab stop, and goes to the next stop after the trigger; Shift+Tab leaves only when it has no earlier
// stop, and goes to the trigger. When nothing follows a trigger inside an outer popover's content, the
// walk continues after the outer trigger, at any depth. Shared with the React lane's Popover.tsx.
const contentSelector = '.hl-popover__content'
const tabbableCandidates = 'a[href],area[href],button,input,select,textarea,iframe,summary,[tabindex],[contenteditable]:not([contenteditable="false"])'
// Each open content's trigger, read when a key arrives (the reference can be replaced by a re-render).
const owners = new WeakMap()
// The open contents, to find the content an open trigger owns (the way in, nested).
const openContents = new Set()
const tabbable = element => {
  if (element.tabIndex < 0 || element.matches(':disabled')) return false
  if (element instanceof HTMLInputElement && element.type === 'hidden') return false
  for (let node = element; node; node = node.parentElement) if (node.hidden || node.inert === true) return false
  return typeof element.checkVisibility !== 'function' || element.checkVisibility({visibilityProperty: true, checkVisibilityCSS: true})
}
const radioGroup = element => element instanceof HTMLInputElement && element.type === 'radio' && element.name ? element : null
const sameGroup = (a, b) => !!radioGroup(a) && !!radioGroup(b) && a.name === b.name && a.form === b.form
// The nearest popover content or modal layer around an element: the focus navigation scope it is in.
const scopeOf = element => element.parentElement?.closest(`${contentSelector},[aria-modal="true"]`) ?? null
// The scope's Tab stops in document order, as the browser has them: tabbable, directly in this scope
// (not in another popover's content), and a radio group as one stop, its checked radio or else its first.
const stopsIn = scope => {
  const own = scope && scope.matches(contentSelector) ? scope : null
  const all = [...(scope ?? document).querySelectorAll(tabbableCandidates)].filter(tabbable).filter(element => element.closest(contentSelector) === own)
  return all.filter(element => {
    if (!radioGroup(element)) return true
    const group = all.filter(other => sameGroup(other, element))
    return element === (group.find(radio => radio.checked) ?? group[0])
  })
}
const follows = (from, other) => !!(from.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_FOLLOWING) && !other.contains(from)
const precedes = (from, other) => !!(from.compareDocumentPosition(other) & Node.DOCUMENT_POSITION_PRECEDING) && !other.contains(from)
const ownerOf = content => { const trigger = owners.get(content)?.(); return trigger?.isConnected ? trigger : null }
const contentOf = trigger => { for (const content of openContents) if (content.isConnected && ownerOf(content) === trigger) return content; return null }
// The next Tab stop after an element in its scope; past the end of a popover content, the next stop
// after that content's trigger. Null at the end of the page or of a modal layer: the key is then left to
// the browser, or to the layer's trap, which is where native Tab from the trigger goes.
const stopAfter = (element, depth = 0) => {
  const scope = scopeOf(element)
  const next = stopsIn(scope).find(stop => !element.contains(stop) && follows(element, stop))
  if (next) return next
  const owner = scope?.matches(contentSelector) && depth < 32 ? ownerOf(scope) : null
  return owner ? stopAfter(owner, depth + 1) : null
}
// Where Shift+Tab out of a trigger's content lands: the trigger, or, when it cannot take focus (disabled
// or hidden while open), the Tab stop before it, walking out of popover contents the same way. Null when
// nothing precedes it: the key is then left to the browser.
const stopBack = (trigger, depth = 0) => {
  if (tabbable(trigger)) return trigger
  const scope = scopeOf(trigger)
  const previous = stopsIn(scope).filter(stop => precedes(trigger, stop)).at(-1)
  if (previous) return previous
  const owner = scope?.matches(contentSelector) && depth < 32 ? ownerOf(scope) : null
  return owner ? stopBack(owner, depth + 1) : null
}
// Handles (prevents) the key only when it moves focus itself; Tab within the content stays native.
function routeTab(event, trigger, content) {
  if (event.key !== 'Tab' || event.defaultPrevented || !trigger?.isConnected || !content) return
  const target = event.target
  if (!(target instanceof HTMLElement) || !content.contains(target)) return
  // Tab on an open inner popover's trigger is that popover's way in (on window, later), not a way out.
  if (!event.shiftKey && contentOf(target)) return
  const others = stopsIn(content).filter(stop => stop !== target && !sameGroup(stop, target))
  if (others.some(stop => event.shiftKey ? precedes(target, stop) : follows(target, stop))) return
  const destination = event.shiftKey ? stopBack(trigger) : stopAfter(trigger)
  if (!destination) return
  event.preventDefault()
  destination.focus()
}

// The way in mirrors the way out (b31 re-review): while open, Tab on the trigger goes to the content's
// first Tab stop, or the content element when it has none (as the opening focus does), and Shift+Tab from
// the next stop after the trigger in the trigger's own scope (modal layer, outer content or page) goes to
// the content's last stop. A key already handled is left alone; nothing is handled once closed.
function enterContent(event, trigger, content) {
  if (event.key !== 'Tab' || event.defaultPrevented || !trigger?.isConnected || !content?.isConnected) return
  let destination
  if (!event.shiftKey) {
    if (event.target !== trigger) return
    destination = stopsIn(content)[0] ?? content
  } else {
    const next = stopsIn(scopeOf(trigger)).find(stop => !trigger.contains(stop) && follows(trigger, stop))
    if (!next || event.target !== next) return
    destination = stopsIn(content).at(-1) ?? content
    // The last stop may be an open inner trigger, whose content follows it: walk in (WHATWG 6.6.2).
    for (let inner = contentOf(destination), depth = 0; inner && depth < 32; inner = contentOf(destination), depth++) {
      destination = stopsIn(inner).at(-1) ?? inner
    }
  }
  event.preventDefault()
  destination.focus()
}

// The placement contract, shared with the React lane's place() in Popover.tsx and replayed by both
// lanes from conformance/hlp.ui.popover/placement-v1.json: flip to the opposite side when the
// requested side lacks room and the opposite has more, THEN clamp the resolved coordinate into the
// viewport. This lane used to clamp only, which left the surface over its anchor at an edge and
// disagreed with React on 12 of the 24 fixture rows (ticket 298).
export function place(r, c, requestedSide, align, offset, direction, viewport) {
  const margin = 8
  const viewportWidth = viewport.width
  const viewportHeight = viewport.height
  let side = requestedSide

  const available = {
    top: r.top - margin,
    right: viewportWidth - r.right - margin,
    bottom: viewportHeight - r.bottom - margin,
    left: r.left - margin,
  }
  const required = side === 'top' || side === 'bottom' ? c.height + offset : c.width + offset
  const opposite = {top: 'bottom', right: 'left', bottom: 'top', left: 'right'}
  if (available[side] < required && available[opposite[side]] > available[side]) side = opposite[side]

  let left = r.left
  let top = r.bottom + offset
  if (side === 'top') top = r.top - c.height - offset
  if (side === 'right') left = r.right + offset
  if (side === 'left') left = r.left - c.width - offset

  if (side === 'top' || side === 'bottom') {
    if (align === 'center') left = r.left + (r.width - c.width) / 2
    if (align === 'start') left = direction === 'rtl' ? r.right - c.width : r.left
    if (align === 'end') left = direction === 'rtl' ? r.left : r.right - c.width
  } else {
    if (align === 'center') top = r.top + (r.height - c.height) / 2
    if (align === 'start') top = r.top
    if (align === 'end') top = r.bottom - c.height
  }

  const maximumLeft = Math.max(margin, viewportWidth - c.width - margin)
  const maximumTop = Math.max(margin, viewportHeight - c.height - margin)
  return {
    side,
    left: Math.min(Math.max(left, margin), maximumLeft),
    top: Math.min(Math.max(top, margin), maximumTop),
  }
}

export function connect(trigger, anchor, content, options, callback) {
  const placeholder = document.createComment('hl-popover-portal')
  content.before(placeholder)
  document.body.append(content)
  // Marks the content as a connected popover layer, so an overlay below it (a sheet) can tell a key
  // from popover content, now outside the overlay's portal, from a key from ordinary page content.
  content.dataset.hlLayer = 'popover'
  const reposition = () => {
    const reference = anchor ?? trigger
    if (!reference || !content) return
    const r = reference.getBoundingClientRect()
    const c = content.getBoundingClientRect()
    const placement = place(
      r,
      {height: c.height, width: c.width},
      options.side ?? 'bottom',
      options.align ?? 'center',
      Number(options.sideOffset ?? 6),
      getComputedStyle(reference).direction === 'rtl' ? 'rtl' : 'ltr',
      {
        height: document.documentElement.clientHeight || window.innerHeight,
        width: document.documentElement.clientWidth || window.innerWidth,
      },
    )
    content.dataset.side = placement.side
    content.style.left = `${placement.left}px`
    content.style.top = `${placement.top}px`
  }
  const dismiss = () => callback.invokeMethodAsync('DismissFromJavaScriptAsync')
  // The open popover is the topmost layer. An Escape from its trigger button is taken in the capture
  // phase, before an outer layer (a dialog or sheet) listening on the document; the trigger has no
  // Escape behaviour of its own. Any other Escape, including one from an anchor that may use Escape
  // itself, is handled on the document after Blazor's own listener, so one already handled (including
  // a Blazor @onkeydown:preventDefault) is left alone. Previously every Escape closed it.
  const fromTrigger = target => target instanceof Node && trigger?.contains(target)
  const onKeyCapture = event => { if (event.key === 'Escape' && !event.defaultPrevented && fromTrigger(event.target)) { event.preventDefault(); dismiss() } }
  const onKey = event => { if (event.key === 'Escape' && !event.defaultPrevented) { event.preventDefault(); dismiss() } }
  // Focus after a close (G5, R-0125; WHATWG HTML hide popover algorithm). An outside pointer dismissal
  // never moves focus: light dismiss passes focusPreviousElement false, and focus stays where the
  // browser's pointer default put it (the pressed field, else the body). The decision is recorded here,
  // synchronously, before the dismissal request: over a Server circuit the closing render and dispose()
  // arrive a round trip later, after the user may have started typing in the pressed field. Each
  // opening is a new connection, so the flag never outlives one. The flag describes only a press that
  // closed the popover: a controlled host may refuse the request. When the request completes with this
  // connection still live and the content still in the document, the host refused it, and a later close is
  // an ordinary one. Blazor Server sends the closing render before the call's completion, so an accepted
  // press has removed the content (or disposed this connection) by then.
  let outsidePointer = false
  let disposed = false
  const onPointer = event => {
    if (content.contains(event.target) || trigger?.contains(event.target)) return
    outsidePointer = true
    Promise.resolve(dismiss()).then(() => { if (!disposed && content.isConnected) outsidePointer = false }, () => {})
  }
  const onViewport = () => reposition()
  // Blazor Server renders over a connection: the content's children can arrive after this call and the
  // trigger reference can be missing or replaced by a re-render. Both are read when a key arrives.
  // The trigger is found by its data-hl-popover-trigger marker, which HarborlinePopoverTrigger renders
  // after the caller's attributes, so a caller's own id cannot defeat the lookup (b31 review note 2).
  const currentTrigger = () => (trigger?.isConnected ? trigger : null)
    ?? (options.triggerId ? [...document.querySelectorAll('[data-hl-popover-trigger]')].find(node => node.dataset.hlPopoverTrigger === options.triggerId) ?? null : null)
  const onContentKey = event => routeTab(event, currentTrigger(), content)
  owners.set(content, currentTrigger)
  openContents.add(content)
  content.addEventListener('keydown', onContentKey)
  // Tab on the trigger and Shift+Tab from the stop after it, on window in the bubble phase: after Blazor's
  // delegated dispatch on the document, so a caller's @onkeydown (and @onkeydown:preventDefault) or any
  // handler on the trigger runs first and a key it handled is left alone (b31 final check). The browser's
  // default Tab runs only after dispatch ends, so preventDefault here still works. The trigger is looked
  // up when the key arrives. dialog.js and sheet.js leave forward Tab on the trigger to this listener while
  // the content carries data-hl-popover-open (set last in connect(), removed first in dispose()).
  const onEnter = event => enterContent(event, currentTrigger(), content)
  window.addEventListener('keydown', onEnter)
  document.addEventListener('keydown', onKeyCapture, true)
  document.addEventListener('keydown', onKey)
  document.addEventListener('pointerdown', onPointer, true)
  window.addEventListener('resize', onViewport)
  window.addEventListener('scroll', onViewport, true)
  // T-713: placement used to be computed once, a frame after the WebAssembly render, and again only
  // on window resize or scroll, so an anchor that moved while the page settled left the popover
  // behind (seen as a 16 px displacement in the RTL gallery story). While open, re-place whenever
  // the anchor's box, the content's size or the viewport changes; an unchanged frame costs two reads.
  let frame = 0
  let measured = ''
  const follow = () => {
    const reference = anchor ?? trigger
    if (reference && content) {
      const r = reference.getBoundingClientRect()
      const c = content.getBoundingClientRect()
      const key = [r.left, r.top, r.width, r.height, c.width, c.height,
        document.documentElement.clientWidth || window.innerWidth, document.documentElement.clientHeight || window.innerHeight].join()
      if (key !== measured) { measured = key; reposition() }
    }
    frame = requestAnimationFrame(follow)
  }
  frame = requestAnimationFrame(() => { follow(); (content.querySelector(focusable) ?? content).focus() })
  // Set last, once everything above is in place, so a connect() that throws part-way never leaves it; a
  // content element removed without dispose() is no longer connected, which the traps also check.
  if (options.triggerId) content.dataset.hlPopoverOpen = options.triggerId
  return {
    dispose() {
      delete content.dataset.hlPopoverOpen
      // Every other close (Escape, a close control, trigger activation, a host-driven close, disposal)
      // restores the trigger, without scrolling, only when focus is inside the closing content or was
      // lost with it to the body (Blazor removes the content before dispose() runs). Read first, before
      // the content goes back next to its placeholder. If the user has put focus elsewhere meanwhile, it
      // stays there.
      const active = document.activeElement
      const lostWithContent = active === null || active === document.body || content.contains(active)
      disposed = true
      cancelAnimationFrame(frame)
      content.removeEventListener('keydown', onContentKey)
      owners.delete(content)
      openContents.delete(content)
      window.removeEventListener('keydown', onEnter)
      document.removeEventListener('keydown', onKeyCapture, true)
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer, true)
      window.removeEventListener('resize', onViewport)
      window.removeEventListener('scroll', onViewport, true)
      delete content.dataset.hlLayer
      if (content.isConnected && placeholder.isConnected) placeholder.before(content)
      placeholder.remove()
      if (!outsidePointer && lostWithContent) trigger?.focus({preventScroll: true})
    }
  }
}
