import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { act, fireEvent, render } from '@testing-library/react'
import * as React from 'react'
import { describe, expect, it, vi } from 'vitest'

import { ActionMenu, ActionMenuScopeContext, type ActionMenuEntry, type ActionMenuItem } from '../ActionMenu'

const repo = resolve(import.meta.dirname, '../../../../../..')
const read = (relative: string) => JSON.parse(readFileSync(resolve(repo, relative), 'utf8'))
const fixture = read('conformance/hlp.ui.action-menu/disclosure-v1.json')
const authorityCss = readFileSync(resolve(repo, fixture.render.hintComputed.styleAuthority), 'utf8')

/** Fixture authority: a name the fixture does not declare fails loudly, never replays as nothing. */
function declared(itemId: string) {
  const item = fixture.items.find((candidate: { id: string }) => candidate.id === itemId)
  if (!item) throw new Error(`missing-fixture-item: ${itemId}`)
  return item as { id: string; label: string; shortcutHint?: string; keyShortcuts?: string; checked?: boolean; destructive?: boolean }
}

type Checked = Record<string, boolean>

function entries(checked: Checked, invoked: string[]): ActionMenuEntry[] {
  return fixture.items.map((raw: ReturnType<typeof declared>): ActionMenuItem => ({
    id: raw.id,
    label: raw.label,
    shortcutHint: raw.shortcutHint,
    keyShortcuts: raw.keyShortcuts,
    checked: raw.checked === undefined ? undefined : checked[raw.id],
    variant: raw.destructive ? 'destructive' : undefined,
    // The ONE callback every entry reports through: a toggle has no second channel.
    onClick: () => invoked.push(raw.id),
  }))
}

/** The accessible name is the text the AT reads: aria-hidden subtrees contribute nothing. */
function accessibleName(element: HTMLElement) {
  return [...element.childNodes]
    .filter(node => !(node instanceof HTMLElement && node.getAttribute('aria-hidden') === 'true'))
    .map(node => node.textContent ?? '')
    .join('')
    .trim()
}

function open(container: HTMLElement) {
  fireEvent.click(container.querySelector('.hl-action-menu__trigger')!)
}

function item(container: HTMLElement, itemId: string) {
  const element = container.querySelector<HTMLElement>(`[data-hl-item-id='${itemId}']`)
  if (!element) throw new Error(`missing-rendered-item: ${itemId}`)
  return element
}

describe('ActionMenu disclosure rules (conformance/hlp.ui.action-menu/disclosure-v1.json)', () => {
  it('renders every declared hint in the shared token and none where the fixture declares none', () => {
    const { container } = render(<ActionMenu items={entries(fixture.initial.checked, [])} />)
    open(container)

    for (const [itemId, hint] of Object.entries(fixture.render.hints as Record<string, string>)) {
      const rendered = item(container, itemId).querySelector(`.${fixture.render.hintClass}`)
      expect(rendered, itemId).not.toBeNull()
      expect(rendered!.textContent).toBe(hint)
      // The hint is the LAST child: right alignment is the token's, not a call site's.
      expect(rendered).toBe(item(container, itemId).lastElementChild)
      expect(declared(itemId).shortcutHint).toBe(hint)
    }
    for (const itemId of fixture.render.withoutHint as string[]) {
      expect(item(container, itemId).querySelector(`.${fixture.render.hintClass}`), itemId).toBeNull()
    }
  })

  it('exposes every declared chord through aria-keyshortcuts, leaving the visible hint decorative', () => {
    const { container } = render(<ActionMenu items={entries(fixture.initial.checked, [])} />)
    open(container)

    for (const [itemId, chord] of Object.entries(fixture.render.keyShortcuts as Record<string, string>)) {
      const rendered = item(container, itemId)
      expect(rendered.getAttribute('aria-keyshortcuts'), itemId).toBe(chord)
      expect(declared(itemId).keyShortcuts, itemId).toBe(chord)
      // The hint stays visual: the chord reaches AT through the attribute, not the name.
      expect(rendered.querySelector(`.${fixture.render.hintClass}`)!.getAttribute('aria-hidden'))
        .toBe(fixture.render.hintIsDecorative ? 'true' : null)
    }
    for (const [itemId, name] of Object.entries(fixture.render.accessibleNames as Record<string, string>)) {
      expect(accessibleName(item(container, itemId)), itemId).toBe(name)
      if (!(itemId in fixture.render.keyShortcuts)) {
        expect(item(container, itemId).getAttribute('aria-keyshortcuts'), itemId).toBeNull()
      }
    }
  })

  it('takes the hint alignment and the monospace family from the authority stylesheet', () => {
    const style = document.createElement('style')
    style.textContent = authorityCss
    document.head.append(style)
    const { container } = render(<ActionMenu items={entries(fixture.initial.checked, [])} />)
    open(container)

    const hint = item(container, 'edit').querySelector(`.${fixture.render.hintClass}`)!
    const computed = getComputedStyle(hint)
    expect(computed.textAlign).toBe(fixture.render.hintComputed.textAlign)
    expect(computed.fontFamily).toContain(fixture.render.hintComputed.fontFamilyContains)
    expect(computed.marginInlineStart || authorityCss).toContain('auto')
    style.remove()
  })

  it('renders a toggle as a checkbox item carrying a check mark, and a plain entry as neither', () => {
    const { container } = render(<ActionMenu items={entries(fixture.initial.checked, [])} />)
    open(container)

    for (const [itemId, state] of Object.entries(fixture.render.checkboxItems as Record<string, string>)) {
      const rendered = item(container, itemId)
      expect(rendered.getAttribute('role'), itemId).toBe('menuitemcheckbox')
      expect(rendered.getAttribute('aria-checked'), itemId).toBe(state)
      const mark = rendered.querySelector(`.${fixture.render.checkClass}`)
      expect(mark, itemId).not.toBeNull()
      expect(mark!.textContent === '✓').toBe(state === 'true')
    }
    for (const itemId of fixture.render.commandItems as string[]) {
      const rendered = item(container, itemId)
      expect(rendered.getAttribute('role'), itemId).toBe('menuitem')
      expect(rendered.getAttribute('aria-checked'), itemId).toBeNull()
      expect(rendered.querySelector(`.${fixture.render.checkClass}`), itemId).toBeNull()
    }
  })

  it('replays every declared transition from the declared initial state', () => {
    const invoked: string[] = []
    let checked: Checked = { ...fixture.initial.checked }
    expect(invoked).toEqual(fixture.initial.invoked)

    let checkedSetter: React.Dispatch<React.SetStateAction<Checked>> = () => {}
    function Harness() {
      const [state, setState] = React.useState<Checked>(checked)
      checkedSetter = setState
      checked = state
      const declaredEntries = entries(state, invoked).map(entry => !('separator' in entry) && entry.checked !== undefined
        ? { ...entry, onClick: () => { invoked.push(entry.id!); setState(previous => ({ ...previous, [entry.id!]: !previous[entry.id!] })) } }
        : entry)
      return <ActionMenu items={declaredEntries} />
    }
    const { container } = render(<Harness />)

    for (const transition of fixture.transitions) {
      if (transition.action.kind === 'activate') {
        open(container)
        fireEvent.click(item(container, declared(transition.action.itemId).id))
      } else if (transition.action.kind === 'host') {
        // Value, not identity: the host hands back an equal-by-value map and the marks follow it.
        act(() => checkedSetter({ ...transition.action.checked }))
      } else throw new Error(`unknown-fixture-action: ${transition.action.kind}`)

      expect(checked, transition.id).toEqual(transition.expect.checked)
      expect(invoked, transition.id).toEqual(transition.expect.invoked)
      open(container)
      for (const [itemId, state] of Object.entries(transition.expect.checked as Checked)) {
        expect(item(container, itemId).getAttribute('aria-checked'), `${transition.id} / ${itemId}`).toBe(String(state))
      }
      fireEvent.keyDown(container.querySelector('[role=menu]')!, { key: 'Escape' })
    }
  })

  it('refuses a second menu at one scope and lets a different scope have its own home', () => {
    const scope = fixture.oneHomePerScope
    const items = entries(fixture.initial.checked, [])
    expect(() => render(<><ActionMenu items={items} scopeId={scope.scopeId} /><ActionMenu items={items} scopeId={scope.scopeId} /></>))
      .toThrow(scope.error)

    const { unmount } = render(<><ActionMenu items={items} scopeId={scope.scopeId} /><ActionMenu items={items} scopeId={scope.otherScopeId} /></>)
    unmount()
    // Unmounting releases the home, so the same scope can be mounted again.
    expect(() => render(<ActionMenu items={items} scopeId={scope.scopeId} />).unmount()).not.toThrow()
  })

  it('derives a scope when the composer declares none, so no menu can decline to have a home', () => {
    const derived = fixture.derivedScope
    if (typeof derived?.rootScopeId !== 'string' || typeof derived.hostScopeId !== 'string') {
      throw new Error('missing-fixture-case: derivedScope')
    }
    const items = entries(fixture.initial.checked, [])

    // Omission does not opt out: the chain terminates at the shell root, so two undeclared menus
    // under one host are the same scope and the second is refused.
    expect(() => render(<><ActionMenu items={items} /><ActionMenu items={items} /></>)).toThrow(derived.error)

    // The declared root scope and an undeclared menu resolve to the SAME home, by value.
    expect(() => render(<><ActionMenu items={items} scopeId={derived.rootScopeId} /><ActionMenu items={items} /></>))
      .toThrow(derived.error)

    // The nearest scope host supplies the scope, and a menu under it collides with that host id.
    expect(() => render(
      <ActionMenuScopeContext.Provider value={derived.hostScopeId}>
        <ActionMenu items={items} /><ActionMenu items={items} scopeId={derived.hostScopeId} />
      </ActionMenuScopeContext.Provider>,
    )).toThrow(derived.error)

    // A host scope and the root scope are different homes; a declared scope overrides the host.
    const composed = render(
      <>
        <ActionMenu items={items} />
        <ActionMenuScopeContext.Provider value={derived.hostScopeId}>
          <ActionMenu items={items} />
          <ActionMenu items={items} scopeId={fixture.oneHomePerScope.scopeId} />
        </ActionMenuScopeContext.Provider>
      </>,
    )
    expect(composed.container.querySelectorAll('.hl-action-menu')).toHaveLength(3)
    composed.unmount()
  })

  it('refuses a facet as an entry, reading the facets the detail panel actually declares', () => {
    const fence = fixture.noFacetsInTheMenu
    const panel = read(fence.facetSource)
    const object = panel.objects[fence.facetObjectId]
    if (!object) throw new Error(`missing-fixture-object: ${fence.facetObjectId}`)
    const facetIds: string[] = object.facets.map((facet: { id: string }) => facet.id)
    expect(facetIds).toContain(fence.itemId)

    const withFacet: ActionMenuEntry[] = [{ id: fence.itemId, label: 'Attachments', onClick: vi.fn() }]
    expect(() => render(<ActionMenu items={withFacet} facetIds={facetIds} />)).toThrow(fence.error)
    // Without declared facets the composer has stated nothing to fence against.
    expect(() => render(<ActionMenu items={withFacet} />).unmount()).not.toThrow()
    expect(() => render(<ActionMenu items={entries(fixture.initial.checked, [])} facetIds={facetIds} />).unmount()).not.toThrow()
  })
})
