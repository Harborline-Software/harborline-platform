import type * as React from 'react'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { SearchInput } from '../SearchInput'
import { sharedCases } from './fixtures'

// Each neutral case runs the production component against its own input and expected values.
// rtl concerns logical CSS edges a browser must observe; projection-equivalence compares lanes.
type Case = { id: string; input: Record<string, any>; expected: Record<string, any> }
const cases = sharedCases as unknown as Case[]
const unasserted = ['search-input.rtl', 'search-input.projection-equivalence']

function mount(props: { value?: string; debounceMs?: number } = {}) {
  const published: string[] = []
  const view = render(<SearchInput value={props.value ?? ''} debounceMs={props.debounceMs} onChange={value => published.push(value)}/>)
  const box = () => screen.getByRole('searchbox') as HTMLInputElement
  return {
    view, published, box,
    type: (value: string) => fireEvent.change(box(), { target: { value } }),
    busy: () => box().closest('[aria-busy]')!.getAttribute('aria-busy') === 'true',
    clear: () => screen.queryByRole('button'),
  }
}

afterEach(() => vi.useRealTimers())

describe('SearchInput shared fixture semantics', () => {
  it('covers every case or names it as unasserted', () => {
    for (const entry of cases) expect(entry.id in assertions || unasserted.includes(entry.id), entry.id).toBe(true)
  })
  for (const entry of cases.filter(entry => !unasserted.includes(entry.id))) {
    it(entry.id, () => { vi.useFakeTimers(); assertions[entry.id]!(entry) })
  }
})

const assertions: Record<string, (entry: Case) => void> = {
  'search-input.defaults': ({ input, expected }) => {
    const h = mount({ value: input.value })
    expect(h.box().getAttribute('placeholder')).toBe(expected.placeholder)
    expect(h.box().getAttribute('role')).toBe(expected.role)
    expect(h.clear() !== null).toBe(expected.clearPresent)
    h.type('a')
    act(() => vi.advanceTimersByTime(expected.debounceMs - 1))
    expect(h.published).toEqual([])
    act(() => vi.advanceTimersByTime(1))
    expect(h.published).toEqual(['a'])
  },
  'search-input.typing': ({ input, expected }) => {
    const h = mount({ debounceMs: input.debounceMs })
    for (const edit of input.edits) h.type(edit)
    act(() => vi.advanceTimersByTime(input.advanceMs))
    expect(h.box().value).toBe(expected.draft)
    expect(h.published).toEqual(expected.published)
  },
  'search-input.coalesce': ({ input, expected }) => {
    const h = mount({ debounceMs: input.debounceMs })
    for (const edit of input.edits) { h.type(edit); act(() => vi.advanceTimersByTime(input.intervalMs)) }
    act(() => vi.advanceTimersByTime(input.debounceMs))
    expect(h.published).toEqual(expected.published)
    expect(input.edits.length - h.published.length).toBe(expected.supersededCount)
  },
  'search-input.busy': ({ input, expected }) => {
    const h = mount()
    h.type(input.edit)
    const states: boolean[] = []
    let elapsed = 0
    for (const at of input.advanceMs) { act(() => vi.advanceTimersByTime(at - elapsed)); elapsed = at; states.push(h.busy()) }
    expect(states).toEqual(expected.busyStates)
  },
  'search-input.external-sync': ({ input, expected }) => {
    const published: string[] = []
    const view = render(<SearchInput value="" onChange={value => published.push(value)}/>)
    fireEvent.change(screen.getByRole('searchbox'), { target: { value: input.draft } })
    expect(input.pending).toBe(true)
    view.rerender(<SearchInput value={input.externalValue} onChange={value => published.push(value)}/>)
    act(() => vi.advanceTimersByTime(1000))
    const box = screen.getByRole('searchbox') as HTMLInputElement
    expect(box.value).toBe(expected.draft)
    expect(box.closest('[aria-busy]')!.getAttribute('aria-busy') === 'true').toBe(expected.busy)
    expect(published).toEqual(expected.published)
  },
  'search-input.clear': ({ input, expected }) => {
    const h = mount({ value: input.value })
    h.type(input.pending)
    fireEvent.click(h.clear()!)
    act(() => vi.advanceTimersByTime(1000))
    expect(h.box().value).toBe(expected.draft)
    expect(h.busy()).toBe(expected.busy)
    expect(h.published).toEqual(expected.published)
  },
  'search-input.empty': ({ input, expected }) => {
    const h = mount({ value: input.value })
    expect(h.clear() !== null).toBe(expected.clearPresent)
  },
  'search-input.localization': ({ input, expected }) => {
    render(<HarborlineLocaleProvider catalog={input.catalog}><SearchInput value="oak" onChange={() => undefined}/></HarborlineLocaleProvider>)
    expect(screen.getByRole('searchbox').getAttribute('placeholder')).toBe(expected.placeholder)
    expect(screen.getByRole('button', { name: expected.clearName })).toBeInTheDocument()
  },
  'search-input.catalog-copy': ({ input, expected }) => {
    // Placeholder, searchbox name, clear name and direction, read from one render.
    const copy = (node: React.ReactElement, withDir = false) => {
      const view = render(node)
      const box = screen.getByRole('searchbox')
      const result: Record<string, string | null> = { placeholder: box.getAttribute('placeholder'), name: box.getAttribute('aria-label'), clearName: view.container.querySelector('.hl-search-input__clear')!.getAttribute('aria-label') }
      if (withDir) result.dir = view.container.querySelector('.hl-search-input')!.getAttribute('dir')
      view.unmount()
      return result
    }
    const host = (catalog: { locale: string; entries: Record<string, string> }, props: Record<string, string> = {}) =>
      <HarborlineLocaleProvider locale={catalog.locale} catalog={catalog.entries}><SearchInput value={input.value} onChange={() => undefined} {...props}/></HarborlineLocaleProvider>
    expect(copy(<SearchInput value={input.value} onChange={() => undefined}/>)).toEqual(expected.defaultCatalog)
    // A catalog change in one live tree: French, then Arabic (rtl), then the en-XA pseudo-locale.
    const live = render(host(input.hostCatalogs[0]))
    const read = () => { const box = screen.getByRole('searchbox'); return { placeholder: box.getAttribute('placeholder'), name: box.getAttribute('aria-label'), clearName: live.container.querySelector('.hl-search-input__clear')!.getAttribute('aria-label'), dir: live.container.querySelector('.hl-search-input')!.getAttribute('dir') } }
    const seen = [read()]
    for (const catalog of input.hostCatalogs.slice(1)) { live.rerender(host(catalog)); seen.push(read()) }
    live.unmount()
    expect(seen).toEqual(expected.hostCatalogs)
    expect(copy(host(input.keyEchoHost))).toEqual(expected.keyEchoHost)
    expect(copy(<SearchInput value={input.value} onChange={() => undefined} accessibleLabel={input.accessibleLabels.blank}/>).name).toBe(expected.accessibleLabels.blank)
    expect(copy(<SearchInput value={input.value} onChange={() => undefined} accessibleLabel={input.accessibleLabels.explicit}/>).name).toBe(expected.accessibleLabels.explicit)
    expect(copy(host(input.hostCatalogs[1], input.blankCaller))).toEqual(expected.blankCaller)
    expect(copy(host(input.blankHost))).toEqual(expected.blankHost)
  },
  'search-input.unmount': ({ input, expected }) => {
    const h = mount()
    h.type(input.draft)
    expect(input.unmountBeforeDelay).toBe(true)
    h.view.unmount()
    act(() => vi.advanceTimersByTime(1000))
    expect(h.published).toEqual(expected.published)
  },
  'search-input.invalid-delay': ({ input, expected }) => {
    const silence = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    try { expect(() => render(<SearchInput value="" debounceMs={input.debounceMs} onChange={() => undefined}/>)).toThrow(expected.error) } finally { silence.mockRestore() }
  },
}
