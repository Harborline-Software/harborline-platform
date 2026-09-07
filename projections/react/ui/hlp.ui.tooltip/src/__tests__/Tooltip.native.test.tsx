import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { Tooltip } from '../Tooltip'
import { qualityCases } from './fixtures'

describe('Tooltip React projection', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('remains visible until both hover and focus ownership are released', () => {
    render(<Tooltip content="Details" delayDuration={0}><button type="button">Trigger</button></Tooltip>)
    const trigger = screen.getByRole('button', { name: 'Trigger' })
    const wrapper = trigger.parentElement!

    fireEvent.mouseEnter(wrapper)
    fireEvent.focus(trigger)
    act(() => vi.runOnlyPendingTimers())
    expect(screen.getByRole('tooltip')).toBeInTheDocument()

    fireEvent.mouseLeave(wrapper)
    expect(screen.getByRole('tooltip')).toBeInTheDocument()
    fireEvent.blur(trigger)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('cancels pending work on unmount without a stale callback', () => {
    const onOpenChange = vi.fn()
    const { unmount } = render(
      <Tooltip content="Details" delayDuration={400} onOpenChange={onOpenChange}><button type="button">Trigger</button></Tooltip>,
    )
    fireEvent.mouseEnter(screen.getByRole('button', { name: 'Trigger' }).parentElement!)
    unmount()
    act(() => vi.runOnlyPendingTimers())
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('cancels pending work on Escape without emitting a close for hidden content', () => {
    const onOpenChange = vi.fn()
    render(<Tooltip content="Details" delayDuration={400} onOpenChange={onOpenChange}><button type="button">Trigger</button></Tooltip>)
    const trigger = screen.getByRole('button', { name: 'Trigger' })
    fireEvent.focus(trigger)
    fireEvent.keyDown(trigger, { key: 'Escape' })
    act(() => vi.runOnlyPendingTimers())
    expect(screen.queryByRole('tooltip')).toBeNull()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('retains skipDelayDuration as a compatibility no-op', () => {
    render(<Tooltip content="Details" open skipDelayDuration={250}><button type="button">Trigger</button></Tooltip>)
    expect(screen.getByRole('tooltip')).toHaveTextContent('Details')
  })

  it('retains multiple children without inventing an inaccessible linkage', () => {
    render(
      <Tooltip content="Details" open>
        <button type="button">First</button>
        <button type="button">Second</button>
      </Tooltip>,
    )
    expect(screen.getByRole('button', { name: 'First' })).not.toHaveAttribute('aria-describedby')
    expect(screen.getByRole('button', { name: 'Second' })).not.toHaveAttribute('aria-describedby')
  })

  it('preserves ordinary trigger interaction', () => {
    const onClick = vi.fn()
    render(<Tooltip content="Details"><button onClick={onClick} type="button">Trigger</button></Tooltip>)
    fireEvent.click(screen.getByRole('button', { name: 'Trigger' }))
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('keeps the requested physical side and inherited direction under RTL', () => {
    render(<div dir="rtl"><Tooltip content="تفاصيل" open side="left"><button type="button">المشغل</button></Tooltip></div>)
    expect(screen.getByRole('tooltip')).toHaveAttribute('data-side', 'left')
    expect(screen.getByRole('tooltip').closest('[dir="rtl"]')).toBeInTheDocument()
  })

  it('publishes reflow, logical, token, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-tooltip-surface')
    expect(css).toContain('max-inline-size')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('inset-inline-start')
    expect(css).toContain("[data-theme='dark'] .hl-tooltip__content")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/animation:\s*none/)
  })
})
  it('consumes every frozen quality case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'tooltip.quality.relationship',
      'tooltip.quality.keyboard',
      'tooltip.quality.focus',
      'tooltip.quality.dismissal',
      'tooltip.quality.reflow',
      'tooltip.quality.caller-copy',
      'tooltip.quality.rtl',
      'tooltip.quality.pseudo',
      'tooltip.quality.light-dark',
      'tooltip.quality.tokens',
      'tooltip.quality.forced-colors',
      'tooltip.quality.reduced-motion',
      'tooltip.quality.visual-parity',
    ])
  })
