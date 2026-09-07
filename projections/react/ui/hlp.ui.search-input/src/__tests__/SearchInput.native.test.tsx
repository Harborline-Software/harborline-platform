import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SearchInput } from '../SearchInput'

afterEach(() => vi.useRealTimers())
describe('SearchInput React projection', () => {
  it('coalesces edits and clears immediately', () => {
    vi.useFakeTimers()
    const publish = vi.fn()
    render(<SearchInput value="" onChange={publish}/>)
    const input = screen.getByRole('searchbox')
    fireEvent.change(input, { target: { value: 'o' } })
    fireEvent.change(input, { target: { value: 'oak' } })
    expect(input.closest('[aria-busy]')).toHaveAttribute('aria-busy', 'true')
    act(() => vi.advanceTimersByTime(200))
    expect(publish).toHaveBeenCalledWith('oak')
    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }))
    expect(publish).toHaveBeenLastCalledWith('')
  })

  it('uses locale catalog copy and logical direction', () => {
    render(<HarborlineLocaleProvider locale="ar-SA" catalog={{ 'structural.search.placeholder': 'بحث…', 'structural.search.clear': 'مسح' }}><SearchInput value="بلوط" onChange={() => undefined}/></HarborlineLocaleProvider>)
    expect(screen.getByRole('searchbox', { name: 'بحث…' }).closest('[dir]')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('button', { name: 'مسح' })).toBeInTheDocument()
  })
})
