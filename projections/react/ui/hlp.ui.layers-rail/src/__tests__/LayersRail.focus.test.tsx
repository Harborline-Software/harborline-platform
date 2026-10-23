import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Host } from './setup'

// Contract: focus is never stranded, and the tree is one tab stop (APG treeview).
const control = (label: string) => screen.getByText(label).closest('button')!

describe('LayersRail focus', () => {
  it('moves focus to the lens that was active when Exit lens removes itself', async () => {
    render(<Host/>)
    const exit = screen.getByRole('button', { name: 'Exit lens (back to Layout)' })
    exit.focus()
    await userEvent.setup().keyboard('[Enter]')
    expect(screen.queryByRole('status')).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show Layout lens on the canvas' }))
  })

  it('is one tab stop: Tab from the focused node leaves the tree, and disclosures are not tab stops', async () => {
    const user = userEvent.setup()
    render(<><Host/><button type="button">After</button></>)
    control('Root').focus()
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'After' }))
    expect(screen.getByRole('button', { name: 'Collapse' })).toHaveAttribute('tabindex', '-1')
  })

  it('moves focus to the visible parent when collapsing hides the focused node', async () => {
    render(<Host/>)
    control('Child').focus()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Collapse' })) })
    expect(screen.queryByText('Child')).toBeNull()
    expect(document.activeElement).toBe(control('Root'))
  })

  it('moves focus to the first node when the host removes the focused one, only if focus was lost', () => {
    const nodes = [{ id: 'a', kind: 'field', label: 'A', depth: 0, parentId: null }, { id: 'b', kind: 'field', label: 'B', depth: 0, parentId: null }]
    const view = render(<><Host nodes={nodes}/><button type="button">Elsewhere</button></>)
    control('A').focus()
    view.rerender(<><Host nodes={nodes.slice(1)}/><button type="button">Elsewhere</button></>)
    expect(document.activeElement).toBe(control('B'))
  })

  it('leaves focus alone when the user is elsewhere and the focused node goes away', () => {
    const nodes = [{ id: 'a', kind: 'field', label: 'A', depth: 0, parentId: null }, { id: 'b', kind: 'field', label: 'B', depth: 0, parentId: null }]
    const view = render(<><Host nodes={nodes}/><button type="button">Elsewhere</button></>)
    const elsewhere = screen.getByRole('button', { name: 'Elsewhere' })
    elsewhere.focus()
    view.rerender(<><Host nodes={nodes.slice(1)}/><button type="button">Elsewhere</button></>)
    expect(document.activeElement).toBe(elsewhere)
  })
})
