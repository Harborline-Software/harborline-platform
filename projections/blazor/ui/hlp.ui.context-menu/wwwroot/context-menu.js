// The element that had focus when a menu opened, per trigger, so a close can return focus there.
// <body> is not recorded: there is nothing to return to. Nor is anything inside this trigger's own
// open menu (rendered right after the trigger): a re-open while it is open must not replace the real
// opener with one of its items. Focus inside some other menu (a trigger placed in an outer menu) is
// an opener like any other.
const openers = new WeakMap()
const focusable = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'

function ownMenu(trigger) {
  const next = trigger?.nextElementSibling
  return next?.getAttribute?.('role') === 'menu' ? next : null
}

function remember(trigger) {
  const active = document.activeElement
  if (active && ownMenu(trigger)?.contains(active)) return
  if (active && active !== document.body) openers.set(trigger, active)
  else openers.delete(trigger)
}

export function triggerAnchor(trigger) {
  remember(trigger)
  const bounds = trigger.getBoundingClientRect()
  return {left: bounds.left, bottom: bounds.bottom}
}

export function rememberOpener(trigger) {
  remember(trigger)
}

// Tab and Shift+Tab close the menu and move on in the tab sequence from the opener (APG menu pattern).
// Blazor's keydown handler runs asynchronously and cannot decide preventDefault per key, so this real DOM
// listener does the focus half in the same keydown: it hands focus back to the opener under the rule every
// close follows (only while focus is in this menu or on <body>), makes the menu inert so the browser's
// default Tab cannot land on one of its items before Blazor removes it, and forgets the opener so the
// close that follows does not pull focus back. It never calls preventDefault: the browser then moves
// focus to the element after (or, with Shift, before) the opener. HandleKeyAsync closes the menu.
const tabBound = new WeakSet()

function returnTarget(opener, trigger) {
  if (!opener) return null
  return opener.isConnected ? opener : trigger?.matches?.(focusable) ? trigger : trigger?.querySelector?.(focusable)
}

function closeOnTab(event) {
  if (event.key !== 'Tab') return
  const menu = event.currentTarget
  const trigger = menu?.previousElementSibling
  const opener = openers.get(trigger)
  openers.delete(trigger)
  menu.inert = true
  const active = document.activeElement
  if (active && active !== document.body && !menu.contains(active)) return
  returnTarget(opener, trigger)?.focus()
}

// Called after every render that opens the menu or moves its active item; also binds the Tab listener
// once per rendered menu element.
export function focusActive(menu) {
  if (menu?.addEventListener && !tabBound.has(menu)) {
    tabBound.add(menu)
    menu.addEventListener('keydown', closeOnTab)
  }
  ;(menu?.querySelector('[data-active="true"]') ?? menu)?.focus()
}

// Called after the closed menu has left the DOM. Focus goes back only if it is still where the menu
// left it, which by now is <body>: an item whose action moved focus elsewhere (an inline editor, a
// dialog) keeps it there, as React Aria's FocusScope restoreFocus does. With no recorded opener (focus
// was on <body> at open) there is nothing to return to. The opener is used while it is still in the
// document; only when a recorded opener has since left it does focus go to the trigger's first
// focusable element.
export function focusTrigger(trigger) {
  const opener = openers.get(trigger)
  openers.delete(trigger)
  if (!opener) return
  const active = document.activeElement
  if (active && active !== document.body) return
  returnTarget(opener, trigger)?.focus()
}
