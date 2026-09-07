import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { Spotlight, type SpotlightItem, type SpotlightSection } from '../Spotlight'
import { fixture, sharedCases } from './fixtures'

function item(id: string, onSelect = vi.fn(), keepOpen = false): SpotlightItem {
  return { id, label: id, onSelect, keepOpen }
}

function sections(...ids: string[]): SpotlightSection[] {
  return [{ id: 'commands', label: 'Commands', items: ids.map(id => item(id)) }]
}

const required = {
  ariaLabel: 'Search commands',
  onOpenChange: vi.fn(),
  onQueryChange: vi.fn(),
  query: '',
}

describe('Spotlight shared fixtures', () => {
  it('spotlight.closed', () => {
    fixture(sharedCases, 'spotlight.closed')
    render(<Spotlight {...required} open={false} sections={[]} />)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('spotlight.combobox', () => {
    fixture(sharedCases, 'spotlight.combobox')
    render(<Spotlight {...required} open sections={sections('one')} />)
    const input = screen.getByRole('combobox', { name: 'Search commands' })
    const listbox = screen.getByRole('listbox')
    expect(input).toHaveFocus()
    expect(input).toHaveAttribute('aria-expanded', 'true')
    expect(input).toHaveAttribute('aria-controls', listbox.id)
    expect(input).toHaveAttribute('aria-activedescendant', screen.getByRole('option').id)
  })

  it('spotlight.sections', () => {
    fixture(sharedCases, 'spotlight.sections')
    render(<Spotlight {...required} open sections={[
      { id: 'commands', label: 'Commands', items: [item('one'), item('two')] },
      { id: 'records', label: 'Records', items: [item('three')] },
    ]} />)
    expect(screen.getAllByRole('group').map(group => group.getAttribute('aria-label'))).toEqual(['Commands', 'Records'])
    expect(screen.getAllByRole('option').map(option => option.getAttribute('data-item-id'))).toEqual(['one', 'two', 'three'])
  })

  it('spotlight.query', () => {
    const value = fixture(sharedCases, 'spotlight.query')
    const onQueryChange = vi.fn()
    render(<Spotlight {...required} onQueryChange={onQueryChange} open sections={[]} />)
    fireEvent.change(screen.getByRole('combobox'), { target: { value: value.input.typed } })
    expect(onQueryChange).toHaveBeenCalledWith(value.expected.emitted)
    expect(screen.getByRole('combobox')).toHaveValue('')
  })

  it('spotlight.keyboard', () => {
    fixture(sharedCases, 'spotlight.keyboard')
    render(<Spotlight {...required} open sections={sections('one', 'two')} />)
    const input = screen.getByRole('combobox')
    const active = () => input.getAttribute('aria-activedescendant')
    const selected = () => screen.getByRole('option', { selected: true }).getAttribute('data-item-id')
    expect(selected()).toBe('one')
    fireEvent.keyDown(input, { key: 'ArrowDown' }); expect(selected()).toBe('two')
    fireEvent.keyDown(input, { key: 'ArrowDown' }); expect(selected()).toBe('two')
    fireEvent.keyDown(input, { key: 'ArrowUp' }); expect(selected()).toBe('one')
    expect(active()).toBe(screen.getByRole('option', { selected: true }).id)
  })

  it('spotlight.select-close', () => {
    fixture(sharedCases, 'spotlight.select-close')
    const calls: string[] = []
    render(<Spotlight {...required} onOpenChange={() => calls.push('close')} open sections={[
      { id: 'commands', label: 'Commands', items: [item('one', () => calls.push('select'))] },
    ]} />)
    fireEvent.keyDown(screen.getByRole('combobox'), { key: 'Enter' })
    expect(calls).toEqual(['close', 'select'])
  })

  it('spotlight.keep-open', () => {
    fixture(sharedCases, 'spotlight.keep-open')
    const onOpenChange = vi.fn()
    const onSelect = vi.fn()
    render(<Spotlight {...required} onOpenChange={onOpenChange} open sections={[
      { id: 'commands', label: 'Commands', items: [item('one', onSelect, true)] },
    ]} />)
    fireEvent.keyDown(screen.getByRole('combobox'), { key: 'Enter' })
    expect(onOpenChange).not.toHaveBeenCalled()
    expect(onSelect).toHaveBeenCalledOnce()
  })

  it('spotlight.pinned', () => {
    fixture(sharedCases, 'spotlight.pinned')
    render(<Spotlight {...required} open pinnedAction={item('ask')} sections={sections('one', 'two')} />)
    expect(screen.getAllByRole('option').map(option => option.getAttribute('data-item-id'))).toEqual(['ask', 'one', 'two'])
  })

  it('spotlight.empty', () => {
    fixture(sharedCases, 'spotlight.empty')
    render(<Spotlight {...required} empty="Aucun résultat" open sections={[]} />)
    expect(screen.getByText('Aucun résultat')).toBeInTheDocument()
    expect(screen.queryAllByRole('option')).toHaveLength(0)
  })

  it('spotlight.loading', () => {
    fixture(sharedCases, 'spotlight.loading')
    render(<Spotlight {...required} open sections={[{ id: 'records', label: 'Records', items: [], loading: true, loadingLabel: 'Loading' }]} />)
    expect(screen.getByRole('group', { name: 'Records' })).toBeInTheDocument()
    expect(screen.getByText('Loading')).toBeInTheDocument()
    expect(screen.queryAllByRole('option')).toHaveLength(0)
  })

  it('spotlight.footer', () => {
    fixture(sharedCases, 'spotlight.footer')
    render(<Spotlight {...required} footer={<button type="button">See all</button>} open sections={[]} />)
    expect(screen.getByRole('button', { name: 'See all' })).toBeInTheDocument()
  })

  it('spotlight.results-announcement', () => {
    fixture(sharedCases, 'spotlight.results-announcement')
    render(<Spotlight {...required} open resultsCountLabel={count => `${count} résultats`} sections={sections('one', 'two', 'three')} />)
    expect(screen.getByRole('status')).toHaveTextContent('3 résultats')
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite')
  })

  it('spotlight.dismiss-focus', async () => {
    fixture(sharedCases, 'spotlight.dismiss-focus')
    const onOpenChange = vi.fn()
    function Harness() {
      const [open, setOpen] = useState(false)
      return <><button type="button" onClick={() => setOpen(true)}>Open</button><Spotlight {...required} onOpenChange={next => { onOpenChange(next); setOpen(next) }} open={open} sections={sections('one')} /></>
    }
    render(<Harness />)
    const trigger = screen.getByRole('button', { name: 'Open' })
    await userEvent.setup().click(trigger)
    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(trigger).toHaveFocus()
  })

  it('spotlight.invalid-identities', () => {
    fixture(sharedCases, 'spotlight.invalid-identities')
    expect(() => render(<Spotlight {...required} open sections={[{ id: 'bad', label: 'Bad', items: [item('same'), item('same')] }]} />)).toThrow('duplicate-item-id')
    expect(() => render(<Spotlight {...required} open sections={[{ id: 'bad', label: '', items: [item('one')] }]} />)).toThrow('section-label-required')
  })

  // 282 s11: the Blazor lane once spelled the query input hl-spotlight__input as well and put
  // hl-visually-hidden on the status node; neither is defined by the authority stylesheet, and
  // hl-spotlight__group was styled by neither lane. This asserts the one spelling in this lane;
  // the Blazor case asserts the same fixture row in the other.
  it('spotlight.classes', () => {
    const expected = fixture(sharedCases, 'spotlight.classes').expected as {
      queryClasses: string[]
      statusClasses: string[]
      groupClasses: string[]
    }
    render(<Spotlight {...required} open sections={sections('one')} />)
    expect([...screen.getByRole('combobox').classList]).toEqual(expected.queryClasses)
    expect([...screen.getByRole('status').classList]).toEqual(expected.statusClasses)
    expect([...screen.getByRole('group').classList]).toEqual(expected.groupClasses)
  })

  it('spotlight.projection-equivalence', () => {
    fixture(sharedCases, 'spotlight.projection-equivalence')
    const onSelect = vi.fn()
    render(<Spotlight {...required} open pinnedAction={item('ask')} sections={[{ id: 'commands', label: 'Commands', items: [item('one', onSelect)] }]} />)
    expect(screen.getAllByRole('option').map(option => option.getAttribute('data-item-id'))).toEqual(['ask', 'one'])
    fireEvent.keyDown(screen.getByRole('combobox'), { key: 'ArrowDown' })
    fireEvent.keyDown(screen.getByRole('combobox'), { key: 'Enter' })
    expect(onSelect).toHaveBeenCalledOnce()
  })
})
