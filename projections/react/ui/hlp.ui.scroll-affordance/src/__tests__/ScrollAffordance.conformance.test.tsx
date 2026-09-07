import { act, fireEvent, render, screen } from '@testing-library/react'
import * as React from 'react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ScrollAffordance } from '../ScrollAffordance'
import { fixture, sharedCases } from './fixtures'

function geometry(element: HTMLElement, values: { clientHeight?: number; clientWidth?: number; scrollHeight?: number; scrollWidth?: number }): void {
  for (const [key, value] of Object.entries(values)) {
    Object.defineProperty(element, key, { configurable: true, value })
  }
}

async function remeasure(): Promise<void> {
  await act(async () => { window.dispatchEvent(new Event('resize')) })
}

describe('ScrollAffordance shared fixtures', () => {
  it('consumes every frozen shared case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'scroll-affordance.component-defaults',
      'scroll-affordance.component-horizontal',
      'scroll-affordance.component-vertical',
      'scroll-affordance.component-no-overflow',
      'scroll-affordance.component-overflow-focus',
      'scroll-affordance.component-keyboard',
      'scroll-affordance.component-snap',
      'scroll-affordance.component-fade',
      'scroll-affordance.component-live-status',
      'scroll-affordance.component-rtl',
      'scroll-affordance.component-host-ref',
      'scroll-affordance.component-replacement',
      'scroll-affordance.component-projection-equivalence',
    ])
  })

  it('renders defaults, both axes, snapping, children, host attributes, and the forwarded region ref', () => {
    fixture(sharedCases, 'scroll-affordance.component-defaults')
    fixture(sharedCases, 'scroll-affordance.component-horizontal')
    fixture(sharedCases, 'scroll-affordance.component-vertical')
    fixture(sharedCases, 'scroll-affordance.component-snap')
    const owner = fixture(sharedCases, 'scroll-affordance.component-host-ref').input['data-owner'] as string
    const ref = React.createRef<HTMLDivElement>()
    const { rerender } = render(
      <ScrollAffordance ariaLabel="Results" className="consumer" data-owner={owner} ref={ref} snap>
        <span>Latest result</span>
      </ScrollAffordance>,
    )
    const region = screen.getByRole('group', { name: 'Results' })
    expect(region).toHaveClass('hl-scroll-affordance--horizontal', 'hl-scroll-affordance--snap', 'consumer')
    expect(region).toHaveAttribute('data-owner', owner)
    expect(ref.current).toBe(region)
    expect(screen.getByText('Latest result')).toBeInTheDocument()

    rerender(<ScrollAffordance ariaLabel="Timeline" orientation="vertical"><span>Vertical</span></ScrollAffordance>)
    expect(screen.getByRole('group', { name: 'Timeline' })).toHaveClass('hl-scroll-affordance--vertical')
  })

  it('enters the tab order, masks hidden content, announces position, and handles axis keys only when overflowing', async () => {
    fixture(sharedCases, 'scroll-affordance.component-no-overflow')
    fixture(sharedCases, 'scroll-affordance.component-overflow-focus')
    fixture(sharedCases, 'scroll-affordance.component-keyboard')
    fixture(sharedCases, 'scroll-affordance.component-fade')
    fixture(sharedCases, 'scroll-affordance.component-live-status')
    vi.useFakeTimers()
    render(<ScrollAffordance ariaLabel="Seven items" fadeSize={32} itemCount={7}><span>Items</span></ScrollAffordance>)
    const region = screen.getByRole('group', { name: 'Seven items' })
    geometry(region, { clientWidth: 300, scrollWidth: 700 })
    await remeasure()
    expect(region).toHaveAttribute('tabindex', '0')
    expect(region.style.maskImage).toContain('32px')

    fireEvent.keyDown(region, { key: 'End' })
    expect(region.scrollLeft).toBe(400)
    const ignored = new KeyboardEvent('keydown', { key: 'PageDown', cancelable: true })
    region.dispatchEvent(ignored)
    expect(ignored.defaultPrevented).toBe(false)
    await act(async () => { vi.runAllTimers() })
    expect(screen.getByText(/7/)).toHaveAttribute('aria-live', 'polite')
    expect(screen.getByText(/7/)).toHaveAttribute('aria-atomic', 'true')
    vi.useRealTimers()
  })

  it('preserves logical RTL scrolling and replacement without duplicated caller handlers', async () => {
    fixture(sharedCases, 'scroll-affordance.component-rtl')
    fixture(sharedCases, 'scroll-affordance.component-replacement')
    fixture(sharedCases, 'scroll-affordance.component-projection-equivalence')
    const onKeyDown = vi.fn()
    const { rerender } = render(
      <HarborlineLocaleProvider direction="rtl" locale="ar-SA">
        <ScrollAffordance ariaLabel="النتائج" dir="rtl" onKeyDown={onKeyDown}><span>قديم</span></ScrollAffordance>
      </HarborlineLocaleProvider>,
    )
    const region = screen.getByRole('group', { name: 'النتائج' })
    geometry(region, { clientWidth: 100, scrollWidth: 500 })
    await remeasure()
    fireEvent.keyDown(region, { key: 'ArrowLeft' })
    expect(onKeyDown).toHaveBeenCalledOnce()
    expect(region.scrollLeft).toBeLessThan(0)

    for (let index = 0; index < 96; index += 1) {
      rerender(<ScrollAffordance ariaLabel="Results"><span>{`Result ${index}`}</span></ScrollAffordance>)
    }
    expect(screen.getByText('Result 95')).toBeInTheDocument()
  })

  it.each([
    ['', 24, undefined, 'missing-accessible-name'],
    ['Results', -1, undefined, 'invalid-fade-size'],
    ['Results', Number.NaN, undefined, 'invalid-fade-size'],
    ['Results', 24, 1.5, 'invalid-item-count'],
  ] as const)('rejects invalid contract input', (ariaLabel, fadeSize, itemCount, message) => {
    expect(() => render(<ScrollAffordance ariaLabel={ariaLabel} fadeSize={fadeSize} itemCount={itemCount} />)).toThrow(message)
  })
})
