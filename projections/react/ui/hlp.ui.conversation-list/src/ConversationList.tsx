import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useHarborlineStrings } from '@harborline-platform/hlp.ui.locale-provider'

export interface ConversationSummary {
  readonly id: string
  readonly title: string
  readonly timestamp: string
  readonly preview?: string
}

export interface ConversationListLabels {
  readonly heading?: string
  readonly newConversation?: string
  readonly empty?: string
  readonly rename?: string
  readonly delete?: string
  readonly confirmDelete?: string
  readonly cancel?: string
  readonly getRowMenuLabel?: (title: string) => string
}

export interface ConversationListProps {
  readonly conversations: readonly ConversationSummary[]
  readonly activeId?: string | null
  readonly onSelect: (id: string) => void
  readonly onNew?: () => void
  readonly onRename?: (id: string, title: string) => void
  readonly onDelete?: (id: string) => void
  readonly labels?: ConversationListLabels
  readonly className?: string
}

type RowMode = 'view' | 'rename' | 'confirm-delete'
type FocusDestination = 'select' | 'rename' | 'delete'
type ResolvedLabels = Required<Omit<ConversationListLabels, 'getRowMenuLabel'>>

interface ConversationRowProps {
  readonly item: ConversationSummary
  readonly active: boolean
  readonly labels: ResolvedLabels
  readonly getRowMenuLabel: (title: string) => string
  readonly getRenameLabel: (title: string) => string
  readonly getDeleteLabel: (title: string) => string
  readonly onSelect: () => void
  readonly onRename?: (title: string) => void
  readonly onDelete?: () => void
}

function ConversationRow({
  item,
  active,
  labels,
  getRowMenuLabel,
  getRenameLabel,
  getDeleteLabel,
  onSelect,
  onRename,
  onDelete,
}: ConversationRowProps) {
  const [mode, setMode] = React.useState<RowMode>('view')
  const [draft, setDraft] = React.useState(item.title)
  const pendingFocus = React.useRef<FocusDestination | null>(null)
  const renameComplete = React.useRef(false)
  const selectRef = React.useRef<HTMLButtonElement>(null)
  const renameRef = React.useRef<HTMLButtonElement>(null)
  const deleteRef = React.useRef<HTMLButtonElement>(null)
  const inputRef = React.useRef<HTMLInputElement>(null)
  const confirmRef = React.useRef<HTMLButtonElement>(null)

  React.useLayoutEffect(() => {
    if (mode === 'rename') {
      inputRef.current?.focus()
      inputRef.current?.select()
    }
    if (mode === 'confirm-delete') confirmRef.current?.focus()
    if (mode !== 'view' || pendingFocus.current === null) return
    const destination = pendingFocus.current
    pendingFocus.current = null
    const target = destination === 'rename'
      ? renameRef.current ?? selectRef.current
      : destination === 'delete'
        ? deleteRef.current ?? selectRef.current
        : selectRef.current
    target?.focus()
  }, [mode])

  const leaveMode = (destination: FocusDestination) => {
    pendingFocus.current = destination
    setMode('view')
  }

  const commitRename = () => {
    if (renameComplete.current) return
    renameComplete.current = true
    const title = draft.trim()
    if (title && title !== item.title) onRename?.(title)
    leaveMode(onRename ? 'rename' : 'select')
  }

  if (mode === 'rename') {
    return (
      <li className="hl-conversation-list__item hl-conversation-list__item--editing">
        <form
          className="hl-conversation-list__rename-form"
          onSubmit={event => {
            event.preventDefault()
            commitRename()
          }}
        >
          <input
            aria-label={labels.rename}
            className="hl-conversation-list__rename-input"
            onBlur={commitRename}
            onChange={event => setDraft(event.currentTarget.value)}
            onKeyDown={event => {
              if (event.key !== 'Escape') return
              event.preventDefault()
              renameComplete.current = true
              setDraft(item.title)
              leaveMode(onRename ? 'rename' : 'select')
            }}
            ref={inputRef}
            value={draft}
          />
        </form>
      </li>
    )
  }

  if (mode === 'confirm-delete') {
    return (
      <li
        className="hl-conversation-list__item hl-conversation-list__item--confirm"
        onKeyDown={event => {
          if (event.key !== 'Escape') return
          event.preventDefault()
          leaveMode(onDelete ? 'delete' : 'select')
        }}
      >
        <p className="hl-conversation-list__confirm-title">{item.title}</p>
        <div className="hl-conversation-list__confirm-actions">
          <button
            className="hl-conversation-list__confirm"
            onClick={() => {
              onDelete?.()
              leaveMode(onDelete ? 'delete' : 'select')
            }}
            ref={confirmRef}
            type="button"
          >
            <span aria-hidden="true" className="hl-conversation-list__danger-mark"><svg fill="none" focusable="false" viewBox="0 0 16 16"><path d="M8 2.5 14 13H2L8 2.5Z" /><path d="M8 6v3.25M8 11.25h.01" /></svg></span>
            {labels.confirmDelete}
          </button>
          <button
            className="hl-conversation-list__cancel"
            onClick={() => leaveMode(onDelete ? 'delete' : 'select')}
            type="button"
          >
            {labels.cancel}
          </button>
        </div>
      </li>
    )
  }

  return (
    <li className={cn('hl-conversation-list__item', active && 'hl-conversation-list__item--active')}>
      <button
        aria-current={active ? 'true' : undefined}
        className="hl-conversation-list__select"
        onClick={onSelect}
        ref={selectRef}
        type="button"
      >
        <span className="hl-conversation-list__title">{item.title}</span>
        {item.preview ? <span className="hl-conversation-list__preview">{item.preview}</span> : null}
        <span className="hl-conversation-list__timestamp">{item.timestamp}</span>
      </button>
      {onRename || onDelete ? (
        <div className="hl-conversation-list__row-actions">
          {onRename ? (
            <button
              aria-label={getRenameLabel(item.title)}
              className="hl-conversation-list__row-action"
              onClick={() => {
                renameComplete.current = false
                setDraft(item.title)
                setMode('rename')
              }}
              ref={renameRef}
              type="button"
            >
              <svg aria-hidden="true" fill="none" focusable="false" viewBox="0 0 16 16"><path d="m3 11.75-.5 2 2-.5L12.75 5 11 3.25 3 11.75Z" /><path d="m9.75 4.5 1.75 1.75" /></svg>
            </button>
          ) : null}
          {onDelete ? (
            <button
              aria-label={getDeleteLabel(item.title)}
              className="hl-conversation-list__row-action hl-conversation-list__row-action--delete"
              onClick={() => setMode('confirm-delete')}
              ref={deleteRef}
              type="button"
            >
              <svg aria-hidden="true" fill="none" focusable="false" viewBox="0 0 16 16"><path d="M4 4l8 8M12 4l-8 8" /></svg>
            </button>
          ) : null}
        </div>
      ) : null}
      <span className="hl-sr-only">{getRowMenuLabel(item.title)}</span>
    </li>
  )
}

export function ConversationList({
  conversations,
  activeId,
  onSelect,
  onNew,
  onRename,
  onDelete,
  labels: labelOverrides,
  className,
}: ConversationListProps) {
  const { direction, t } = useHarborlineStrings()
  const labels: ResolvedLabels = {
    heading: labelOverrides?.heading ?? t('ai.conversations.heading'),
    newConversation: labelOverrides?.newConversation ?? t('ai.conversations.new'),
    empty: labelOverrides?.empty ?? t('ai.conversations.empty'),
    rename: labelOverrides?.rename ?? t('ai.conversations.rename'),
    delete: labelOverrides?.delete ?? t('ai.conversations.delete'),
    confirmDelete: labelOverrides?.confirmDelete ?? t('ai.conversations.delete'),
    cancel: labelOverrides?.cancel ?? t('ai.conversations.cancel'),
  }
  const getRowMenuLabel = labelOverrides?.getRowMenuLabel
    ?? ((title: string) => t('ai.conversations.actionsFor', { title }))
  const getRenameLabel = labelOverrides?.rename === undefined
    ? (title: string) => t('ai.conversations.renameTitle', { title })
    : (title: string) => `${labels.rename}: ${title}`
  const getDeleteLabel = labelOverrides?.delete === undefined
    ? (title: string) => t('ai.conversations.deleteTitle', { title })
    : (title: string) => `${labels.delete}: ${title}`

  const seenIds = new Set<string>()
  let duplicateId: string | undefined
  for (const item of conversations) {
    if (seenIds.has(item.id)) {
      duplicateId = item.id
      break
    }
    seenIds.add(item.id)
  }
  if (duplicateId) throw new Error(`duplicate-conversation-id: ${duplicateId}`)
  if (!labels.heading.trim()) throw new Error('accessible-heading-required')

  return (
    <section className={cn('hl-conversation-list', className)} data-testid="conversation-list" dir={direction}>
      <header className="hl-conversation-list__header">
        <h2 className="hl-conversation-list__heading">{labels.heading}</h2>
        {onNew ? (
          <button className="hl-conversation-list__new" onClick={onNew} type="button">
            {labels.newConversation}
          </button>
        ) : null}
      </header>
      {conversations.length === 0 ? (
        <p className="hl-conversation-list__empty">{labels.empty}</p>
      ) : (
        <ul aria-label={labels.heading} className="hl-conversation-list__list">
          {conversations.map(item => (
            <ConversationRow
              active={item.id === activeId}
              getDeleteLabel={getDeleteLabel}
              getRowMenuLabel={getRowMenuLabel}
              getRenameLabel={getRenameLabel}
              item={item}
              key={item.id}
              labels={labels}
              onDelete={onDelete ? () => onDelete(item.id) : undefined}
              onRename={onRename ? title => onRename(item.id, title) : undefined}
              onSelect={() => onSelect(item.id)}
            />
          ))}
        </ul>
      )}
    </section>
  )
}
