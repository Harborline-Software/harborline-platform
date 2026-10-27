const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

// Sequential focus order (T-1128). connect() moves the content to the end of <body>, so native Tab order
// put it after the whole page. As in the HTML popover focus navigation model (WHATWG HTML 6.6.2: a
// showing popover's focus navigation scope is inserted after its invoker), the content sits right after
// its trigger: Tab out of its last tabbable element (or the content itself, when nothing in it is
// tabbable) goes to the first tabbable element after the trigger, and Shift+Tab out of its first one
// (or the content itself) goes to the trigger. Shared with the React lane's Popover.tsx.
const tabbableCandidates = 'a[href],area[href],button,input,select,textarea,iframe,summary,[tabindex],[contenteditable]:not([contenteditable="false"])'
const tabbable = element => {
  if (element.tabIndex < 0 || element.matches(':disabled')) return false
  if (element instanceof HTMLInputElement && element.type === 'hidden') return false
  for (let node = element; node; node = node.parentElement) if (node.hidden || node.inert === true) return false
  return typeof element.checkVisibility !== 'function' || element.checkVisibility({visibilityProperty: true, checkVisibilityCSS: true})
}
const tabbablesIn = root => [...root.querySelectorAll(tabbableCandidates)].filter(tabbable)
// The first tabbable element after the trigger in document order, skipping this content and any other
// popover content not around the trigger. Inside a modal layer the search stays in that layer, so the
// layer's own trap owns its boundary. Null when nothing follows: the trigger is last, and the key is
// left alone. The content is already last in <body>, so the browser's Tab goes past the end of the page
// (or the layer's trap wraps), where native Tab from the trigger goes. Focusing the trigger first would
// send the browser's Tab straight back into the content, the next thing after the trigger.
const tabbableAfter = (trigger, content) => {
  const scope = trigger.closest('[aria-modal="true"]') ?? trigger.ownerDocument
  return tabbablesIn(scope).find(candidate => {
    if (!(trigger.compareDocumentPosition(candidate) & Node.DOCUMENT_POSITION_FOLLOWING)) return false
    if (trigger.contains(candidate) || content.contains(candidate)) return false
    const owner = candidate.closest('.hl-popover__content')
    return !owner || owner.contains(trigger)
  }) ?? null
}
// Handles (prevents) the key only when it moves focus itself; Tab within the content stays native.
function routeTab(event, trigger, content) {
  if (event.key !== 'Tab' || event.defaultPrevented || !trigger?.isConnected || !content) return
  const target = event.target
  if (!(target instanceof HTMLElement) || !content.contains(target)) return
  const inside = tabbablesIn(content)
  if (event.shiftKey) {
    if (target !== content && target !== inside[0]) return
    event.preventDefault()
    trigger.focus()
    return
  }
  if (target !== (inside.length ? inside.at(-1) : content)) return
  const next = tabbableAfter(trigger, content)
  if (!next) return
  event.preventDefault()
  next.focus()
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
  const onPointer = event => {
    if (!content.contains(event.target) && !trigger?.contains(event.target)) dismiss()
  }
  const onViewport = () => reposition()
  // Blazor Server renders over a connection: the content's children can arrive after this call and the
  // trigger reference can be missing or replaced by a re-render. Both are read when a key arrives.
  const currentTrigger = () => (trigger?.isConnected ? trigger : null) ?? (options.triggerId ? document.getElementById(options.triggerId) : null)
  const onContentKey = event => routeTab(event, currentTrigger(), content)
  content.addEventListener('keydown', onContentKey)
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
  return {
    dispose() {
      cancelAnimationFrame(frame)
      content.removeEventListener('keydown', onContentKey)
      document.removeEventListener('keydown', onKeyCapture, true)
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer, true)
      window.removeEventListener('resize', onViewport)
      window.removeEventListener('scroll', onViewport, true)
      delete content.dataset.hlLayer
      if (content.isConnected && placeholder.isConnected) placeholder.before(content)
      placeholder.remove()
      trigger?.focus()
    }
  }
}
