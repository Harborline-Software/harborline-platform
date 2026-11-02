import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export type ScrollAffordanceOrientation = 'horizontal' | 'vertical'

export interface UseScrollAffordanceOptions {
  orientation?: ScrollAffordanceOrientation
  fadeSize?: number
  itemCount?: number
  announceDebounceMs?: number
}

export interface ScrollAffordanceState {
  canScroll: boolean
  atStart: boolean
  atEnd: boolean
  maskImage: string | undefined
  srMessage: string
}

const IDLE_GEOMETRY: Omit<ScrollAffordanceState, 'srMessage'> = {
  canScroll: false,
  atStart: true,
  atEnd: true,
  maskImage: undefined,
}

interface Announcement {
  key: string
  values?: Record<string, number>
}

function isRightToLeft(element: HTMLElement, horizontal: boolean): boolean {
  return horizontal
    && typeof getComputedStyle === 'function'
    && getComputedStyle(element).direction === 'rtl'
}

function logicalPosition(element: HTMLElement, horizontal: boolean, rtl: boolean): number {
  const raw = horizontal ? element.scrollLeft : element.scrollTop
  return rtl ? -raw : raw
}

function setLogicalPosition(
  element: HTMLElement,
  horizontal: boolean,
  rtl: boolean,
  value: number,
): void {
  const physical = rtl ? -value : value
  if (horizontal) element.scrollLeft = physical
  else element.scrollTop = physical
}

export function useScrollAffordance(
  ref: React.RefObject<HTMLElement | null>,
  options: UseScrollAffordanceOptions = {},
): ScrollAffordanceState {
  const {
    orientation = 'horizontal',
    fadeSize = 24,
    itemCount,
    announceDebounceMs = 300,
  } = options
  const [geometry, setGeometry] = React.useState<Omit<ScrollAffordanceState, 'srMessage'>>(IDLE_GEOMETRY)
  const [announcement, setAnnouncement] = React.useState<Announcement | undefined>(undefined)
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const previousRef = React.useRef<{ raw: number; scrollSize: number; clientSize: number } | undefined>(undefined)
  const { t } = useHarborlineStrings()

  const measure = React.useCallback(() => {
    const element = ref.current
    if (!element) return

    const horizontal = orientation === 'horizontal'
    const rtl = isRightToLeft(element, horizontal)
    const raw = horizontal ? element.scrollLeft : element.scrollTop
    const position = logicalPosition(element, horizontal, rtl)
    const scrollSize = horizontal ? element.scrollWidth : element.scrollHeight
    const clientSize = horizontal ? element.clientWidth : element.clientHeight
    const canScroll = scrollSize > clientSize + 1
    const atStart = position <= 1
    const atEnd = position >= scrollSize - clientSize - 1

    let maskImage: string | undefined
    if (canScroll) {
      const towardEnd = horizontal ? (rtl ? 'left' : 'right') : 'bottom'
      const towardStart = horizontal ? (rtl ? 'right' : 'left') : 'top'
      const startFade = !atStart
      const endFade = !atEnd
      if (startFade && endFade) {
        maskImage = `linear-gradient(to ${towardEnd}, transparent, black ${fadeSize}px, black calc(100% - ${fadeSize}px), transparent)`
      } else if (startFade) {
        maskImage = `linear-gradient(to ${towardEnd}, transparent, black ${fadeSize}px)`
      } else if (endFade) {
        maskImage = `linear-gradient(to ${towardStart}, transparent, black ${fadeSize}px)`
      }
    }

    // Revision 2 (T-1135): WAI-ARIA and the APG keep a live region in the DOM, empty, and write only changes
    // that follow load. The first measurement, the initial scroll and every resize or layout re-measure (the
    // scroll or client size changed) leave the message alone. A measurement whose sizes match the previous one
    // while the position moved is a scroll (pointer, wheel, touch or the keyboard handler) and announces.
    // Without overflow the message is empty.
    const previous = previousRef.current
    const scrolled = previous !== undefined
      && previous.scrollSize === scrollSize
      && previous.clientSize === clientSize
      && previous.raw !== raw
    previousRef.current = { raw, scrollSize, clientSize }

    setGeometry({ canScroll, atStart, atEnd, maskImage })
    if (!canScroll) {
      if (debounceRef.current !== undefined) clearTimeout(debounceRef.current)
      debounceRef.current = undefined
      setAnnouncement(undefined)
      return
    }
    // A resize that lands while a scroll's announcement is pending reschedules it with the current geometry,
    // so the message never describes a position the layout no longer has. Otherwise a resize stays quiet.
    if (!scrolled && debounceRef.current === undefined) return
    if (debounceRef.current !== undefined) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => {
      debounceRef.current = undefined
      if (itemCount !== undefined && itemCount > 0) {
        const unitSize = scrollSize / itemCount
        const start = Math.min(itemCount, Math.max(1, Math.round(position / unitSize) + 1))
        const visibleCount = Math.max(1, Math.round(clientSize / unitSize))
        const end = Math.min(itemCount, start + visibleCount - 1)
        setAnnouncement(start >= end
          ? { key: 'scrollAffordance.showingOne', values: { index: start, total: itemCount } }
          : { key: 'scrollAffordance.showingRange', values: { start, end, total: itemCount } })
      } else if (atEnd) {
        setAnnouncement({ key: 'scrollAffordance.endReached' })
      } else if (atStart) {
        setAnnouncement({ key: 'scrollAffordance.moreAvailable' })
      } else {
        const percent = Math.round((position / (scrollSize - clientSize)) * 100)
        setAnnouncement({ key: 'scrollAffordance.percentScrolled', values: { percent } })
      }
    }, announceDebounceMs)
  }, [announceDebounceMs, fadeSize, itemCount, orientation, ref])

  React.useEffect(() => {
    const element = ref.current
    if (!element) return

    // A new observation (a new element or axis) starts from load again: its first measurement is quiet.
    previousRef.current = undefined
    measure()
    element.addEventListener('scroll', measure, { passive: true })
    const observer = typeof ResizeObserver === 'undefined' ? undefined : new ResizeObserver(measure)
    observer?.observe(element)
    window.addEventListener('resize', measure)

    return () => {
      element.removeEventListener('scroll', measure)
      observer?.disconnect()
      window.removeEventListener('resize', measure)
      if (debounceRef.current !== undefined) clearTimeout(debounceRef.current)
      debounceRef.current = undefined
    }
  }, [measure, ref])

  // The announcement is held as a key and values and resolved here, so a catalog or locale change
  // relocalizes the current message without announcing a new position.
  const srMessage = announcement === undefined ? '' : t(announcement.key, announcement.values)
  return React.useMemo(() => ({ ...geometry, srMessage }), [geometry, srMessage])
}

/** Move a scroll region in physical arrow-key directions using logical coordinates. */
export function handleScrollAffordanceKeyDown(
  event: React.KeyboardEvent,
  element: HTMLElement,
  orientation: ScrollAffordanceOrientation = 'horizontal',
): void {
  const horizontal = orientation === 'horizontal'
  const rtl = isRightToLeft(element, horizontal)
  const clientSize = horizontal ? element.clientWidth : element.clientHeight
  const scrollSize = horizontal ? element.scrollWidth : element.scrollHeight
  const step = Math.max(40, clientSize * 0.8)
  const maximum = Math.max(0, scrollSize - clientSize)
  const current = logicalPosition(element, horizontal, rtl)
  let target: number | undefined

  if (horizontal && event.key === 'ArrowRight') target = current + (rtl ? -step : step)
  else if (horizontal && event.key === 'ArrowLeft') target = current + (rtl ? step : -step)
  else if (!horizontal && event.key === 'ArrowDown') target = current + step
  else if (!horizontal && event.key === 'ArrowUp') target = current - step
  else if (event.key === 'Home') target = 0
  else if (event.key === 'End') target = maximum

  if (target === undefined) return
  event.preventDefault()
  setLogicalPosition(element, horizontal, rtl, Math.min(maximum, Math.max(0, target)))
}
