import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
  type CardPadding,
  type CardVariant,
} from '../Card'
import { fixture, sharedCases } from './fixtures'

describe('Card revision-1 shared fixtures', () => {
  it('consumes every frozen neutral case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'card.defaults',
      'card.appearances',
      'card.padding',
      'card.horizontal',
      'card.separators',
      'card.heading-level',
      'card.region-composition',
      'card.react-compatibility-facades',
      'card.host-attributes',
      'card.projection-equivalence',
    ])
  })

  it('card.defaults', () => {
    fixture(sharedCases, 'card.defaults')
    render(<Card data-testid="card"><CardHeader>Header</CardHeader><CardContent>Body</CardContent></Card>)
    const card = screen.getByTestId('card')
    expect(card).toHaveAttribute('data-hl-variant', 'outlined')
    expect(card).toHaveAttribute('data-hl-padding', 'md')
    expect(card).toHaveAttribute('data-hl-orientation', 'vertical')
    expect(card).toHaveAttribute('data-hl-separators', 'false')
    expect(card).not.toHaveAttribute('role')
  })

  it('card.appearances', () => {
    const value = fixture(sharedCases, 'card.appearances')
    for (const variant of value.input.values as CardVariant[]) {
      const { unmount } = render(<Card data-testid={variant} variant={variant}><CardContent>{variant}</CardContent></Card>)
      expect(screen.getByTestId(variant)).toHaveAttribute('data-hl-variant', variant)
      unmount()
    }
  })

  it('card.padding', () => {
    const value = fixture(sharedCases, 'card.padding')
    for (const padding of value.input.values as CardPadding[]) {
      const { unmount } = render(
        <Card padding={padding}>
          <CardHeader data-testid="header">Header</CardHeader>
          <CardContent data-testid="content">Body</CardContent>
        </Card>,
      )
      expect(screen.getByTestId('header')).toHaveAttribute('data-hl-padding', padding)
      expect(screen.getByTestId('content')).toHaveAttribute('data-hl-padding', padding)
      unmount()
    }
  })

  it('card.horizontal', () => {
    fixture(sharedCases, 'card.horizontal')
    render(<Card data-testid="card" orientation="horizontal"><CardContent data-testid="content">Body</CardContent></Card>)
    expect(screen.getByTestId('card')).toHaveAttribute('data-hl-orientation', 'horizontal')
    expect(screen.getByTestId('content')).toHaveAttribute('data-hl-flex', 'true')
  })

  it('card.separators', () => {
    fixture(sharedCases, 'card.separators')
    render(
      <Card data-testid="card" orientation="horizontal" separators>
        <CardHeader>Header</CardHeader><CardContent>Body</CardContent>
      </Card>,
    )
    expect(screen.getByTestId('card')).toHaveAttribute('data-hl-separators', 'true')
  })

  it('card.heading-level', () => {
    fixture(sharedCases, 'card.heading-level')
    const levels = [1, 2, 3, 4, 5, 6] as const
    const { rerender } = render(<CardTitle>Title</CardTitle>)
    expect(screen.getByRole('heading', { level: 3 })).toBeInTheDocument()
    for (const level of levels) {
      rerender(<CardTitle as={`h${level}` as const}>Title</CardTitle>)
      expect(screen.getByRole('heading', { level })).toBeInTheDocument()
    }
  })

  it('card.region-composition', () => {
    fixture(sharedCases, 'card.region-composition')
    render(
      <Card>
        <CardHeader><CardTitle>Inspection</CardTitle></CardHeader>
        <CardContent><strong>Caller content</strong></CardContent>
      </Card>,
    )
    expect(screen.getByRole('heading', { name: 'Inspection' })).toBeInTheDocument()
    expect(screen.getByText('Caller content')).toBeInTheDocument()
  })

  it('card.react-compatibility-facades', () => {
    fixture(sharedCases, 'card.react-compatibility-facades')
    render(
      <Card asChild variant="elevated">
        <article data-testid="article">
          <CardDescription>Description</CardDescription>
          <CardFooter>Footer</CardFooter>
        </article>
      </Card>,
    )
    expect(screen.getByTestId('article')).toHaveClass('hl-card')
    expect(screen.getByTestId('article')).toHaveAttribute('data-hl-variant', 'elevated')
    expect(screen.getByText('Description').tagName).toBe('P')
    expect(screen.getByText('Footer')).toHaveClass('hl-card__footer')
  })

  it('card.host-attributes', () => {
    fixture(sharedCases, 'card.host-attributes')
    render(<Card className="consumer" data-case="card" data-testid="card"><CardContent>Body</CardContent></Card>)
    expect(screen.getByTestId('card')).toHaveClass('hl-card', 'consumer')
    expect(screen.getByTestId('card')).toHaveAttribute('data-case', 'card')
  })

  it('card.projection-equivalence', () => {
    fixture(sharedCases, 'card.projection-equivalence')
    render(
      <Card data-testid="card" orientation="horizontal" padding="lg" separators variant="raised">
        <CardHeader data-testid="header"><CardTitle as="h2">Title</CardTitle></CardHeader>
        <CardContent data-testid="content">Body</CardContent>
      </Card>,
    )
    expect(screen.getByTestId('card')).toHaveAttribute('data-hl-orientation', 'horizontal')
    expect(screen.getByTestId('header')).toHaveAttribute('data-hl-padding', 'lg')
    expect(screen.getByTestId('content')).toHaveAttribute('data-hl-padding', 'lg')
    expect(screen.getByRole('heading', { level: 2 })).toBeInTheDocument()
  })
})
