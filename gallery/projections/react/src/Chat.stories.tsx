import { useMemo, useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import {
  Chat,
  HarborlineLocaleProvider,
  type ChatMessage,
  type ChatMessageContentProps,
  type ChatParticipant,
} from '@harborline-software/ui-react'

type ScenarioId =
  | 'chat.empty-suggestions'
  | 'chat.app-conversation'
  | 'chat.controlled-composer'
  | 'chat.streaming-keyboard'
  | 'chat.replacement-validation'
  | 'chat.large-data'
  | 'chat.locale-pseudo'
  | 'chat.locale-ar'
  | 'chat.theme-light'
  | 'chat.theme-dark'

const currentUser: ChatParticipant = { name: 'Casey Morgan' }
const assistant: ChatParticipant = { name: 'Harborline Copilot' }

const conversation: readonly ChatMessage[] = [
  {
    id: 'capture-request',
    author: currentUser,
    role: 'user',
    text: 'Place the west-wall capture on the Level 2 blueprint.',
    status: 'complete',
    timestamp: new Date('2026-01-02T15:02:00Z'),
  },
  {
    id: 'capture-result',
    author: assistant,
    role: 'assistant',
    text: 'The pose aligns with grid C-7. I found one clearance conflict near the service chase.',
    status: 'complete',
    timestamp: new Date('2026-01-02T15:04:05Z'),
  },
  {
    id: 'review-state',
    author: { name: 'Inspection workflow' },
    role: 'system',
    text: 'Placement evidence is ready for review.',
    status: 'complete',
  },
]

const copy: Record<ScenarioId, [string, string]> = {
  'chat.empty-suggestions': ['Empty state and suggestions', 'The conversation log retains its accessible shell while starter prompts remain reachable.'],
  'chat.app-conversation': ['Harborline conversation', 'Ordered roles, bounded custom content, hidden avatars, and host-owned fill height match the Harborline Copilot surface.'],
  'chat.controlled-composer': ['Controlled composer', 'The host owns draft state, receives one trimmed prompt, and removes suggestions after the first user message.'],
  'chat.streaming-keyboard': ['Streaming and keyboard navigation', 'Partial assistant text is quiet, typing remains announced, and adjacent articles support arrow-key focus.'],
  'chat.replacement-validation': ['Replacement and validation', 'Complete replacement removes stale messages while stable contract errors remain projection-neutral.'],
  'chat.large-data': ['Bounded conversation under load', 'Two hundred fifty-six ordered messages exercise the Tier-C structural boundary inside one scroll owner.'],
  'chat.locale-pseudo': ['Pseudo locale', 'Expanded participant names, messages, and composer copy remain readable without clipping.'],
  'chat.locale-ar': ['Arabic RTL and localized time', 'Logical ownership alignment and localized instants remain intact in a right-to-left conversation.'],
  'chat.theme-light': ['Light theme', 'Conversation, controls, focus, and status surfaces use the public light tokens.'],
  'chat.theme-dark': ['Dark theme', 'The same semantic conversation uses the public dark tokens without changing behavior.'],
}

const suggestions = [
  { title: 'Explain the conflict', prompt: 'Explain the clearance conflict' },
  { title: 'Suggest next step', prompt: 'Suggest the next inspection step' },
]

function EvidenceMessage({ message }: ChatMessageContentProps) {
  return <div>
    <strong>{message.status === 'error' ? 'Needs attention' : 'Spatial evidence'}</strong>
    <p style={{ margin: '0.25rem 0 0' }}>{message.text}</p>
  </div>
}

function EmptyAndSuggestions() {
  return <div style={{ display: 'grid', gap: '1rem', gridTemplateColumns: 'repeat(auto-fit, minmax(min(100%, 20rem), 1fr))' }}>
    <Chat
      messages={[]}
      user={currentUser}
      empty={<span>No inspection conversation yet.</span>}
      style={{ blockSize: 320, border: '1px solid var(--hl-border, #64748b)' }}
    />
    <Chat
      messages={[]}
      user={currentUser}
      onSubmit={() => undefined}
      suggestions={suggestions}
      empty={<span>Choose a prompt or describe the placement question.</span>}
      style={{ blockSize: 320, border: '1px solid var(--hl-border, #64748b)' }}
    />
  </div>
}

function ControlledComposer() {
  const [messages, setMessages] = useState<readonly ChatMessage[]>([])
  const [inputValue, setInputValue] = useState('Review grid C-7')
  const [lastSubmission, setLastSubmission] = useState('No prompt submitted yet.')

  return <div>
    <Chat
      messages={messages}
      user={currentUser}
      inputValue={inputValue}
      onInputValueChange={setInputValue}
      onSubmit={text => {
        setLastSubmission(`Submitted once: ${text}`)
        setMessages(current => [...current, {
          id: `submitted-${current.length + 1}`,
          author: currentUser,
          role: 'user',
          text,
          status: 'complete',
          timestamp: new Date('2026-01-02T15:06:00Z'),
        }])
      }}
      suggestions={suggestions}
      empty={<span>Suggestions are available before the first user message.</span>}
      style={{ blockSize: 420, border: '1px solid var(--hl-border, #64748b)' }}
    />
    <p aria-live="polite" style={{ marginBlockEnd: 0 }}>{lastSubmission}</p>
  </div>
}

function StreamingConversation() {
  const messages: readonly ChatMessage[] = [
    conversation[0]!,
    {
      id: 'streaming-result',
      author: assistant,
      role: 'assistant',
      text: 'Comparing capture geometry with Level 2…',
      status: 'streaming',
      timestamp: new Date('2026-01-02T15:04:05Z'),
    },
  ]

  return <Chat
    messages={messages}
    user={currentUser}
    inputValue="Waiting for the current response"
    onInputValueChange={() => undefined}
    onSubmit={() => undefined}
    composerDisabled
    showTypingIndicator
    showAvatar={false}
    style={{ blockSize: 420, border: '1px solid var(--hl-border, #64748b)' }}
  />
}

function AppConversationFixture() {
  return <div style={{ blockSize: 420 }}>
    <Chat
      messages={conversation}
      user={currentUser}
      showAvatar={false}
      messageContentTemplate={EvidenceMessage}
      data-owner="app"
      style={{ height: '100%', border: '1px solid var(--hl-border, #64748b)' }}
    />
  </div>
}

function ReplacementAndValidation() {
  const [revision, setRevision] = useState(1)
  const messages: readonly ChatMessage[] = revision === 1
    ? conversation.slice(0, 2)
    : [{
        id: 'replacement-only',
        author: assistant,
        role: 'assistant',
        text: 'Revision 2 is authoritative; no stale revision 1 rows remain.',
        status: 'complete',
      }]

  return <div style={{ display: 'grid', gap: '1rem' }}>
    <button type="button" onClick={() => setRevision(current => current === 1 ? 2 : 1)}>
      Show revision {revision === 1 ? 2 : 1}
    </button>
    <Chat
      messages={messages}
      user={currentUser}
      style={{ blockSize: 300, border: '1px solid var(--hl-border, #64748b)' }}
    />
    <aside aria-label="Stable validation codes">
      <strong>Stable validation codes</strong>
      <p style={{ marginBlockEnd: 0 }}>duplicate-message-id · controlled-composer-handler-required · accessible-user-name-required · invalid-message-timestamp</p>
    </aside>
  </div>
}

function LargeConversation() {
  const messages = useMemo<readonly ChatMessage[]>(() => Array.from({ length: 256 }, (_, index) => ({
    id: `load-message-${index + 1}`,
    author: index % 2 === 0 ? currentUser : assistant,
    role: index % 2 === 0 ? 'user' as const : 'assistant' as const,
    text: `Inspection message ${index + 1}: structure grid ${String.fromCharCode(65 + (index % 8))}-${(index % 12) + 1}.`,
    status: 'complete' as const,
  })), [])

  return <Chat
    messages={messages}
    user={currentUser}
    showAvatar={false}
    data-gallery-message-count="256"
    style={{ blockSize: 520, border: '1px solid var(--hl-border, #64748b)' }}
  />
}

function PseudoConversation() {
  const pseudoUser = { name: '⟦ Çåšëÿ Møřĝåñ ···· ⟧' }
  const pseudoAssistant = { name: '⟦ Håřƀøřļîñë Çøþîļøţ ······ ⟧' }
  const messages: readonly ChatMessage[] = [
    { id: 'pseudo-user', author: pseudoUser, role: 'user', text: '⟦ Þļåçë ţĥë çåþţûřë ŵîţĥîñ ţĥë šëļëçţëđ ƀļûëþřîñţ šţřûçţûřë. ········ ⟧' },
    { id: 'pseudo-assistant', author: pseudoAssistant, role: 'assistant', text: '⟦ Þļåçëmëñţ îš řëåđÿ ƒøř řëṽîëŵ; øñë çļëåřåñçë çøñƒļîçţ řëmåîñš. ·········· ⟧' },
  ]

  return <HarborlineLocaleProvider
    locale="en-XA"
    catalog={{
      'ai.chat.conversation': '⟦ Šþåţîåļ îñšþëçţîøñ çøñṽëřšåţîøñ ······ ⟧',
      'ai.chat.inputLabel': '⟦ Mëššåĝë ···· ⟧',
      'ai.chat.placeholder': '⟦ Åšķ åƀøûţ ţĥë çåþţûřëđ þøšë ········ ⟧',
      'ai.send': '⟦ Šëñđ ···· ⟧',
    }}
  >
    <Chat
      messages={messages}
      user={pseudoUser}
      onSubmit={() => undefined}
      messageContentTemplate={EvidenceMessage}
      style={{ blockSize: 420, border: '1px solid var(--hl-border, #64748b)' }}
    />
  </HarborlineLocaleProvider>
}

function ArabicConversation() {
  const arabicUser = { name: 'ليلى' }
  const arabicAssistant = { name: 'مساعد هاربورلاين' }
  const messages: readonly ChatMessage[] = [
    { id: 'arabic-user', author: arabicUser, role: 'user', text: 'ضع الصورة عند الشبكة ج-٧.', timestamp: new Date('2026-01-02T15:02:00Z') },
    { id: 'arabic-assistant', author: arabicAssistant, role: 'assistant', text: 'تمت محاذاة الوضعية مع المخطط.', timestamp: new Date('2026-01-02T15:04:05Z') },
  ]

  return <HarborlineLocaleProvider locale="ar-SA">
    <Chat
      messages={messages}
      user={arabicUser}
      direction="rtl"
      onSubmit={() => undefined}
      style={{ blockSize: 420, border: '1px solid var(--hl-border, #64748b)' }}
    />
  </HarborlineLocaleProvider>
}

function StandardConversation({ dark }: { dark: boolean }) {
  return <Chat
    messages={conversation}
    user={currentUser}
    onSubmit={() => undefined}
    messageContentTemplate={EvidenceMessage}
    data-theme={dark ? 'dark' : 'light'}
    style={{ blockSize: 440, border: '1px solid var(--hl-border, #64748b)' }}
  />
}

function scenarioContent(scenarioId: ScenarioId) {
  switch (scenarioId) {
    case 'chat.empty-suggestions': return <EmptyAndSuggestions />
    case 'chat.app-conversation': return <AppConversationFixture />
    case 'chat.controlled-composer': return <ControlledComposer />
    case 'chat.streaming-keyboard': return <StreamingConversation />
    case 'chat.replacement-validation': return <ReplacementAndValidation />
    case 'chat.large-data': return <LargeConversation />
    case 'chat.locale-pseudo': return <PseudoConversation />
    case 'chat.locale-ar': return <ArabicConversation />
    case 'chat.theme-light': return <StandardConversation dark={false} />
    case 'chat.theme-dark': return <StandardConversation dark />
  }
}

function ChatScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const dark = scenarioId === 'chat.theme-dark'
  const rtl = scenarioId === 'chat.locale-ar'

  return <section
    className="hl-gallery-scene"
    data-gallery-probe
    data-gallery-scenario={scenarioId}
    data-theme={dark ? 'dark' : undefined}
    dir={rtl ? 'rtl' : 'ltr'}
  >
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage" style={{ minWidth: 0 }}>
      {scenarioContent(scenarioId)}
    </div>
  </section>
}

const meta = {
  title: 'Platform/Chat',
  component: ChatScenario,
  tags: ['autodocs'],
  parameters: { layout: 'padded', controls: { disable: true } },
} satisfies Meta<typeof ChatScenario>

export default meta
type Story = StoryObj<typeof meta>

export const EmptyStateAndSuggestions: Story = { name: 'Empty state and suggestions', args: { scenarioId: 'chat.empty-suggestions' } }
export const AppConversation: Story = { name: 'Harborline conversation', args: { scenarioId: 'chat.app-conversation' } }
export const ControlledComposerStory: Story = { name: 'Controlled composer', args: { scenarioId: 'chat.controlled-composer' } }
export const StreamingAndKeyboardNavigation: Story = { name: 'Streaming and keyboard navigation', args: { scenarioId: 'chat.streaming-keyboard' } }
export const ReplacementAndValidationStory: Story = { name: 'Replacement and validation', args: { scenarioId: 'chat.replacement-validation' } }
export const BoundedConversationUnderLoad: Story = { name: 'Bounded conversation under load', args: { scenarioId: 'chat.large-data' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'chat.locale-pseudo' } }
export const ArabicRtlAndLocalizedTime: Story = { name: 'Arabic RTL and localized time', args: { scenarioId: 'chat.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'chat.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'chat.theme-dark' } }
