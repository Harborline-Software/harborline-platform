import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ScopeSwitcher, orderScopeOptions } from '../ScopeSwitcher'

const options = (n: number) => Array.from({ length: n }, (_, i) => ({ id: String.fromCharCode(97 + i), label: `Option ${String.fromCharCode(97 + i).toUpperCase()}` }))
const base = { switcherId: 'tenant', shellId: 'ops', scopeLabel: 'Tenant', presentation: 'header' as const }

describe('ScopeSwitcher', () => {
  it('orders active first, then pins in pin order, then the rest', () => {
    expect(orderScopeOptions(options(5), 'c', ['e']).map(o => o.id)).toEqual(['c', 'e', 'a', 'b', 'd'])
    expect(orderScopeOptions(options(5), 'e', ['e', 'b']).map(o => o.id)).toEqual(['e', 'b', 'a', 'c', 'd'])
  })
  it('renders a static label at one option and nothing at zero', () => {
    const { rerender } = render(<ScopeSwitcher {...base} options={options(1)} />)
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText('Option A')).toBeInTheDocument()
    rerender(<ScopeSwitcher {...base} options={[]} />)
    expect(screen.queryByText('Option A')).toBeNull()
  })
  it('hides entirely in rail presentation at one option', () => {
    const { container } = render(<ScopeSwitcher {...base} presentation="rail" options={options(1)} />)
    expect(container).toBeEmptyDOMElement()
  })
  it('opens a menuitemradio menu, selects once, closes, and restores trigger focus on Escape', async () => {
    const changed = vi.fn()
    render(<ScopeSwitcher {...base} options={options(3)} activeId="a" onActiveChange={changed} />)
    const trigger = screen.getByRole('button', { name: /Tenant/ })
    fireEvent.click(trigger)
    const items = screen.getAllByRole('menuitemradio')
    expect(items).toHaveLength(3)
    expect(items[0]).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(items[1])
    expect(changed).toHaveBeenCalledExactlyOnceWith('b')
    expect(screen.queryByRole('menu')).toBeNull()
    fireEvent.click(trigger)
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    expect(screen.queryByRole('menu')).toBeNull()
    await waitFor(() => expect(trigger).toHaveFocus())
  })
  it('renders the filter only above 7 options and filters by label substring', () => {
    const { rerender } = render(<ScopeSwitcher {...base} options={options(7)} />)
    fireEvent.click(screen.getByRole('button'))
    expect(screen.queryByRole('textbox')).toBeNull()
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' })
    rerender(<ScopeSwitcher {...base} options={options(8)} />)
    fireEvent.click(screen.getByRole('button'))
    const filter = screen.getByRole('textbox')
    fireEvent.change(filter, { target: { value: 'Option B' } })
    expect(screen.getAllByRole('menuitemradio')).toHaveLength(1)
  })
  it('caps uncontrolled pins at 4 and treats pinning as sort preference only', () => {
    const pinned = vi.fn(); const changed = vi.fn()
    render(<ScopeSwitcher {...base} options={options(6)} activeId="a" defaultPinnedIds={['b', 'c', 'd', 'e']} onPinnedChange={pinned} onActiveChange={changed} />)
    fireEvent.click(screen.getByRole('button'))
    const pinToggles = document.querySelectorAll('.hl-app-shell__switcher-menu .hl-app-shell__pin-toggle[title^="Pin "]')
    expect(pinToggles.length).toBeGreaterThan(0)
    fireEvent.click(pinToggles[0])
    expect(pinned).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
  })
  it('renders the directory entry as menu footer and invokes it', () => {
    const invoke = vi.fn()
    render(<ScopeSwitcher {...base} options={options(3)} directory={{ label: 'All tenants', invoke }} />)
    fireEvent.click(screen.getByRole('button'))
    fireEvent.click(screen.getByRole('menuitem', { name: 'All tenants' }))
    expect(invoke).toHaveBeenCalledTimes(1)
  })
  it('truncates the trigger label to one line and carries the full label in title', () => {
    render(<ScopeSwitcher {...base} options={[{ id: 'mta', label: 'Metro Transit Authority' }, { id: 'x', label: 'X' }]} activeId="mta" />)
    const label = screen.getByText('Metro Transit Authority')
    expect(label.className).toContain('hl-app-shell__switcher-label')
    expect(screen.getByRole('button').title).toBe('Metro Transit Authority')
  })
})
