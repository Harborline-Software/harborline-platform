import { createRef } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { IconButton } from '../IconButton'

describe('IconButton React projection', () => {
  it.each(['{Enter}', ' '] as const)('activates exactly once with %s', async key => {
    const onClick = vi.fn()
    render(<IconButton aria-label="Open layers" onClick={onClick}>+</IconButton>)
    const button = screen.getByRole('button')
    button.focus()
    await userEvent.setup().keyboard(key)
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('retains native disabled behavior while loading without announcing a status', () => {
    const { container } = render(
      <IconButton aria-label="Open layers" loading aria-live="assertive">+</IconButton>,
    )
    const button = screen.getByRole('button')
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(container.querySelector('[role="status"]')).toBeNull()
    expect(container.querySelector('.hl-icon-button__loading-indicator')).toHaveAttribute('aria-hidden', 'true')
  })

  it('forwards the native ref and form binding', () => {
    const ref = createRef<HTMLButtonElement>()
    render(
      <form id="commands">
        <IconButton ref={ref} aria-label="Submit command" form="commands" type="submit">+</IconButton>
      </form>,
    )
    expect(ref.current).toBe(screen.getByRole('button'))
    expect(ref.current).toHaveAttribute('form', 'commands')
  })

  it('publishes resilient token, focus, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-icon-button-surface')
    expect(css).toContain('--hl-icon-button-focus')
    expect(css).toMatch(/\.hl-icon-button\s*\{[\s\S]*margin:\s*0;[\s\S]*padding:\s*0;/)
    expect(css).toContain(':active:not(:disabled)')
    expect(css).toContain("[data-theme='dark'] .hl-icon-button")
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion[\s\S]*animation: none/)
  })
})
