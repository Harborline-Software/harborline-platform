import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { Host } from './setup'

// Contract: focus is never stranded, and the tree is one tab stop (APG treeview).
// The treeitem is the focus target (APG treeview); its buttons are pointer-only.
const item = (label: string) => screen.getByText(label).closest<HTMLElement>('[role="treeitem"]')!

describe('LayersRail focus', () => {
  it('moves focus to the lens that was active when Exit lens removes itself', async () => {
    render(<Host/>)
    const exit = screen.getByRole('button', { name: 'Exit lens (back to Layout)' })
    exit.focus()
    await userEvent.setup().keyboard('[Enter]')
    expect(screen.queryByRole('status')).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Show Layout lens on the canvas' }))
  })

  it('is one tab stop: Tab from the focused treeitem leaves the tree, and its buttons are not tab stops', async () => {
    const user = userEvent.setup()
    render(<><Host/><button type="button">After</button></>)
    item('Root').focus()
    await user.tab()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: 'After' }))
    expect(screen.getByRole('button', { name: 'Collapse' })).toHaveAttribute('tabindex', '-1')
    for (const button of screen.getByRole('tree').querySelectorAll('button')) expect(button).toHaveAttribute('tabindex', '-1')
  })

  it('selects exactly once for Enter or Space on the focused treeitem', async () => {
    const user = userEvent.setup()
    const selected: string[] = []
    render(<Host onSelect={id => selected.push(id)}/>)
    item('Child').focus()
    await user.keyboard('[Enter]')
    expect(selected).toEqual(['child'])
    await user.keyboard('[Space]')
    expect(selected).toEqual(['child', 'child'])
    expect(document.activeElement).toBe(item('Child'))
  })

  it('selects once when the treeitem itself is activated, by a click on it or an assistive technology', () => {
    const selected: string[] = []
    render(<Host onSelect={id => selected.push(id)}/>)
    fireEvent.click(item('Child'))
    expect(selected).toEqual(['child'])
    fireEvent.click(screen.getByText('Child').closest('button')!)
    expect(selected).toEqual(['child', 'child'])
  })

  it('leaves Alt, Ctrl and Meta chords to the browser', () => {
    const selected: string[] = []
    render(<Host onSelect={id => selected.push(id)}/>)
    item('Child').focus()
    for (const chord of [{ key: 'ArrowLeft', altKey: true }, { key: 'Home', ctrlKey: true }, { key: 'Enter', metaKey: true }]) {
      const event = new KeyboardEvent('keydown', { ...chord, bubbles: true, cancelable: true })
      item('Child').dispatchEvent(event)
      expect(event.defaultPrevented, chord.key).toBe(false)
    }
    expect(document.activeElement).toBe(item('Child'))
    expect(selected).toEqual([])
  })

  it('hands focus to the treeitem when a pointer clicks its node button or disclosure, and the disclosure does not select', async () => {
    const user = userEvent.setup()
    const selected: string[] = []
    render(<Host onSelect={id => selected.push(id)}/>)
    await user.click(screen.getByText('Sibling'))
    expect(document.activeElement).toBe(item('Sibling'))
    await user.click(screen.getByRole('button', { name: 'Collapse' }))
    expect(document.activeElement).toBe(item('Root'))
    expect(item('Root')).toHaveAttribute('aria-selected', 'false')
    expect(selected).toEqual(['sibling'])
  })

  it('names each treeitem by its label alone and describes it by its provenance', () => {
    render(<Host/>)
    const root = screen.getByRole('treeitem', { name: 'Root' })
    expect(root).toHaveAttribute('aria-labelledby', root.querySelector('.hl-layers-rail__node-label')!.id)
    expect(root).toHaveAccessibleDescription('tenant')
  })

  it('moves focus to the visible parent when collapsing hides the focused node', async () => {
    render(<Host/>)
    item('Child').focus()
    act(() => { fireEvent.click(screen.getByRole('button', { name: 'Collapse' })) })
    expect(screen.queryByText('Child')).toBeNull()
    expect(document.activeElement).toBe(item('Root'))
  })

  it('moves focus to the first node when the host removes the focused one, only if focus was lost', () => {
    const nodes = [{ id: 'a', kind: 'field', label: 'A', depth: 0, parentId: null }, { id: 'b', kind: 'field', label: 'B', depth: 0, parentId: null }]
    const view = render(<><Host nodes={nodes}/><button type="button">Elsewhere</button></>)
    item('A').focus()
    view.rerender(<><Host nodes={nodes.slice(1)}/><button type="button">Elsewhere</button></>)
    expect(document.activeElement).toBe(item('B'))
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
