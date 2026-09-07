import * as React from 'react'

import {
  handleScrollAffordanceKeyDown,
  useScrollAffordance,
  type ScrollAffordanceOrientation,
} from '@harborline-platform/hlp.ui.use-scroll-affordance'

export interface ScrollAffordanceProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'onScroll'> {
  ariaLabel: string
  orientation?: ScrollAffordanceOrientation
  snap?: boolean
  fadeSize?: number
  itemCount?: number
}

function classes(...values: Array<string | undefined | false>): string {
  return values.filter(Boolean).join(' ')
}

function assignRef<T>(ref: React.ForwardedRef<T>, value: T | null): void {
  if (typeof ref === 'function') ref(value)
  else if (ref) ref.current = value
}

function assertProps(ariaLabel: string, fadeSize: number, itemCount: number | undefined): void {
  if (ariaLabel.trim().length === 0) throw new Error('missing-accessible-name')
  if (!Number.isFinite(fadeSize) || fadeSize < 0) throw new Error('invalid-fade-size')
  if (itemCount !== undefined && (!Number.isInteger(itemCount) || itemCount < 0)) {
    throw new Error('invalid-item-count')
  }
}

export const ScrollAffordance = React.forwardRef<HTMLDivElement, ScrollAffordanceProps>(function ScrollAffordance(
  {
    ariaLabel,
    children,
    className,
    fadeSize = 24,
    itemCount,
    onKeyDown,
    orientation = 'horizontal',
    snap = false,
    style,
    ...hostAttributes
  },
  forwardedRef,
) {
  assertProps(ariaLabel, fadeSize, itemCount)
  const regionRef = React.useRef<HTMLDivElement | null>(null)
  const state = useScrollAffordance(regionRef, { fadeSize, itemCount, orientation })
  const setRegionRef = React.useCallback((value: HTMLDivElement | null) => {
    regionRef.current = value
    assignRef(forwardedRef, value)
  }, [forwardedRef])

  return (
    <>
      <div
        {...hostAttributes}
        aria-label={ariaLabel}
        className={classes(
          'hl-scroll-affordance',
          `hl-scroll-affordance--${orientation}`,
          snap && 'hl-scroll-affordance--snap',
          className,
        )}
        data-hl-can-scroll={state.canScroll || undefined}
        data-hl-orientation={orientation}
        onKeyDown={event => {
          handleScrollAffordanceKeyDown(event, regionRef.current ?? event.currentTarget, orientation)
          onKeyDown?.(event)
        }}
        ref={setRegionRef}
        role="group"
        style={{
          ...style,
          ...(state.maskImage
            ? { maskImage: state.maskImage, WebkitMaskImage: state.maskImage }
            : { maskImage: undefined, WebkitMaskImage: undefined }),
        }}
        tabIndex={state.canScroll ? 0 : undefined}
      >
        {children}
      </div>
      <div aria-atomic="true" aria-live="polite" className="hl-scroll-affordance__status">
        {state.srMessage}
      </div>
    </>
  )
})

export type { ScrollAffordanceOrientation }
