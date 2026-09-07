import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { LoadingState } from '../LoadingState'

describe('LoadingState React projection', () => {
  it('preserves pinned page-default and inline element semantics', () => {
    const { container, rerender } = render(<LoadingState label="Loading" />)
    expect(container.firstElementChild?.tagName).toBe('DIV')
    expect(screen.getByRole('status')).toHaveClass('hl-loading-state--page')

    rerender(<LoadingState label="Loading" variant="inline" />)
    expect(container.firstElementChild?.tagName).toBe('P')
    expect(screen.getByRole('status')).toHaveClass('hl-loading-state--inline')
  })

  it('exposes exactly one polite status with the caller-localized visible label', () => {
    render(<LoadingState label="جارٍ تحميل عمليات الفحص" lang="ar-SA" dir="rtl" />)
    const statuses = screen.getAllByRole('status')
    expect(statuses).toHaveLength(1)
    expect(statuses[0]).toHaveAttribute('aria-live', 'polite')
    expect(statuses[0]).toHaveAccessibleName('جارٍ تحميل عمليات الفحص')
    expect(statuses[0]).toHaveTextContent('جارٍ تحميل عمليات الفحص')
  })

  it('updates the same live region without remounting it', () => {
    const { rerender } = render(<LoadingState label="Loading inspections" />)
    const region = screen.getByRole('status')
    rerender(<LoadingState label="Loading attachments" />)
    expect(screen.getByRole('status')).toBe(region)
    expect(region).toHaveAccessibleName('Loading attachments')
  })

  it('forwards class, direction, locale, data, ARIA, and theme attributes', () => {
    render(
      <LoadingState
        label="Loading"
        className="consumer"
        lang="en-XA"
        dir="ltr"
        data-case="native"
        data-theme="dark"
        aria-atomic="true"
      />,
    )
    const status = screen.getByRole('status')
    expect(status).toHaveClass('hl-loading-state', 'consumer')
    expect(status).toHaveAttribute('lang', 'en-XA')
    expect(status).toHaveAttribute('dir', 'ltr')
    expect(status).toHaveAttribute('data-case', 'native')
    expect(status).toHaveAttribute('data-theme', 'dark')
    expect(status).toHaveAttribute('aria-atomic', 'true')
  })

  it('keeps status semantics and label naming authoritative over host attributes', () => {
    render(<LoadingState label="Loading" role="alert" aria-live="assertive" aria-label="Different" />)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Loading' })).toHaveAttribute('aria-live', 'polite')
  })

  it('rejects an empty effective label', () => {
    expect(() => render(<LoadingState label="  " />)).toThrow(/label-required/)
  })

  // The indicator is drawn by CSS, not rendered as an element. Two gallery tests assert this status
  // region holds no button, no [role=progressbar] and NO svg -- an aria-hidden SVG spinner still
  // failed them, because the component root IS the status region. So the region stays text-only and
  // the spinner is paint; this test guards that boundary rather than the presence of a node.
  it('carries no element inside the live status that could read as a control', () => {
    render(<LoadingState label="Loading" />)
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()
    const status = screen.getByRole('status')
    expect(status.querySelector('svg, button, [role="progressbar"]')).toBeNull()
    expect(screen.getAllByRole('status')).toHaveLength(1)
    expect(status).not.toHaveAttribute('tabindex')
  })
  it('publishes semantic tokens and host-context quality styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-loading-state-foreground')
    expect(css).toContain('block-size: 12rem')
    expect(css).toContain("[data-theme='dark'] .hl-loading-state")
    expect(css).toContain('text-align: start')
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toContain('.hl-loading-state__label')
  })
})
