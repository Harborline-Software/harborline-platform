import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Separator } from '../Separator'

interface FixtureCase {
  id: string
  input: Record<string, unknown>
  expected: Record<string, unknown>
}

interface FixtureDocument { cases: FixtureCase[] }

function loadFixtures(): FixtureCase[] {
  const path = resolve(process.cwd(), '../../../../conformance/hlp.ui.separator/fixtures.yaml')
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

describe('Separator shared fixtures', () => {
  it('separator.decorative-horizontal', () => {
    const contract = fixture('separator.decorative-horizontal')
    const { container } = render(<Separator />)
    const separator = container.firstElementChild
    expect(separator).toHaveAttribute('role', contract.expected.role)
    expect(separator).not.toHaveAttribute('aria-orientation')
    expect(separator).toHaveAttribute('data-hl-orientation', contract.expected.orientation)
  })

  it('separator.semantic-horizontal', () => {
    const contract = fixture('separator.semantic-horizontal')
    render(<Separator decorative={false} />)
    expect(screen.getByRole('separator')).toHaveAttribute(
      'aria-orientation',
      contract.expected.ariaOrientation,
    )
  })

  it('separator.semantic-vertical', () => {
    const contract = fixture('separator.semantic-vertical')
    render(<Separator decorative={false} orientation="vertical" />)
    expect(screen.getByRole('separator')).toHaveAttribute(
      'aria-orientation',
      contract.expected.ariaOrientation,
    )
  })

  it('separator.labeled', () => {
    const contract = fixture('separator.labeled')
    const { container } = render(
      <Separator decorative={false} label={contract.input.label as string} />,
    )
    expect(screen.getByRole('separator')).toBeInTheDocument()
    expect(container.querySelector('.hl-separator__label')).toHaveTextContent(contract.input.label as string)
    expect(container.querySelectorAll('[data-hl-rule="true"]')).toHaveLength(
      contract.expected.ruleSegments as number,
    )
  })

  it('separator.blank-label', () => {
    const contract = fixture('separator.blank-label')
    const { container } = render(<Separator label={contract.input.label as string} />)
    expect(container.querySelector('.hl-separator__label')).toBeNull()
    expect(container.querySelectorAll('[data-hl-rule="true"]')).toHaveLength(
      contract.expected.ruleSegments as number,
    )
  })

  it('separator.host-attributes', () => {
    const contract = fixture('separator.host-attributes')
    render(
      <Separator
        decorative={false}
        className={contract.input.class as string}
        data-case={contract.input['data-case'] as string}
        data-hl-orientation="consumer-orientation"
        dir="rtl"
      />,
    )
    const separator = screen.getByRole('separator')
    expect(separator).toHaveClass(contract.input.class as string)
    expect(separator).toHaveAttribute('data-case', contract.input['data-case'])
    expect(separator).toHaveAttribute('dir', 'rtl')
    expect(separator).toHaveAttribute('data-hl-orientation', 'horizontal')
  })

  it('separator.projection-equivalence', () => {
    fixture('separator.projection-equivalence')
    render(<Separator decorative={false} orientation="vertical" label="أو" />)
    const separator = screen.getByRole('separator')
    expect(separator).toHaveAttribute('aria-orientation', 'vertical')
    expect(separator).toHaveClass('hl-separator--labeled', 'hl-separator--vertical')
    expect(separator.querySelectorAll('[data-hl-rule="true"]')).toHaveLength(2)
  })
})
