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

const IDLE_STATE: ScrollAffordanceState = {
  canScroll: false,
  atStart: true,
  atEnd: true,
  maskImage: undefined,
  srMessage: '',
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
  const [state, setState] = React.useState<ScrollAffordanceState>(IDLE_STATE)
  const debounceRef = React.useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const { t } = useHarborlineStrings()

  const measure = React.useCallback(() => {
    const element = ref.current
    if (!element) return

    const horizontal = orientation === 'horizontal'
    const rtl = isRightToLeft(element, horizontal)
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

    setState(previous => ({ canScroll, atStart, atEnd, maskImage, srMessage: previous.srMessage }))
    if (debounceRef.current !== undefined) clearTimeout(debounceRef.current)
    debounceRef.current = setTimeout(() => {
      if (!canScroll) {
        setState(previous => ({ ...previous, srMessage: '' }))
        return
      }

      let srMessage: string
      if (itemCount !== undefined && itemCount > 0) {
        const unitSize = scrollSize / itemCount
        const start = Math.min(itemCount, Math.max(1, Math.round(position / unitSize) + 1))
        const visibleCount = Math.max(1, Math.round(clientSize / unitSize))
        const end = Math.min(itemCount, start + visibleCount - 1)
        srMessage = start >= end
          ? t('scrollAffordance.showingOne', { index: start, total: itemCount })
          : t('scrollAffordance.showingRange', { start, end, total: itemCount })
      } else if (atEnd) {
        srMessage = t('scrollAffordance.endReached')
      } else if (atStart) {
        srMessage = t('scrollAffordance.moreAvailable')
      } else {
        const percent = Math.round((position / (scrollSize - clientSize)) * 100)
        srMessage = t('scrollAffordance.percentScrolled', { percent })
      }
      setState(previous => ({ ...previous, srMessage }))
    }, announceDebounceMs)
  }, [announceDebounceMs, fadeSize, itemCount, orientation, ref, t])

  React.useEffect(() => {
    const element = ref.current
    if (!element) return

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
    }
  }, [measure, ref])

  return state
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
