import type { ChatMessage, ChatParticipant } from './Chat.types'

export interface PreparedChatMessages {
  readonly messages: readonly ChatMessage[]
  readonly validationOperations: number
}

function requireParticipant(participant: ChatParticipant): void {
  if (participant.name.trim().length === 0) throw new Error('accessible-user-name-required')
}

export function prepareChatMessages(
  messages: readonly ChatMessage[],
  user: ChatParticipant,
  controlledInput: boolean,
  hasInputChangeHandler: boolean,
): PreparedChatMessages {
  requireParticipant(user)
  if (controlledInput && !hasInputChangeHandler) throw new Error('controlled-composer-handler-required')

  const ids = new Set<string>()
  let validationOperations = 0
  for (const message of messages) {
    validationOperations += 1
    if (ids.has(message.id)) throw new Error('duplicate-message-id')
    ids.add(message.id)
    requireParticipant(message.author)
    if (message.timestamp !== undefined) {
      if (!(message.timestamp instanceof Date) || !Number.isFinite(message.timestamp.getTime())) {
        throw new Error('invalid-message-timestamp')
      }
    }
  }

  return { messages, validationOperations }
}
