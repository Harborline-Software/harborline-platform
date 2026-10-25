#!/usr/bin/env node
// T-943 review 3: the focus half of the Blazor context menu lives in its JS module
// (projections/blazor/ui/hlp.ui.context-menu/wwwroot/context-menu.js), which bUnit cannot run. These
// cases drive it against a minimal DOM: a close returns focus to the opener only if focus is still where
// the closed menu left it (<body>); an item action that moved focus keeps it; a detached opener falls
// back to the trigger's first focusable element, but no recorded opener moves focus nowhere; a re-open
// from inside the trigger's own menu keeps the opener; and focus inside an outer menu is an opener.
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
    name, isConnected: true, focusable, children, nextElementSibling: next, parent: null,
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
  return node
}

const {focusTrigger, rememberOpener} = await import(pathToFileURL(resolve(root, 'projections/blazor/ui/hlp.ui.context-menu/wwwroot/context-menu.js')).href)

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
