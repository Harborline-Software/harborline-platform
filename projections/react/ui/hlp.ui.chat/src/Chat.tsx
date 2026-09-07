import * as React from 'react'

import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

import type {
  ChatMessageContentProps,
  ChatParticipant,
  ChatProps,
} from './Chat.types'
import { prepareChatMessages } from './chat-model'

const FOLLOW_END_THRESHOLD = 24

function classes(base: string, extra?: string): string {
  return extra ? `${base} ${extra}` : base
}

function initials(name: string): string {
  return name.trim().split(/\s+/u).slice(0, 2).map(part => part[0]).join('').toLocaleUpperCase()
}

function Avatar({ participant }: { readonly participant: ChatParticipant }) {
  return participant.avatarUrl ? (
    <img className="hl-chat__avatar" src={participant.avatarUrl} alt={participant.name} />
  ) : (
    <span aria-hidden="true" className="hl-chat__avatar hl-chat__avatar--fallback">
      {initials(participant.name)}
    </span>
  )
}

function renderNoData(value: ChatProps['empty']): React.ReactNode {
  return typeof value === 'function' ? value() : value
}

function formatInstant(value: Date, locale: string): string {
  try {
    return new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'short' }).format(value)
  } catch {
    return new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'short' }).format(value)
  }
}

function MessageContent({ message }: ChatMessageContentProps) {
  return <>{message.text}</>
}

export function Chat(props: ChatProps) {
  const {
    messages,
    user,
    onSubmit,
    inputValue,
    onInputValueChange,
    composerDisabled = false,
    placeholder,
    suggestions = [],
    showTypingIndicator = false,
    showAvatar = true,
    direction,
    messageContentTemplate: MessageContentTemplate = MessageContent,
    empty,
    className,
    onScroll,
    ...attributes
  } = props
  const { direction: localeDirection, locale, resolveString, t } = useHarborlineStrings()
  const controlledInput = inputValue !== undefined
  const prepared = prepareChatMessages(messages, user, controlledInput, onInputValueChange !== undefined)
  const [uncontrolledInput, setUncontrolledInput] = React.useState('')
  const currentInput = controlledInput ? inputValue : uncontrolledInput
  const logRef = React.useRef<HTMLDivElement>(null)
  const articleRefs = React.useRef(new Map<string, HTMLElement>())
  const followEnd = React.useRef(true)
  const generatedComposerId = React.useId()
  const composerInputId = `${attributes.id ?? generatedComposerId}-input`
  const hasUserMessage = prepared.messages.some(message => message.role === 'user')
  const visibleSuggestions = hasUserMessage ? [] : suggestions
  const effectiveDirection = direction ?? localeDirection
  const conversationLabel = t('ai.chat.conversation')

  React.useLayoutEffect(() => {
    const log = logRef.current
    if (log && followEnd.current) log.scrollTop = log.scrollHeight
  }, [prepared.messages, showTypingIndicator])

  const changeInput = React.useCallback((next: string) => {
    if (!controlledInput) setUncontrolledInput(next)
    onInputValueChange?.(next)
  }, [controlledInput, onInputValueChange])

  const requestSubmit = React.useCallback((rawText: string) => {
    if (composerDisabled || onSubmit === undefined) return
    const text = rawText.trim()
    if (text.length === 0) return
    onSubmit(text)
    changeInput('')
  }, [changeInput, composerDisabled, onSubmit])

  const handleArticleKeyDown = (event: React.KeyboardEvent<HTMLElement>, messageId: string) => {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return
    const index = prepared.messages.findIndex(message => message.id === messageId)
    const nextIndex = event.key === 'ArrowUp' ? index - 1 : index + 1
    const nextMessage = prepared.messages[nextIndex]
    if (nextMessage === undefined) return
    event.preventDefault()
    articleRefs.current.get(nextMessage.id)?.focus()
  }

  return (
    <div
      {...attributes}
      className={classes('hl-chat', className)}
      dir={effectiveDirection}
      data-message-count={prepared.messages.length}
    >
      <div
        ref={logRef}
        role="log"
        aria-label={conversationLabel}
        aria-live="polite"
        aria-relevant="additions text"
        className="hl-chat__log"
        onScroll={event => {
          const element = event.currentTarget
          followEnd.current = element.scrollHeight - element.scrollTop - element.clientHeight <= FOLLOW_END_THRESHOLD
          onScroll?.(event)
        }}
      >
        {prepared.messages.length === 0 && empty !== undefined ? (
          <div className="hl-chat__empty">{renderNoData(empty)}</div>
        ) : null}

        {prepared.messages.map(message => {
          const timestamp = message.timestamp === undefined ? undefined : formatInstant(message.timestamp, locale)
          const roleLabel = message.role === 'user'
            ? t('ai.chat.roleUser')
            : message.role === 'assistant'
              ? t('ai.chat.roleAssistant')
              : message.author.name
          const articleLabel = timestamp === undefined
            ? t('ai.chat.messageArticle', { role: roleLabel })
            : t('ai.chat.messageArticleTime', { role: roleLabel, time: timestamp })
          return (
            <article
              ref={element => {
                if (element) articleRefs.current.set(message.id, element)
                else articleRefs.current.delete(message.id)
              }}
              key={message.id}
              tabIndex={0}
              aria-label={articleLabel}
              aria-live={message.status === 'streaming' ? 'off' : undefined}
              className="hl-chat__message"
              data-message-id={message.id}
              data-owner={message.role === 'user' ? 'outgoing' : message.role === 'assistant' ? 'incoming' : 'system'}
              data-status={message.status}
              onKeyDown={event => handleArticleKeyDown(event, message.id)}
            >
              {showAvatar && message.role !== 'system' ? <Avatar participant={message.author} /> : null}
              <div className="hl-chat__message-body">
                <span className="hl-chat__author">{message.author.name}</span>
                <div className="hl-chat__bubble">
                  <MessageContentTemplate message={message} />
                </div>
                {timestamp === undefined ? null : (
                  <time className="hl-chat__timestamp" dateTime={message.timestamp!.toISOString()}>
                    {timestamp}
                  </time>
                )}
              </div>
            </article>
          )
        })}

        {showTypingIndicator ? (
          <div role="status" className="hl-chat__typing">
            <span className="hl-chat__sr-only">{t('ai.chat.aiTyping')}</span>
            <span aria-hidden="true" className="hl-chat__typing-dots"><i /><i /><i /></span>
          </div>
        ) : null}
      </div>

      {visibleSuggestions.length > 0 ? (
        <div role="group" aria-label={t('ai.chat.suggestedActions')} className="hl-chat__suggestions">
          {visibleSuggestions.map((suggestion, index) => (
            <button
              key={`${suggestion.title}-${index}`}
              type="button"
              className="hl-chat__suggestion"
              disabled={composerDisabled || onSubmit === undefined}
              onClick={() => requestSubmit(suggestion.prompt)}
            >
              {suggestion.title}
            </button>
          ))}
        </div>
      ) : null}

      {onSubmit === undefined ? null : (
        <form
          className="hl-chat__composer"
          onSubmit={event => {
            event.preventDefault()
            requestSubmit(currentInput)
          }}
        >
          <label className="hl-chat__sr-only" htmlFor={composerInputId}>
            {t('ai.chat.inputLabel')}
          </label>
          <input
            id={composerInputId}
            type="text"
            className="hl-chat__input"
            value={currentInput}
            disabled={composerDisabled}
            placeholder={resolveString(placeholder, 'ai.chat.placeholder')}
            onChange={event => changeInput(event.currentTarget.value)}
          />
          <button
            type="submit"
            className="hl-chat__send"
            disabled={composerDisabled || currentInput.trim().length === 0}
          >
            {t('ai.send')}
          </button>
        </form>
      )}
    </div>
  )
}
