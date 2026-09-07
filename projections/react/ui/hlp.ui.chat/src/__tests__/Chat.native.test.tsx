import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chat } from '../Chat'
import type { ChatMessage } from '../Chat.types'
import { qualityCases } from './fixtures'

const user = { name: 'Casey' }
const assistant = { name: 'Harborline' }
const message = (id: string, role: ChatMessage['role'] = 'assistant'): ChatMessage => ({
  id,
  author: role === 'user' ? user : assistant,
  role,
  text: `Message ${id}`,
})

describe('Chat React projection quality', () => {
  it('consumes every non-performance Tier-C quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'chat.quality.log-articles',
      'chat.quality.live-regions',
      'chat.quality.touch-reflow',
      'chat.quality.locale-time',
      'chat.quality.catalog',
      'chat.quality.pseudo',
      'chat.quality.light-dark',
      'chat.quality.tokens',
      'chat.quality.forced-colors',
      'chat.quality.visual-parity',
      'chat.quality.submit-keyboard',
      'chat.quality.article-navigation',
      'chat.quality.focus-stability',
      'chat.quality.rtl',
      'chat.quality.reduced-motion',
      'chat.quality.controlled-state',
    ])
  })

  it('renders one named polite log and one focusable article per message', () => {
    render(<Chat messages={[message('1'), message('2', 'user')]} user={user} />)
    const log = screen.getByRole('log', { name: 'Conversation' })
    expect(log).toHaveAttribute('aria-live', 'polite')
    expect(log).toHaveAttribute('aria-relevant', 'additions text')
    expect(screen.getAllByRole('article')).toHaveLength(2)
    expect(screen.getAllByRole('article').every(article => article.tabIndex === 0)).toBe(true)
  })

  it('keeps the controlled textbox focused while its host replaces the value', async () => {
    const changed = vi.fn()
    const view = render(<Chat messages={[]} user={user} inputValue="one" onInputValueChange={changed} onSubmit={() => undefined} />)
    const input = screen.getByRole('textbox')
    await userEvent.setup().click(input)
    view.rerender(<Chat messages={[]} user={user} inputValue="two" onInputValueChange={changed} onSubmit={() => undefined} />)
    expect(input).toHaveFocus()
    expect(input).toHaveValue('two')
  })

  it('supports an uncontrolled composer without inventing application state', async () => {
    const submitted = vi.fn()
    render(<Chat messages={[]} user={user} onSubmit={submitted} />)
    const input = screen.getByRole('textbox')
    await userEvent.setup().type(input, '  local draft  ')
    await userEvent.keyboard('{Enter}')
    expect(submitted).toHaveBeenCalledOnce()
    expect(submitted).toHaveBeenCalledWith('local draft')
    expect(input).toHaveValue('')
  })

  it('uses catalog strings and provider direction for long pseudo-localized content', () => {
    const pseudo = `[!! ${'Conversation expansion '.repeat(10)} !!]`
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'ai.chat.conversation': pseudo, 'ai.chat.aiTyping': `[!! Typing expansion !!]` }}>
        <Chat messages={[]} user={user} showTypingIndicator onSubmit={() => undefined} />
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('log', { name: /Conversation expansion/u })).not.toHaveAttribute('dir')
    expect(document.querySelector('.hl-chat')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('status')).toHaveTextContent('Typing expansion')
  })

  it('publishes semantic tokens, logical layout, touch floors, dark/forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-chat-surface')
    expect(css).toContain('margin-inline-start: auto')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toMatch(/\.hl-chat__suggestion,[\s\S]*min-block-size: 2\.75rem;[\s\S]*min-inline-size: 2\.75rem;/)
    expect(css).toContain("[data-theme='dark'] .hl-chat")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion[\s\S]*animation: none/)
  })
})
