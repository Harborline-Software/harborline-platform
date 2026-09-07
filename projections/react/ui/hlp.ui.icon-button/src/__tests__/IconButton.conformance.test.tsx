import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { IconButton, type IconButtonSize, type IconButtonVariant } from '../IconButton'

interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument { cases: FixtureCase[] }

function loadFixtures(): FixtureCase[] {
  const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.icon-button/fixtures.yaml')
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

describe('IconButton shared fixtures', () => {
  it('icon-button.invoke', async () => {
    const contract = fixture('icon-button.invoke')
    const onClick = vi.fn()
    render(<IconButton aria-label="Open layers" onClick={onClick}>+</IconButton>)
    const button = screen.getByRole('button', { name: 'Open layers' })
    expect(button).toHaveAttribute('type', contract.expected.defaultType)
    await userEvent.setup().click(button)
    expect(onClick).toHaveBeenCalledTimes(contract.expected.callbacks as number)
  })

  it('icon-button.disabled-noop', async () => {
    const contract = fixture('icon-button.disabled-noop')
    const onClick = vi.fn()
    render(<IconButton aria-label="Open layers" disabled onClick={onClick}>+</IconButton>)
    await userEvent.setup().click(screen.getByRole('button'))
    expect(onClick).toHaveBeenCalledTimes(contract.expected.callbacks as number)
  })

  it('icon-button.loading-noop', async () => {
    const contract = fixture('icon-button.loading-noop')
    const onClick = vi.fn()
    const { container } = render(
      <IconButton aria-label="Open layers" loading onClick={onClick}>+</IconButton>,
    )
    const button = screen.getByRole('button')
    await userEvent.setup().click(button)
    expect(onClick).toHaveBeenCalledTimes(contract.expected.callbacks as number)
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', contract.expected.ariaBusy)
    expect(container.querySelector('[data-hl-loading-indicator]')).toHaveAttribute('aria-hidden', 'true')
    expect(container.querySelector('[aria-live], [role="status"]')).toBeNull()
  })

  it('icon-button.accessible-name', () => {
    const contract = fixture('icon-button.accessible-name')
    render(<IconButton aria-label={contract.input.name as string}>+</IconButton>)
    expect(screen.getByRole('button')).toHaveAccessibleName(contract.expected.accessibleName as string)
    expect(() => render(<IconButton aria-label="   ">+</IconButton>)).toThrow('accessible-name-required')
  })

  it('icon-button.form-type', () => {
    const contract = fixture('icon-button.form-type')
    const types = contract.input.types as Array<'button' | 'submit' | 'reset'>
    const { rerender } = render(<IconButton aria-label="Command" type={types[0]}>+</IconButton>)
    for (const type of types) {
      rerender(<IconButton aria-label="Command" type={type}>+</IconButton>)
      expect(screen.getByRole('button')).toHaveAttribute('type', type)
    }
  })

  it('icon-button.variants', () => {
    const contract = fixture('icon-button.variants')
    const variants = contract.input.values as IconButtonVariant[]
    const { rerender } = render(<IconButton aria-label="Command" variant={variants[0]}>+</IconButton>)
    for (const variant of variants) {
      rerender(<IconButton aria-label="Command" variant={variant}>+</IconButton>)
      expect(screen.getByRole('button')).toHaveAttribute('data-hl-variant', variant)
      expect(screen.getByRole('button')).toHaveClass(`hl-icon-button--${variant}`)
    }
  })

  it('icon-button.sizes', () => {
    const contract = fixture('icon-button.sizes')
    const sizes = contract.input.values as IconButtonSize[]
    const { rerender } = render(<IconButton aria-label="Command" size={sizes[0]}>+</IconButton>)
    for (const size of sizes) {
      rerender(<IconButton aria-label="Command" size={size}>+</IconButton>)
      expect(screen.getByRole('button')).toHaveAttribute('data-hl-size', size)
      expect(screen.getByRole('button')).toHaveClass(`hl-icon-button--${size}`)
    }
  })

  it('icon-button.touch-target', () => {
    const contract = fixture('icon-button.touch-target')
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain(`min-inline-size: ${(contract.expected.minimumWidth as number) / 16}rem`)
    expect(css).toContain(`min-block-size: ${(contract.expected.minimumHeight as number) / 16}rem`)
    expect(css).toMatch(/\.hl-icon-button--touch\s*\{[^}]*2\.75rem[^}]*2\.75rem/s)
  })

  it('icon-button.host-attributes', () => {
    const contract = fixture('icon-button.host-attributes')
    render(
      <IconButton
        aria-label="Open layers"
        className={contract.input.class as string}
        data-case={contract.input['data-case'] as string}
        dir="rtl"
        data-hl-size="consumer-size"
      >
        +
      </IconButton>,
    )
    const button = screen.getByRole('button')
    expect(button).toHaveClass(contract.input.class as string)
    expect(button).toHaveAttribute('data-case', contract.input['data-case'])
    expect(button).toHaveAttribute('dir', 'rtl')
    expect(button).toHaveAttribute('data-hl-size', 'md')
  })

  it('icon-button.projection-equivalence', () => {
    fixture('icon-button.projection-equivalence')
    const { container } = render(<IconButton aria-label="Open layers" loading>+</IconButton>)
    const button = screen.getByRole('button')
    expect(button.tagName).toBe('BUTTON')
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(container.querySelector('[data-hl-loading-indicator]')).toHaveAttribute('aria-hidden', 'true')
  })
})
