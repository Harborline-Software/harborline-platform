import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MASTER_DETAIL_RAIL_QUERY } from '@harborline-platform/hlp.ui.use-can-show-master-detail'
import { DetailPanel } from '../DetailPanel'

const base = { label: 'Elevator 2', railCapable: true }

function stubMatchMedia(matcher: (query: string) => boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    value: vi.fn((query: string): MediaQueryList => ({
      matches: matcher(query),
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    })),
  })
}

describe('DetailPanel React projection', () => {
  it('defaults its docked-versus-modal gate to the capability policy (ticket 154: the panel CALLS the policy)', () => {
    // The rail matches: the policy grants master-detail, so the default presentation is docked.
    stubMatchMedia(query => query === MASTER_DETAIL_RAIL_QUERY)
    render(<DetailPanel label="Elevator 2" open><p>x</p></DetailPanel>)
    expect(screen.getByRole('complementary', { name: 'Elevator 2' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('refuses the rail through the policy when the rail query does not match (desktop width alone is not enough)', () => {
    // Desktop width matches but the rail does not (the 1280x500 class): the policy refuses,
    // so the default presentation is the modal sheet — the direction the old panel-owned
    // matchMedia gate and the pre-154 hook disagreed on.
    stubMatchMedia(query => query === '(min-width: 1280px)' && query !== MASTER_DETAIL_RAIL_QUERY)
    render(<DetailPanel label="Elevator 2" open><p>x</p></DetailPanel>)
    expect(screen.getByRole('dialog', { name: 'Elevator 2' })).toHaveAttribute('aria-modal', 'true')
    expect(screen.queryByRole('complementary')).toBeNull()
  })
  it('renders nothing closed and one named complementary open', () => {
    const { rerender, container } = render(<DetailPanel {...base}><p>Props</p></DetailPanel>)
    expect(container).toBeEmptyDOMElement()
    rerender(<DetailPanel {...base} open><p>Props</p></DetailPanel>)
    expect(screen.getByRole('complementary', { name: 'Elevator 2' })).toBeInTheDocument()
  })
  it('clamps width 220-720 with default 360', () => {
    const { rerender } = render(<DetailPanel {...base} open width={100}><p>x</p></DetailPanel>)
    const size = () => screen.getByRole('complementary').style.getPropertyValue('--hl-detail-panel-size')
    expect(size()).toBe('220px')
    rerender(<DetailPanel {...base} open><p>x</p></DetailPanel>)
    expect(size()).toBe('360px')
    rerender(<DetailPanel {...base} open width={4000}><p>x</p></DetailPanel>)
    expect(size()).toBe('720px')
  })
  it('close control emits once; controlled rendering follows the supplied value; dismissible false hides it', () => {
    const changed = vi.fn()
    const { rerender } = render(<DetailPanel {...base} open onOpenChange={changed}><p>x</p></DetailPanel>)
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(changed).toHaveBeenCalledExactlyOnceWith(false)
    expect(screen.getByRole('complementary')).toBeInTheDocument()
    rerender(<DetailPanel {...base} open dismissible={false}><p>x</p></DetailPanel>)
    expect(screen.queryByRole('button', { name: 'Close' })).toBeNull()
  })
  it('presents as a modal sheet below rail capability with Escape restore, keeping open state across the boundary', async () => {
    const changed = vi.fn()
    const { rerender } = render(<DetailPanel label="Elevator 2" railCapable={false} open onOpenChange={changed}><button>Inside</button></DetailPanel>)
    expect(screen.getByRole('dialog', { name: 'Elevator 2' })).toHaveAttribute('aria-modal', 'true')
    rerender(<DetailPanel label="Elevator 2" railCapable open onOpenChange={changed}><button>Inside</button></DetailPanel>)
    expect(screen.getByRole('complementary', { name: 'Elevator 2' })).toBeInTheDocument()
    expect(changed).not.toHaveBeenCalled()
    rerender(<DetailPanel label="Elevator 2" railCapable={false} open onOpenChange={changed}><button>Inside</button></DetailPanel>)
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    await waitFor(() => expect(changed).toHaveBeenCalledExactlyOnceWith(false))
  })
  it('throws content-required when open with nullish children and preserves host attributes', () => {
    expect(() => render(<DetailPanel {...base} open />)).toThrow('detail-panel-content-required')
    render(<DetailPanel {...base} open className="host" data-case="panel"><p>x</p></DetailPanel>)
    const aside = screen.getByRole('complementary')
    expect(aside.className).toContain('host')
    expect(aside.dataset.case).toBe('panel')
  })
})
