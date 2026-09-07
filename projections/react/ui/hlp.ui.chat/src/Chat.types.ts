import type * as React from 'react'

export type ChatMessageRole = 'user' | 'assistant' | 'system'
export type ChatMessageStatus = 'streaming' | 'complete' | 'error'
export type ChatDirection = 'ltr' | 'rtl'

export interface ChatParticipant {
  readonly name: string
  readonly avatarUrl?: string
}

export interface ChatMessage {
  readonly id: string
  readonly author: ChatParticipant
  readonly role: ChatMessageRole
  readonly text: string
  readonly status?: ChatMessageStatus
  readonly timestamp?: Date
}

export interface ChatSuggestion {
  readonly title: string
  readonly prompt: string
}

export interface ChatMessageContentProps {
  readonly message: ChatMessage
}

export interface ChatProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'children' | 'dir' | 'onSubmit'> {
  readonly messages: readonly ChatMessage[]
  readonly user: ChatParticipant
  readonly onSubmit?: (text: string) => void
  readonly inputValue?: string
  readonly onInputValueChange?: (value: string) => void
  readonly composerDisabled?: boolean
  readonly placeholder?: string
  readonly suggestions?: readonly ChatSuggestion[]
  readonly showTypingIndicator?: boolean
  readonly showAvatar?: boolean
  readonly direction?: ChatDirection
  readonly messageContentTemplate?: React.ComponentType<ChatMessageContentProps>
  readonly empty?: React.ReactNode | (() => React.ReactNode)
}
