import * as React from 'react'

export function DockDivider({ vertical, fraction, minimum, secondMinimum, change, reset }: { vertical: boolean; fraction: number; minimum: number; secondMinimum: number; change: (ratio: number) => void; reset: () => void }) {
  const element = React.useRef<HTMLDivElement>(null)
  const [extent, setExtent] = React.useState(0)
  const edit = React.useRef<{ fraction: number; position?: number; pointer?: number } | null>(null)
  React.useLayoutEffect(() => {
    const parent = element.current!.parentElement!
    const measure = () => { const rect = parent.getBoundingClientRect(); setExtent(vertical ? rect.width : rect.height) }
    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(parent)
    return () => observer?.disconnect()
  }, [vertical])
  const total = Math.max(extent, minimum + secondMinimum)
  const maximum = total - secondMinimum
  const value = Math.max(minimum, Math.min(maximum, fraction * total))
  const update = (next: number) => change(Math.max(minimum, Math.min(maximum, next)) / total)
  const finish = (cancel: boolean) => { const start = edit.current; edit.current = null; if (cancel && start) change(start.fraction) }
  const keyDown = (event: React.KeyboardEvent) => {
    // Dividers handle BARE keys only. A Mod chord on a focused divider is the shell's (Mod+Enter, Mod+0,
    // Mod+K...), so it must not be consumed here — the Blazor mirror is DockDivider.razor KeyDown.
    if (event.ctrlKey || event.metaKey || event.altKey) return
    const increase = vertical ? 'ArrowRight' : 'ArrowDown', decrease = vertical ? 'ArrowLeft' : 'ArrowUp'
    if (![increase, decrease, 'Home', 'End', 'Enter', 'Escape', '0'].includes(event.key)) return
    event.preventDefault(); event.stopPropagation()
    if (event.key === '0') { finish(false); reset(); return }
    if (event.key === 'Enter' || event.key === 'Escape') { finish(event.key === 'Escape'); return }
    edit.current ??= { fraction }
    update(event.key === 'Home' ? minimum : event.key === 'End' ? maximum : value + (event.key === increase ? 1 : -1) * (event.shiftKey ? 32 : 8))
  }
  return <div ref={element} className="hl-app-shell__dock-divider" role="separator" tabIndex={0} aria-label="Resize panels" aria-description="Arrow keys resize; Shift uses larger steps. Enter commits, Escape cancels, 0 resets panels." aria-orientation={vertical ? 'vertical' : 'horizontal'} aria-valuemin={minimum} aria-valuemax={maximum} aria-valuenow={value} onKeyDown={keyDown} onBlur={() => finish(false)}
    onPointerDown={event => { if (event.button !== 0 || edit.current?.pointer !== undefined) return; event.preventDefault(); event.currentTarget.focus(); event.currentTarget.setPointerCapture(event.pointerId); edit.current = { fraction, pointer: event.pointerId, position: vertical ? event.clientX : event.clientY } }}
    onPointerMove={event => { const start = edit.current; if (start?.position === undefined || start.pointer !== event.pointerId) return; const direction = vertical && getComputedStyle(event.currentTarget).direction === 'rtl' ? -1 : 1; update(start.fraction * total + direction * ((vertical ? event.clientX : event.clientY) - start.position)) }}
    onPointerUp={event => { if (edit.current?.pointer !== event.pointerId) return; finish(false); event.currentTarget.releasePointerCapture(event.pointerId) }} onPointerCancel={event => { if (edit.current?.pointer === event.pointerId) finish(true) }} onLostPointerCapture={event => { if (edit.current?.pointer === event.pointerId) finish(true) }} />
}
