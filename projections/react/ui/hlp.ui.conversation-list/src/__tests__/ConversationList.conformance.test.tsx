import * as React from 'react'

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ConversationList, type ConversationSummary } from '../ConversationList'
import { fixture, sharedCases } from './fixtures'

const conversations: ConversationSummary[] = [
  { id: 'c1', title: 'Original', timestamp: '2 min ago', preview: 'First preview' },
  { id: 'c2', title: 'Second', timestamp: '1 hr ago', preview: 'Second preview' },
]

describe('Conversation List shared fixtures', () => {
  it('conversation-list.empty', () => {
    fixture(sharedCases, 'conversation-list.empty')
    render(<ConversationList conversations={[]} onSelect={vi.fn()} />)
    expect(screen.getByText('No conversations yet.')).toBeInTheDocument()
    expect(screen.queryByRole('list')).toBeNull()
  })

  it('conversation-list.rows', () => {
    fixture(sharedCases, 'conversation-list.rows')
    render(<ConversationList conversations={conversations} onSelect={vi.fn()} />)
    const list = screen.getByRole('list', { name: 'Conversations' })
    expect(list.tagName).toBe('UL')
    expect(within(list).getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText('Original').closest('button')?.parentElement?.tagName).toBe('LI')
  })

  it('conversation-list.active', () => {
    fixture(sharedCases, 'conversation-list.active')
    render(<ConversationList activeId="c2" conversations={conversations} onSelect={vi.fn()} />)
    expect(screen.getByRole('button', { name: /Original/ })).not.toHaveAttribute('aria-current')
    expect(screen.getByRole('button', { name: /Second/ })).toHaveAttribute('aria-current', 'true')
  })

  it('conversation-list.select', async () => {
    fixture(sharedCases, 'conversation-list.select')
    const onSelect = vi.fn()
    render(<ConversationList conversations={conversations} onSelect={onSelect} />)
    await userEvent.setup().click(screen.getByRole('button', { name: /Original/ }))
    expect(onSelect).toHaveBeenCalledOnce()
    expect(onSelect).toHaveBeenCalledWith('c1')
  })

  it('conversation-list.optional-actions', () => {
    fixture(sharedCases, 'conversation-list.optional-actions')
    render(<ConversationList conversations={conversations} onSelect={vi.fn()} />)
    expect(screen.queryByRole('button', { name: 'New conversation' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Rename: Original' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Delete: Original' })).toBeNull()
  })

  it('conversation-list.new', async () => {
    fixture(sharedCases, 'conversation-list.new')
    const onNew = vi.fn()
    render(<ConversationList conversations={conversations} onNew={onNew} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'New conversation' }))
    expect(onNew).toHaveBeenCalledOnce()
  })

  it('conversation-list.rename-commit', async () => {
    fixture(sharedCases, 'conversation-list.rename-commit')
    const onRename = vi.fn()
    render(<ConversationList conversations={conversations} onRename={onRename} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Rename: Original' }))
    const input = screen.getByRole('textbox', { name: 'Rename' })
    expect(input).toHaveFocus()
    expect((input as HTMLInputElement).selectionStart).toBe(0)
    expect((input as HTMLInputElement).selectionEnd).toBe('Original'.length)
    fireEvent.change(input, { target: { value: '  Renamed title  ' } })
    await userEvent.setup().keyboard('{Enter}')
    expect(onRename).toHaveBeenCalledOnce()
    expect(onRename).toHaveBeenCalledWith('c1', 'Renamed title')
  })

  it('conversation-list.rename-cancel', async () => {
    fixture(sharedCases, 'conversation-list.rename-cancel')
    const onRename = vi.fn()
    render(<ConversationList conversations={conversations} onRename={onRename} onSelect={vi.fn()} />)
    const trigger = screen.getByRole('button', { name: 'Rename: Original' })
    await userEvent.setup().click(trigger)
    const input = screen.getByRole('textbox', { name: 'Rename' })
    fireEvent.change(input, { target: { value: 'Discard' } })
    await userEvent.setup().keyboard('{Escape}')
    expect(onRename).not.toHaveBeenCalled()
    expect(screen.getByText('Original')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Rename: Original' })).toHaveFocus())
  })

  it('conversation-list.rename-noop', async () => {
    fixture(sharedCases, 'conversation-list.rename-noop')
    const onRename = vi.fn()
    render(<ConversationList conversations={conversations} onRename={onRename} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Rename: Original' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Rename' }), { target: { value: '   ' } })
    fireEvent.blur(screen.getByRole('textbox', { name: 'Rename' }))
    await waitFor(() => expect(screen.queryByRole('textbox', { name: 'Rename' })).toBeNull())
    await userEvent.setup().click(screen.getByRole('button', { name: 'Rename: Original' }))
    fireEvent.blur(screen.getByRole('textbox', { name: 'Rename' }))
    expect(onRename).not.toHaveBeenCalled()
  })

  it('conversation-list.delete-confirm', async () => {
    fixture(sharedCases, 'conversation-list.delete-confirm')
    const onDelete = vi.fn()
    render(<ConversationList conversations={conversations} onDelete={onDelete} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Delete: Second' }))
    const confirm = screen.getByRole('button', { name: 'Delete' })
    expect(confirm).toHaveFocus()
    expect(onDelete).not.toHaveBeenCalled()
    await userEvent.setup().click(confirm)
    expect(onDelete).toHaveBeenCalledOnce()
    expect(onDelete).toHaveBeenCalledWith('c2')
  })

  it('conversation-list.delete-cancel', async () => {
    fixture(sharedCases, 'conversation-list.delete-cancel')
    const onDelete = vi.fn()
    render(<ConversationList conversations={conversations} onDelete={onDelete} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Delete: Second' }))
    await userEvent.setup().keyboard('{Escape}')
    expect(onDelete).not.toHaveBeenCalled()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Delete: Second' })).toHaveFocus())
  })

  it('conversation-list.keyboard-order', async () => {
    fixture(sharedCases, 'conversation-list.keyboard-order')
    render(
      <ConversationList
        conversations={[conversations[0]!]}
        onDelete={vi.fn()}
        onRename={vi.fn()}
        onSelect={vi.fn()}
      />,
    )
    const row = screen.getByRole('listitem')
    const ordered = [
      within(row).getAllByRole('button')[0]!,
      screen.getByRole('button', { name: 'Rename: Original' }),
      screen.getByRole('button', { name: 'Delete: Original' }),
    ]
    for (const control of ordered) {
      await userEvent.setup().tab()
      expect(control).toHaveFocus()
    }
  })

  it('conversation-list.localization-rtl', () => {
    fixture(sharedCases, 'conversation-list.localization-rtl')
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <ConversationList
          conversations={[conversations[0]!]}
          labels={{
            heading: 'المحادثات',
            newConversation: 'محادثة جديدة',
            empty: 'لا توجد محادثات.',
            rename: 'إعادة تسمية',
            delete: 'حذف',
            confirmDelete: 'تأكيد الحذف',
            cancel: 'إلغاء',
            getRowMenuLabel: title => `إجراءات ${title}`,
          }}
          onDelete={vi.fn()}
          onNew={vi.fn()}
          onRename={vi.fn()}
          onSelect={vi.fn()}
        />
      </HarborlineLocaleProvider>,
    )
    const root = screen.getByTestId('conversation-list')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('list', { name: 'المحادثات' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'محادثة جديدة' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إعادة تسمية: Original' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'حذف: Original' })).toBeInTheDocument()
  })

  it('conversation-list.invalid-input', () => {
    fixture(sharedCases, 'conversation-list.invalid-input')
    expect(() => render(
      <ConversationList conversations={[conversations[0]!, conversations[0]!]} labels={{ heading: ' ' }} onSelect={vi.fn()} />,
    )).toThrow('duplicate-conversation-id')
    expect(() => render(
      <ConversationList conversations={[]} labels={{ heading: ' ' }} onSelect={vi.fn()} />,
    )).toThrow('accessible-heading-required')
  })

  // Both lanes emit exactly these classes on a row and its action group. The Blazor lane used to
  // add hl-conversation-list__row and hl-conversation-list__actions, which the authority never
  // defined, so the two lanes styled the same row differently and parity could not see it.
  it('conversation-list.row-classes', () => {
    const declared = fixture(sharedCases, 'conversation-list.row-classes')
    const expected = declared.expected as { itemClasses: string[], activeItemClasses: string[], rowActionsClasses: string[] }
    render(<ConversationList activeId="c2" conversations={conversations} onDelete={vi.fn()} onRename={vi.fn()} onSelect={vi.fn()} />)
    const items = [...document.querySelectorAll('li')]
    expect(items).toHaveLength(2)
    expect([...items[0]!.classList]).toEqual(expected.itemClasses)
    expect([...items[1]!.classList]).toEqual(expected.activeItemClasses)
    const actions = [...document.querySelectorAll('.hl-conversation-list__row-actions')]
    expect(actions).toHaveLength(2)
    for (const group of actions) expect([...group.classList]).toEqual(expected.rowActionsClasses)
  })
})
