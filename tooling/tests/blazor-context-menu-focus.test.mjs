#!/usr/bin/env node
// T-943 review 3: the focus half of the Blazor context menu lives in its JS module
// (projections/blazor/ui/hlp.ui.context-menu/wwwroot/context-menu.js), which bUnit cannot run. These
// cases drive it against a minimal DOM: a close returns focus to the opener only if focus is still where
// the closed menu left it (<body>); an item action that moved focus keeps it; a detached opener falls
// back to the trigger's first focusable element, but no recorded opener moves focus nowhere; a re-open
// from inside the trigger's own menu keeps the opener; and focus inside an outer menu is an opener.
// The Tab listener (APG menu pattern: Tab closes the menu and moves on in the tab sequence) hands focus to
// the opener inside the same keydown and never prevents the default action.
import assert from 'node:assert/strict'
import {resolve} from 'node:path'
import {pathToFileURL} from 'node:url'
import {test} from 'node:test'

const root = resolve(import.meta.dirname, '../..')
const body = {name: 'body', closest: () => null}
const document = {activeElement: body, body}
globalThis.document = document

function element(name, {focusable = true, role = null, children = [], next = null} = {}) {
  const node = {
    name, isConnected: true, focusable, children, nextElementSibling: next, previousElementSibling: null, parent: null,
    inert: false, listeners: [],
    addEventListener(type, listener) { this.listeners.push({type, listener}) },
    focus() { if (this.focusable) document.activeElement = this },
    getAttribute: attribute => attribute === 'role' ? role : null,
    contains: other => other === node || node.children.some(child => child.contains(other)),
    closest: selector => {
      for (let at = node; at; at = at.parent) if (selector === '[role="menu"]' && at.getAttribute('role') === 'menu') return at
      return null
    },
    matches: () => node.focusable,
    querySelector: () => node.children.find(child => child.focusable) ?? null,
  }
  for (const child of children) child.parent = node
  if (next) next.previousElementSibling = node
  return node
}

// A keydown as the browser dispatches it to the menu's own listeners; reports whether any called preventDefault.
function keydown(menu, key, shiftKey = false) {
  let defaultPrevented = false
  const event = {type: 'keydown', key, shiftKey, currentTarget: menu, target: document.activeElement, preventDefault() { defaultPrevented = true }}
  for (const {type, listener} of menu.listeners) if (type === 'keydown') listener(event)
  return {defaultPrevented}
}

// An open Blazor menu: <trigger><opener/></trigger><div role="menu"><item/><item/></div>, opener recorded.
function openMenu() {
  const opener = element('record')
  const first = element('Blocked')
  const second = element('Copy')
  const menu = element('menu', {focusable: false, role: 'menu', children: [first, second]})
  const trigger = element('trigger', {focusable: false, children: [opener], next: menu})
  opener.focus()
  rememberOpener(trigger)
  focusActive(menu)
  return {opener, first, second, menu, trigger}
}

const {focusActive, focusTrigger, rememberOpener} = await import(pathToFileURL(resolve(root, 'projections/blazor/ui/hlp.ui.context-menu/wwwroot/context-menu.js')).href)

test('a close returns focus to the opener when the closed menu left it on <body>', () => {
  const opener = element('record')
  const trigger = element('trigger', {focusable: false, children: [opener]})
  opener.focus()
  rememberOpener(trigger)
  document.activeElement = body // the menu took focus and is gone now
  focusTrigger(trigger)
  assert.equal(document.activeElement, opener)
})

test('an item action that moved focus keeps it: the close does not take it back', () => {
  const opener = element('record')
  const trigger = element('trigger', {focusable: false, children: [opener]})
  const editor = element('editor')
  opener.focus()
  rememberOpener(trigger)
  editor.focus()
  focusTrigger(trigger)
  assert.equal(document.activeElement, editor)
})

test('an opener that left the document falls back to the trigger\'s first focusable element', () => {
  const opener = element('record')
  const fallback = element('fallback')
  const trigger = element('trigger', {focusable: false, children: [fallback]})
  opener.focus()
  rememberOpener(trigger)
  opener.isConnected = false
  document.activeElement = body
  focusTrigger(trigger)
  assert.equal(document.activeElement, fallback)
})

test('a re-open from inside the trigger\'s own open menu keeps the real opener', () => {
  const opener = element('record')
  const item = element('menuitem')
  const menu = element('menu', {focusable: false, role: 'menu', children: [item]})
  const trigger = element('trigger', {focusable: false, children: [opener], next: menu})
  opener.focus()
  rememberOpener(trigger)
  item.focus()
  rememberOpener(trigger)
  document.activeElement = body
  focusTrigger(trigger)
  assert.equal(document.activeElement, opener)
})

test('focus inside an outer menu is an opener: a trigger placed in another menu gets focus back', () => {
  // <div role="menu"><trigger><A/><B/></trigger></div>, focus on B: the outer menu is not this
  // trigger's own (its own renders after the trigger), so B is recorded and returned to.
  const first = element('A')
  const second = element('B')
  const trigger = element('trigger', {focusable: false, children: [first, second]})
  element('outer menu', {focusable: false, role: 'menu', children: [trigger]})
  second.focus()
  rememberOpener(trigger)
  document.activeElement = body
  focusTrigger(trigger)
  assert.equal(document.activeElement, second)
})

test('with nothing focused at open there is no opener, so no close moves focus', () => {
  // Escape, a selection and an outside press all reach focusTrigger after the menu has left the DOM,
  // with focus on <body>; the trigger's first focusable element must not take it.
  for (const close of ['Escape', 'selection', 'outside press']) {
    const row = element('Edit row 1')
    const trigger = element('trigger', {focusable: false, children: [row]})
    document.activeElement = body
    rememberOpener(trigger)
    document.activeElement = body
    focusTrigger(trigger)
    assert.equal(document.activeElement, body, close)
  }
})

for (const shiftKey of [false, true]) {
  test(`${shiftKey ? 'Shift+Tab' : 'Tab'} in an open menu hands focus to the opener in the same keydown and keeps the default action`, () => {
    const {opener, first, menu, trigger} = openMenu()
    assert.equal(document.activeElement, first, 'focusActive focused the active item')
    const {defaultPrevented} = keydown(menu, 'Tab', shiftKey)
    assert.equal(defaultPrevented, false)
    assert.equal(document.activeElement, opener)
    assert.equal(menu.inert, true, 'the browser\'s Tab cannot land on an item of the closing menu')
    // The browser's default Tab then moves focus off the opener (to <body> past the last element, say). The
    // close Blazor runs next must not pull it back: the listener has already used the recorded opener.
    document.activeElement = body
    focusTrigger(trigger)
    assert.equal(document.activeElement, body)
  })
}

test('the Tab listener ignores every other key and acts on Tab only', () => {
  const {opener, first, menu} = openMenu()
  for (const key of ['ArrowDown', 'Enter', ' ', 'Escape']) assert.equal(keydown(menu, key).defaultPrevented, false)
  assert.equal(document.activeElement, first)
  assert.equal(menu.inert, false)
  keydown(menu, 'Tab')
  assert.equal(document.activeElement, opener)
})

test('the Tab listener follows the focus-return rule: focus outside the menu stays where it is', () => {
  const {menu} = openMenu()
  const editor = element('editor')
  editor.focus()
  keydown(menu, 'Tab')
  assert.equal(document.activeElement, editor)
  assert.equal(menu.inert, true, 'the closing menu still leaves the tab sequence')
})

test('with no recorded opener, Tab moves focus nowhere itself', () => {
  const row = element('Edit row 1')
  const first = element('Blocked')
  const menu = element('menu', {focusable: false, role: 'menu', children: [first]})
  const trigger = element('trigger', {focusable: false, children: [row], next: menu})
  document.activeElement = body
  rememberOpener(trigger)
  focusActive(menu)
  keydown(menu, 'Tab')
  assert.equal(document.activeElement, first)
  assert.equal(menu.inert, true, 'the closing menu still leaves the tab sequence')
})

test('focusActive binds the Tab listener once per menu element', () => {
  const {menu} = openMenu()
  focusActive(menu)
  focusActive(menu)
  assert.equal(menu.listeners.filter(row => row.type === 'keydown').length, 1)
})
