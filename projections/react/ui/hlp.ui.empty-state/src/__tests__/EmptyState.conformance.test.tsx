import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import {
  EmptyState,
  type EmptyStateAction,
  type EmptyStateVariant,
} from '../EmptyState'

interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument { cases: FixtureCase[] }

function loadFixtures(): FixtureCase[] {
  const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.empty-state/fixtures.yaml')
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

describe('EmptyState shared fixtures', () => {
  it.each([
    ['empty-state.informational', 'informational'],
    ['empty-state.positive', 'positive'],
    ['empty-state.actionable', 'actionable'],
  ] as const)('%s', (id, variant) => {
    const contract = fixture(id)
    render(<EmptyState variant={variant} title={contract.input.title as string} />)
    const root = screen.getByText(contract.input.title as string).closest('.hl-empty-state')
    expect(root).toHaveAttribute('data-hl-variant', contract.expected.variant)
    expect(root?.querySelector('[data-hl-icon]')).toHaveAttribute('data-hl-icon', contract.expected.icon)
  })

  it('empty-state.description', () => {
    const contract = fixture('empty-state.description')
    render(
      <EmptyState
        variant="informational"
        title="No results"
        description={contract.input.description as string}
      />,
    )
    expect(screen.getByText(contract.input.description as string)).toHaveClass('hl-empty-state__description')
  })

  it('empty-state.no-description', () => {
    fixture('empty-state.no-description')
    const { container } = render(<EmptyState variant="informational" title="No results" />)
    expect(container.querySelectorAll('.hl-empty-state__description')).toHaveLength(0)
  })

  it('empty-state.action', async () => {
    const contract = fixture('empty-state.action')
    const onClick = vi.fn()
    render(
      <EmptyState
        variant="actionable"
        title="No payments"
        action={{ label: contract.input.label as string, onClick }}
      />,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: contract.input.label as string }))
    expect(onClick).toHaveBeenCalledTimes(contract.expected.callbacks as number)
  })

  it('empty-state.no-action', () => {
    fixture('empty-state.no-action')
    render(<EmptyState variant="informational" title="No results" />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('empty-state.action-any-variant', async () => {
    const contract = fixture('empty-state.action-any-variant')
    const variants = contract.input.variants as EmptyStateVariant[]
    const callbacks = variants.map(() => vi.fn())
    const { rerender } = render(
      <EmptyState
        variant={variants[0]}
        title="Empty"
        action={{ label: contract.input.label as string, onClick: callbacks[0] }}
      />,
    )
    const user = userEvent.setup()
    for (const [index, variant] of variants.entries()) {
      rerender(
        <EmptyState
          variant={variant}
          title="Empty"
          action={{ label: contract.input.label as string, onClick: callbacks[index] }}
        />,
      )
      await user.click(screen.getByRole('button'))
      expect(callbacks[index]).toHaveBeenCalledTimes(contract.expected.callbacksPerVariant as number)
    }
  })

  it('empty-state.decorative-icon', () => {
    const contract = fixture('empty-state.decorative-icon')
    const variants = contract.input.variants as EmptyStateVariant[]
    const { container, rerender } = render(<EmptyState variant={variants[0]} title="Empty" />)
    for (const variant of variants) {
      rerender(<EmptyState variant={variant} title="Empty" />)
      const icon = container.querySelector('[data-hl-icon]')
      expect(icon).toHaveAttribute('aria-hidden', String(contract.expected.ariaHidden))
      expect(icon).not.toHaveAttribute('aria-label')
      expect(icon?.querySelector('title')).toBeNull()
    }
  })

  it('empty-state.native-button', async () => {
    const contract = fixture('empty-state.native-button')
    const onClick = vi.fn()
    render(
      <EmptyState
        variant="actionable"
        title="Empty"
        action={{ label: contract.input.label as string, onClick }}
      />,
    )
    const button = screen.getByRole('button')
    expect(button.tagName.toLowerCase()).toBe(contract.expected.element)
    expect(button).toHaveAttribute('type', contract.expected.type)
    button.focus()
    await userEvent.setup().keyboard('{Enter}')
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('empty-state.host-attributes', () => {
    const contract = fixture('empty-state.host-attributes')
    render(
      <EmptyState
        variant="positive"
        title="لا توجد نتائج"
        className={contract.input.class as string}
        lang={contract.input.lang as string}
        dir={contract.input.dir as 'rtl'}
        data-case={contract.input['data-case'] as string}
        {...{ 'data-hl-variant': 'consumer-value' }}
      />,
    )
    const root = screen.getByText('لا توجد نتائج').closest('.hl-empty-state')
    expect(root).toHaveClass(contract.input.class as string)
    expect(root).toHaveAttribute('lang', contract.input.lang)
    expect(root).toHaveAttribute('dir', contract.input.dir)
    expect(root).toHaveAttribute('data-case', contract.input['data-case'])
    expect(root).toHaveAttribute('data-hl-variant', 'positive')
  })

  it('empty-state.invalid-title', () => {
    const contract = fixture('empty-state.invalid-title')
    expect(() => render(
      <EmptyState variant="informational" title={contract.input.title as string} />,
    )).toThrow(contract.expected.error as string)
  })

  it('empty-state.incomplete-action', () => {
    const contract = fixture('empty-state.incomplete-action')
    const incomplete = { label: contract.input.label as string } as EmptyStateAction
    expect(() => render(
      <EmptyState variant="actionable" title="Empty" action={incomplete} />,
    )).toThrow(contract.expected.error as string)
  })

  it('empty-state.projection-equivalence', () => {
    const contract = fixture('empty-state.projection-equivalence')
    const variants = contract.input.variants as EmptyStateVariant[]
    const { container, rerender } = render(
      <EmptyState
        variant={variants[0]}
        title="Empty"
        description="Nothing matches"
        action={{ label: 'Act', onClick: () => undefined }}
      />,
    )
    for (const variant of variants) {
      rerender(
        <EmptyState
          variant={variant}
          title="Empty"
          description="Nothing matches"
          action={{ label: 'Act', onClick: () => undefined }}
        />,
      )
      const root = container.firstElementChild
      expect(root).toHaveAttribute('data-hl-variant', variant)
      expect(root?.children[0]).toHaveAttribute('aria-hidden', 'true')
      expect(root?.children[1]).toHaveTextContent('Empty')
      expect(root?.children[2]).toHaveTextContent('Nothing matches')
      expect(root?.children[3]?.tagName).toBe('BUTTON')
      expect(root).not.toHaveAttribute('role')
      expect(root).not.toHaveAttribute('aria-live')
    }
  })
})
