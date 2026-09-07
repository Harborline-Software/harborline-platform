import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '../Card'
import { fixture, qualityCases } from './fixtures'

describe('Card React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'card.quality.heading',
      'card.quality.reflow',
      'card.quality.caller-content',
      'card.quality.rtl',
      'card.quality.pseudo',
      'card.quality.light-dark',
      'card.quality.tokens',
      'card.quality.forced-colors',
      'card.quality.contrast',
      'card.quality.visual-parity',
    ])
  })

  it('preserves asChild element attributes, classes, styles, and card semantics', () => {
    render(
      <Card asChild className="consumer" style={{ color: 'red' }} variant="flat">
        <a className="child" data-child="yes" href="/details" style={{ display: 'block' }}>Details</a>
      </Card>,
    )
    const link = screen.getByRole('link', { name: 'Details' })
    expect(link).toHaveClass('hl-card', 'consumer', 'child')
    expect(link).toHaveAttribute('data-child', 'yes')
    expect(link).toHaveAttribute('data-hl-variant', 'flat')
    expect(link.style.color).toBe('red')
    expect(link.style.display).toBe('block')
  })

  it('preserves both child and Card event handlers through the asChild facade', async () => {
    const onCardClick = vi.fn()
    const onChildClick = vi.fn()
    render(
      <Card asChild onClick={onCardClick}>
        <button onClick={onChildClick} type="button">Open</button>
      </Card>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Open' }))
    expect(onChildClick).toHaveBeenCalledOnce()
    expect(onCardClick).toHaveBeenCalledOnce()
  })

  it('gives footer the same inherited padding compatibility behavior', () => {
    render(<Card padding="sm"><CardFooter data-testid="footer">Actions</CardFooter></Card>)
    expect(screen.getByTestId('footer')).toHaveAttribute('data-hl-padding', 'sm')
    expect(screen.getByTestId('footer')).toHaveClass('hl-card__footer')
  })

  it('preserves caller-selected semantics without adding a Card landmark', () => {
    fixture(qualityCases, 'card.quality.heading')
    render(
      <Card aria-label="Summary" data-testid="card">
        <CardHeader><CardTitle as="h1">Summary</CardTitle></CardHeader>
        <CardContent>Body</CardContent>
      </Card>,
    )
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Summary')
    expect(screen.getByTestId('card')).not.toHaveAttribute('role')
  })

  it('publishes logical inline separators and quality-profile host styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-card-surface')
    expect(css).toMatch(/data-hl-orientation='horizontal'[\s\S]*border-inline-start/)
    expect(css).toContain('padding-inline')
    expect(css).toContain("[data-theme='dark'] .hl-card")
    expect(css).toContain('@media (forced-colors: active)')
  })
})
