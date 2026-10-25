#!/usr/bin/env node
// T-943 review 3: the focus half of the Blazor context menu lives in its JS module
// (projections/blazor/ui/hlp.ui.context-menu/wwwroot/context-menu.js), which bUnit cannot run. These
// cases drive it against a minimal DOM: a close returns focus to the opener only if focus is still where
// the closed menu left it (<body>); an item action that moved focus keeps it; a detached opener falls
// back to the trigger's first focusable element; and a re-open from inside the menu keeps the opener.
import assert from 'node:assert/strict'
import {resolve} from 'node:path'
import {pathToFileURL} from 'node:url'
import {test} from 'node:test'

const root = resolve(import.meta.dirname, '../..')
const body = {name: 'body', closest: () => null}
const document = {activeElement: body, body}
globalThis.document = document

function element(name, {focusable = true, inMenu = false, children = []} = {}) {
  const node = {
    name, isConnected: true, focusable, children,
    focus() { if (this.focusable) document.activeElement = this },
    closest: selector => selector === '[role="menu"]' && inMenu ? {} : null,
    matches: () => node.focusable,
    querySelector: () => node.children.find(child => child.focusable) ?? null,
  }
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

test('a re-open from inside the open menu keeps the real opener', () => {
  const opener = element('record')
  const item = element('menuitem', {inMenu: true})
  const trigger = element('trigger', {focusable: false, children: [opener]})
  opener.focus()
  rememberOpener(trigger)
  item.focus()
  rememberOpener(trigger)
  document.activeElement = body
  focusTrigger(trigger)
  assert.equal(document.activeElement, opener)
})
