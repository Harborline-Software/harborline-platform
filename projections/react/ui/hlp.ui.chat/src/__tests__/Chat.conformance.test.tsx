import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Chat } from '../Chat'
import type { ChatMessage } from '../Chat.types'
import { contractCases, fixture, sharedCases } from './fixtures'

const participant = { name: 'Casey', avatarUrl: '/casey.png' }
const assistant = { name: 'Harborline' }
const messages: readonly ChatMessage[] = [
  { id: 'm1', author: participant, role: 'user', text: 'Inspect this' },
  { id: 'm2', author: assistant, role: 'assistant', text: 'Inspection ready' },
]

describe('Chat revision-1 conformance', () => {
  it('consumes every frozen neutral case in contract order', () => {
    expect(sharedCases.map(value => value.id)).toEqual(contractCases.map(value => value.id))
  })

  it('chat.empty, ordered-messages, outgoing-incoming, avatar-hidden, and replacement preserve the message shell', () => {
    fixture(sharedCases, 'chat.empty')
    const view = render(<Chat messages={[]} user={participant} />)
    expect(screen.getAllByRole('log')).toHaveLength(1)
    expect(screen.queryAllByRole('article')).toHaveLength(0)
    expect(screen.queryByRole('textbox')).toBeNull()

    fixture(sharedCases, 'chat.ordered-messages')
    fixture(sharedCases, 'chat.outgoing-incoming')
    view.rerender(<Chat messages={[messages[1]!, messages[0]!, { id: 'm3', author: assistant, role: 'system', text: 'System note' }]} user={participant} showAvatar={false} />)
    expect(screen.getAllByRole('article').map(node => node.dataset.messageId)).toEqual(['m2', 'm1', 'm3'])
    expect(screen.getAllByRole('article').map(node => node.dataset.owner)).toEqual(['incoming', 'outgoing', 'system'])
    expect(document.querySelectorAll('.hl-chat__avatar')).toHaveLength(0)

    fixture(sharedCases, 'chat.replacement')
    view.rerender(<Chat messages={[{ id: 'm4', author: assistant, role: 'assistant', text: 'Latest' }]} user={participant} />)
    expect(screen.getAllByRole('article').map(node => node.dataset.messageId)).toEqual(['m4'])
    expect(screen.queryByText('Inspect this')).toBeNull()
  })

  it('submits controlled input by click and Enter, trims text, and requests one clear', async () => {
    fixture(sharedCases, 'chat.controlled-input')
    fixture(sharedCases, 'chat.submit-click')
    fixture(sharedCases, 'chat.submit-enter')
    const submitted = vi.fn()
    const changed = vi.fn()
    const view = render(<Chat messages={[]} user={participant} inputValue="  inspect this  " onInputValueChange={changed} onSubmit={submitted} />)
    const input = screen.getByRole('textbox')
    expect(input).toHaveValue('  inspect this  ')
    await userEvent.setup().click(screen.getByRole('button', { name: 'Send' }))
    expect(submitted).toHaveBeenLastCalledWith('inspect this')
    expect(changed).toHaveBeenLastCalledWith('')
    expect(submitted).toHaveBeenCalledTimes(1)
    expect(changed).toHaveBeenCalledTimes(1)

    submitted.mockClear()
    changed.mockClear()
    view.rerender(<Chat messages={[]} user={participant} inputValue="inspect" onInputValueChange={changed} onSubmit={submitted} />)
    input.focus()
    await userEvent.keyboard('{Enter}')
    expect(submitted).toHaveBeenCalledTimes(1)
    expect(submitted).toHaveBeenCalledWith('inspect')
    expect(changed).toHaveBeenCalledTimes(1)
    expect(changed).toHaveBeenCalledWith('')
  })

  it('suppresses blank and disabled submission across every route', async () => {
    fixture(sharedCases, 'chat.submit-trim-empty')
    fixture(sharedCases, 'chat.submit-disabled')
    const submitted = vi.fn()
    const changed = vi.fn()
    const view = render(<Chat messages={[]} user={participant} inputValue="   " onInputValueChange={changed} onSubmit={submitted} suggestions={[{ title: 'Explain', prompt: 'explain' }]} />)
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled()
    view.rerender(<Chat messages={[]} user={participant} inputValue="inspect" onInputValueChange={changed} onSubmit={submitted} composerDisabled suggestions={[{ title: 'Explain', prompt: 'explain' }]} />)
    expect(screen.getByRole('textbox')).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Explain' })).toBeDisabled()
    expect(submitted).not.toHaveBeenCalled()
    expect(changed).not.toHaveBeenCalled()
  })

  it('shows suggestions only before the first user message and activates one prompt once', async () => {
    fixture(sharedCases, 'chat.suggestions-initial')
    fixture(sharedCases, 'chat.suggestion-submit')
    fixture(sharedCases, 'chat.suggestions-after-user-message')
    const submitted = vi.fn()
    const changed = vi.fn()
    const suggestions = [{ title: 'Explain', prompt: '  explain finding  ' }, { title: 'Retry', prompt: 'retry' }]
    const view = render(<Chat messages={[]} user={participant} inputValue="draft" onInputValueChange={changed} onSubmit={submitted} suggestions={suggestions} />)
    expect(screen.getAllByRole('button').map(button => button.textContent)).toEqual(['Explain', 'Retry', 'Send'])
    await userEvent.setup().click(screen.getByRole('button', { name: 'Explain' }))
    expect(submitted).toHaveBeenCalledOnce()
    expect(submitted).toHaveBeenCalledWith('explain finding')
    expect(changed).toHaveBeenCalledOnce()
    expect(changed).toHaveBeenCalledWith('')
    view.rerender(<Chat messages={[messages[0]!]} user={participant} inputValue="" onInputValueChange={changed} onSubmit={submitted} suggestions={suggestions} />)
    expect(screen.queryByRole('button', { name: 'Explain' })).toBeNull()
  })

  it('retains bounded content slots and accessible typing/streaming behavior', () => {
    fixture(sharedCases, 'chat.typing')
    fixture(sharedCases, 'chat.streaming-announcement')
    fixture(sharedCases, 'chat.message-content-slot')
    fixture(sharedCases, 'chat.empty-slot')
    const Custom = ({ message }: { message: ChatMessage }) => <strong>Custom {message.id}</strong>
    const view = render(<Chat messages={[{ ...messages[1]!, status: 'streaming' }]} user={participant} showTypingIndicator messageContentTemplate={Custom} />)
    const article = screen.getByRole('article')
    expect(article).toHaveAttribute('aria-live', 'off')
    expect(article).toHaveTextContent('Custom m2')
    expect(article).toHaveTextContent('Harborline')
    expect(screen.getByRole('status')).toHaveTextContent('AI is typing')
    view.rerender(<Chat messages={[]} user={participant} empty={<strong>Custom empty</strong>} />)
    expect(screen.getByRole('log')).toContainElement(screen.getByText('Custom empty'))
  })

  it('moves article focus to adjacent messages without wrapping', async () => {
    fixture(sharedCases, 'chat.keyboard-navigation')
    render(<Chat messages={messages} user={participant} />)
    const articles = screen.getAllByRole('article')
    articles[1]!.focus()
    await userEvent.keyboard('{ArrowUp}')
    expect(articles[0]).toHaveFocus()
    await userEvent.keyboard('{ArrowUp}')
    expect(articles[0]).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    expect(articles[1]).toHaveFocus()
    await userEvent.keyboard('{ArrowDown}')
    expect(articles[1]).toHaveFocus()
  })

  it('localizes timestamps, honors explicit direction, and preserves host attributes', () => {
    fixture(sharedCases, 'chat.locale-rtl-time')
    const hostAttributes = fixture(sharedCases, 'chat.host-attributes').input.attributes as Record<string, string>
    const owner = hostAttributes['data-owner']!
    const instant = new Date('2026-01-02T15:04:05Z')
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <Chat messages={[{ ...messages[1]!, timestamp: instant }]} user={participant} direction="rtl" data-owner={owner} style={{ height: '100%' }} />
      </HarborlineLocaleProvider>,
    )
    const root = document.querySelector('.hl-chat')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toHaveAttribute('data-owner', owner)
    expect(root).toHaveStyle({ height: '100%' })
    expect(screen.getByRole('time')).toHaveAttribute('datetime', instant.toISOString())
    expect(screen.getByRole('time').textContent).not.toBe(instant.toISOString())
  })

  it('emits every frozen stable validation code', () => {
    fixture(sharedCases, 'chat.invalid-input')
    const duplicate = [{ ...messages[0]! }, { ...messages[1]!, id: 'm1' }]
    expect(() => render(<Chat messages={duplicate} user={participant} />)).toThrow('duplicate-message-id')
    expect(() => render(<Chat messages={[]} user={participant} inputValue="draft" onSubmit={() => undefined} />)).toThrow('controlled-composer-handler-required')
    expect(() => render(<Chat messages={[]} user={{ name: ' ' }} />)).toThrow('accessible-user-name-required')
    expect(() => render(<Chat messages={[{ ...messages[0]!, timestamp: new Date(Number.NaN) }]} user={participant} />)).toThrow('invalid-message-timestamp')
  })

  it('keeps projection and private scroll details outside the public contract', () => {
    fixture(sharedCases, 'chat.projection-equivalence')
    render(<Chat messages={messages} user={participant} />)
    expect(screen.getByRole('log')).toHaveClass('hl-chat__log')
    const publicSurface = [
      readFileSync(resolve(import.meta.dirname, '../Chat.types.ts'), 'utf8'),
      readFileSync(resolve(import.meta.dirname, '../index.ts'), 'utf8'),
    ].join('\n')
    expect(publicSurface).not.toMatch(/telerik|tanstack|scrollTop|scrollHeight|HTMLElement/iu)
  })
})
