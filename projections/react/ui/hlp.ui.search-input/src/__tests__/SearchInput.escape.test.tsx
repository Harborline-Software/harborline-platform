import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SearchInput } from '../SearchInput'

// Contract: clearing publishes the empty string exactly once without waiting. Escape on a nonempty
// search is a clear, owned by the searchbox (marked handled so an enclosing overlay leaves it alone);
// on an empty search Escape belongs to the overlay.
beforeEach(() => vi.useFakeTimers())
afterEach(() => vi.useRealTimers())

describe('SearchInput Escape', () => {
  it('clears a nonempty search at once, cancels the pending publish, keeps focus and marks the key handled', () => {
    const published: string[] = []
    render(<SearchInput value="oak" onChange={value => published.push(value)} debounceMs={200}/>)
    const box = screen.getByRole('searchbox')
    fireEvent.change(box, { target: { value: 'oak tr' } })
    // Focus starts elsewhere, so only the clear can put it on the searchbox.
    box.blur()
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    act(() => { box.dispatchEvent(escape) })
    expect(published).toEqual([''])
    expect(escape.defaultPrevented).toBe(true)
    expect(box).toHaveValue('')
    expect(document.activeElement).toBe(box)
    act(() => { vi.advanceTimersByTime(500) })
    expect(published).toEqual([''])
  })

  it('leaves Escape on an empty search to the enclosing overlay', () => {
    const published: string[] = []
    render(<SearchInput value="" onChange={value => published.push(value)}/>)
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    act(() => { screen.getByRole('searchbox').dispatchEvent(escape) })
    expect(escape.defaultPrevented).toBe(false)
    expect(published).toEqual([])
  })

  it('leaves Escape during IME composition to the composition', () => {
    const published: string[] = []
    render(<SearchInput value="oak" onChange={value => published.push(value)}/>)
    const box = screen.getByRole('searchbox')
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true, isComposing: true })
    act(() => { box.dispatchEvent(escape) })
    expect(escape.defaultPrevented).toBe(false)
    expect(published).toEqual([])
    expect(box).toHaveValue('oak')
  })

  it('leaves an Escape another handler already claimed alone', () => {
    const published: string[] = []
    render(<SearchInput value="oak" onChange={value => published.push(value)}/>)
    const box = screen.getByRole('searchbox')
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })
    escape.preventDefault()
    act(() => { box.dispatchEvent(escape) })
    expect(published).toEqual([])
    expect(box).toHaveValue('oak')
  })
})
