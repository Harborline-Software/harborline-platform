const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

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
  const onKey = event => { if (event.key === 'Escape') { event.preventDefault(); dismiss() } }
  const onPointer = event => {
    if (!content.contains(event.target) && !trigger?.contains(event.target)) dismiss()
  }
  const onViewport = () => reposition()
  document.addEventListener('keydown', onKey)
  document.addEventListener('pointerdown', onPointer, true)
  window.addEventListener('resize', onViewport)
  window.addEventListener('scroll', onViewport, true)
  requestAnimationFrame(() => { reposition(); (content.querySelector(focusable) ?? content).focus() })
  return {
    dispose() {
      document.removeEventListener('keydown', onKey)
      document.removeEventListener('pointerdown', onPointer, true)
      window.removeEventListener('resize', onViewport)
      window.removeEventListener('scroll', onViewport, true)
      if (content.isConnected && placeholder.isConnected) placeholder.before(content)
      placeholder.remove()
      trigger?.focus()
    }
  }
}
