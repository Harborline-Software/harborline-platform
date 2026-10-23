import { useEffect, useId, useLayoutEffect, useRef } from 'react'
import { sameOwner, tabFocusIndex } from './presentation-boundary.mjs'

export type AppShellRev6TabKind = 'Details' | 'Related' | 'Log' | 'Timeline' | 'Files' | 'Page' | 'Measures' | 'Layout' | 'Output' | 'Assistant'
export interface AppShellRev6Owner { person: string; tenant: string; workspace: string }
export interface AppShellRev6Tab { id: string; kind: AppShellRev6TabKind; target: string; preview: boolean; pinned: boolean; dirty: boolean }
export interface AppShellRev6Presentation {
  destination: string
  tabs: readonly AppShellRev6Tab[]
  active: string | null
  pendingClose: string | null
  saveRequest: { owner: AppShellRev6Owner; id: string; generation: number; requestId: number } | null
  drafts: Readonly<Record<string, unknown>>
}
export type AppShellRev6Intent =
  | { type: 'open'; kind: AppShellRev6TabKind; target: string }
  | { type: 'close' | 'keep' | 'pin' | 'unpin'; id: string }
  | { type: 'edit'; id: string; draft: string }
  | { type: 'resolve-close'; choice: 'save' | 'discard' | 'cancel' }
export interface AppShellRev6Props {
  shellId: string
  owner: AppShellRev6Owner | null
  presentationOwner: AppShellRev6Owner | null
  presentation: AppShellRev6Presentation | null
  onIntent: (intent: AppShellRev6Intent) => void
}

/** Controlled first slice: the host supplies only the current owner's presentation. */
export function AppShellRev6TabHost({ shellId, owner, presentationOwner, presentation, onIntent }: AppShellRev6Props) {
  const main = useRef<HTMLElement>(null)
  if (!/^[a-z0-9-]+$/.test(shellId)) throw new Error('app-shell-id-required')
  const scope = owner ? JSON.stringify([owner.person, owner.tenant, owner.workspace]) : 'signed-out'
  const view = sameOwner(owner, presentationOwner) ? presentation : null
  return <div data-shell-id={shellId} data-revision="6">
    <main ref={main} tabIndex={-1} aria-label="Current destination"><h1>{owner ? view?.destination ?? 'Home' : 'Signed out'}</h1></main>
    {owner && view && <TabHost key={scope} presentation={view} onIntent={onIntent} focusMain={() => main.current?.focus()} />}
  </div>
}

function TabHost({ presentation: view, onIntent, focusMain }: Pick<AppShellRev6Props, 'presentation' | 'onIntent'> & { focusMain: () => void }) {
  const prefix = useId()
  const buttons = useRef(new Map<string, HTMLButtonElement>())
  const dialog = useRef<HTMLDivElement>(null)
  const returnFocus = useRef<{ id: string; element: HTMLElement | null } | null>(null)
  const cleanClose = useRef<string | null>(null)
  const pending = view?.pendingClose
  useLayoutEffect(() => {
    if (pending) {
      if (returnFocus.current?.id !== pending) {
        const element = document.activeElement as HTMLElement
        returnFocus.current = { id: pending, element: element.closest('[data-editor-id]')?.getAttribute('data-editor-id') === pending ? element : null }
        dialog.current?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
      } else if (document.activeElement instanceof HTMLButtonElement && document.activeElement.disabled && dialog.current?.contains(document.activeElement)) {
        dialog.current.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus()
      }
      return
    }
    const origin = returnFocus.current
    const closedCleanEditor = cleanClose.current !== null && !view?.tabs.some(tab => tab.id === cleanClose.current)
    if (closedCleanEditor) cleanClose.current = null
    if (!origin && !closedCleanEditor) return
    returnFocus.current = null
    if (origin?.element?.isConnected && view?.tabs.some(tab => tab.id === origin.id) && view.active === origin.id) {
      origin.element.focus()
      return
    }
    const selected = view?.active ? buttons.current.get(view.active) : null
    const fallback = selected ?? buttons.current.values().next().value
    if (fallback) fallback.focus()
    else focusMain()
  }, [pending, view?.saveRequest, view?.active, view?.tabs, focusMain])
  useEffect(() => { if (view?.active) buttons.current.get(view.active)?.scrollIntoView?.({ block: 'nearest', inline: 'nearest' }) }, [view?.active])
  if (!view) return null
  const tabs = [...view.tabs.filter(tab => tab.pinned), ...view.tabs.filter(tab => !tab.pinned)]
  const active = tabs.find(tab => tab.id === view.active)
  const activate = (tab: AppShellRev6Tab) => onIntent({ type: 'open', kind: tab.kind, target: tab.target })
  return <aside aria-label="Panel tabs" style={{ border: '1px solid currentColor', padding: '1rem', maxWidth: '48rem' }}>
    <div inert={!!pending}>
    <div role="tablist" aria-label="Open tabs" style={{ display: 'flex', overflowX: 'auto', gap: '.25rem' }}>{tabs.map((tab, index) => <button type="button" key={tab.id}
      ref={node => { if (node) buttons.current.set(tab.id, node); else buttons.current.delete(tab.id) }}
      id={`${prefix}-tab-${index}`} role="tab" aria-selected={tab.id === view.active}
      aria-controls={`${prefix}-panel`} tabIndex={tab.id === view.active || (!active && index === 0) ? 0 : -1}
      aria-label={`${tab.kind}: ${tab.target}${tab.preview ? ', preview' : ''}${tab.pinned ? ', pinned' : ''}${tab.dirty ? ', unsaved' : ''}`}
      onClick={() => activate(tab)} onDoubleClick={() => onIntent({ type: 'keep', id: tab.id })}
      onKeyDown={event => {
        const next = tabFocusIndex(event.key, index, tabs.length)
        if (next < 0) return
        event.preventDefault(); buttons.current.get(tabs[next].id)?.focus(); activate(tabs[next])
      }}>{tab.preview ? <em>{tab.kind}: {tab.target}</em> : `${tab.kind}: ${tab.target}`}</button>)}</div>
    {active ? <section key={active.id} data-editor-id={active.id} id={`${prefix}-panel`} role="tabpanel" aria-labelledby={`${prefix}-tab-${tabs.indexOf(active)}`}>
      <p>{active.preview ? 'Preview' : 'Permanent'}{active.dirty ? ' - Unsaved changes' : ''}</p>
      <label>Draft for {active.target}<textarea value={typeof view.drafts[active.id] === 'string' ? view.drafts[active.id] as string : ''}
        onChange={event => onIntent({ type: 'edit', id: active.id, draft: event.target.value })} /></label>
      {active.preview && <button type="button" onClick={() => onIntent({ type: 'keep', id: active.id })}>Keep open</button>}
      <button type="button" onClick={() => onIntent({ type: active.pinned ? 'unpin' : 'pin', id: active.id })}>{active.pinned ? 'Unpin' : 'Pin'}</button>
      <button type="button" disabled={active.pinned} onClick={() => {
        if (!active.dirty) cleanClose.current = active.id
        onIntent({ type: 'close', id: active.id })
      }}>Close tab</button>
    </section> : <p>No open tabs</p>}
    </div>
    {pending && <div ref={dialog} role="dialog" aria-modal="true" aria-labelledby={`${prefix}-dirty-title`}
      style={{ border: '2px solid currentColor', padding: '1rem', marginTop: '1rem' }}
      onKeyDown={event => {
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); onIntent({ type: 'resolve-close', choice: 'cancel' }) }
        if (event.key === 'Tab') {
          const controls = Array.from(dialog.current?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? [])
          const index = controls.indexOf(document.activeElement as HTMLButtonElement)
          const next = index < 0 ? (event.shiftKey ? controls.length - 1 : 0) : (index + (event.shiftKey ? controls.length - 1 : 1)) % controls.length
          event.preventDefault(); controls[next]?.focus()
        }
      }}>
      <h2 id={`${prefix}-dirty-title`}>Save changes before closing?</h2>
      <p>{view.saveRequest ? 'Save requested. Waiting for host acknowledgment.' : 'Your editor remains open until you choose.'}</p>
      <button type="button" disabled={!!view.saveRequest} onClick={() => {
        dialog.current?.querySelector<HTMLButtonElement>('[data-close-choice="discard"]')?.focus()
        onIntent({ type: 'resolve-close', choice: 'save' })
      }}>Save</button>
      <button type="button" data-close-choice="discard" onClick={() => onIntent({ type: 'resolve-close', choice: 'discard' })}>Discard</button>
      <button type="button" onClick={() => onIntent({ type: 'resolve-close', choice: 'cancel' })}>Cancel</button>
    </div>}
  </aside>
}
