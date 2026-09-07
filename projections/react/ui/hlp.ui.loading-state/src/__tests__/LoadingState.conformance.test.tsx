import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { LoadingState, type LoadingStateVariant } from '../LoadingState'

interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument { cases: FixtureCase[] }

function loadFixtures(): FixtureCase[] {
  const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.loading-state/fixtures.yaml')
  const defaults = (JSON.parse(readFileSync(path, 'utf8')) as FixtureDocument).cases
  const injected = process.env.HARBORLINE_CONFORMANCE_FIXTURE
  if (!injected) return defaults

  const injectedCase = JSON.parse(injected) as FixtureCase
  return defaults.map(candidate => candidate.id === injectedCase.id ? injectedCase : candidate)
}

const fixtures = loadFixtures()
const fixture = (id: string) => {
  const value = fixtures.find(candidate => candidate.id === id)
  if (!value) throw new Error(`missing-neutral-fixture: ${id}`)
  return value
}

describe('LoadingState shared fixtures', () => {
  it('loading-state.default', () => {
    const contract = fixture('loading-state.default')
    const { container } = render(<LoadingState label={contract.input.label as string} />)
    expect(container.firstElementChild?.tagName.toLowerCase()).toBe(contract.expected.element)
    expect(screen.getByRole('status')).toHaveAttribute('data-hl-variant', contract.expected.variant)
  })

  it('loading-state.inline', () => {
    const contract = fixture('loading-state.inline')
    const { container } = render(<LoadingState label={contract.input.label as string} variant="inline" />)
    expect(container.firstElementChild?.tagName.toLowerCase()).toBe(contract.expected.element)
    expect(screen.getByRole('status')).toHaveClass('hl-loading-state--inline')
  })

  it('loading-state.announcement', () => {
    const contract = fixture('loading-state.announcement')
    render(<LoadingState label={contract.input.label as string} />)
    const status = screen.getByRole(contract.expected.role as 'status')
    expect(status).toHaveAttribute('aria-live', contract.expected.ariaLive)
  })

  it('loading-state.label', () => {
    const contract = fixture('loading-state.label')
    render(<LoadingState label={contract.input.label as string} />)
    const status = screen.getByRole('status', { name: contract.expected.accessibleName as string })
    expect(status).toHaveTextContent(contract.expected.visibleText as string)
  })

  it('loading-state.label-update', () => {
    const contract = fixture('loading-state.label-update')
    const [first, latest] = contract.input.labels as [string, string]
    const { rerender } = render(<LoadingState label={first} />)
    const originalRegion = screen.getByRole('status')
    rerender(<LoadingState label={latest} />)
    expect(screen.getByRole('status')).toBe(originalRegion)
    expect(originalRegion).toHaveTextContent(contract.expected.latestVisible as string)
    expect(originalRegion).toHaveAttribute('aria-live', 'polite')
    expect(originalRegion).toHaveAccessibleName(latest)
  })

  it('loading-state.semantic-tone', () => {
    const contract = fixture('loading-state.semantic-tone')
    const variants = contract.input.variants as LoadingStateVariant[]
    const { rerender } = render(<LoadingState label="Loading" variant={variants[0]} />)
    for (const variant of variants) {
      rerender(<LoadingState label="Loading" variant={variant} />)
      expect(screen.getByRole('status')).toHaveAttribute('data-hl-tone', contract.expected.tone)
    }
  })

  it('loading-state.locale-direction', () => {
    const contract = fixture('loading-state.locale-direction')
    render(
      <LoadingState
        label={contract.input.label as string}
        lang={contract.input.locale as string}
        dir={contract.input.direction as 'rtl'}
      />,
    )
    const status = screen.getByRole('status')
    expect(status).toHaveAttribute('lang', contract.expected.lang)
    expect(status).toHaveAttribute('dir', contract.expected.dir)
  })

  it('loading-state.host-attributes', () => {
    const contract = fixture('loading-state.host-attributes')
    const attributes = contract.input.attributes as Record<string, string>
    render(
      <LoadingState
        label="Loading"
        className={attributes.class}
        data-case={attributes['data-case']}
        aria-atomic={attributes['aria-atomic'] as 'true'}
      />,
    )
    const status = screen.getByRole('status')
    expect(status).toHaveClass(attributes.class)
    expect(status).toHaveAttribute('data-case', attributes['data-case'])
    expect(status).toHaveAttribute('aria-atomic', attributes['aria-atomic'])
  })

  // 282 s5: these three selectors used to live only in the error-card lane stylesheet
  // (projections/blazor/ui/hlp.ui.error-card/wwwroot/feedback.css). Both lanes assert the spellings.
  it('loading-state.variant-classes', () => {
    const contract = fixture('loading-state.variant-classes')
    const expected = contract.expected as unknown as Record<string, string[]>
    const { rerender, container } = render(<LoadingState label={contract.input.label as string} />)
    expect([...screen.getByRole('status').classList]).toEqual(expected.pageClasses)
    expect([...container.querySelector('.hl-loading-state__label')!.classList]).toEqual(expected.labelClasses)
    rerender(<LoadingState label={contract.input.label as string} variant="inline" />)
    expect([...screen.getByRole('status').classList]).toEqual(expected.inlineClasses)
  })

  it('loading-state.projection-equivalence', () => {
    const contract = fixture('loading-state.projection-equivalence')
    const variants = contract.input.variants as LoadingStateVariant[]
    const { rerender } = render(<LoadingState label="Loading" variant={variants[0]} />)
    for (const variant of variants) {
      rerender(<LoadingState label="Loading" variant={variant} />)
      const status = screen.getByRole('status')
      expect(status).toHaveAttribute('data-hl-variant', variant)
      expect(status).toHaveAttribute('aria-live', 'polite')
      expect(status).toHaveTextContent('Loading')
    }
  })
})
