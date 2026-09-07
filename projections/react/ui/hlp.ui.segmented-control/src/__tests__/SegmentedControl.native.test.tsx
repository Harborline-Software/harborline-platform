import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SegmentedControl, type SegmentedOption } from '../SegmentedControl'

const options: readonly SegmentedOption[] = [
  { value: 'day', label: 'Day' },
  { value: 'week', label: 'Week', disabled: true },
  { value: 'month', label: <><span aria-hidden="true">▦</span> Month</>, accessibleLabel: 'Calendar month', automationId: 'month-option' },
]

describe('SegmentedControl React projection', () => {
  it('renders one named radiogroup with checked and roving radio state', () => {
    render(<SegmentedControl accessibleName="View" options={options} value="day" onValueChange={() => undefined} data-case="calendar" />)
    const group = screen.getByRole('radiogroup', { name: 'View' })
    const radios = screen.getAllByRole('radio')
    expect(group).toHaveAttribute('data-case', 'calendar')
    expect(radios).toHaveLength(3)
    expect(radios[0]).toHaveAttribute('aria-checked', 'true')
    expect(radios[0]).toHaveAttribute('tabindex', '0')
    expect(radios[1]).toBeDisabled()
    expect(radios[2]).toHaveAttribute('aria-label', 'Calendar month')
    expect(radios[2]).toHaveAttribute('data-automation-id', 'month-option')
  })

  it('falls back to the first enabled tab stop and requests pointer activation once', () => {
    const changed = vi.fn()
    render(<SegmentedControl accessibleName="View" options={options} value="missing" onValueChange={changed} />)
    const day = screen.getByRole('radio', { name: 'Day' })
    expect(day).toHaveAttribute('tabindex', '0')
    fireEvent.click(day)
    expect(changed).toHaveBeenCalledOnce()
    expect(changed).toHaveBeenCalledWith('day')
  })

  it('wraps, skips disabled options, and handles Home and End in LTR', () => {
    const changed = vi.fn()
    render(<SegmentedControl accessibleName="View" options={options} value="day" onValueChange={changed} />)
    const day = screen.getByRole('radio', { name: 'Day' })
    const month = screen.getByRole('radio', { name: 'Calendar month' })
    day.focus()
    fireEvent.keyDown(day, { key: 'ArrowRight' })
    expect(month).toHaveFocus()
    expect(changed).toHaveBeenLastCalledWith('month')
    fireEvent.keyDown(month, { key: 'ArrowDown' })
    expect(day).toHaveFocus()
    expect(changed).toHaveBeenLastCalledWith('day')
    fireEvent.keyDown(day, { key: 'End' })
    expect(month).toHaveFocus()
    fireEvent.keyDown(month, { key: 'Home' })
    expect(day).toHaveFocus()
    expect(changed).toHaveBeenCalledTimes(4)
  })

  it('mirrors horizontal arrows while retaining vertical progression in RTL', () => {
    const changed = vi.fn()
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <SegmentedControl accessibleName="العرض" options={options} value="day" onValueChange={changed} />
      </HarborlineLocaleProvider>,
    )
    const day = screen.getByRole('radio', { name: 'Day' })
    fireEvent.keyDown(day, { key: 'ArrowLeft' })
    expect(changed).toHaveBeenLastCalledWith('month')
    fireEvent.keyDown(day, { key: 'ArrowDown' })
    expect(changed).toHaveBeenLastCalledWith('month')
    fireEvent.keyDown(day, { key: 'ArrowRight' })
    expect(changed).toHaveBeenLastCalledWith('month')
    expect(screen.getByRole('radiogroup')).toHaveAttribute('dir', 'rtl')
  })

  it('native-disables the group, exposes touch sizing, and validates inputs', () => {
    const changed = vi.fn()
    const { rerender } = render(<SegmentedControl accessibleName="View" disabled fullWidth options={options} size="touch" value="day" onValueChange={changed} />)
    expect(screen.getAllByRole('radio')).toEqual(expect.arrayContaining(screen.getAllByRole('radio').filter(node => (node as HTMLButtonElement).disabled)))
    expect(screen.getByRole('radiogroup')).toHaveAttribute('data-hl-touch', 'true')
    expect(screen.getByRole('radiogroup')).toHaveAttribute('data-hl-full-width', 'true')
    fireEvent.click(screen.getByRole('radio', { name: 'Day' }))
    expect(changed).not.toHaveBeenCalled()
    expect(() => rerender(<SegmentedControl accessibleName=" " options={options} value="day" onValueChange={changed} />)).toThrow('accessible-segmented-control-label-required')
  })
})
