import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { Tooltip, type TooltipSide } from '../Tooltip'
import { fixture, sharedCases } from './fixtures'

function wrapperFor(name = 'Trigger'): HTMLElement {
  return screen.getByRole('button', { name }).parentElement!
}

describe('Tooltip shared fixtures', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('tooltip.closed', () => {
    fixture(sharedCases, 'tooltip.closed')
    render(<Tooltip content="Details"><button type="button">Trigger</button></Tooltip>)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('tooltip.delayed-hover', () => {
    fixture(sharedCases, 'tooltip.delayed-hover')
    const onOpenChange = vi.fn()
    render(<Tooltip content="Details" delayDuration={400} onOpenChange={onOpenChange}><button type="button">Trigger</button></Tooltip>)
    fireEvent.mouseEnter(wrapperFor())
    act(() => vi.advanceTimersByTime(399))
    expect(screen.queryByRole('tooltip')).toBeNull()
    act(() => vi.advanceTimersByTime(1))
    expect(screen.getByRole('tooltip')).toHaveTextContent('Details')
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(true)
  })

  it('tooltip.cancel-delay', () => {
    fixture(sharedCases, 'tooltip.cancel-delay')
    const onOpenChange = vi.fn()
    render(<Tooltip content="Details" delayDuration={400} onOpenChange={onOpenChange}><button type="button">Trigger</button></Tooltip>)
    const wrapper = wrapperFor()
    fireEvent.mouseEnter(wrapper)
    act(() => vi.advanceTimersByTime(200))
    fireEvent.mouseLeave(wrapper)
    act(() => vi.runOnlyPendingTimers())
    expect(screen.queryByRole('tooltip')).toBeNull()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('tooltip.focus-blur', () => {
    fixture(sharedCases, 'tooltip.focus-blur')
    render(<Tooltip content="Details" delayDuration={0}><button type="button">Trigger</button></Tooltip>)
    const trigger = screen.getByRole('button', { name: 'Trigger' })
    fireEvent.focus(trigger)
    act(() => vi.runOnlyPendingTimers())
    expect(screen.getByRole('tooltip')).toBeInTheDocument()
    fireEvent.blur(trigger)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('tooltip.controlled', () => {
    fixture(sharedCases, 'tooltip.controlled')
    const onOpenChange = vi.fn()
    render(<Tooltip content="Details" delayDuration={0} onOpenChange={onOpenChange} open={false}><button type="button">Trigger</button></Tooltip>)
    fireEvent.mouseEnter(wrapperFor())
    act(() => vi.runOnlyPendingTimers())
    expect(onOpenChange).toHaveBeenCalledWith(true)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('tooltip.default-open', () => {
    fixture(sharedCases, 'tooltip.default-open')
    render(<Tooltip content="Details" defaultOpen><button type="button">Trigger</button></Tooltip>)
    expect(screen.getByRole('tooltip')).toBeInTheDocument()
  })

  it('tooltip.escape', () => {
    fixture(sharedCases, 'tooltip.escape')
    const onOpenChange = vi.fn()
    const parentKeyDown = vi.fn()
    render(
      <div onKeyDown={parentKeyDown}>
        <Tooltip content="Details" defaultOpen onOpenChange={onOpenChange}><button type="button">Trigger</button></Tooltip>
      </div>,
    )
    fireEvent.keyDown(screen.getByRole('button', { name: 'Trigger' }), { key: 'Escape' })
    expect(screen.queryByRole('tooltip')).toBeNull()
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(false)
    expect(parentKeyDown).not.toHaveBeenCalled()
  })

  it('tooltip.relationship', () => {
    fixture(sharedCases, 'tooltip.relationship')
    render(<Tooltip content="Details" open><button aria-describedby="existing" type="button">Trigger</button></Tooltip>)
    const trigger = screen.getByRole('button', { name: 'Trigger' })
    const tooltip = screen.getByRole('tooltip')
    expect(trigger).toHaveAttribute('aria-describedby', `existing ${tooltip.id}`)
    expect(trigger.parentElement).not.toHaveAttribute('tabindex')
  })

  it('tooltip.sides', () => {
    fixture(sharedCases, 'tooltip.sides')
    const { rerender } = render(<Tooltip content="Details" open><button type="button">Trigger</button></Tooltip>)
    expect(screen.getByRole('tooltip')).toHaveAttribute('data-side', 'top')
    for (const side of ['top', 'right', 'bottom', 'left'] as TooltipSide[]) {
      rerender(<Tooltip content="Details" open side={side}><button type="button">Trigger</button></Tooltip>)
      expect(screen.getByRole('tooltip')).toHaveAttribute('data-side', side)
    }
  })

  it('tooltip.host-classes', () => {
    fixture(sharedCases, 'tooltip.host-classes')
    render(<Tooltip className="content-consumer" content="Details" open triggerClassName="trigger-consumer"><button type="button">Trigger</button></Tooltip>)
    expect(screen.getByRole('tooltip')).toHaveClass('content-consumer')
    expect(wrapperFor()).toHaveClass('trigger-consumer')
    expect(screen.getByRole('tooltip')).not.toHaveClass('trigger-consumer')
  })

  it('tooltip.projection-equivalence', () => {
    fixture(sharedCases, 'tooltip.projection-equivalence')
    render(<Tooltip content="Details" open side="right"><button type="button">Trigger</button></Tooltip>)
    const tooltip = screen.getByRole('tooltip')
    expect(screen.getByRole('button', { name: 'Trigger' })).toHaveAttribute('aria-describedby', tooltip.id)
    expect(tooltip).toHaveAttribute('data-side', 'right')
  })
})
