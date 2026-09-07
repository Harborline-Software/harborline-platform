import * as React from 'react'
import { shellAddress, type ShellNavItem, type ShellNavThread } from './types'
import type { ShellGroupViewModel, ShellWorkspaceViewModel } from './pack-navigation-mapper'

export function validateNav(workspaces: readonly ShellWorkspaceViewModel[]): void {
  const workspaceIds = new Set<string>()
  for (const workspace of workspaces) {
    if (workspaceIds.has(workspace.id)) throw new Error('duplicate-nav-identity')
    workspaceIds.add(workspace.id)
    const ids = new Set<string>()
    const claim = (id: string) => { if (ids.has(id)) throw new Error('duplicate-nav-identity'); ids.add(id) }
    for (const action of workspace.createActions) claim(action.id)
    for (const group of workspace.groups) {
      claim(group.id)
      for (const item of group.items) {
        if ('children' in item || 'items' in item) throw new Error('unsupported-nav-shape')
        claim(item.id)
        for (const thread of item.threads ?? []) { assertThread(thread); claim(thread.id) }
      }
    }
    for (const item of workspace.recent ?? []) claim(item.id)
    if (workspace.suggested) claim(workspace.suggested.id)
    const creates = workspace.createActions
    if (creates.length >= 2 && creates.length <= 3 && !creates.some(action => action.id === workspace.defaultCreateActionId)) throw new Error('primary-create-required')
  }
}

function assertThread(thread: ShellNavThread) {
  if ('threads' in thread && (thread as { threads?: unknown }).threads !== undefined) throw new Error('unsupported-nav-shape')
}

export function splitPinned(workspace: ShellWorkspaceViewModel, pinnedItemIds: readonly string[]): { pinned: ShellNavItem[]; groups: ShellGroupViewModel[] } {
  const all = new Map(workspace.groups.flatMap(group => group.items.map(item => [item.id, item] as const)))
  const pinned = pinnedItemIds.map(id => all.get(id)).filter((item): item is ShellNavItem => item !== undefined)
  const pinnedSet = new Set(pinned.map(item => item.id))
  return { pinned, groups: workspace.groups.map(group => ({ ...group, items: group.items.filter(item => !pinnedSet.has(item.id)) })) }
}

export interface ShellRailProps {
  workspace: ShellWorkspaceViewModel; activeItemId?: string
  pinnedItemIds: readonly string[]; pinCap: number; groupCap?: number; onBindingInvoke?: (binding: string) => void
  onNavigate?: (item: ShellNavItem) => void; onPinToggle: (itemId: string, pinned: boolean) => void
  onThreadActivate?: (thread: ShellNavThread, ownerId: string) => void; onThreadAction?: (thread: ShellNavThread, actionId: string) => void
  onThreadRename?: (thread: ShellNavThread, value: string) => void; onThreadRenameCancel?: (thread: ShellNavThread) => void
  pinnedLabel?: string; pinnedEmptyLabel?: string; groupsLabel?: string; pinLabel?: string; unpinLabel?: string; threadOptionsLabel?: string
}

export function ShellRail({ workspace, activeItemId, pinnedItemIds, pinCap, groupCap = 7, onBindingInvoke, onNavigate, onPinToggle, onThreadActivate, onThreadAction, onThreadRename, onThreadRenameCancel, pinnedLabel = 'Pinned', pinnedEmptyLabel = 'Nothing pinned yet', groupsLabel = 'Groups', pinLabel = 'Pin', unpinLabel = 'Unpin', threadOptionsLabel = 'Session options' }: ShellRailProps) {
  const { pinned, groups } = splitPinned(workspace, pinnedItemIds)
  const [showAllPinned, setShowAllPinned] = React.useState(false)
  const [expandedGroups, setExpandedGroups] = React.useState<ReadonlySet<string>>(new Set())
  const visibleGroups = groups.filter(group => group.items.length > 0 || group.addAction)
  const row = (item: ShellNavItem, isPinned: boolean) => <div key={item.id} className="hl-app-shell__rail-row" data-shell-row>
    <a className="hl-app-shell__rail-link" href={shellAddress(item.kind ?? 'workspaces', item.id)} aria-current={item.id === activeItemId ? 'page' : undefined} onClick={() => onNavigate?.(item)}>
      {item.icon}<span className="hl-app-shell__rail-label">{item.label}</span>{item.count !== undefined ? <span className="hl-app-shell__count" data-shell-count>{item.count}</span> : null}
    </a>
    {item.pinnable === false ? null : <button type="button" className="hl-app-shell__pin-toggle" aria-label={`${isPinned ? unpinLabel : pinLabel} ${item.label}`} onClick={event => { event.stopPropagation(); if (!isPinned && pinnedItemIds.length >= pinCap) return; onPinToggle(item.id, !isPinned) }}>
      <svg aria-hidden="true" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d={isPinned ? 'M12 17v5M15 9.34V6h1a2 2 0 0 0 0-4H7.89M2 2l20 20M9 9v1.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h11' : 'M12 17v5M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V6h1a2 2 0 0 0 0-4H8a2 2 0 0 0 0 4h1z'} /></svg>
    </button>}
    {item.threads?.length ? <Threads threads={item.threads} ownerId={item.id} onThreadActivate={onThreadActivate} onThreadAction={onThreadAction} onThreadRename={onThreadRename} onThreadRenameCancel={onThreadRenameCancel} threadOptionsLabel={threadOptionsLabel} /> : null}
  </div>
  const visiblePinned = showAllPinned ? pinned : pinned.slice(0, pinCap)
  return <>
    <section className="hl-app-shell__zone" data-shell-zone="pinned" data-zone-cap={pinCap} aria-label={pinnedLabel}>
      <div className="hl-app-shell__zone-label">{pinnedLabel}</div>
      {visiblePinned.length ? visiblePinned.map(item => row(item, true)) : <p className="hl-app-shell__zone-empty">{pinnedEmptyLabel}</p>}
      {!showAllPinned && pinned.length > pinCap ? <button type="button" className="hl-app-shell__show-more" onClick={() => setShowAllPinned(true)}>Show {pinned.length - pinCap} more</button> : null}
    </section>
    {visibleGroups.length ? <section className="hl-app-shell__zone" data-shell-zone="groups" data-zone-cap={groupCap} aria-label={groupsLabel}>
      <div className="hl-app-shell__zone-label">{groupsLabel}</div>
      {visibleGroups.map(group => { const expanded = expandedGroups.has(group.id); const visible = expanded ? group.items : group.items.slice(0, groupCap); return <div key={group.id} className="hl-app-shell__group" data-group-id={group.id}>
        <div className="hl-app-shell__group-label"><span>{group.label}</span>{group.addAction ? <button type="button" className="hl-app-shell__group-add" aria-label={group.addAction.label} onClick={() => onBindingInvoke?.(group.addAction!.binding)}>+</button> : null}</div>{visible.map(item => row(item, false))}
        {!expanded && group.items.length > groupCap ? <button type="button" className="hl-app-shell__show-more" onClick={() => setExpandedGroups(current => new Set(current).add(group.id))}>Show {group.items.length - groupCap} more</button> : null}
      </div> })}
    </section> : null}
  </>
}

function Threads({ threads, ownerId, onThreadActivate, onThreadAction, onThreadRename, onThreadRenameCancel, threadOptionsLabel }: { threads: readonly ShellNavThread[]; ownerId: string; onThreadActivate?: (thread: ShellNavThread, ownerId: string) => void; onThreadAction?: (thread: ShellNavThread, actionId: string) => void; onThreadRename?: (thread: ShellNavThread, value: string) => void; onThreadRenameCancel?: (thread: ShellNavThread) => void; threadOptionsLabel: string }) {
  const [menuFor, setMenuFor] = React.useState<string | null>(null)
  return <div className="hl-app-shell__threads">{threads.map(thread => <div key={thread.id} className="hl-app-shell__thread-row">
    {thread.editing ? <RenameInput thread={thread} onCommit={onThreadRename} onCancel={onThreadRenameCancel} /> : <button type="button" className="hl-app-shell__thread-btn" data-active={thread.active || undefined} title={thread.label} onClick={() => onThreadActivate?.(thread, ownerId)}>{thread.mark}<span className="hl-app-shell__thread-label">{thread.label}</span></button>}
    {thread.actions?.length ? <><button type="button" className="hl-app-shell__kebab" aria-label={threadOptionsLabel} aria-haspopup="menu" aria-expanded={menuFor === thread.id} onClick={() => setMenuFor(menuFor === thread.id ? null : thread.id)}><svg aria-hidden="true" width="14" height="14" viewBox="0 0 16 16" fill="currentColor"><circle cx="8" cy="3" r="1.4" /><circle cx="8" cy="8" r="1.4" /><circle cx="8" cy="13" r="1.4" /></svg></button>{menuFor === thread.id ? <div role="menu" aria-label={threadOptionsLabel} className="hl-app-shell__row-menu" onKeyDown={event => { if (event.key === 'Escape') setMenuFor(null) }}>{[...thread.actions.filter(action => !action.destructive), ...thread.actions.filter(action => action.destructive)].map(action => <button key={action.id} type="button" role="menuitem" className="hl-app-shell__row-menu-item" data-destructive={action.destructive || undefined} onClick={() => { setMenuFor(null); onThreadAction?.(thread, action.id) }}>{action.label}{action.keyHint ? <span className="hl-app-shell__key-hint">{action.keyHint}</span> : null}</button>)}</div> : null}</> : null}
  </div>)}</div>
}

function RenameInput({ thread, onCommit, onCancel }: { thread: ShellNavThread; onCommit?: (thread: ShellNavThread, value: string) => void; onCancel?: (thread: ShellNavThread) => void }) {
  const [value, setValue] = React.useState(thread.label)
  return <input className="hl-app-shell__thread-rename" aria-label={`Rename ${thread.label}`} value={value} onChange={event => setValue(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') { event.preventDefault(); onCommit?.(thread, value) } else if (event.key === 'Escape') { event.preventDefault(); onCancel?.(thread) } }} />
}
