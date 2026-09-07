import * as React from 'react'

/**
 * Observe document pointer events and invoke the latest callback only when the
 * target is outside every supplied live boundary.
 */
export function useOutsideClick(
  refs:
    | React.RefObject<HTMLElement | null>
    | ReadonlyArray<React.RefObject<HTMLElement | null>>,
  onOutside: (event: MouseEvent | PointerEvent) => void,
  options: { enabled?: boolean; eventType?: 'mousedown' | 'pointerdown' } = {},
): void {
  const { enabled = true, eventType = 'mousedown' } = options
  if (eventType !== 'mousedown' && eventType !== 'pointerdown') {
    throw new Error('unsupported-event-type')
  }

  const onOutsideRef = React.useRef(onOutside)
  React.useEffect(() => {
    onOutsideRef.current = onOutside
  })

  const refList = React.useMemo(
    () => (Array.isArray(refs) ? refs : [refs]),
    // Preserve the pinned membership semantics: changing only the containing
    // array does not rebind, while replacing any boundary does.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    Array.isArray(refs) ? refs : [refs],
  )

  React.useEffect(() => {
    if (!enabled) return

    const handleEvent = (event: MouseEvent | PointerEvent) => {
      const target = event.target
      const inside = target instanceof Node && refList.some(ref => ref.current?.contains(target))
      if (!inside) onOutsideRef.current(event)
    }

    document.addEventListener(eventType, handleEvent)
    return () => document.removeEventListener(eventType, handleEvent)
  }, [enabled, eventType, refList])
}
