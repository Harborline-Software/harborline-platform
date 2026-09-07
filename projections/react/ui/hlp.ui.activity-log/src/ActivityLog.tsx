import * as React from 'react'

import { cn } from '@harborline-platform/hlp.ui.cn'
import { useIsMobile } from '@harborline-platform/hlp.ui.use-is-mobile'

export type ActivityTone = 'neutral' | 'positive' | 'warning' | 'danger' | 'info'

export interface ActivityEntry {
  id: string
  actor: string
  action: string
  timestamp: string
  machineTimestamp?: string
  tone?: ActivityTone
  category?: string
  detail?: string
}

export type ActivityLogLayout = 'list' | 'table'

export interface ActivityLogProps {
  entries: readonly ActivityEntry[]
  layout?: ActivityLogLayout
  label?: string
  maxVisible?: number
  onShowMore?: () => void
  empty?: string
  actorLabel?: string
  actionLabel?: string
  timestampLabel?: string
  showMoreLabel?: string
  className?: string
}

function compatibilityTone(category?: string): ActivityTone {
  const normalized = category?.toLocaleLowerCase() ?? ''
  if (/(confirm|success|approv)/.test(normalized)) return 'positive'
  if (/(deny|error|fail|reject)/.test(normalized)) return 'danger'
  if (/(warning|pending|caution)/.test(normalized)) return 'warning'
  if (/(system|info)/.test(normalized)) return 'info'
  return 'neutral'
}

function toneFor(entry: ActivityEntry): ActivityTone {
  return entry.tone ?? compatibilityTone(entry.category)
}

function ActivityTime({ entry }: { entry: ActivityEntry }) {
  return (
    <time dateTime={entry.machineTimestamp} className="hl-activity-log__time">
      {entry.timestamp}
    </time>
  )
}

function ToneMarker({ tone }: { tone: ActivityTone }) {
  return <span aria-hidden="true" className="hl-activity-log__marker" data-hl-tone={tone} />
}

function ListEntry({ entry }: { entry: ActivityEntry }) {
  const tone = toneFor(entry)
  return (
    <li className="hl-activity-log__list-item" data-hl-tone={tone}>
      <ToneMarker tone={tone} />
      <div className="hl-activity-log__entry-content">
        <p className="hl-activity-log__summary">
          <span className="hl-activity-log__actor">{entry.actor}</span>{' '}
          <span className="hl-activity-log__action">{entry.action}</span>
        </p>
        {entry.detail ? <p className="hl-activity-log__detail">{entry.detail}</p> : null}
        <ActivityTime entry={entry} />
      </div>
    </li>
  )
}

function TableEntry({ entry }: { entry: ActivityEntry }) {
  const tone = toneFor(entry)
  return (
    <tr className="hl-activity-log__table-row" data-hl-tone={tone}>
      <td className="hl-activity-log__cell hl-activity-log__cell--actor">
        <span className="hl-activity-log__actor-wrap">
          <ToneMarker tone={tone} />
          <span className="hl-activity-log__actor">{entry.actor}</span>
        </span>
      </td>
      <td className="hl-activity-log__cell hl-activity-log__cell--action">
        <span className="hl-activity-log__action">{entry.action}</span>
        {entry.detail ? <span className="hl-activity-log__detail">{entry.detail}</span> : null}
      </td>
      <td className="hl-activity-log__cell hl-activity-log__cell--time"><ActivityTime entry={entry} /></td>
    </tr>
  )
}

export function ActivityLog({
  entries,
  layout,
  label = 'Activity log',
  maxVisible = 0,
  onShowMore,
  empty = 'No activity yet',
  actorLabel = 'Actor',
  actionLabel = 'Action',
  timestampLabel = 'Timestamp',
  showMoreLabel = 'Show more',
  className,
}: ActivityLogProps) {
  const isMobile = useIsMobile()
  const effectiveLayout: ActivityLogLayout = layout ?? (isMobile ? 'list' : 'table')
  const visible = maxVisible > 0 ? entries.slice(0, maxVisible) : entries
  const hasMore = maxVisible > 0 && entries.length > maxVisible && onShowMore !== undefined
  const headingId = React.useId()

  if (label.trim().length === 0) throw new Error('accessible-name-required')
  const ids = new Set<string>()
  for (const entry of entries) {
    if (ids.has(entry.id)) throw new Error(`duplicate-entry-id: ${entry.id}`)
    ids.add(entry.id)
  }

  return (
    <section
      aria-labelledby={headingId}
      className={cn('hl-activity-log', className)}
      data-hl-layout={effectiveLayout}
    >
      <h2 id={headingId} className="hl-activity-log__heading">{label}</h2>

      {entries.length === 0 ? (
        <p className="hl-activity-log__empty">{empty}</p>
      ) : effectiveLayout === 'list' ? (
        <ol className="hl-activity-log__list">
          {visible.map(entry => <ListEntry key={entry.id} entry={entry} />)}
        </ol>
      ) : (
        <div className="hl-activity-log__table-scroll">
          <table className="hl-activity-log__table">
            <thead>
              <tr>
                <th scope="col" className="hl-activity-log__header hl-activity-log__header--actor">{actorLabel}</th>
                <th scope="col" className="hl-activity-log__header hl-activity-log__header--action">{actionLabel}</th>
                <th scope="col" className="hl-activity-log__header hl-activity-log__header--time">{timestampLabel}</th>
              </tr>
            </thead>
            <tbody>{visible.map(entry => <TableEntry key={entry.id} entry={entry} />)}</tbody>
          </table>
        </div>
      )}

      {hasMore ? (
        <div className="hl-activity-log__more">
          <button type="button" className="hl-activity-log__more-button" onClick={onShowMore}>
            {showMoreLabel}
          </button>
        </div>
      ) : null}
    </section>
  )
}
