import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { ConversationList, type ConversationSummary } from '../ConversationList'
import { qualityCases } from './fixtures'

const conversation: ConversationSummary = {
  id: 'c1',
  title: 'Original',
  timestamp: '2 min ago',
  preview: 'First preview',
}

describe('Conversation List React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'conversation-list.quality.pattern',
      'conversation-list.quality.keyboard',
      'conversation-list.quality.focus',
      'conversation-list.quality.dismissal',
      'conversation-list.quality.forced-colors',
      'conversation-list.quality.reflow',
      'conversation-list.quality.locales',
      'conversation-list.quality.rtl',
      'conversation-list.quality.pseudo',
      'conversation-list.quality.formatting-boundary',
      'conversation-list.quality.themes',
      'conversation-list.quality.tokens',
      'conversation-list.quality.contrast',
      'conversation-list.quality.reduced-motion',
      'conversation-list.quality.visual-parity',
    ])
  })

  it('preserves caller timestamp text without introducing machine formatting', () => {
    render(<ConversationList conversations={[conversation]} onSelect={vi.fn()} />)
    const timestamp = screen.getByText('2 min ago')
    expect(timestamp).toHaveTextContent('2 min ago')
    expect(document.querySelector('time')).toBeNull()
  })

  it('commits rename on blur once and restores focus', async () => {
    const onRename = vi.fn()
    render(<ConversationList conversations={[conversation]} onRename={onRename} onSelect={vi.fn()} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Rename: Original' }))
    const input = screen.getByRole('textbox', { name: 'Rename' })
    fireEvent.change(input, { target: { value: 'Changed' } })
    fireEvent.blur(input)
    expect(onRename).toHaveBeenCalledOnce()
    expect(onRename).toHaveBeenCalledWith('c1', 'Changed')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Rename: Original' })).toHaveFocus())
  })

  it('localizes every owned chrome string through labels and provider direction', async () => {
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <ConversationList
          conversations={[conversation]}
          labels={{
            heading: 'المحادثات', newConversation: 'جديد', empty: 'فارغ', rename: 'إعادة تسمية',
            delete: 'حذف', confirmDelete: 'تأكيد', cancel: 'إلغاء',
          }}
          onDelete={vi.fn()}
          onNew={vi.fn()}
          onRename={vi.fn()}
          onSelect={vi.fn()}
        />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByTestId('conversation-list')).toHaveAttribute('dir', 'rtl')
    await userEvent.setup().click(screen.getByRole('button', { name: 'حذف: Original' }))
    expect(screen.getByRole('button', { name: 'تأكيد' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'إلغاء' })).toBeInTheDocument()
  })

  it('publishes logical, token, focus, danger, reflow, theme, forced-color, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-conversation-list-surface')
    expect(css).toContain('padding-inline')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain(':focus-visible')
    expect(css).toContain('.hl-conversation-list__confirm')
    expect(css).toContain("[data-theme='dark'] .hl-conversation-list")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
