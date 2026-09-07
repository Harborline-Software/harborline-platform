import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import * as React from 'react'
import { describe, expect, it, vi } from 'vitest'

import { DetailPanel } from '../DetailPanel'
import type { DetailPanelSubject } from '../DetailPanel'

const root = resolve(import.meta.dirname, '../../../../../../')
const fixture = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.ui.detail-panel/drilldown-v1.json'), 'utf8')) as Fixture

interface Fixture {
  objects: Record<string, DetailPanelSubject>
  labels: { backPrefix: string; openAsPage: string; tabListName: string }
  initial: { objectId: string; pushedObjectId: string | null; activeFacetId: string | null }
  transitions: Transition[]
  passThroughKeys: string[]
  handledKeys: string[]
  focusRoute: { invocationsPerHandledKey: number }
  controlledAddressing: { objectId: string; activeFacetId: string }
  affordance: { minimumTargetCssPixels: number }
  validationCases: Array<{ name: string; object?: DetailPanelSubject; activeFacetId?: string; pushedOnly?: boolean; expected: string }>
}

interface Transition {
  name: string
  action: { kind: string; facetId?: string; relationId?: string; key?: string; objectId?: string; pushedObjectId?: string | null }
  expect: {
    title?: string
    activeFacetId?: string
    focusedTabId?: string
    tabs?: Array<{ id: string; label: string; count: number; selected: boolean }>
    backRow?: string | null
    relations?: string[]
    promote?: boolean
    promoteRoute?: string
    events: Array<Record<string, unknown>>
  }
}

// The fixture is the authority: a name it does not declare is an error, never an empty object.
function subject(name: string): DetailPanelSubject {
  const found = fixture.objects[name]
  if (!found) throw new Error(`fixture object ${name} is not declared`)
  return found
}

function tabs() {
  return screen.queryAllByRole('tab')
}

function relationIds() {
  return [...document.querySelectorAll('[data-relation-id]')].map(element => element.getAttribute('data-relation-id'))
}

describe('DetailPanel drill-down (conformance/hlp.ui.detail-panel/drilldown-v1.json)', () => {
  it('replays every declared transition from the declared initial state', () => {
    const events: Array<Record<string, unknown>> = []
    let current = { subject: subject(fixture.initial.objectId), pushed: fixture.initial.pushedObjectId ? subject(fixture.initial.pushedObjectId) : null }

    const view = render(
      <DetailPanel
        label="Inspector"
        open
        railCapable
        subject={current.subject}
        pushed={current.pushed}
        onFacetChange={facetId => events.push({ kind: 'facetChange', facetId })}
        onFollowRelation={relation => events.push({ kind: 'followRelation', relationId: relation.id, targetId: relation.targetId })}
        onBack={() => events.push({ kind: 'back' })}
        onOpenAsPage={route => events.push({ kind: 'openAsPage', route })}
      >
        <p>body</p>
      </DetailPanel>,
    )

    const rerender = () => view.rerender(
      <DetailPanel
        label="Inspector"
        open
        railCapable
        subject={current.subject}
        pushed={current.pushed}
        onFacetChange={facetId => events.push({ kind: 'facetChange', facetId })}
        onFollowRelation={relation => events.push({ kind: 'followRelation', relationId: relation.id, targetId: relation.targetId })}
        onBack={() => events.push({ kind: 'back' })}
        onOpenAsPage={route => events.push({ kind: 'openAsPage', route })}
      >
        <p>body</p>
      </DetailPanel>,
    )

    for (const transition of fixture.transitions) {
      const action = transition.action
      if (action.kind === 'activateFacet') {
        fireEvent.click(document.querySelector(`[role="tab"][data-facet-id="${action.facetId}"]`)!)
      } else if (action.kind === 'key') {
        const selected = document.querySelector('[role="tab"][aria-selected="true"]') as HTMLElement
        selected.focus()
        fireEvent.keyDown(selected, { key: action.key })
      } else if (action.kind === 'activateRelation') {
        fireEvent.click(document.querySelector(`[data-relation-id="${action.relationId}"]`)!)
      } else if (action.kind === 'activateRelationRefused') {
        expect(document.querySelector(`[data-relation-id="${action.relationId}"]`), transition.name).toBeNull()
      } else if (action.kind === 'activateBack') {
        fireEvent.click(screen.getByRole('button', { name: new RegExp(`^${fixture.labels.backPrefix}`) }))
      } else if (action.kind === 'activatePromote') {
        fireEvent.click(screen.getByRole('button', { name: fixture.labels.openAsPage }))
      } else if (action.kind === 'host') {
        current = { subject: subject(action.objectId!), pushed: action.pushedObjectId ? subject(action.pushedObjectId) : null }
        rerender()
      } else if (action.kind !== 'none') {
        throw new Error(`fixture action ${action.kind} is not declared`)
      }

      const expected = transition.expect
      if (expected.title !== undefined) expect(screen.getByRole('heading', { level: 2 }).textContent, transition.name).toBe(expected.title)
      if (expected.tabs !== undefined) {
        expect(tabs().map(tab => ({
          id: tab.getAttribute('data-facet-id'),
          label: tab.querySelector('[data-facet-label]')!.textContent,
          count: Number(tab.querySelector('[data-facet-count]')!.textContent),
          selected: tab.getAttribute('aria-selected') === 'true',
        })), transition.name).toEqual(expected.tabs)
      }
      if (expected.activeFacetId !== undefined) {
        expect(document.querySelector('[role="tab"][aria-selected="true"]')!.getAttribute('data-facet-id'), transition.name).toBe(expected.activeFacetId)
        expect(screen.getByRole('tabpanel').getAttribute('aria-labelledby'), transition.name)
          .toBe(document.querySelector('[role="tab"][aria-selected="true"]')!.id)
      }
      if (expected.focusedTabId !== undefined) {
        expect((document.activeElement as HTMLElement).getAttribute('data-facet-id'), transition.name).toBe(expected.focusedTabId)
        expect(tabs().filter(tab => tab.tabIndex === 0).map(tab => tab.getAttribute('data-facet-id')), transition.name).toEqual([expected.focusedTabId])
      }
      if (expected.backRow !== undefined) {
        const back = document.querySelector('[data-detail-panel-back]')
        expect(back?.textContent ?? null, transition.name).toBe(expected.backRow)
      }
      if (expected.relations !== undefined) expect(relationIds(), transition.name).toEqual(expected.relations)
      if (expected.promote !== undefined) {
        expect(document.querySelectorAll('[data-detail-panel-promote]').length, transition.name).toBe(expected.promote ? 1 : 0)
      }
      // Each transition declares exactly the events its own action emits; the log is drained
      // per step so an event emitted one step late cannot hide inside a running tally.
      expect(events, transition.name).toEqual(expected.events)
      events.length = 0
    }
  })

  it('renders the controlled facet the host addresses, not the first declared one', () => {
    render(
      <DetailPanel label="Inspector" open railCapable subject={subject(fixture.controlledAddressing.objectId)} activeFacetId={fixture.controlledAddressing.activeFacetId}>
        <p>body</p>
      </DetailPanel>,
    )
    expect(document.querySelector('[role="tab"][aria-selected="true"]')!.getAttribute('data-facet-id')).toBe(fixture.controlledAddressing.activeFacetId)
  })

  it('leaves the fixture-declared pass-through keys to the surrounding shell', () => {
    const onOpenChange = vi.fn()
    render(
      <DetailPanel label="Inspector" open railCapable={false} subject={subject('PMP-0412')} onOpenChange={onOpenChange}>
        <p>body</p>
      </DetailPanel>,
    )
    const tab = tabs()[0]
    for (const key of fixture.passThroughKeys) {
      const event = fireEvent.keyDown(tab, { key })
      if (key === 'Escape') continue
      expect(event, key).toBe(true)
    }
    // Escape still reaches the sheet's dismissal handler through the tab bar.
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('prevents the browser default for exactly the fixture-declared handled keys', () => {
    render(
      <DetailPanel label="Inspector" open railCapable subject={subject('PMP-0412')}>
        <p>body</p>
      </DetailPanel>,
    )
    // The rendered set IS the set the shell handles, so removing a key from the fixture or adding
    // one to it turns this red until the shell agrees.
    expect(screen.getByRole('tablist').getAttribute('data-handled-keys')!.split(' ')).toEqual(fixture.handledKeys)

    // The browser default for these keys is a document scroll (Home/End jump to top/bottom). jsdom
    // does not implement that default, so the page stands in for it: it only moves for an event
    // that is still default-allowed when it reaches the document.
    let scrollTop = 0
    const page = (event: KeyboardEvent) => { if (!event.defaultPrevented) scrollTop += 1 }
    document.addEventListener('keydown', page)
    try {
      for (const key of fixture.handledKeys) {
        const selected = document.querySelector('[role="tab"][aria-selected="true"]') as HTMLElement
        selected.focus()
        fireEvent.keyDown(selected, { key })
        expect(scrollTop, key).toBe(0)
      }
      for (const key of fixture.passThroughKeys) {
        fireEvent.keyDown(tabs()[0], { key })
      }
      expect(scrollTop).toBe(fixture.passThroughKeys.length)
    } finally {
      document.removeEventListener('keydown', page)
    }
  })

  // The same invariant the Blazor suite carries as a property: the key handling belongs to the tab
  // bar ELEMENT, not to the boolean that decides which branch renders it. React is immune by
  // construction (onKeyDown is a JSX prop, re-attached with every element), and the fixture states
  // the behaviour for both projections rather than leaving it implied.
  it('keeps handling the fixture keys after the rail/modal branch swap replaces the tab bar', () => {
    const panel = (rail: boolean) => (
      <DetailPanel label="Inspector" open railCapable={rail} subject={subject('PMP-0412')}>
        <p>body</p>
      </DetailPanel>
    )
    const view = render(panel(true))
    const before = screen.getByRole('tablist')

    view.rerender(panel(false))

    const after = screen.getByRole('tablist')
    expect(after).not.toBe(before)
    let scrollTop = 0
    const page = (event: KeyboardEvent) => { if (!event.defaultPrevented) scrollTop += 1 }
    document.addEventListener('keydown', page)
    try {
      for (const key of fixture.handledKeys) {
        const selected = document.querySelector('[role="tab"][aria-selected="true"]') as HTMLElement
        fireEvent.keyDown(selected, { key })
      }
      expect(scrollTop).toBe(0)
    } finally {
      document.removeEventListener('keydown', page)
    }
  })

  it('refuses the invalid declarations the fixture names', () => {
    for (const invalid of fixture.validationCases) {
      expect(() => render(
        invalid.pushedOnly
          ? <DetailPanel label="Inspector" open railCapable pushed={subject('WO-8841')}><p>body</p></DetailPanel>
          : <DetailPanel label="Inspector" open railCapable subject={invalid.object!} activeFacetId={invalid.activeFacetId}><p>body</p></DetailPanel>,
      ), invalid.name).toThrow(invalid.expected)
    }
  })
})
