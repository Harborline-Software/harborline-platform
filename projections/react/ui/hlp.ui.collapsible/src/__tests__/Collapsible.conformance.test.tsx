import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Collapsible } from '../Collapsible'
import { fixture, sharedCases } from './fixtures'

describe('Collapsible shared fixtures', () => {
  it('collapsible.initial-closed', () => {
    fixture(sharedCases, 'collapsible.initial-closed')
    render(<Collapsible title="Settings">Content</Collapsible>)
    expect(screen.getByRole('button', { name: 'Settings' })).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.getByText('Content')).not.toBeVisible()
  })

  it('collapsible.default-open', () => {
    fixture(sharedCases, 'collapsible.default-open')
    render(<Collapsible defaultOpen title="Settings">Content</Collapsible>)
    expect(screen.getByRole('button', { name: 'Settings' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('region', { name: 'Settings' })).toBeVisible()
  })

  it('collapsible.controlled-change', async () => {
    fixture(sharedCases, 'collapsible.controlled-change')
    const onOpenChange = vi.fn()
    render(<Collapsible onOpenChange={onOpenChange} open={false} title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    await userEvent.setup().click(trigger)
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(onOpenChange).toHaveBeenCalledWith(true)
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
  })

  it('collapsible.disabled-noop', async () => {
    fixture(sharedCases, 'collapsible.disabled-noop')
    const onOpenChange = vi.fn()
    render(<Collapsible disabled onOpenChange={onOpenChange} title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    await userEvent.setup().click(trigger)
    expect(trigger).toBeDisabled()
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('collapsible.relationships', () => {
    fixture(sharedCases, 'collapsible.relationships')
    render(<Collapsible open title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    const content = document.getElementById(trigger.getAttribute('aria-controls')!)
    expect(trigger).toHaveAttribute('type', 'button')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(content).toHaveAttribute('role', 'region')
    expect(content).toHaveAttribute('aria-labelledby')
  })

  it('collapsible.header-action-isolation', async () => {
    fixture(sharedCases, 'collapsible.header-action-isolation')
    const onAction = vi.fn()
    const onOpenChange = vi.fn()
    render(
      <Collapsible
        headerActions={<button onClick={onAction} type="button">Pin</button>}
        onOpenChange={onOpenChange}
        title="Settings"
      >
        Content
      </Collapsible>,
    )
    await userEvent.setup().click(screen.getByRole('button', { name: 'Pin' }))
    expect(onAction).toHaveBeenCalledOnce()
    expect(onOpenChange).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: 'Settings' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('collapsible.subtitle', () => {
    fixture(sharedCases, 'collapsible.subtitle')
    render(<Collapsible subtitle="Optional configuration" title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    expect(trigger).toHaveAccessibleDescription('Optional configuration')
    expect(trigger).toContainElement(screen.getByText('Optional configuration'))
  })

  it('collapsible.host-attributes', () => {
    fixture(sharedCases, 'collapsible.host-attributes')
    const { container } = render(
      <Collapsible className="consumer" data-test="x" dir="rtl" id="settings" lang="ar" title="Settings" />,
    )
    const root = container.querySelector('.hl-collapsible')
    expect(root).toHaveClass('consumer')
    expect(root).toHaveAttribute('data-test', 'x')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toHaveAttribute('id', 'settings')
    expect(root).toHaveAttribute('lang', 'ar')
  })

  it('collapsible.title-required', () => {
    fixture(sharedCases, 'collapsible.title-required')
    expect(() => render(<Collapsible title="   " />)).toThrow('title-required')
  })

  it('collapsible.projection-equivalence', () => {
    fixture(sharedCases, 'collapsible.projection-equivalence')
    render(<Collapsible defaultOpen title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    const region = screen.getByRole('region', { name: 'Settings' })
    expect(trigger).toHaveAttribute('aria-controls', region.id)
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(region).toHaveTextContent('Content')
  })
})
