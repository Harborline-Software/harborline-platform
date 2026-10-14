import { useState } from 'react'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AppShellRev6TabHost, type AppShellRev6Presentation } from '../AppShellRev6'
import { presentation, runScenario, transition } from '../../../../../../shared/ui/hlp.ui.app-shell/rev6/tab-identity-state.mjs'

const owner = { person: 'alice', tenant: 'north', workspace: 'work' }
const initial = () => runScenario([{ type: 'identity', owner }, { type: 'open', kind: 'Details', target: 'record-a', preview: true }, { type: 'open', kind: 'Assistant', target: 'conversation-a' }])
function Host() {
  const [state, setState] = useState(initial)
  return <AppShellRev6TabHost shellId="test" owner={state.owner} presentationOwner={state.owner} presentation={presentation(state) as AppShellRev6Presentation} onIntent={event => setState(current => transition(current, event))} />
}
describe('rev6 native tab-host slice', () => {
  it('renders no previous private presentation after an owner change or sign-out', () => {
    const props = { shellId: 'test', presentationOwner: owner, presentation: presentation(initial()) as AppShellRev6Presentation, onIntent: vi.fn() }
    const rendered = render(<AppShellRev6TabHost {...props} owner={{ ...owner, person: 'bob' }} />)
    expect(screen.queryByRole('tablist')).toBeNull()
    expect(screen.queryByText(/record-a/)).toBeNull()
    rendered.rerender(<AppShellRev6TabHost {...props} owner={null} />)
    expect(screen.getByRole('main')).toHaveTextContent('Signed out')
    expect(screen.queryByRole('tablist')).toBeNull()
  })
  it('moves tab focus and selection with Home, End and wrapped arrows', () => {
    render(<Host />)
    const tabs = screen.getAllByRole('tab')
    tabs[1].focus()
    fireEvent.keyDown(tabs[1], { key: 'ArrowRight' })
    expect(tabs[0]).toHaveFocus()
    expect(tabs[0]).toHaveAttribute('aria-selected', 'true')
    fireEvent.keyDown(tabs[0], { key: 'End' })
    expect(tabs[1]).toHaveFocus()
    fireEvent.keyDown(tabs[1], { key: 'Home' })
    expect(tabs[0]).toHaveFocus()
  })
  it('focuses and traps the dirty prompt, cancels with Escape, and requests Save without destroying the editor', () => {
    render(<Host />)
    fireEvent.click(screen.getAllByRole('tab')[0])
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'private draft' } })
    const close = screen.getByRole('button', { name: 'Close tab' })
    close.focus(); fireEvent.click(close)
    let dialog = screen.getByRole('dialog')
    expect(within(dialog).getByRole('button', { name: 'Save' })).toHaveFocus()
    fireEvent.keyDown(dialog, { key: 'Tab', shiftKey: true })
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus()
    fireEvent.keyDown(dialog, { key: 'Escape' })
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(close).toHaveFocus()
    fireEvent.click(close)
    dialog = screen.getByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(screen.getByRole('textbox')).toHaveValue('private draft')
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(within(dialog).getByRole('button', { name: 'Discard' })).toHaveFocus()
    expect(screen.getAllByRole('tab')).toHaveLength(2)
  })
  it('focuses the surviving tab after discard instead of a reused Close button', () => {
    render(<Host />)
    fireEvent.click(screen.getAllByRole('tab')[0])
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'draft' } })
    screen.getByRole('button', { name: 'Close tab' }).focus()
    fireEvent.click(screen.getByRole('button', { name: 'Close tab' }))
    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Discard' }))
    expect(screen.getByRole('tab', { name: /Assistant/ })).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Close tab' })).not.toHaveFocus()
  })
  it('focuses a surviving tab, then main after clean Close removes its trigger', () => {
    render(<Host />)
    screen.getByRole('button', { name: 'Close tab' }).focus()
    fireEvent.click(screen.getByRole('button', { name: 'Close tab' }))
    expect(screen.getByRole('tab', { name: /Details/ })).toHaveFocus()
    screen.getByRole('button', { name: 'Close tab' }).focus()
    fireEvent.click(screen.getByRole('button', { name: 'Close tab' }))
    expect(screen.getByRole('main')).toHaveFocus()
  })
  it('keeps draft focus on an ordinary edit', () => {
    render(<Host />)
    const editor = screen.getByRole('textbox')
    editor.focus()
    fireEvent.change(editor, { target: { value: 'typing' } })
    expect(editor).toHaveFocus()
  })
  it('focuses main after discarding the final editor', () => {
    const state = runScenario([{ type: 'identity', owner }, { type: 'open', kind: 'Details', target: 'record-a' }, { type: 'edit', id: '["Details","record-a"]', draft: 'draft' }, { type: 'close', id: '["Details","record-a"]' }])
    const view = presentation(state) as AppShellRev6Presentation
    const props = { shellId: 'test', owner, presentationOwner: owner, onIntent: vi.fn() }
    const rendered = render(<AppShellRev6TabHost {...props} presentation={view} />)
    rendered.rerender(<AppShellRev6TabHost {...props} presentation={presentation(transition(state, { type: 'resolve-close', choice: 'discard' })) as AppShellRev6Presentation} />)
    expect(screen.getByRole('main')).toHaveFocus()
  })
  it('focuses the surviving tab after successful save acknowledgment', () => {
    const id = '["Details","record-a"]'
    const state = runScenario([{ type: 'identity', owner }, { type: 'open', kind: 'Assistant', target: 'conversation-a' }, { type: 'open', kind: 'Details', target: 'record-a' }, { type: 'edit', id, draft: 'draft' }, { type: 'close', id }, { type: 'resolve-close', choice: 'save' }])
    const view = presentation(state) as AppShellRev6Presentation
    const props = { shellId: 'test', owner, presentationOwner: owner, onIntent: vi.fn() }
    const rendered = render(<AppShellRev6TabHost {...props} presentation={view} />)
    rendered.rerender(<AppShellRev6TabHost {...props} presentation={presentation(transition(state, { type: 'saved', ...view.saveRequest })) as AppShellRev6Presentation} />)
    expect(screen.getByRole('tab', { name: /Assistant/ })).toHaveFocus()
  })
})
