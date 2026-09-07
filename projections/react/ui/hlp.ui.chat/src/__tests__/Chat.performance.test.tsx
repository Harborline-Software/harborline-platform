import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { Chat } from '../Chat'
import type { ChatMessage } from '../Chat.types'
import { prepareChatMessages } from '../chat-model'
import { fixture, performanceCases } from './fixtures'

const user = { name: 'Casey' }
const assistant = { name: 'Harborline' }

function messagesFor(revision: number): readonly ChatMessage[] {
  return Array.from({ length: 256 }, (_, index) => ({
    id: `r${revision}-m${index}`,
    author: index % 2 === 0 ? user : assistant,
    role: index % 2 === 0 ? 'user' as const : 'assistant' as const,
    text: `Revision ${revision} message ${index}`,
  }))
}

describe('Chat deterministic Tier-C evidence', () => {
  it('chat.quality.large-data validates and renders exactly 256 messages with linear work', () => {
    fixture(performanceCases, 'chat.quality.large-data')
    const messages = messagesFor(1)
    const prepared = prepareChatMessages(messages, user, false, false)
    expect(prepared.messages).toBe(messages)
    expect(prepared.validationOperations).toBe(256)
    render(<Chat messages={messages} user={user} />)
    expect(screen.getAllByRole('article')).toHaveLength(256)
  }, 30_000)

  it('chat.quality.repeated-update retains only update 96 without stale messages or callbacks', () => {
    fixture(performanceCases, 'chat.quality.repeated-update')
    const submitted = vi.fn()
    const changed = vi.fn()
    const view = render(<Chat messages={messagesFor(0)} user={user} inputValue="draft 0" onInputValueChange={changed} onSubmit={submitted} />)
    for (let revision = 1; revision <= 96; revision += 1) {
      view.rerender(<Chat messages={messagesFor(revision)} user={user} inputValue={`draft ${revision}`} onInputValueChange={changed} onSubmit={submitted} />)
    }
    expect(screen.getAllByRole('article')).toHaveLength(256)
    expect(document.querySelector('[data-message-id="r96-m0"]')).toBeInTheDocument()
    expect(document.querySelector('[data-message-id="r95-m0"]')).toBeNull()
    expect(screen.getByRole('textbox')).toHaveValue('draft 96')
    expect(submitted).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
  }, 30_000)
})
