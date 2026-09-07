import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ErrorCard } from '../ErrorCard'

describe('ErrorCard React projection', () => {
  it('preserves the pinned default, page, and compact title semantics', () => {
    const { rerender } = render(<ErrorCard title="Failure" />)
    expect(screen.getByText('Failure').tagName).toBe('P')
    expect(screen.getByRole('alert')).toHaveAttribute('data-hl-variant', 'default')

    rerender(<ErrorCard title="Failure" variant="page" />)
    expect(screen.getByRole('heading', { level: 2, name: 'Failure' })).toBeInTheDocument()

    rerender(<ErrorCard title="Failure" variant="compact" />)
    expect(screen.queryByRole('heading')).not.toBeInTheDocument()
    expect(screen.getByText('Failure').tagName).toBe('P')
  })

  it('omits nullish and empty messages while preserving non-empty caller text', () => {
    const { rerender } = render(<ErrorCard title="Failure" message="Connection lost" />)
    expect(screen.getByText('Connection lost')).toBeInTheDocument()
    rerender(<ErrorCard title="Failure" />)
    expect(screen.queryByText('Connection lost')).not.toBeInTheDocument()
    rerender(<ErrorCard title="Failure" message="" />)
    expect(document.querySelector('.hl-error-card__message')).toBeNull()
  })

  it('uses the source-compatible English retry fallback and a caller override', () => {
    const { rerender } = render(<ErrorCard title="Failure" onRetry={() => undefined} />)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    rerender(<ErrorCard title="Échec" onRetry={() => undefined} retryLabel="Réessayer" lang="fr-CA" />)
    expect(screen.getByRole('button', { name: 'Réessayer' })).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveAttribute('lang', 'fr-CA')
  })

  it('falls back to English when the caller override is blank', () => {
    render(<ErrorCard title="Failure" onRetry={() => undefined} retryLabel="   " />)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('uses a non-submit native button and invokes retry once', async () => {
    const onRetry = vi.fn()
    render(<ErrorCard title="Failure" onRetry={onRetry} />)
    const retry = screen.getByRole('button')
    expect(retry).toHaveAttribute('type', 'button')
    expect(retry).toHaveAttribute('data-hl-tone', 'destructive')
    await userEvent.setup().click(retry)
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it.each(['{Enter}', ' '])('activates retry once with the %s key and retains focus', async key => {
    const onRetry = vi.fn()
    render(<ErrorCard title="Failure" onRetry={onRetry} />)
    const retry = screen.getByRole('button')
    retry.focus()
    await userEvent.setup().keyboard(key)
    expect(onRetry).toHaveBeenCalledTimes(1)
    expect(retry).toHaveFocus()
  })

  it('forwards class, locale, direction, data, ARIA, and theme attributes', () => {
    render(
      <ErrorCard
        title="تعذر التحميل"
        className="consumer"
        lang="ar-SA"
        dir="rtl"
        data-case="native"
        data-theme="dark"
        aria-describedby="details"
      />,
    )
    const alert = screen.getByRole('alert')
    expect(alert).toHaveClass('hl-error-card', 'consumer')
    expect(alert).toHaveAttribute('lang', 'ar-SA')
    expect(alert).toHaveAttribute('dir', 'rtl')
    expect(alert).toHaveAttribute('data-case', 'native')
    expect(alert).toHaveAttribute('data-theme', 'dark')
    expect(alert).toHaveAttribute('aria-describedby', 'details')
  })

  it('keeps alert semantics authoritative over host role attributes', () => {
    render(<ErrorCard title="Failure" role="status" />)
    expect(screen.getByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('rejects an empty effective title', () => {
    expect(() => render(<ErrorCard title="  " />)).toThrow(/title-required/)
  })

  it('publishes semantic tokens and host-context quality styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-error-card-surface')
    expect(css).toMatch(/--hl-error-card-retry:\s*var\(--hl-danger/)
    expect(css).toContain("[data-theme='dark'] .hl-error-card")
    expect(css).toContain('text-align: start')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
  })
})
