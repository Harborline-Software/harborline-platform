// Experimental rev6 presentation model. No storage, commands, authorization or pack execution.
const kinds = new Set(['Details', 'Related', 'Log', 'Timeline', 'Files', 'Page', 'Measures', 'Layout', 'Output', 'Assistant'])
const key = owner => JSON.stringify([owner.person, owner.tenant, owner.workspace])
const tabKey = tab => JSON.stringify([tab.kind, tab.target])
const empty = () => ({ destination: 'home', tabs: [], active: null, pendingClose: null, saveRequest: null, draftGenerations: {}, caches: {}, drafts: {}, queuedActions: [] })
export const initialState = () => ({ owner: null, owners: {}, nextRequest: 1 })
export function presentation(state) { return state.owner ? state.owners[key(state.owner)] : null }

export function transition(state, event) {
  const next = structuredClone(state)
  if (event.type === 'identity') {
    if (!event.owner || !['person', 'tenant', 'workspace'].every(field => typeof event.owner[field] === 'string' && event.owner[field].length)) throw new Error('identity-required')
    next.owner = { person: event.owner.person, tenant: event.owner.tenant, workspace: event.owner.workspace }
    next.owners[key(next.owner)] ??= empty()
    return next
  }
  if (event.type === 'sign-out') { next.owner = null; return next }
  if (event.type === 'saved') {
    // An acknowledgment belongs to the request's original owner, even after sign-out.
    const owned = event.owner && next.owners[key(event.owner)]
    const request = owned?.saveRequest
    if (request && owned.pendingClose === event.id && request.requestId === event.requestId && request.id === event.id
      && request.generation === event.generation && owned.draftGenerations[event.id] === event.generation) {
      delete owned.drafts[event.id]
      remove(owned, event.id)
      owned.pendingClose = null
      owned.saveRequest = null
    }
    return next
  }
  const view = presentation(next)
  if (!view) throw new Error('identity-required')
  switch (event.type) {
    case 'open': {
      if (!kinds.has(event.kind) || typeof event.target !== 'string' || !event.target) throw new Error('unregistered-tab-kind-or-target')
      const id = tabKey(event)
      const existing = view.tabs.find(tab => tab.id === id)
      if (!existing) {
        if (event.preview) view.tabs = view.tabs.filter(tab => !tab.preview)
        view.tabs.push({ id, kind: event.kind, target: event.target, preview: !!event.preview, pinned: false, dirty: false })
      }
      view.active = id
      break
    }
    case 'navigate':
      if (view.destination !== event.destination) {
        view.tabs = view.tabs.filter(tab => !tab.preview)
        view.destination = event.destination
        if (!view.tabs.some(tab => tab.id === view.active)) view.active = view.tabs.at(-1)?.id ?? null
      }
      break
    case 'keep': case 'pin': case 'edit': {
      const tab = view.tabs.find(tab => tab.id === event.id)
      if (!tab) throw new Error('tab-not-found')
      tab.preview = false
      if (event.type === 'pin') tab.pinned = true
      if (event.type === 'edit') {
        tab.dirty = true; view.drafts[tab.id] = event.draft
        view.draftGenerations[tab.id] = (view.draftGenerations[tab.id] ?? 0) + 1
        if (view.saveRequest?.id === tab.id) view.saveRequest = null
      }
      break
    }
    case 'unpin': {
      const tab = view.tabs.find(tab => tab.id === event.id)
      if (tab) tab.pinned = false
      break
    }
    case 'close': {
      // The dirty destruction prompt is one modal; a second close cannot replace it.
      if (view.pendingClose && view.pendingClose !== event.id) break
      const tab = view.tabs.find(tab => tab.id === event.id)
      if (!tab || tab.pinned) break
      if (tab.dirty) view.pendingClose = tab.id
      else remove(view, tab.id)
      break
    }
    case 'resolve-close': {
      if (!['save', 'discard', 'cancel'].includes(event.choice)) throw new Error('invalid-close-choice')
      const id = view.pendingClose
      if (!id) break
      // Save is a request only. The editor survives until the adapter acknowledges success.
      if (event.choice === 'save') {
        view.saveRequest ??= { owner: structuredClone(next.owner), id, generation: view.draftGenerations[id], requestId: next.nextRequest++ }
        break
      }
      view.pendingClose = null
      view.saveRequest = null
      if (event.choice === 'discard') { delete view.drafts[id]; remove(view, id) }
      break
    }
    case 'retain':
      view.caches[event.id] = event.cache
      view.queuedActions.push(event.action)
      break
    default: throw new Error('unknown-transition')
  }
  return next
}
function remove(view, id) {
  view.tabs = view.tabs.filter(tab => tab.id !== id)
  if (view.active === id) view.active = view.tabs.at(-1)?.id ?? null
}
export function runScenario(events) { return events.reduce(transition, initialState()) }
