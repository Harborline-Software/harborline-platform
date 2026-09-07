import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ErrorCard, type ErrorCardVariant } from '../ErrorCard'

interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument { cases: FixtureCase[] }

function loadFixtures(): FixtureCase[] {
  const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.error-card/fixtures.yaml')
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

describe('ErrorCard shared fixtures', () => {
  it('error-card.default', () => {
    const contract = fixture('error-card.default')
    render(<ErrorCard title={contract.input.title as string} />)
    const alert = screen.getByRole('alert')
    expect(alert).toHaveAttribute('data-hl-variant', contract.expected.variant)
    expect(alert.querySelector(contract.expected.titleElement as string)).toHaveTextContent(contract.input.title as string)
  })

  it.each([
    ['error-card.page', 'page'],
    ['error-card.compact', 'compact'],
  ] as const)('%s', (id, variant) => {
    const contract = fixture(id)
    render(<ErrorCard title={contract.input.title as string} variant={variant} />)
    const title = screen.getByText(contract.input.title as string)
    expect(title.tagName.toLowerCase()).toBe(contract.expected.titleElement)
    expect(screen.getByRole('alert')).toHaveAttribute('data-hl-variant', variant)
  })

  it('error-card.message', () => {
    const contract = fixture('error-card.message')
    const [visible, absent, empty] = contract.input.messages as [string, null, string]
    const { rerender } = render(<ErrorCard title={contract.input.title as string} message={visible} />)
    const alert = screen.getByRole('alert')
    expect(alert.lastElementChild).toHaveTextContent(visible)
    rerender(<ErrorCard title={contract.input.title as string} message={absent ?? undefined} />)
    expect(screen.queryByText(visible)).not.toBeInTheDocument()
    rerender(<ErrorCard title={contract.input.title as string} message={empty} />)
    expect(alert.querySelector('.hl-error-card__message')).toBeNull()
  })

  it('error-card.retry', async () => {
    fixture('error-card.retry')
    const onRetry = vi.fn()
    render(<ErrorCard title="Unable to load" onRetry={onRetry} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Retry' }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('error-card.no-retry', () => {
    fixture('error-card.no-retry')
    render(<ErrorCard title="Unable to load" />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('error-card.localized-retry', () => {
    const contract = fixture('error-card.localized-retry')
    render(
      <ErrorCard
        title="Impossible de charger"
        onRetry={() => undefined}
        retryLabel={contract.input.retryLabel as string}
        lang={contract.input.locale as string}
      />,
    )
    expect(screen.getByRole('button')).toHaveTextContent(contract.expected.retryLabel as string)
    expect(screen.getByRole('alert')).toHaveAttribute('lang', contract.expected.lang)
  })

  it('error-card.semantic-tone', () => {
    const contract = fixture('error-card.semantic-tone')
    render(<ErrorCard title="Unable to load" message="Try later" onRetry={() => undefined} />)
    expect(screen.getByRole('alert')).toHaveAttribute('data-hl-tone', contract.expected.container as string)
    expect(screen.getByText('Unable to load')).toHaveAttribute('data-hl-tone', contract.expected.title as string)
    expect(screen.getByText('Try later')).toHaveAttribute('data-hl-tone', contract.expected.message as string)
    expect(screen.getByRole('button')).toHaveAttribute('data-hl-tone', contract.expected.retry as string)
  })

  it('error-card.host-attributes', () => {
    const contract = fixture('error-card.host-attributes')
    const attributes = contract.input.attributes as Record<string, string>
    render(
      <ErrorCard
        title="تعذر التحميل"
        className={attributes.class}
        data-case={attributes['data-case']}
        aria-describedby={attributes['aria-describedby']}
        dir={contract.input.direction as 'rtl'}
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveClass(attributes.class)
    expect(alert).toHaveAttribute('data-case', attributes['data-case'])
    expect(alert).toHaveAttribute('aria-describedby', attributes['aria-describedby'])
    expect(alert).toHaveAttribute('dir', contract.expected.dir)
  })

  // 282 s5: the Blazor lane's feedback.css was a hand-written variant of the authority. Nothing but
  // the class spellings ties the two lanes to one stylesheet, so both lanes assert this row.
  it('error-card.class-vocabulary', () => {
    const contract = fixture('error-card.class-vocabulary')
    const expected = contract.expected as unknown as Record<string, string[]>
    render(<ErrorCard title="Unable to load" message={contract.input.message as string} onRetry={() => undefined} />)
    const alert = screen.getByRole('alert')
    expect([...alert.classList]).toEqual(expected.containerClasses)
    expect([...alert.querySelector('.hl-error-card__title')!.classList]).toEqual(expected.titleClasses)
    expect([...alert.querySelector('.hl-error-card__message')!.classList]).toEqual(expected.messageClasses)
    expect([...screen.getByRole('button').classList]).toEqual(expected.retryClasses)
  })

  it('error-card.projection-equivalence', () => {
    const contract = fixture('error-card.projection-equivalence')
    const variants = contract.input.variants as ErrorCardVariant[]
    const { rerender } = render(<ErrorCard title="Failure" variant={variants[0]} onRetry={() => undefined} />)
    for (const variant of variants) {
      rerender(<ErrorCard title="Failure" variant={variant} onRetry={() => undefined} />)
      const alert = screen.getByRole('alert')
      expect(alert).toHaveAttribute('data-hl-variant', variant)
      expect(screen.getByRole('button')).toHaveAttribute('type', 'button')
      if (variant === 'page') expect(screen.getByRole('heading', { level: 2 })).toBeInTheDocument()
      else expect(screen.queryByRole('heading')).not.toBeInTheDocument()
    }
  })
})
