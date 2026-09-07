import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { act, fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { SideNav, type SideNavStructure } from '../SideNav'
import { sharedCases } from './fixtures'

interface ContractDocument { cases: Array<{ id: string }> }

const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../specs/modules/ui/hlp.ui.side-nav/interface.yaml'), 'utf8')) as ContractDocument

describe('SideNav revision-1 shared fixtures', () => {
  it('consumes every behavior case in frozen contract order', () => {
    expect(sharedCases.map(value => value.id)).toEqual(contract.cases.map(value => value.id))
  })

  it('renders named flat and grouped navigation in source order', () => {
    const { rerender } = render(<SideNav items={[]} />)
    expect(screen.getByRole('navigation', { name: 'Navigation' })).toBeInTheDocument()

    rerender(<SideNav navigationLabel="Workspace" items={[
      { id: 'primary', label: 'Primary', items: [{ id: 'home', label: 'Home' }, { id: 'reports', label: 'Reports' }] },
      { id: 'secondary', label: 'Secondary', items: [{ id: 'settings', label: 'Settings' }] },
    ]} />)
    const nav = screen.getByRole('navigation', { name: 'Workspace' })
    expect(within(nav).getByText('Primary')).toHaveAttribute('id')
    expect(within(nav).getByText('Secondary')).toHaveAttribute('id')
    expect(within(nav).getAllByRole('button').map(value => value.textContent)).toEqual(['Home', 'Reports', 'Settings'])
  })

  it('opens the active descendant path and toggles branches without leaf activation', async () => {
    const user = userEvent.setup()
    const activated = vi.fn()
    render(<SideNav activeItemId="audit" items={[
      { id: 'reports', label: 'Reports', children: [{ id: 'detail', label: 'Detail', children: [{ id: 'audit', label: 'Audit' }] }] },
    ]} onItemActivate={activated} />)
    expect(screen.getByRole('button', { name: 'Reports' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'Detail' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'Audit' })).toHaveAttribute('aria-current', 'page')
    await user.click(screen.getByRole('button', { name: 'Reports' }))
    expect(screen.queryByRole('button', { name: 'Audit' })).not.toBeInTheDocument()
    expect(activated).not.toHaveBeenCalled()
  })

  it('requests enabled button and link leaves exactly once', async () => {
    const user = userEvent.setup()
    const activated = vi.fn()
    render(<SideNav items={[
      { id: 'home', label: 'Home' },
      { id: 'reports', label: 'Reports', href: '#reports' },
    ]} onItemActivate={activated} />)
    await user.click(screen.getByRole('button', { name: 'Home' }))
    fireEvent.click(screen.getByRole('link', { name: 'Reports' }))
    expect(activated.mock.calls.map(call => call[0].id)).toEqual(['home', 'reports'])
    expect(screen.getByRole('link', { name: 'Reports' })).toHaveAttribute('href', '#reports')
  })

  it('makes disabled links inert for pointer and keyboard navigation', () => {
    const activated = vi.fn()
    render(<SideNav items={[{ id: 'reports', label: 'Reports', href: '/reports', disabled: true }]} onItemActivate={activated} />)
    const disabledLink = screen.getByText('Reports').closest('a')!
    expect(disabledLink).toHaveAttribute('aria-disabled', 'true')
    expect(disabledLink).not.toHaveAttribute('href')
    expect(disabledLink).toHaveAttribute('tabindex', '-1')
    expect(fireEvent.click(disabledLink)).toBe(false)
    fireEvent.keyDown(disabledLink, { key: 'Enter' })
    fireEvent.keyDown(disabledLink, { key: ' ' })
    expect(activated).not.toHaveBeenCalled()
  })

  it('renders a trailing interactive accessory as a sibling after the navigation control', () => {
    render(<SideNav items={[{
      id: 'reports',
      label: 'Reports',
      badge: <button aria-label="Pin reports" type="button">Pin</button>,
    }]} />)
    const control = screen.getByRole('button', { name: 'Reports' })
    const accessory = screen.getByRole('button', { name: 'Pin reports' })
    expect(control.contains(accessory)).toBe(false)
    expect(control.nextElementSibling).toContainElement(accessory)
  })

  it('preserves collapsed names and logical-side tooltips', () => {
    vi.useFakeTimers()
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <SideNav collapsed items={[{ id: 'reports', label: 'التقارير', icon: <span>R</span> }]} />
      </HarborlineLocaleProvider>,
    )
    const nav = screen.getByRole('navigation')
    const control = screen.getByRole('button', { name: 'التقارير' })
    expect(nav).toHaveAttribute('dir', 'rtl')
    expect(screen.queryByText('التقارير')).not.toBeInTheDocument()
    fireEvent.mouseEnter(control.parentElement!)
    act(() => vi.advanceTimersByTime(400))
    expect(screen.getByRole('tooltip')).toHaveAttribute('data-side', 'left')
    fireEvent.keyDown(control.parentElement!, { key: 'Escape' })
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    vi.useRealTimers()
  })

  // 282 s10: the Blazor lane once spelled the item list hl-side-nav__items, the badge slot
  // hl-side-nav__trailing and the collapsed root hl-side-nav--collapsed, none of them defined by
  // the authority stylesheet. This asserts the one spelling in this lane; the Blazor case asserts
  // the same fixture row in the other.
  it('side-nav.class-vocabulary', () => {
    const expected = sharedCases.find(value => value.id === 'side-nav.class-vocabulary')!.expected as {
      listClasses: string[]
      nestedListClasses: string[]
      activeControlClasses: string[]
      branchControlClasses: string[]
      accessoryClasses: string[]
      collapsedRootClasses: string[]
      collapsedTooltipClasses: { react: string[] }
    }
    const items = [{
      id: 'group',
      label: 'Group',
      items: [{ id: 'reports', label: 'Reports', children: [{ id: 'daily', label: 'Daily', badge: <span>3</span> }] }],
    }] as SideNavStructure
    const { container, rerender } = render(<SideNav activeItemId="daily" items={items} />)
    const lists = [...container.querySelectorAll('ul')]
    expect([...lists[0]!.classList]).toEqual(expected.listClasses)
    expect([...lists[1]!.classList]).toEqual(expected.nestedListClasses)
    expect([...screen.getByRole('button', { name: 'Daily' }).classList]).toEqual(expected.activeControlClasses)
    expect([...screen.getByRole('button', { name: 'Reports' }).classList]).toEqual(expected.branchControlClasses)
    expect([...container.querySelector('.hl-side-nav__accessory')!.classList]).toEqual(expected.accessoryClasses)

    rerender(<SideNav activeItemId="daily" collapsed items={items} />)
    expect([...screen.getByRole('navigation').classList]).toEqual(expected.collapsedRootClasses)
    expect([...screen.getByRole('button', { name: 'Reports' }).parentElement!.classList])
      .toEqual(expected.collapsedTooltipClasses.react)
  })

  it('rejects duplicate identities, missing labels, and href branches', () => {
    expect(() => render(<SideNav items={[{ id: 'same', label: 'One' }, { id: 'same', label: 'Two' }]} />)).toThrow('duplicate-id')
    expect(() => render(<SideNav items={[{ id: 'blank', label: ' ' }]} />)).toThrow('missing-label')
    const invalid = [{ id: 'reports', label: 'Reports', href: '/reports', children: [{ id: 'child', label: 'Child' }] }] as SideNavStructure
    expect(() => render(<SideNav items={invalid} />)).toThrow('invalid-action')
  })

  it('replaces active, collapsed, and tree state without stale callbacks', async () => {
    const user = userEvent.setup()
    const activated = vi.fn()
    const rendered = render(<SideNav items={[{ id: 'old', label: 'Old' }]} onItemActivate={activated} />)
    await user.click(screen.getByRole('button', { name: 'Old' }))
    rendered.rerender(<SideNav activeItemId="new" collapsed items={[{ id: 'new', label: 'New', icon: <span>N</span> }]} onItemActivate={activated} />)
    expect(screen.queryByRole('button', { name: 'Old' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'New' })).toHaveAttribute('aria-current', 'page')
    expect(activated).toHaveBeenCalledOnce()
  })
})
