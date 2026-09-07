import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ShellRail, splitPinned, validateNav } from '../ShellRail'
import type { ShellWorkspaceViewModel } from '../pack-navigation-mapper'

const ws: ShellWorkspaceViewModel = { id: 'operations', label: 'Operations', count: undefined, createActions: [], documentSpine: [], recent: [], groups: [
  { id: 'portfolio', label: 'Portfolio', items: [
    { id: 'overview', label: 'Overview' },
    { id: 'inspections', label: 'Inspections', count: 218, threads: [
      { id: 's1', label: 'Richmond retry audit', active: true, actions: [{ id: 'rename', label: 'Rename' }, { id: 'archive', label: 'Archive' }, { id: 'delete', label: 'Delete', destructive: true }] },
      { id: 's2', label: 'SLA sweep' },
    ] },
  ] },
  { id: 'work', label: 'Work', items: [{ id: 'workorders', label: 'Work orders', count: 42 }] },
] }
const noop = { onPinToggle: vi.fn(), pinnedItemIds: [] as string[], pinCap: 8 }

describe('ShellRail', () => {
  it('validates identity and shape', () => {
    expect(() => validateNav([ws])).not.toThrow()
    expect(() => validateNav([ws, { ...ws }])).toThrow('duplicate-nav-identity')
    expect(() => validateNav([{ ...ws, id: 'w', groups: [{ id: 'g', label: 'G', items: [{ id: 'a', label: 'A' }, { id: 'a', label: 'B' }] }] }])).toThrow('duplicate-nav-identity')
    expect(() => validateNav([{ ...ws, id: 'w', groups: [{ id: 'g', label: 'G', items: [{ id: 'a', label: 'A', threads: [{ id: 'g', label: 'clash' }] }] }] }])).toThrow('duplicate-nav-identity')
    expect(() => validateNav([{ ...ws, id: 'w', groups: [{ id: 'g', label: 'G', items: [{ id: 'a', label: 'A', threads: [{ id: 't', label: 'T', threads: [] } as never] }] }] }])).toThrow('unsupported-nav-shape')
  })
  it('moves pinned items into the pinned zone without copying', () => {
    const { pinned, groups } = splitPinned(ws, ['inspections', 'ghost'])
    expect(pinned.map(i => i.id)).toEqual(['inspections'])
    expect(groups.flatMap(g => g.items).map(i => i.id)).toEqual(['overview', 'workorders'])
    render(<ShellRail workspace={ws} activeItemId="inspections" {...noop} pinnedItemIds={['inspections']} />)
    expect(screen.getAllByText('Inspections')).toHaveLength(1)
    expect(screen.getByText('Pinned')).toBeInTheDocument()
    expect(document.querySelectorAll('[aria-current="page"]')).toHaveLength(1)
  })
  it('pin toggle neither navigates nor loses focus and emits one request', () => {
    const navigate = vi.fn(); const pin = vi.fn()
    render(<ShellRail workspace={ws} onNavigate={navigate} onPinToggle={pin} pinnedItemIds={[]} pinCap={8} />)
    const toggle = screen.getByRole('button', { name: 'Pin Inspections' })
    fireEvent.click(toggle)
    expect(pin).toHaveBeenCalledExactlyOnceWith('inspections', true)
    expect(navigate).not.toHaveBeenCalled()
  })
  it('renders declared item threads and emits activate without routing', () => {
    const activate = vi.fn()
    render(<ShellRail workspace={ws} {...noop} onThreadActivate={activate} />)
    fireEvent.click(screen.getByRole('button', { name: 'Richmond retry audit' }))
    expect(activate).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 's1' }), 'inspections')
  })
  it('opens the kebab menu with destructive entries after a divider and emits the action', () => {
    const action = vi.fn()
    render(<ShellRail workspace={ws} {...noop} onThreadAction={action} />)
    fireEvent.click(screen.getAllByRole('button', { name: 'Session options' })[0])
    const menu = screen.getByRole('menu')
    const items = screen.getAllByRole('menuitem')
    expect(items.map(i => i.textContent)).toEqual(['Rename', 'Archive', 'Delete'])
    expect(items[2].dataset.destructive).toBe('true')
    fireEvent.click(items[1])
    expect(action).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 's1' }), 'archive')
    expect(menu).not.toBeInTheDocument()
  })
  it('inline rename emits commit on Enter and cancel on Escape without changing the label', () => {
    const renamed = vi.fn(); const cancelled = vi.fn()
    const editing: ShellWorkspaceViewModel = { ...ws, id: 'w', label: 'W', groups: [{ id: 'g', label: 'G', items: [{ id: 'a', label: 'A', threads: [{ id: 't', label: 'Old name', editing: true }] }] }] }
    render(<ShellRail workspace={editing} {...noop} onThreadRename={renamed} onThreadRenameCancel={cancelled} />)
    const input = screen.getByRole('textbox')
    expect(input).toHaveValue('Old name')
    fireEvent.change(input, { target: { value: 'Trip audit' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(renamed).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ id: 't' }), 'Trip audit')
    fireEvent.keyDown(input, { key: 'Escape' })
    expect(cancelled).toHaveBeenCalledTimes(1)
  })
})
