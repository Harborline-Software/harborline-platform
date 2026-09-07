import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { EmptyState, type EmptyStateAction, type EmptyStateVariant } from '../EmptyState'

describe('EmptyState React projection', () => {
  it.each([
    ['informational', 'info', 'muted'],
    ['positive', 'circle-check', 'success'],
    ['actionable', 'circle-plus', 'muted'],
  ] as const)('maps %s to its decorative semantic icon and tone', (variant, iconName, tone) => {
    const { container } = render(<EmptyState variant={variant} title="Nothing here" />)
    const icon = container.querySelector('[data-hl-icon]')
    expect(icon).toHaveAttribute('data-hl-icon', iconName)
    expect(icon).toHaveAttribute('data-hl-tone', tone)
    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect(icon).toHaveAttribute('focusable', 'false')
    expect(icon).not.toHaveAttribute('role')
    expect(icon).not.toHaveAttribute('aria-label')
    expect(icon?.querySelector('title')).toBeNull()
  })

  it('renders title and optional caller-localized description as plain paragraph text', () => {
    const { container, rerender } = render(
      <EmptyState
        variant="informational"
        title="لا توجد نتائج"
        description="غيّر عوامل التصفية وحاول مرة أخرى."
      />,
    )
    expect(screen.getByText('لا توجد نتائج').tagName).toBe('P')
    expect(screen.getByText('غيّر عوامل التصفية وحاول مرة أخرى.').tagName).toBe('P')
    expect(container.querySelectorAll('h1, h2, h3, h4, h5, h6')).toHaveLength(0)

    rerender(<EmptyState variant="informational" title="No results" description="   " />)
    expect(container.querySelector('.hl-empty-state__description')).toBeNull()
  })

  it.each(['{Enter}', ' '])('uses a native non-submit action and activates once with %s', async key => {
    const onClick = vi.fn()
    render(
      <EmptyState
        variant="positive"
        title="No conflicts"
        action={{ label: 'Refresh', onClick }}
      />,
    )
    const action = screen.getByRole('button', { name: 'Refresh' })
    expect(action.tagName).toBe('BUTTON')
    expect(action).toHaveAttribute('type', 'button')
    action.focus()
    await userEvent.setup().keyboard(key)
    expect(onClick).toHaveBeenCalledTimes(1)
    expect(action).toHaveFocus()
  })

  it('allows an action under every variant without changing activation behavior', async () => {
    const variants: EmptyStateVariant[] = ['informational', 'positive', 'actionable']
    const user = userEvent.setup()
    for (const variant of variants) {
      const onClick = vi.fn()
      const rendered = render(
        <EmptyState variant={variant} title="Empty" action={{ label: 'Act', onClick }} />,
      )
      await user.click(screen.getByRole('button', { name: 'Act' }))
      expect(onClick).toHaveBeenCalledTimes(1)
      rendered.unmount()
    }
  })

  it('owns no announcement semantics or focus movement', () => {
    const { container } = render(<EmptyState variant="informational" title="No results" />)
    const root = container.firstElementChild
    expect(root).not.toHaveAttribute('role')
    expect(root).not.toHaveAttribute('aria-live')
    expect(root).not.toHaveAttribute('aria-atomic')
    expect(root).not.toHaveAttribute('tabindex')
    expect(document.activeElement).toBe(document.body)
  })

  it('allows a host to provide announcement semantics without adding another live region', () => {
    render(
      <section role="status" aria-live="polite">
        <EmptyState variant="informational" title="No results" />
      </section>,
    )
    expect(screen.getAllByRole('status')).toHaveLength(1)
    const root = screen.getByText('No results').closest('.hl-empty-state')
    expect(root).not.toHaveAttribute('role')
    expect(root).not.toHaveAttribute('aria-live')
  })

  it('forwards host class, locale, direction, data, ARIA, theme, and role attributes', () => {
    render(
      <EmptyState
        variant="informational"
        title="لا توجد نتائج"
        className="consumer-empty"
        lang="ar-SA"
        dir="rtl"
        data-case="native"
        data-theme="dark"
        aria-describedby="empty-help"
        role="status"
      />,
    )
    const root = screen.getByRole('status')
    expect(root).toHaveClass('hl-empty-state', 'consumer-empty')
    expect(root).toHaveAttribute('lang', 'ar-SA')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toHaveAttribute('data-case', 'native')
    expect(root).toHaveAttribute('data-theme', 'dark')
    expect(root).toHaveAttribute('aria-describedby', 'empty-help')
  })

  it('keeps the component-owned variant authoritative over a host collision', () => {
    render(
      <EmptyState
        variant="positive"
        title="No conflicts"
        {...{ 'data-hl-variant': 'host-value' }}
      />,
    )
    expect(screen.getByText('No conflicts').closest('.hl-empty-state')).toHaveAttribute(
      'data-hl-variant',
      'positive',
    )
  })

  it('rejects blank titles and partially supplied actions deterministically', () => {
    expect(() => render(<EmptyState variant="informational" title="  " />)).toThrow(/title-required/)
    expect(() => render(
      <EmptyState
        variant="actionable"
        title="Empty"
        action={{ label: 'Act' } as EmptyStateAction}
      />,
    )).toThrow(/action-incomplete/)
    expect(() => render(
      <EmptyState
        variant="actionable"
        title="Empty"
        action={{ label: ' ', onClick: () => undefined }}
      />,
    )).toThrow(/action-incomplete/)
  })

  it('publishes shared theme tokens and resilient host-context styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-empty-state-foreground')
    expect(css).toContain('--hl-empty-state-muted')
    expect(css).toContain('--hl-empty-state-positive')
    expect(css).toContain("[data-theme='dark'] .hl-empty-state")
    expect(css).toContain('text-align: center')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
    expect(css).not.toMatch(/animation(?:-name)?:/)
  })
})
