import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { APP_LAYOUT_RAIL_QUERY, AppLayout } from '../AppLayout'

describe('AppLayout React projection', () => {
  it('uses the Material expanded boundary and renders an open rail or no rail', () => {
    expect(APP_LAYOUT_RAIL_QUERY).toBe('(min-width: 840px)')
    const { rerender } = render(<AppLayout body={<div>Body</div>} header={<button>Bar</button>} sideNav={<button>Navigation</button>} railCapable sideNavOpen/>)
    expect(screen.getAllByRole('main')).toHaveLength(1)
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main')
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(document.querySelector('.hl-app-layout__frame')?.firstElementChild).toHaveClass('hl-app-layout__header')
    rerender(<AppLayout body={<div>Body</div>} header={<button>Bar</button>} sideNav={<button>Navigation</button>} railCapable sideNavOpen={false}/>)
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Navigation' })).toBeNull()
  })

  it('renders one modal drawer subtree and dismisses it with focus restoration', async () => {
    const changed = vi.fn()
    render(<AppLayout body="Body" sideNav={<a href="/home">Home</a>} railCapable={false} onMobileNavOpenChange={changed}/>)
    const trigger = screen.getByRole('button', { name: 'Navigation' })
    fireEvent.click(trigger)
    expect(changed).toHaveBeenLastCalledWith(true)
    expect(screen.getAllByRole('navigation')).toHaveLength(1)
    expect(screen.getByRole('dialog', { name: 'Navigation' })).toHaveAttribute('aria-modal', 'true')
    await waitFor(() => expect(screen.getByRole('link', { name: 'Home' })).toHaveFocus())
    fireEvent.keyDown(screen.getByRole('dialog'), { key: 'Escape' })
    expect(changed).toHaveBeenLastCalledWith(false)
    expect(screen.queryByRole('dialog')).toBeNull()
    await waitFor(() => expect(trigger).toHaveFocus())
  })

  it('keeps controlled mobile state host-owned and hides all navigation in hidden mode', () => {
    const changed = vi.fn()
    const { rerender } = render(<AppLayout body="Body" sideNav="Nav" railCapable={false} mobileNavOpen={false} onMobileNavOpenChange={changed}/>)
    fireEvent.click(screen.getByRole('button', { name: 'Navigation' }))
    expect(changed).toHaveBeenCalledWith(true)
    expect(screen.queryByRole('dialog')).toBeNull()
    rerender(<AppLayout body="Body" sideNav="Nav" sideNavMode="hidden" railCapable={false}/>)
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Navigation' })).toBeNull()
    expect(screen.getAllByRole('main')).toHaveLength(1)
  })
})
