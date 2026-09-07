const focusable = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'
export function connect(portalRoot, root, titleBar, options, callback) {
  const previous = document.activeElement
  const placeholder = document.createComment('hl-window-portal')
  portalRoot.before(placeholder)
  document.body.append(portalRoot)
  let current = {...options}
  let drag = null, resize = null
  const onTitleDown = event => {
    if (!current.draggable || event.target.closest('.hl-window__actions')) return
    drag = { id:event.pointerId, x:event.clientX, y:event.clientY, top:current.top, left:current.left }
    titleBar.setPointerCapture?.(event.pointerId)
  }
  const onRootDown = event => {
    const handle = event.target.closest('[data-resize-edge]')
    if (!current.resizable || !handle) return
    event.stopPropagation()
    resize = { id:event.pointerId, edge:handle.dataset.resizeEdge, x:event.clientX, y:event.clientY, width:current.width, height:current.height }
    handle.setPointerCapture?.(event.pointerId)
  }
  const onMove = event => {
    if (drag) callback.invokeMethodAsync('MoveFromJavaScriptAsync', drag.top + event.clientY-drag.y, drag.left + event.clientX-drag.x)
    if (resize) {
      const width = resize.edge.includes('e') ? Math.max(current.minWidth,event.clientX-resize.x+resize.width) : resize.width
      const height = resize.edge.includes('s') ? Math.max(current.minHeight,event.clientY-resize.y+resize.height) : resize.height
      callback.invokeMethodAsync('ResizeFromJavaScriptAsync', width, height)
    }
  }
  const cancel = () => { drag = null; resize = null }
  const onKey = event => {
    if (event.key === 'Escape') { event.stopPropagation(); callback.invokeMethodAsync('EscapeFromJavaScriptAsync'); return }
    if (!current.modal || event.key !== 'Tab') return
    const nodes = [...root.querySelectorAll(focusable)]
    if (nodes.length === 0) { event.preventDefault(); root.focus(); return }
    const first=nodes[0], last=nodes[nodes.length-1]
    if (event.shiftKey && document.activeElement===first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement===last) { event.preventDefault(); first.focus() }
  }
  titleBar.addEventListener('pointerdown',onTitleDown)
  root.addEventListener('pointerdown',onRootDown)
  root.addEventListener('pointermove',onMove)
  root.addEventListener('pointerup',cancel)
  root.addEventListener('pointercancel',cancel)
  document.addEventListener('keydown',onKey)
  if(current.autoFocus) requestAnimationFrame(()=>root.focus())
  return {
    update(next){ current={...next}; cancel() },
    dispose(){ titleBar.removeEventListener('pointerdown',onTitleDown); root.removeEventListener('pointerdown',onRootDown); root.removeEventListener('pointermove',onMove); root.removeEventListener('pointerup',cancel); root.removeEventListener('pointercancel',cancel); document.removeEventListener('keydown',onKey); if(portalRoot.isConnected&&placeholder.isConnected)placeholder.before(portalRoot); placeholder.remove(); previous?.focus?.() }
  }
}
