// ── The keyboard tab walk (T-962) ──
//
// The engine behind assertTabWalk in gallery.spec.ts. It contains no `expect`, like
// navigation-resilience.ts: it walks and reports, and the spec judges what it reports against the
// register. Everything a run decides is in lines that carry accessible names (stop keys), never
// indices, so a register row does not drift when an unrelated stop is added.
//
// What it proves, per scenario and per lane (DES-0034 §9; R-0125 B5 and recommendation 3):
// - WCAG 2.1.2 No Keyboard Trap: Tab from the top, and from the focus the scenario prepared, always
//   leaves the scenario, or a modal releases it by Escape or one of its own buttons.
// - WCAG 2.4.3 Focus Order: Shift+Tab retraces the Tab order; focus is never lost to the body, never
//   leaks out of an open modal, never follows a positive tabindex (technique F44); and the cross-lane
//   half (the same order in React and Blazor) is judged by the spec from laneOrderLines.
// - WCAG 2.4.11 Focus Not Obscured (Minimum): every stop, in both directions, is inside the viewport
//   and not entirely hidden by author content.
//
// Prior art, implemented clean room: the "Tab stops" assessment of Accessibility Insights for Web
// (walk the sequential focus order, record it, look for traps and order problems), the APG modal
// dialog pattern (a modal keeps Tab inside and releases on Escape or a dismiss control), the APG
// keyboard interface guidance on roving tabindex (a composite is one Tab stop), and HTML's
// sequential focus navigation with its starting point (what happens after focus is lost).
import type { Page } from '@playwright/test'

import { evaluateSettled } from './navigation-resilience.ts'

export const TAB_WALK_ANNOTATION = 'hlp-tab-walk'

export type Projection = 'react' | 'blazor'
export type StopLocation = 'probe' | 'portal' | 'host' | 'body' | 'frame' | 'sentinel-start' | 'sentinel-end'
export type StopDescription = {
  // Registry id. 0 is the body (focus lost), -1 the start sentinel, -2 the end sentinel.
  id: number
  tag: string
  location: StopLocation
  // Registry id of the aria-modal container the stop sits in, or -1.
  modal: number
  radioGroup?: string
  segmented: boolean
  scroller: boolean
  hasRole: boolean
  tabIndex: number
}
export type StopVerdict = {
  kind: 'visible' | 'obscured' | 'clipped' | 'offscreen' | 'unmeasured' | 'exempt-user-opened'
  partial?: boolean
  occluder?: string
}
export type LogEntry = { stop: StopDescription; mutated: boolean; verdict: StopVerdict | null; leak: number; gone?: boolean }
export type ReadResult = { active: StopDescription; entries: LogEntry[]; overshoot: number; mutated: boolean; costMs?: Record<string, number> }
export type Census = {
  count: number
  // The first tab stop from the top, when it is plain document order (tabindex 0): where F starts.
  first: number
  digest: string
  openModals: number[]
  openPopups: number
  positiveTabindex: Array<{ stop: StopDescription }>
}
export type TabWalkRuntime = {
  read(): Promise<ReadResult>
  readSettled(): Promise<ReadResult>
  settle(maxMs: number): Promise<boolean>
  census(): Census
  describe(id: number): StopDescription | null
  anchor(direction: 'forward' | 'reverse'): { landed: boolean; active: StopDescription }
  active(): StopDescription
  documentOrder(ids: number[]): number[]
  released(modalId: number): boolean
  submitsForm(id: number): boolean
  teardown(): void
}

declare global {
  interface Window { __hlpTabWalk?: TabWalkRuntime }
}

// ── In-page runtime ──
//
// One self-contained function handed to page.evaluate: no module closures, because Playwright
// serialises its source. It installs two body-level sentinels that bracket the whole document, a
// registry that gives every element it describes a stable id (and a data attribute the Node side
// builds a locator from), a change observer, and a capture keydown listener on window, which runs
// before every component's document-level handler.
export function installTabWalkRuntime(): void {
  window.__hlpTabWalk?.teardown()
  const SENTINEL = 'data-hl-tab-walk'
  const ID_ATTRIBUTE = 'data-hl-tab-walk-id'
  const makeSentinel = (which: 'start' | 'end') => {
    const span = document.createElement('span')
    span.tabIndex = 0
    span.setAttribute(SENTINEL, which)
    span.setAttribute('style', 'position:fixed;top:0;left:0;width:1px;height:1px;opacity:0;pointer-events:none;overflow:hidden')
    return span
  }
  const start = makeSentinel('start')
  const end = makeSentinel('end')
  const isSentinel = (node: Node | null | undefined) => node === start || node === end

  const deepActive = (): Element | null => {
    let active: Element | null = document.activeElement
    while (active?.shadowRoot?.activeElement) active = active.shadowRoot.activeElement
    return active
  }
  // The sentinels stay first and last child of body, even when a component portals something to
  // the end of body, but a sentinel that has focus is never moved (moving it would blur it).
  const place = () => {
    const active = deepActive()
    if (document.body.firstChild !== start && active !== start) document.body.prepend(start)
    if (document.body.lastChild !== end && active !== end) document.body.append(end)
  }
  place()
  const bodyObserver = new MutationObserver(place)
  bodyObserver.observe(document.body, { childList: true })

  const registry = new Map<Element, number>()
  const byId = new Map<number, Element>()
  let nextId = 1
  const idOf = (element: Element) => {
    let id = registry.get(element)
    if (id === undefined) {
      id = nextId++
      registry.set(element, id)
      byId.set(id, element)
      element.setAttribute(ID_ATTRIBUTE, String(id))
    }
    return id
  }

  // Changes that can move, hide, add or remove a tab stop. Records about the sentinels are the
  // walker's own and are dropped. Drained synchronously with takeRecords().
  const relevant = (record: MutationRecord) => {
    if (isSentinel(record.target)) return false
    if (record.type === 'childList') {
      const nodes = [...Array.from(record.addedNodes), ...Array.from(record.removedNodes)]
      if (nodes.length > 0 && nodes.every(node => isSentinel(node))) return false
    }
    return true
  }
  let pending = 0
  const changes = new MutationObserver(records => { pending += records.filter(relevant).length })
  changes.observe(document.documentElement, {
    subtree: true, childList: true, attributes: true,
    attributeFilter: ['tabindex', 'inert', 'hidden', 'disabled', 'open', 'aria-modal', 'aria-hidden', 'class', 'style', 'contenteditable'],
  })
  const drain = () => {
    const count = pending + changes.takeRecords().filter(relevant).length
    pending = 0
    return count
  }

  const probe = () => document.querySelector('[data-gallery-probe]')
  const isModalContainer = (element: Element) =>
    element.getAttribute('aria-modal') === 'true' || (element.tagName === 'DIALOG' && element.matches(':modal'))
  const visible = (element: Element) => element.checkVisibility({ visibilityProperty: true })
  const openModals = () => Array.from(document.querySelectorAll('[aria-modal="true"], dialog'))
    .filter(element => isModalContainer(element) && visible(element) && !element.closest('[inert]'))
  const modalAround = (element: Element) => {
    for (let node: Element | null = element; node; node = node.parentElement) if (isModalContainer(node)) return node
    return null
  }
  const popupSelector = '[role="menu"], [role="listbox"], [role="tooltip"], [role="dialog"]:not([aria-modal="true"]), [popover]'
  const openPopups = () => Array.from(document.querySelectorAll(popupSelector))
    .filter(element => visible(element) && (!element.hasAttribute('popover') || element.matches(':popover-open')))
  // WCAG 2.2 Understanding 2.4.11: content the user opened may cover the focused control. The
  // popups open now were opened by prepareScenario, which stands in for the user; a popup that opens
  // during the walk is not exempt.
  const userOpened = openPopups()

  const nativelyFocusable = (element: Element) => {
    const tag = element.tagName
    if ((tag === 'A' || tag === 'AREA') && element.hasAttribute('href')) return true
    if (['BUTTON', 'SELECT', 'TEXTAREA', 'IFRAME'].includes(tag)) return true
    if (tag === 'INPUT') return (element as HTMLInputElement).type !== 'hidden'
    if (tag === 'SUMMARY') return element.parentElement?.tagName === 'DETAILS' && element.parentElement.querySelector(':scope > summary') === element
    if ((tag === 'AUDIO' || tag === 'VIDEO') && element.hasAttribute('controls')) return true
    return (element as HTMLElement).isContentEditable
  }
  const implicitRoleTags = new Set(['BUTTON', 'SELECT', 'TEXTAREA', 'ARTICLE', 'ASIDE', 'DIALOG', 'LI', 'OPTION', 'TD', 'TH', 'TR',
    'TABLE', 'UL', 'OL', 'MAIN', 'NAV', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'HR', 'FIELDSET', 'DETAILS', 'PROGRESS', 'METER', 'OUTPUT',
    'FIGURE', 'MENU', 'DD', 'DT', 'BLOCKQUOTE', 'P'])
  const hasRole = (element: Element) => {
    const explicit = element.getAttribute('role')?.trim().split(/\s+/)[0]
    if (explicit) return !['presentation', 'none', 'generic'].includes(explicit)
    const tag = element.tagName
    if ((tag === 'A' || tag === 'AREA') && element.hasAttribute('href')) return true
    if (tag === 'INPUT') return (element as HTMLInputElement).type !== 'hidden'
    if (tag === 'IMG') return (element.getAttribute('alt') ?? 'x') !== ''
    if (tag === 'SECTION' || tag === 'FORM') return element.hasAttribute('aria-label') || element.hasAttribute('aria-labelledby')
    return implicitRoleTags.has(tag)
  }
  const hasHlClass = (element: Element) => Array.from(element.classList).some(token => token.startsWith('hl-'))
  const locationOf = (element: Element): StopLocation => {
    if (element.tagName === 'IFRAME') return 'frame'
    const scene = probe()
    if (scene?.contains(element)) return 'probe'
    let top: Element = element
    while (top.parentElement && top.parentElement !== document.body) top = top.parentElement
    if (top.parentElement === document.body && !isSentinel(top) && !(scene && top.contains(scene))
      && (hasHlClass(top) || top.querySelector('[class^="hl-"], [class*=" hl-"]'))) return 'portal'
    return 'host'
  }
  const segmentedTypes = new Set(['date', 'time', 'datetime-local', 'month', 'week'])
  const describeElement = (element: Element | null): StopDescription => {
    const base = { modal: -1, segmented: false, scroller: false, hasRole: false, tabIndex: -1 }
    if (!element || element === document.body || element === document.documentElement) return { ...base, id: 0, tag: 'body', location: 'body' }
    if (element === start) return { ...base, id: -1, tag: 'span', location: 'sentinel-start' }
    if (element === end) return { ...base, id: -2, tag: 'span', location: 'sentinel-end' }
    const modal = modalAround(element)
    const input = element.tagName === 'INPUT' ? element as HTMLInputElement : null
    return {
      id: idOf(element),
      tag: element.tagName.toLowerCase(),
      location: locationOf(element),
      modal: modal ? idOf(modal) : -1,
      ...(input?.type === 'radio' && input.name ? { radioGroup: input.name } : {}),
      segmented: Boolean(input && segmentedTypes.has(input.type)),
      scroller: !nativelyFocusable(element) && !element.hasAttribute('tabindex'),
      hasRole: hasRole(element),
      tabIndex: (element as HTMLElement).tabIndex ?? -1,
    }
  }

  // ── 2.4.11 occlusion (Minimum: "not entirely hidden") ──
  // Memoised per measurement: the walk up the ancestors is shared by every sample point.
  let opacityMemo = new Map<Element, number>()
  const effectiveOpacity = (element: Element | null): number => {
    if (!element) return 1
    const known = opacityMemo.get(element)
    if (known !== undefined) return known
    const opacity = Number.parseFloat(getComputedStyle(element).opacity || '1') * effectiveOpacity(element.parentElement)
    opacityMemo.set(element, opacity)
    return opacity
  }
  const alphaOf = (color: string) => {
    if (!color || color === 'transparent') return 0
    const slash = color.match(/\/\s*([\d.]+)(%?)\s*\)$/)
    if (slash) return Number.parseFloat(slash[1]) / (slash[2] ? 100 : 1)
    const rgba = color.match(/^rgba\(([^)]+)\)$/)
    if (rgba) {
      const parts = rgba[1].split(/[\s,]+/).filter(Boolean)
      if (parts.length >= 4) return parts[3].endsWith('%') ? Number.parseFloat(parts[3]) / 100 : Number.parseFloat(parts[3])
    }
    return 1
  }
  const collapsed = (style: CSSStyleDeclaration) => {
    if (style.clip && style.clip !== 'auto' && (style.clip.match(/[\d.]+/g) ?? []).every(value => Number(value) === 0)) return true
    return /^inset\((50|100)%/.test(style.clipPath)
  }
  const visuallyHidden = (element: Element) => {
    const rect = element.getBoundingClientRect()
    return rect.width <= 2 || rect.height <= 2 || effectiveOpacity(element) === 0 || collapsed(getComputedStyle(element))
  }
  const opaque = (element: Element) => {
    if (element instanceof SVGElement) return false
    const opacity = effectiveOpacity(element)
    if (['IMG', 'VIDEO', 'CANVAS'].includes(element.tagName)) return opacity >= 0.9
    const style = getComputedStyle(element)
    if (style.backgroundImage && style.backgroundImage !== 'none') return opacity >= 0.9
    return opacity * alphaOf(style.backgroundColor) >= 0.9
  }
  const occluderName = (element: Element) => {
    const hl = Array.from(element.classList).filter(token => token.startsWith('hl-'))
    const classes = hl.length > 0 ? hl : Array.from(element.classList).slice(0, 1)
    const position = getComputedStyle(element).position
    return `${element.tagName.toLowerCase()}${classes.map(token => `.${token}`).join('')}${position === 'sticky' || position === 'fixed' ? ` (${position})` : ''}`
  }
  type Box = { left: number; top: number; right: number; bottom: number }
  const intersect = (a: Box, b: Box): Box => ({ left: Math.max(a.left, b.left), top: Math.max(a.top, b.top), right: Math.min(a.right, b.right), bottom: Math.min(a.bottom, b.bottom) })
  const empty = (box: Box) => box.right - box.left <= 0 || box.bottom - box.top <= 0
  // The clipping ancestors are the containing-block chain, not the DOM chain: a position:fixed
  // descendant escapes an overflow:hidden ancestor that is not its containing block.
  const clipBox = (target: Element, box: Box): Box => {
    let position = getComputedStyle(target).position
    let clipped = box
    for (let node = target.parentElement; node && node !== document.body && node !== document.documentElement; node = node.parentElement) {
      const style = getComputedStyle(node)
      const containsFixed = style.transform !== 'none' || style.perspective !== 'none' || style.filter !== 'none'
        || /paint|layout|strict|content/.test(style.contain) || /transform|perspective|filter/.test(style.willChange)
      const containing = position === 'fixed' ? containsFixed : position === 'absolute' ? containsFixed || style.position !== 'static' : true
      if (!containing) continue
      if (style.overflowX !== 'visible' || style.overflowY !== 'visible') {
        const rect = node.getBoundingClientRect()
        const left = rect.left + node.clientLeft
        const top = rect.top + node.clientTop
        clipped = intersect(clipped, {
          left: style.overflowX !== 'visible' ? left : -Infinity,
          right: style.overflowX !== 'visible' ? left + node.clientWidth : Infinity,
          top: style.overflowY !== 'visible' ? top : -Infinity,
          bottom: style.overflowY !== 'visible' ? top + node.clientHeight : Infinity,
        })
      }
      position = style.position
    }
    return clipped
  }
  const samples = (box: Box, grid: number) => {
    const axis = (from: number, to: number) => {
      if (to - from < 3) return [(from + to) / 2]
      const inset = [from + 2, to - 2]
      return Array.from({ length: grid }, (_, index) => inset[0] + (inset[1] - inset[0]) * index / (grid - 1))
    }
    const xs = axis(box.left, box.right)
    const ys = axis(box.top, box.bottom)
    return xs.flatMap(x => ys.map(y => [x, y] as const))
  }
  const measure = (element: Element): StopVerdict => {
    opacityMemo = new Map()
    let target: Element | null = element
    if (visuallyHidden(element)) {
      target = null
      for (const label of Array.from((element as HTMLInputElement).labels ?? [])) {
        const rect = label.getBoundingClientRect()
        if (rect.width > 0 && rect.height > 0) { target = label; break }
      }
      if (!target) {
        const rect = element.getBoundingClientRect()
        const x = rect.left + rect.width / 2
        const y = rect.top + rect.height / 2
        let ancestor = element.parentElement
        for (let level = 0; level < 3 && ancestor; level += 1, ancestor = ancestor.parentElement) {
          const box = ancestor.getBoundingClientRect()
          if (box.width >= 4 && box.height >= 4 && x >= box.left && x <= box.right && y >= box.top && y <= box.bottom) { target = ancestor; break }
        }
      }
      if (!target) return { kind: 'unmeasured' }
    }
    const subject = target
    const labels = Array.from((element as HTMLInputElement).labels ?? [])
    const rect = subject.getBoundingClientRect()
    const viewport = { left: 0, top: 0, right: document.documentElement.clientWidth, bottom: document.documentElement.clientHeight }
    const onScreen = intersect({ left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom }, viewport)
    if (empty(onScreen)) return { kind: 'offscreen' }
    const box = clipBox(subject, onScreen)
    if (empty(box)) return { kind: 'clipped' }
    const occluders = new Map<Element, number>()
    const judge = (points: ReadonlyArray<readonly [number, number]>) => {
      let seen = 0
      for (const [x, y] of points) {
        for (const hit of document.elementsFromPoint(x, y)) {
          if (hit === subject || hit === element || subject.contains(hit) || element.contains(hit) || labels.some(label => label.contains(hit))) { seen += 1; break }
          if (hit.contains(subject) || isSentinel(hit)) continue
          if (opaque(hit)) { occluders.set(hit, (occluders.get(hit) ?? 0) + 1); break }
        }
      }
      return seen
    }
    let points = samples(box, 3)
    let seen = judge(points)
    if (seen === 0) {
      occluders.clear()
      points = samples(box, 7)
      seen = judge(points)
    }
    if (seen > 0) return seen < points.length ? { kind: 'visible', partial: true } : { kind: 'visible' }
    if (occluders.size === 0) return { kind: 'clipped' }
    const worst = [...occluders.entries()].sort((left, right) => right[1] - left[1])[0][0]
    const exempt = [...occluders.keys()].every(occluder => userOpened.some(popup => popup.isConnected && popup.contains(occluder)))
    return { kind: exempt ? 'exempt-user-opened' : 'obscured', occluder: occluderName(worst) }
  }

  // A stop is a modal leak when an aria-modal container is open and the stop is outside it.
  const leakOf = (element: Element | null) => {
    if (!element || element === document.body) return -1
    const modals = openModals()
    if (modals.length === 0 || modals.some(modal => modal.contains(element))) return -1
    return idOf(modals[modals.length - 1])
  }

  let overshoot = 0
  const log: LogEntry[] = []
  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== 'Tab') return
    const active = deepActive()
    // The walker's own excess presses past the terminal sentinel: no component should see them.
    if ((active === end && !event.shiftKey) || (active === start && event.shiftKey)) {
      event.preventDefault()
      event.stopImmediatePropagation()
      overshoot += 1
      return
    }
    if (isSentinel(active)) return
    // Measured as it is LEFT: the focus scroll of the press that reached it has happened, and
    // elementsFromPoint forces layout.
    const t0 = performance.now()
    const stop = describeElement(active)
    const t1 = performance.now()
    const mutated = drain() > 0
    const verdict = active && active !== document.body ? measure(active) : null
    const t2 = performance.now()
    const leak = leakOf(active)
    const t3 = performance.now()
    cost.describe += t1 - t0
    cost.measure += t2 - t1
    cost.leak += t3 - t2
    log.push({ stop, mutated, verdict, leak })
  }
  const cost = { describe: 0, measure: 0, leak: 0 }
  window.addEventListener('keydown', onKeyDown, true)

  const frame = () => new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
  const take = (mutated: boolean): ReadResult => {
    // An element removed since it was left has no accessible name left to read.
    const entries = log.splice(0).map(entry => byId.get(entry.stop.id)?.isConnected === false ? { ...entry, gone: true } : entry)
    const result = { active: describeElement(deepActive()), entries, overshoot, mutated, costMs: { ...cost } }
    overshoot = 0
    return result
  }
  const quiet = async (maxMs: number, requireStableProbe: boolean) => {
    const deadline = performance.now() + maxMs
    let calm = 0
    let previous = JSON.stringify(probe()?.getBoundingClientRect() ?? null)
    while (calm < 2) {
      if (performance.now() > deadline) return false
      await frame()
      const rect = JSON.stringify(probe()?.getBoundingClientRect() ?? null)
      const changed = drain() > 0 || (requireStableProbe && rect !== previous)
      previous = rect
      calm = changed ? 0 : calm + 1
    }
    return true
  }

  const runtime: TabWalkRuntime = {
    async read() {
      await frame()
      return take(drain() > 0)
    },
    async readSettled() {
      await frame()
      const mutated = drain() > 0
      if (mutated) await quiet(1_000, false)
      return take(mutated)
    },
    settle: maxMs => quiet(maxMs, true),
    census() {
      const groups = new Set<string>()
      const tabbable = Array.from(document.body.querySelectorAll('*')).filter(element => {
        const html = element as HTMLElement
        if (isSentinel(element) || html.tabIndex < 0 || (html as HTMLButtonElement).disabled || element.closest('[inert]') || !visible(element)) return false
        const details = element.closest('details:not([open])')
        if (details && !(element.tagName === 'SUMMARY' && element.parentElement === details)) return false
        const input = element.tagName === 'INPUT' ? element as HTMLInputElement : null
        if (input?.type === 'radio' && input.name) {
          if (groups.has(input.name)) return false
          groups.add(input.name)
        }
        return true
      })
      const digest = tabbable.map(element => `${element.tagName}:${(element as HTMLElement).tabIndex}:${element.getAttribute('aria-label') ?? ''}:${(element.textContent ?? '').trim().slice(0, 40)}`).join('|')
      const first = tabbable.find(element => (element as HTMLElement).tabIndex === 0)
      return {
        count: tabbable.length,
        first: first ? idOf(first) : -1,
        digest: `${tabbable.length}#${digest.length}#${[...digest].reduce((hash, char) => (hash * 31 + char.charCodeAt(0)) | 0, 7)}`,
        openModals: openModals().map(idOf),
        openPopups: openPopups().length,
        positiveTabindex: tabbable.filter(element => (element as HTMLElement).tabIndex > 0).map(element => ({ stop: describeElement(element) })),
      }
    },
    describe: id => {
      const element = byId.get(id)
      return element ? describeElement(element) : null
    },
    anchor(direction) {
      place()
      const sentinel = direction === 'forward' ? start : end
      if (direction === 'forward') window.scrollTo(0, 0)
      sentinel.focus({ preventScroll: true })
      log.length = 0
      overshoot = 0
      drain()
      return { landed: deepActive() === sentinel, active: describeElement(deepActive()) }
    },
    active: () => describeElement(deepActive()),
    documentOrder(ids) {
      return [...ids].sort((left, right) => {
        const a = byId.get(left)
        const b = byId.get(right)
        if (!a || !b || a === b) return 0
        return a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING ? -1 : 1
      })
    },
    released(modalId) {
      const modal = byId.get(modalId)
      return !modal || !modal.isConnected || !visible(modal) || !modal.contains(deepActive())
    },
    submitsForm(id) {
      const element = byId.get(id)
      return Boolean(element && element.tagName === 'BUTTON' && (element as HTMLButtonElement).type === 'submit' && (element as HTMLButtonElement).form)
    },
    teardown() {
      window.removeEventListener('keydown', onKeyDown, true)
      bodyObserver.disconnect()
      changes.disconnect()
      start.remove()
      end.remove()
      for (const element of registry.keys()) element.removeAttribute(ID_ATTRIBUTE)
      delete window.__hlpTabWalk
    },
  }
  window.__hlpTabWalk = runtime
}

// ── Node driver ──

export type Stop = {
  id: number
  tag: string
  location: StopLocation
  modal: number
  radioGroup?: string
  segmented: boolean
  scroller: boolean
  verdict: StopVerdict | null
  leak: number
  // Taken in step mode, or in a batched segment no change touched: findings come only from these.
  trusted: boolean
  // A consecutive repeat of the previous stop (a date segment, or a stuck element).
  repeat: boolean
  // The walk came back to an earlier stop and then went somewhere else (a menu returning focus).
  returned: boolean
}
export type WalkOutcome = 'exited' | 'wrapped' | 'trap' | 'stuck' | 'cap' | 'frame' | 'modal-cycle' | 'deadline'
export type Release =
  | { released: true; by: string; reverseOk: boolean; modal: number }
  | { released: false; escapes: number; buttons: number; reverseOk: boolean; modal: number }
export type WalkRecord = {
  direction: 'forward' | 'reverse' | 'prepared'
  stops: Stop[]
  outcome: WalkOutcome
  // A confirmed cycle: its ids rotated to the member first in document order (taken while they are
  // all still in the document), the same ids in the order they were walked, and where it began in
  // `stops`.
  cycle?: { start: number; members: number[]; walked: number[] }
  presses: number
  stepPresses: number
  // A failure candidate or a change in a batched segment: the walk is retried in step mode through
  // `stepThrough` presses before anything it saw there is believed.
  unconfirmed: boolean
  stepThrough: number
  c0?: string
  c1?: string
  release?: Release
  cap: number
  // Wall clock of the walk itself, and of its modal release when there was one.
  ms?: number
  releaseMs?: number
}
export type LaneWalk = {
  lane: Projection
  keys: Record<string, string>
  census: Census
  prepared?: WalkRecord
  forward: WalkRecord[]
  clean?: number
  reverse: WalkRecord[]
  cleanReverse?: number
  // The phase stopped for good: a confirmed trap, a modal that would not release, too many releases.
  forwardStopped?: string
  reverseStopped?: string
  walkMs: number
  noisy: boolean
  preparedAtFirstStop?: boolean
  pressMs?: number
}

type Context = {
  page: Page
  deadline: number
  keys: Map<number, string>
  descriptions: Map<number, StopDescription>
  unreleased: Map<number, Release & { released: false }>
  lane: Projection
}

const BATCH = 16
const MAX_RELEASES = 3
const PREPARED_STEP_PRESSES = 32

// Every runtime read awaits a page-side promise (an animation frame), so it goes through the
// garbage-collected-promise retry the rest of the suite uses.
function runtimeCall<T>(_context: Context, call: () => Promise<T>) {
  return evaluateSettled(call)
}

export function stopKeyFromSnapshotLine(line: string): string {
  let text = line.trimStart()
  if (text.startsWith('- ')) text = text.slice(2)
  // A YAML-quoted line: '...' with '' escaping, or "..." with backslash escaping.
  const quoted = text.match(/^'((?:[^']|'')*)'(:.*)?$/)
  if (quoted) text = quoted[1].replaceAll("''", "'")
  const role = text.match(/^[a-zA-Z/][\w-]*/)?.[0] ?? text
  const rest = text.slice(role.length)
  if (!rest.startsWith(' "')) return role
  let index = 2
  while (index < rest.length && rest[index] !== '"') index += rest[index] === '\\' ? 2 : 1
  return `${role} ${rest.slice(1, index + 1)}`
}

async function resolveKeys(context: Context, stops: StopDescription[]) {
  const fresh = [...new Map(stops.filter(stop => stop.id > 0 && !context.keys.has(stop.id)).map(stop => [stop.id, stop])).values()]
  for (const stop of fresh) context.descriptions.set(stop.id, stop)
  await Promise.all(fresh.map(async stop => {
    if (!stop.hasRole) {
      context.keys.set(stop.id, `(no role) ${stop.tag}`)
      return
    }
    const snapshot = await context.page.locator(`[data-hl-tab-walk-id="${stop.id}"]`).first()
      .ariaSnapshot({ depth: 1, timeout: 1_000 }).catch(() => null)
    const first = snapshot?.split(/\r?\n/).find(line => line.trim().length > 0)
    context.keys.set(stop.id, snapshot === null || snapshot === undefined ? `(gone) ${stop.tag}` : first ? stopKeyFromSnapshotLine(first) : `(no snapshot) ${stop.tag}`)
  }))
}

// Keys for the stops just read, and for the aria-modal containers they sit in or leaked out of,
// which the lines name too.
async function describeAndResolve(context: Context, stops: StopDescription[], extraIds: number[] = []) {
  const containers = [...new Set([...stops.map(stop => stop.modal), ...extraIds])]
    .filter(id => id > 0 && !context.descriptions.has(id) && !stops.some(stop => stop.id === id))
  const described = containers.length === 0 ? [] : await runtimeCall(context, () => context.page.evaluate(
    ids => ids.map(id => window.__hlpTabWalk!.describe(id)), containers))
  await resolveKeys(context, [...stops, ...described.filter((stop): stop is StopDescription => stop !== null)])
}

function keyOf(context: Context, id: number) {
  if (id === 0) return '(focus lost)'
  return context.keys.get(id) ?? `(unknown) #${id}`
}

// One walk in one direction. `stepThrough` presses are taken one at a time and settled; the rest go
// in batches of 16, each stop measured by the keydown listener as it is left. A change, or a finding
// candidate, in a batched segment makes the walk unconfirmed, and `stepThrough` is raised past it so
// the caller's next walk takes that stretch in step mode.
async function walkOnce(context: Context, direction: WalkRecord['direction'], stepThrough: number, censusCount: number): Promise<WalkRecord> {
  const started = performance.now()
  const page = context.page
  const key = direction === 'reverse' ? 'Shift+Tab' : 'Tab'
  const terminal: StopLocation = direction === 'reverse' ? 'sentinel-start' : 'sentinel-end'
  const opposite: StopLocation = direction === 'reverse' ? 'sentinel-end' : 'sentinel-start'
  let cap = Math.min(2000, 2 * censusCount + 16)
  let capRecomputed = false
  const record: WalkRecord = { direction, stops: [], outcome: 'exited', presses: 0, stepPresses: 0, unconfirmed: false, stepThrough: 0, cap }
  // Trap detection runs over the walk with consecutive repeats collapsed: `sequence` holds the ids,
  // `sequenceAt` the index in `stops` each came from.
  const sequence: number[] = []
  const sequenceAt: number[] = []
  const firstAt = new Map<number, number>()
  let confirm: { i: number; j: number; length: number } | null = null
  let confirmed: { i: number; length: number } | null = null
  let run = 0
  let stuck = false
  const timing = { press: 0, read: 0, keys: 0 }
  const unconfirm = () => {
    record.unconfirmed = true
    record.stepThrough = Math.max(record.stepThrough, record.presses + 1)
  }
  for (;;) {
    if (Date.now() > context.deadline) { record.outcome = 'deadline'; break }
    if (record.presses >= cap) {
      if (!capRecomputed) {
        capRecomputed = true
        const census = await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.census()))
        const grown = Math.min(2000, 2 * census.count + 16)
        if (grown > cap) { cap = grown; record.cap = cap; continue }
      }
      record.outcome = 'cap'
      break
    }
    const step = record.presses < stepThrough
    const count = step ? 1 : Math.min(BATCH, cap - record.presses)
    const pressing = performance.now()
    // The deadline is checked per press: a lane whose own key handling is slow (the Blazor scheduler
    // pays about 380 ms a Tab press) would otherwise overrun it by a whole batch.
    let pressed = 0
    while (pressed < count && Date.now() <= context.deadline) {
      await page.keyboard.press(key)
      pressed += 1
    }
    record.presses += pressed
    if (step) record.stepPresses += pressed
    const reading = performance.now()
    const read = step
      ? await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.readSettled()))
      : await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.read()))
    const resolving = performance.now()
    for (const entry of read.entries) if (entry.gone && !context.keys.has(entry.stop.id)) context.keys.set(entry.stop.id, `(gone) ${entry.stop.tag}`)
    await describeAndResolve(context, [...read.entries.map(entry => entry.stop), read.active], read.entries.map(entry => entry.leak))
    timing.press += reading - pressing
    timing.read += resolving - reading
    timing.keys += performance.now() - resolving
    // A batched segment that saw a change is not evidence: an async focus move or a rAF-positioned
    // overlay may not have landed before the next press.
    const dirty = !step && (read.mutated || read.entries.some(entry => entry.mutated))
    if (dirty) unconfirm()
    for (const entry of read.entries) {
      const index = record.stops.length
      const previous = record.stops[index - 1]
      const stop: Stop = {
        id: entry.stop.id, tag: entry.stop.tag, location: entry.stop.location, modal: entry.stop.modal,
        ...(entry.stop.radioGroup ? { radioGroup: entry.stop.radioGroup } : {}),
        segmented: entry.stop.segmented, scroller: entry.stop.scroller, verdict: entry.verdict, leak: entry.leak,
        trusted: !dirty, repeat: false, returned: false,
      }
      record.stops.push(stop)
      if (previous && previous.id === stop.id && stop.id > 0) {
        // Chromium moves between a date or time input's segments while activeElement stays put.
        stop.repeat = true
        run += 1
        if (stop.segmented ? run > 8 : run >= 2) { stuck = true; break }
        continue
      }
      run = 0
      if (stop.id <= 0) continue
      const position = sequence.length
      sequence.push(stop.id)
      sequenceAt.push(index)
      if (confirm) {
        if (sequence[confirm.i + (position - confirm.j)] === stop.id) {
          // A full second traversal of the same stops, in the same order, confirms the cycle.
          if (position - confirm.j === confirm.length - 1) { confirmed = { i: confirm.i, length: confirm.length }; break }
          continue
        }
        // Not a cycle: focus came back once (a menu returning focus to its trigger) and moved on.
        record.stops[sequenceAt[confirm.j]].returned = true
        confirm = null
      }
      const first = firstAt.get(stop.id)
      if (first === undefined) firstAt.set(stop.id, position)
      else if (first < position - 1) confirm = { i: first, j: position, length: position - first }
    }
    if (confirmed) {
      const members = sequence.slice(confirmed.i, confirmed.i + confirmed.length)
      const order = await runtimeCall(context, () => page.evaluate(ids => window.__hlpTabWalk!.documentOrder(ids), members))
      record.cycle = { start: sequenceAt[confirmed.i], members: canonicalCycle(members, order), walked: members }
      const modals = new Set(members.map(member => context.descriptions.get(member)?.modal ?? -1))
      const [modal] = [...modals]
      // Legitimate only when every member shares one aria-modal container (APG modal dialog).
      record.outcome = modals.size === 1 && modal !== undefined && modal > 0 ? 'modal-cycle' : 'trap'
      break
    }
    if (stuck) {
      // A modal whose only focusable control keeps Tab (APG: Tab stays on it) is a cycle of one,
      // judged like any modal cycle: it must release on Escape or one of its buttons.
      const last = record.stops[record.stops.length - 1]
      if (last.modal > 0) {
        record.outcome = 'modal-cycle'
        record.cycle = { start: record.stops.findIndex(stop => stop.id === last.id), members: [last.id], walked: [last.id] }
      } else record.outcome = 'stuck'
      break
    }
    if (read.active.location === terminal) { record.outcome = 'exited'; break }
    if (read.active.location === opposite) { record.outcome = 'wrapped'; break }
    if (read.active.location === 'frame') { record.outcome = 'frame'; break }
  }
  record.ms = Math.round(performance.now() - started)
  if (process.env.HARBORLINE_GALLERY_TAB_WALK_TRACE) {
    const read = await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.read()))
    trace(context, `in-page cost so far ${JSON.stringify(Object.fromEntries(Object.entries(read.costMs ?? {}).map(([k, v]) => [k, Math.round(v)])))}; node ${JSON.stringify(Object.fromEntries(Object.entries(timing).map(([k, v]) => [k, Math.round(v)])))}`)
  }
  trace(context, `${direction} ${record.outcome}${record.unconfirmed ? '?' : ''} presses=${record.presses} step=${record.stepPresses} stops=${record.stops.length} ms=${record.ms}`)
  return record
}

// HARBORLINE_GALLERY_TAB_WALK_TRACE=1 prints each walk's outcome and cost, for diagnosing a slow scene.
function trace(context: Context, message: string) {
  if (process.env.HARBORLINE_GALLERY_TAB_WALK_TRACE) process.stdout.write(`[tab-walk ${context.lane}] ${message}\n`)
}

async function waitReleased(context: Context, modal: number) {
  return context.page.waitForFunction(id => window.__hlpTabWalk!.released(id), modal, { timeout: 2_000, polling: 'raf' })
    .then(() => true, () => false)
}

async function pressAndSettle(context: Context, key: string) {
  await context.page.keyboard.press(key)
  const read = await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.readSettled()))
  await describeAndResolve(context, [read.active])
  return read.active
}

// APG modal dialog pattern: a modal may keep Tab inside it, as long as it releases. The cycle must
// also run backwards (Shift+Tab retraces it), and it must release on Escape (twice, for a nested
// layer or a maximised window), or on Enter on one of its own buttons, which is what lets an
// Escape-suppressed confirm dialog pass on its Cancel button.
async function releaseModal(context: Context, walk: WalkRecord): Promise<Release> {
  const members = walk.cycle!.walked
  const modal = context.descriptions.get(members[0])!.modal
  const length = members.length
  // The cycle must run the other way too: Shift+Tab after a Tab walk, Tab after a Shift+Tab walk.
  // Either way each press reaches the member walked just before the current one.
  const back = walk.direction === 'reverse' ? 'Tab' : 'Shift+Tab'
  let current = (await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.active()))).id
  let reverseOk = true
  for (let press = 0; press <= length; press += 1) {
    const at = members.indexOf(current)
    const expected = members[(at - 1 + length) % length]
    const active = await pressAndSettle(context, back)
    if (at < 0 || active.id !== expected) reverseOk = false
    current = active.id
  }
  // A modal this lane already failed to release is not tried again in a later phase: the verdict
  // stands, and each attempt costs seconds of waiting.
  const judged = context.unreleased.get(modal)
  if (judged) return { ...judged, reverseOk }
  let escapes = 0
  for (; escapes < 2 && Date.now() < context.deadline;) {
    await context.page.keyboard.press('Escape')
    escapes += 1
    if (await waitReleased(context, modal)) return { released: true, by: `Escape ×${escapes}`, reverseOk, modal }
  }
  let buttons = 0
  for (const member of members) {
    if (Date.now() >= context.deadline) break
    if (!keyOf(context, member).startsWith('button')) continue
    if (await runtimeCall(context, () => context.page.evaluate(id => window.__hlpTabWalk!.submitsForm(id), member))) continue
    let reached = (await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.active()))).id === member
    for (let press = 0; press <= length && !reached; press += 1) reached = (await pressAndSettle(context, 'Tab')).id === member
    if (!reached) continue
    buttons += 1
    await context.page.keyboard.press('Enter')
    if (await waitReleased(context, modal)) return { released: true, by: `Enter on ${keyOf(context, member)}`, reverseOk, modal }
  }
  const verdict = { released: false as const, escapes, buttons, reverseOk, modal }
  context.unreleased.set(modal, verdict)
  return verdict
}

const settledOutcomes = new Set<WalkOutcome>(['trap', 'stuck', 'cap', 'wrapped', 'frame', 'deadline'])

// One direction from its sentinel: retried (up to three times, not counting modal releases) until a
// walk is clean, that is, it exited, released no modal, left the tab-stop census unchanged, and
// nothing in it is unconfirmed.
async function runPhase(context: Context, direction: 'forward' | 'reverse', lane: LaneWalk) {
  const walks = direction === 'forward' ? lane.forward : lane.reverse
  let stepThrough = 0
  let attempts = 0
  let releases = 0
  while (attempts < 3) {
    if (Date.now() >= context.deadline) return 'deadline'
    await runtimeCall(context, () => context.page.evaluate(dir => window.__hlpTabWalk!.anchor(dir), direction))
    const c0 = await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.census()))
    const walk = await walkOnce(context, direction, stepThrough, c0.count)
    const c1 = await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.census()))
    walk.c0 = c0.digest
    walk.c1 = c1.digest
    walks.push(walk)
    if (walk.outcome === 'modal-cycle' && !walk.unconfirmed) {
      const releasing = performance.now()
      walk.release = await releaseModal(context, walk)
      walk.releaseMs = Math.round(performance.now() - releasing)
      trace(context, `release ${JSON.stringify(walk.release)} ms=${walk.releaseMs}`)
      if (!walk.release.released) return 'modal not released'
      releases += 1
      if (releases > MAX_RELEASES) return 'too many modal releases'
      await runtimeCall(context, () => context.page.evaluate(() => window.__hlpTabWalk!.settle(2_000)))
      stepThrough = 0
      continue
    }
    attempts += 1
    if (settledOutcomes.has(walk.outcome) && !walk.unconfirmed) return walk.outcome
    if (walk.outcome === 'exited' && walk.c0 === walk.c1 && !walk.unconfirmed) {
      if (direction === 'forward') lane.clean = walks.length - 1
      else lane.cleanReverse = walks.length - 1
      return undefined
    }
    stepThrough = Math.max(stepThrough, walk.stepThrough)
  }
  return 'unstable'
}

// `settled` is whether the page went quiet before the walk (prepareLane). A page that never does
// still walks, and the annotation says so.
export async function walkLane(page: Page, { lane, deadline, settled = true, pressMs = 0 }: { lane: Projection; deadline: number; settled?: boolean; pressMs?: number }): Promise<LaneWalk> {
  const started = performance.now()
  const context: Context = { page, deadline, keys: new Map(), descriptions: new Map(), unreleased: new Map(), lane }
  const initial = await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.active()))
  const census = await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.census()))
  await describeAndResolve(context, [initial, ...census.positiveTabindex.map(entry => entry.stop)])
  const walk: LaneWalk = { lane, keys: {}, census, forward: [], reverse: [], walkMs: 0, noisy: !settled, pressMs }
  // Phase E: the focus the scenario prepared (an inline editor, an open menu or listbox). Judged for
  // 2.1.2 only, so it is walked forward until it leaves; it cannot be repeated, because walking it
  // changes the state it was prepared in, so its first presses are always taken in step mode.
  // When the prepared focus already is the first stop from the top, E would walk exactly what F walks
  // next, from the same state, so it is skipped and recorded as such.
  walk.preparedAtFirstStop = initial.id > 0 && initial.id === census.first
  if (initial.id > 0 && !walk.preparedAtFirstStop) {
    walk.prepared = await walkOnce(context, 'prepared', PREPARED_STEP_PRESSES, census.count)
    if (walk.prepared.outcome === 'modal-cycle') {
      walk.prepared.release = await releaseModal(context, walk.prepared)
      trace(context, `prepared release ${JSON.stringify(walk.prepared.release)}`)
    }
    await runtimeCall(context, () => page.evaluate(() => window.__hlpTabWalk!.settle(2_000)))
  }
  walk.forwardStopped = await runPhase(context, 'forward', walk)
  walk.reverseStopped = await runPhase(context, 'reverse', walk)
  walk.keys = Object.fromEntries([...context.keys.entries()].map(([id, key]) => [String(id), key]))
  walk.walkMs = Math.round(performance.now() - started)
  return walk
}

// ── Lane normalisation and install ──
//
// The walk is the last reader of the page. What earlier checks left behind that changes what a
// keyboard user would meet is put back: the viewport (reflow and the select-field and schema-form
// helpers resize it), the root font size (reflow sets 200%), the probe transform (stabilizedBox
// leaves a translate, which would make the probe the containing block of every position:fixed
// overlay inside it) and the scroll position. Nothing else is restored.
export async function prepareLane(page: Page, viewport: { width: number; height: number }) {
  const current = page.viewportSize()
  if (!current || current.width !== viewport.width || current.height !== viewport.height) await page.setViewportSize(viewport)
  await page.evaluate(() => {
    document.documentElement.style.removeProperty('font-size')
    ;(document.querySelector('[data-gallery-probe]') as HTMLElement | null)?.style.removeProperty('transform')
    window.scrollTo(0, 0)
  })
  await page.evaluate(installTabWalkRuntime)
  const settled = await evaluateSettled(() => page.evaluate(() => window.__hlpTabWalk!.settle(2_000)))
  const census = await evaluateSettled(() => page.evaluate(() => window.__hlpTabWalk!.census()))
  // What one key press costs in this lane: a component's own keydown handling is paid on every press
  // of the walk (the Blazor App Shell measured about 160 ms a press with no walker installed). Shift
  // alone has no default action, so three presses measure that cost and change nothing.
  const probing = performance.now()
  for (let press = 0; press < 3; press += 1) await page.keyboard.press('Shift')
  const pressMs = Math.round((performance.now() - probing) / 3)
  return { settled, census, pressMs }
}

export async function teardownLane(page: Page) {
  await page.evaluate(() => window.__hlpTabWalk?.teardown()).catch(() => undefined)
}

// ── Pure functions over a lane's walk ──

export function canonicalCycle(members: readonly number[], documentOrder: readonly number[]): number[] {
  if (members.length === 0) return []
  const rank = new Map(documentOrder.map((id, index) => [id, index]))
  let best = 0
  for (let index = 1; index < members.length; index += 1) {
    if ((rank.get(members[index]) ?? Infinity) < (rank.get(members[best]) ?? Infinity)) best = index
  }
  return [...members.slice(best), ...members.slice(0, best)]
}

const keyIn = (walk: LaneWalk, id: number) => id === 0 ? '(focus lost)' : walk.keys[String(id)] ?? `(unknown) #${id}`

// Consecutive repeats collapsed into one entry with a count (a date input's segments).
function collapse(stops: readonly Stop[]) {
  const out: Array<{ stop: Stop; times: number }> = []
  for (const stop of stops) {
    const last = out[out.length - 1]
    if (last && last.stop.id === stop.id && stop.id > 0) last.times += 1
    else out.push({ stop, times: 1 })
  }
  return out
}

function stopLine(walk: LaneWalk, entry: { stop: Stop; times: number }) {
  return `${entry.stop.returned ? '↩ ' : ''}- ${keyIn(walk, entry.stop.id)}${entry.times > 1 ? ` ×${entry.times}` : ''}`
}

function releaseLines(walk: LaneWalk, record: WalkRecord) {
  const cycle = record.cycle!
  const pre = collapse(record.stops.slice(0, Math.max(0, cycle.start))).filter(entry => !entry.stop.scroller)
  return [
    ...pre.map(entry => stopLine(walk, entry)),
    ...cycle.members.map(member => `↻ - ${keyIn(walk, member)}`),
    record.release!.reverseOk ? '↺ ok' : '↺ differs',
    record.release!.released ? `released by ${record.release!.by}` : 'not released',
  ]
}

// The cross-lane order (2.4.3): every walk that ended in a modal release, then the clean walk. No
// walk numbers, so an extra step-mode walk in one lane is not a difference. Scroller stops (Chromium
// makes a scroll container keyboard-focusable when nothing inside it is) are left out, because they
// depend on overflow, which depends on host font metrics; they stay in the per-lane checks.
// A modal the scenario opened is the same modal whichever phase reaches it first (the prepared walk
// when the scenario put focus inside it, the forward walk otherwise, and the two lanes can differ in
// that), so a prepared walk's modal release is part of the order too; its other stops are not.
export function laneOrderLines(walk: LaneWalk): string[] {
  const lines: string[] = []
  for (const record of [walk.prepared, ...walk.forward]) if (record?.release) lines.push(...releaseLines(walk, record))
  const clean = walk.clean === undefined ? undefined : walk.forward[walk.clean]
  if (clean) {
    lines.push(...collapse(clean.stops).filter(entry => !entry.stop.scroller).map(entry => stopLine(walk, entry)), 'exited')
  } else if (walk.forwardStopped && walk.forwardStopped !== 'modal not released') {
    lines.push(walk.forwardStopped)
  }
  return lines
}

// Native radios in one group are one stop, entered at the first going forward and at the last going
// backward when none is checked, so the reverse comparison keys them by group.
function reverseUnits(walk: LaneWalk, stops: readonly Stop[]) {
  const units: Array<{ id: number; group?: string; key: string }> = []
  for (const stop of stops) {
    const unit = { id: stop.id, ...(stop.radioGroup ? { group: stop.radioGroup } : {}), key: stop.radioGroup ? `radio group ${stop.radioGroup}` : keyIn(walk, stop.id) }
    const last = units[units.length - 1]
    if (last && (last.id === unit.id || (unit.group !== undefined && last.group === unit.group))) continue
    units.push(unit)
  }
  return units
}

export function expectedReverse(walk: LaneWalk, forwardStops: readonly Stop[]) {
  return reverseUnits(walk, forwardStops).reverse()
}

// R must retrace the clean forward walk, unit for unit. A different id with an equal key at the
// same position is accepted, because the element was re-rendered.
export function reverseDifference(walk: LaneWalk, forwardStops: readonly Stop[], reverseStops: readonly Stop[]): string | undefined {
  const expected = expectedReverse(walk, forwardStops)
  const actual = reverseUnits(walk, reverseStops)
  for (let index = 0; index < Math.max(expected.length, actual.length); index += 1) {
    const want = expected[index]
    const got = actual[index]
    if (want && got && (want.id === got.id || (want.group !== undefined && want.group === got.group) || want.key === got.key)) continue
    return `2.4.3 reverse differs: expected ${want?.key ?? 'the start'} got ${got?.key ?? 'the start'}`
  }
  return undefined
}

function focusLost(walk: LaneWalk, record: WalkRecord, index: number, direction: string) {
  const before = record.stops.slice(0, index).reverse().find(prior => prior.id > 0)
  return `2.4.3 ${direction} focus-lost: after ${before ? keyIn(walk, before.id) : 'the start'}`
}

function stopFindings(walk: LaneWalk, record: WalkRecord, direction: string) {
  const lines: string[] = []
  record.stops.forEach((stop, index) => {
    if (!stop.trusted) return
    const key = keyIn(walk, stop.id)
    if (stop.location === 'body') {
      lines.push(focusLost(walk, record, index, direction))
      return
    }
    if (stop.leak >= 0) lines.push(`2.4.3 ${direction} modal-leak: ${key} reached while ${keyIn(walk, stop.leak)} is open`)
    if (stop.verdict?.kind === 'obscured') lines.push(`2.4.11 ${direction} obscured: ${key} under ${stop.verdict.occluder}`)
    if (stop.verdict?.kind === 'clipped' || stop.verdict?.kind === 'offscreen') lines.push(`2.4.11 ${direction} ${stop.verdict.kind}: ${key}`)
  })
  return lines
}

function outcomeFinding(walk: LaneWalk, record: WalkRecord, direction: string): string[] {
  switch (record.outcome) {
    case 'trap': return [`2.1.2 ${direction} trap: ↻ ${record.cycle!.members.map(id => keyIn(walk, id)).join(' → ')}`]
    case 'stuck': return [`2.1.2 ${direction} stuck: ${keyIn(walk, record.stops.at(-1)!.id)}`]
    case 'cap': return [`2.1.2 ${direction} cap: ${record.cap} presses without leaving, last ${collapse(record.stops).slice(-10).map(entry => keyIn(walk, entry.stop.id)).join(' → ')}`]
    case 'wrapped': return [`2.1.2 ${direction} wrapped: focus reached the opposite end of the page`]
    case 'frame': return [`2.1.2 ${direction} unsupported frame`]
    case 'deadline': return [`2.1.2 ${direction} deadline: the walk did not finish within the test's allowance`]
    default: return []
  }
}

function releaseFindings(walk: LaneWalk, record: WalkRecord, direction: string) {
  const release = record.release
  if (!release) return []
  const modal = keyIn(walk, release.modal)
  const lines = release.reverseOk ? [] : [`2.4.3 ${direction} modal reverse differs: ${modal}`]
  if (!release.released) lines.push(`2.1.2 ${direction} modal not released: ${modal} (Escape ×${release.escapes}, Enter on ${release.buttons} buttons)`)
  return lines
}

// The walks a lane is judged on: every walk that released a modal, the clean walk, and, when a phase
// stopped, the walk that stopped it. Transitional walks (the page changed under them) are retried,
// not judged.
function judgedWalks(records: readonly WalkRecord[], clean: number | undefined, stopped: string | undefined) {
  return records.filter((record, index) => record.release !== undefined || index === clean || (stopped && index === records.length - 1))
}

// Every finding of one lane, one line each, sorted: `<clause> <direction> <kind>: <detail>`.
export function laneFindingLines(walk: LaneWalk): string[] {
  const lines = new Set<string>()
  const add = (values: string[]) => values.forEach(value => lines.add(value))
  if (walk.prepared) {
    const prepared = walk.prepared
    if (!prepared.unconfirmed) add(outcomeFinding(walk, prepared, 'prepared'))
    if (prepared.release && !prepared.release.released) add(releaseFindings(walk, prepared, 'prepared').filter(line => line.startsWith('2.1.2')))
  }
  for (const [direction, records, clean, stopped] of [
    ['forward', walk.forward, walk.clean, walk.forwardStopped],
    ['reverse', walk.reverse, walk.cleanReverse, walk.reverseStopped],
  ] as const) {
    for (const record of judgedWalks(records, clean, stopped)) {
      add(stopFindings(walk, record, direction))
      add(releaseFindings(walk, record, direction))
    }
    // A walk that changed the page for good (its tab-stop census differs before and after) cannot be
    // repeated to confirm what it saw, so focus lost to an element the page removed is taken from it
    // as it happened. Its other findings are judged on the walk that follows.
    for (const record of records) {
      if (record.release || record.c0 === undefined || record.c0 === record.c1) continue
      record.stops.forEach((stop, index) => { if (stop.location === 'body') lines.add(focusLost(walk, record, index, direction)) })
    }
    const last = records.at(-1)
    if (stopped === 'deadline') add([`2.1.2 ${direction} deadline: the walk did not finish within the test's allowance`])
    else if (stopped && last && !last.release) add(stopped === 'unstable' ? [`2.4.3 ${direction} unstable: no clean walk in 3 attempts`] : outcomeFinding(walk, last, direction))
    if (stopped === 'too many modal releases') add([`2.1.2 ${direction} modal releases exceeded ${MAX_RELEASES}`])
  }
  if (walk.clean !== undefined && walk.cleanReverse !== undefined) {
    const difference = reverseDifference(walk, walk.forward[walk.clean].stops, walk.reverse[walk.cleanReverse].stops)
    if (difference) lines.add(difference)
  }
  for (const { stop } of walk.census.positiveTabindex) lines.add(`2.4.3 walk positive-tabindex: ${keyIn(walk, stop.id)} tabindex=${stop.tabIndex}`)
  return [...lines].sort()
}

// A stop outside the probe and outside any portaled overlay is the gallery host's, not the
// scenario's. It fails closed and is never waivable.
export function laneHostStops(walk: LaneWalk): string[] {
  const hosts = new Set<string>()
  for (const record of [walk.prepared, ...walk.forward, ...walk.reverse]) {
    for (const stop of record?.stops ?? []) if (stop.location === 'host') hosts.add(`${keyIn(walk, stop.id)} <${stop.tag}>`)
  }
  return [...hosts].sort()
}

export function lanePortalKeys(walk: LaneWalk): Set<string> {
  const keys = new Set<string>()
  for (const record of [...walk.forward, ...walk.reverse]) for (const stop of record.stops) if (stop.location === 'portal') keys.add(keyIn(walk, stop.id))
  return keys
}

// Splits finding lines by the expect that reports them.
export function partitionByClause(lines: readonly string[]) {
  return {
    leave: lines.filter(line => line.startsWith('2.1.2 ')),
    reverse: lines.filter(line => line.startsWith('2.4.3 reverse ')),
    order: lines.filter(line => line.startsWith('2.4.3 ') && !line.startsWith('2.4.3 reverse ')),
    obscured: lines.filter(line => line.startsWith('2.4.11 ')),
  }
}

// A cross-lane order difference that T-943 has already waived is the same defect seen twice, so it
// is attributed to T-943 and not registered again, but only when T-943's waived nodes explain all of
// it and hide nothing else. Each lane's order lines are rewritten, then compared:
// - a stop whose key T-943 lists for BOTH lanes (same role and name, a different state or depth) is
//   kept where both lanes walk it, and dropped where only one lane does (a button disabled in one
//   lane is no tab stop there);
// - a stop whose key T-943 lists for this lane alone (a node only this lane has, or names this way) is
//   replaced by a placeholder, runs of placeholders collapse to one, and when only one lane has any
//   they are dropped (that lane simply has extra nodes);
// - a stop in a portaled overlay is outside T-943's probe scope and is never rewritten.
// So a renamed node that also moved still differs (its placeholders do not line up), and an order
// difference among nodes T-943 saw as equal is never attributed.
export function attributeOrderDiffToAria(
  lines: { react: readonly string[]; blazor: readonly string[] },
  ariaDiff: { reactOnly: readonly string[]; blazorOnly: readonly string[]; unified?: readonly string[] } | undefined,
  portal: { react: ReadonlySet<string>; blazor: ReadonlySet<string> },
): boolean {
  if (!ariaDiff || (lines.react.length === lines.blazor.length && lines.react.every((line, index) => line === lines.blazor[index]))) return false
  const stopKey = (line: string) => line.match(/^(?:↻ |↩ )?- (.*?)(?: ×\d+)?$/)?.[1]
  const keys = { react: new Set(ariaDiff.reactOnly.map(stopKeyFromSnapshotLine)), blazor: new Set(ariaDiff.blazorOnly.map(stopKeyFromSnapshotLine)) }
  const walked = { react: new Set(lines.react.map(stopKey)), blazor: new Set(lines.blazor.map(stopKey)) }
  const placeholder = '\u0000T-943'
  const rewrite = (lane: Projection) => {
    const other: Projection = lane === 'react' ? 'blazor' : 'react'
    const out: string[] = []
    for (const line of lines[lane]) {
      const key = stopKey(line)
      if (key === undefined || portal[lane].has(key) || !keys[lane].has(key)) out.push(line)
      else if (keys[other].has(key)) { if (walked[other].has(key)) out.push(line) }
      else if (out.at(-1) !== placeholder) out.push(placeholder)
    }
    return out
  }
  let react = rewrite('react')
  let blazor = rewrite('blazor')
  if (react.includes(placeholder) !== blazor.includes(placeholder)) {
    react = react.filter(line => line !== placeholder)
    blazor = blazor.filter(line => line !== placeholder)
  }
  return react.length === blazor.length && react.every((line, index) => line === blazor[index])
}

export type TabWalkCoverage = {
  stops: number
  reverseStops: number
  walks: number
  stepPresses: number
  releases: number
  scrollers: number
  partial: number
  exemptUserOpened: number
  unmeasured: number
  returned: number
  census: number
  zeroStop: boolean
  noisy: boolean
  pressMs: number
  preparedSkipped: boolean
  walkMs: number
}

export function laneCoverage(walk: LaneWalk): TabWalkCoverage {
  const records = [walk.prepared, ...walk.forward, ...walk.reverse].filter((record): record is WalkRecord => Boolean(record))
  const clean = walk.clean === undefined ? undefined : walk.forward[walk.clean]
  const cleanReverse = walk.cleanReverse === undefined ? undefined : walk.reverse[walk.cleanReverse]
  const allStops = records.flatMap(record => record.stops)
  return {
    stops: clean ? collapse(clean.stops).length : 0,
    reverseStops: cleanReverse ? collapse(cleanReverse.stops).length : 0,
    walks: records.length,
    stepPresses: records.reduce((total, record) => total + record.stepPresses, 0),
    releases: records.filter(record => record.release?.released).length,
    scrollers: new Set(allStops.filter(stop => stop.scroller).map(stop => stop.id)).size,
    partial: allStops.filter(stop => stop.verdict?.partial).length,
    exemptUserOpened: allStops.filter(stop => stop.verdict?.kind === 'exempt-user-opened').length,
    unmeasured: allStops.filter(stop => stop.verdict?.kind === 'unmeasured').length,
    returned: allStops.filter(stop => stop.returned).length,
    census: walk.census.count,
    zeroStop: Boolean(clean && clean.stops.length === 0),
    noisy: walk.noisy,
    pressMs: walk.pressMs ?? 0,
    preparedSkipped: Boolean(walk.preparedAtFirstStop),
    walkMs: walk.walkMs,
  }
}
