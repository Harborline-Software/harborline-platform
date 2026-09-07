import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Breadcrumb } from '../Breadcrumb'
import { fixture, sharedCases } from './fixtures'

describe('Breadcrumb shared fixtures', () => {
  // Ticket 124: an empty trail is a refusal to render -- no landmark, no list, no sentence.
  it('breadcrumb.empty', () => {
    const declared = fixture(sharedCases, 'breadcrumb.empty')
    expect(declared.expected.rendered).toBe(false)
    const { container } = render(<Breadcrumb items={declared.input.items as never} />)
    expect(container).toBeEmptyDOMElement()
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(screen.queryByRole('list')).toBeNull()
    expect(document.querySelectorAll('.hl-breadcrumb__separator')).toHaveLength(0)
  })

  it('breadcrumb.infer-last-current', () => {
    fixture(sharedCases, 'breadcrumb.infer-last-current')
    render(<Breadcrumb items={[{ id: 'home', label: 'Home', href: '/' }, { id: 'item', label: 'Item' }]} />)
    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/')
    expect(screen.getByText('Item')).toHaveAttribute('aria-current', 'page')
  })

  it('breadcrumb.explicit-current', () => {
    fixture(sharedCases, 'breadcrumb.explicit-current')
    render(<Breadcrumb items={[{ label: 'One', current: true }, { label: 'Two' }]} />)
    expect(document.querySelectorAll('[aria-current="page"]')).toHaveLength(2)
  })

  it('breadcrumb.link-selection', () => {
    fixture(sharedCases, 'breadcrumb.link-selection')
    render(<Breadcrumb items={[
      { label: 'A', href: '/a', current: false },
      { label: 'B', href: '/b', current: true },
      { label: 'C', current: false },
    ]} />)
    expect(screen.getAllByRole('link').map(link => link.textContent)).toEqual(['A'])
    expect(screen.queryByRole('link', { name: 'B' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'C' })).toBeNull()
  })

  it('breadcrumb.separator-order', () => {
    fixture(sharedCases, 'breadcrumb.separator-order')
    render(<Breadcrumb items={[{ label: 'A' }, { label: 'B' }, { label: 'C' }]} separator="/" />)
    const items = screen.getAllByRole('listitem')
    expect(within(items[0]).queryByText('/')).toBeNull()
    expect(within(items[1]).getByText('/')).toHaveAttribute('aria-hidden', 'true')
    expect(within(items[2]).getByText('/')).toHaveAttribute('aria-hidden', 'true')
  })

  it('breadcrumb.relationships', () => {
    fixture(sharedCases, 'breadcrumb.relationships')
    render(<Breadcrumb accessibleLabel="Trail" items={[{ label: 'A', href: '/a' }, { label: 'B' }]} />)
    const landmark = screen.getByRole('navigation', { name: 'Trail' })
    expect(within(landmark).getByRole('list').tagName).toBe('OL')
    expect(within(landmark).getByText('B')).toHaveAttribute('aria-current', 'page')
  })

  it('breadcrumb.host-attributes', () => {
    fixture(sharedCases, 'breadcrumb.host-attributes')
    render(<Breadcrumb className="consumer" data-test="x" dir="rtl" id="trail" items={[{ id: 'a', label: 'A' }]} lang="ar" />)
    const landmark = screen.getByRole('navigation')
    expect(landmark).toHaveClass('hl-breadcrumb', 'consumer')
    expect(landmark).toHaveAttribute('data-test', 'x')
    expect(landmark).toHaveAttribute('dir', 'rtl')
    expect(landmark).toHaveAttribute('id', 'trail')
    expect(landmark).toHaveAttribute('lang', 'ar')
  })

  it('breadcrumb.current-class', () => {
    const declared = fixture(sharedCases, 'breadcrumb.current-class')
    const expected = declared.expected as { currentClasses: string[], textClasses: string[] }
    render(<Breadcrumb items={declared.input.items as never} />)
    expect([...document.querySelector('[aria-current="page"]')!.classList]).toEqual(expected.currentClasses)
    const plain = [...document.querySelectorAll('.hl-breadcrumb__text:not([aria-current])')]
    expect(plain.length).toBeGreaterThan(0)
    for (const span of plain) expect([...span.classList]).toEqual(expected.textClasses)
  })

  it('breadcrumb.projection-equivalence', () => {
    fixture(sharedCases, 'breadcrumb.projection-equivalence')
    render(<Breadcrumb items={[{ label: 'Home', href: '/' }, { label: 'Current' }]} />)
    expect(screen.getByRole('navigation')).toHaveAccessibleName('Breadcrumb')
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText('Current')).toHaveAttribute('aria-current', 'page')
  })
})
