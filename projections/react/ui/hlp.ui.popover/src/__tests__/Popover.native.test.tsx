import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Popover, PopoverAnchor, PopoverClose, PopoverContent, PopoverTrigger } from '../Popover'
import { qualityCases } from './fixtures'

describe('Popover React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'popover.quality.roles',
      'popover.quality.relationships',
      'popover.quality.keyboard',
      'popover.quality.focus',
      'popover.quality.reflow',
      'popover.quality.dismissal',
      'popover.quality.caller-copy',
      'popover.quality.rtl',
      'popover.quality.pseudo',
      'popover.quality.light-dark',
      'popover.quality.tokens',
      'popover.quality.forced-colors',
      'popover.quality.reduced-motion',
      'popover.quality.visual-parity',
    ])
  })

  it('preserves an asChild trigger and supports native keyboard activation', async () => {
    render(
      <Popover>
        <PopoverTrigger asChild><button data-consumer="trigger" type="button">Open</button></PopoverTrigger>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    const trigger = screen.getByRole('button', { name: 'Open' })
    expect(trigger).toHaveAttribute('data-consumer', 'trigger')
    expect(trigger).toHaveClass('hl-popover-trigger')
    trigger.focus()
    await userEvent.setup().keyboard('{Enter}')
    expect(screen.getByRole('dialog', { name: 'Details' })).toBeInTheDocument()
  })

  it('supports a menu role and caller-owned accessible name', () => {
    render(
      <Popover open>
        <PopoverTrigger asChild><button aria-haspopup="menu" type="button">Open</button></PopoverTrigger>
        <PopoverContent aria-label="User menu" role="menu"><button role="menuitem">Profile</button></PopoverContent>
      </Popover>,
    )
    expect(screen.getByRole('button', { name: 'Open' })).toHaveAttribute('aria-haspopup', 'menu')
    expect(screen.getByRole('menu', { name: 'User menu' })).toBeInTheDocument()
  })

  it('allows a consumer to cancel outside dismissal', () => {
    const onOpenChange = vi.fn()
    const onInteractOutside = vi.fn(event => event.preventDefault())
    render(
      <Popover defaultOpen onOpenChange={onOpenChange}>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details" onInteractOutside={onInteractOutside}>Body</PopoverContent>
      </Popover>,
    )
    fireEvent.pointerDown(document.body)
    expect(onInteractOutside).toHaveBeenCalledOnce()
    expect(screen.getByRole('dialog', { name: 'Details' })).toBeInTheDocument()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('treats the explicit anchor as inside the dismissal region', () => {
    const onOpenChange = vi.fn()
    render(
      <Popover defaultOpen onOpenChange={onOpenChange}>
        <PopoverAnchor><button type="button">Anchor action</button></PopoverAnchor>
        <PopoverContent aria-label="Details">Body</PopoverContent>
      </Popover>,
    )
    fireEvent.pointerDown(screen.getByRole('button', { name: 'Anchor action' }))
    expect(screen.getByRole('dialog', { name: 'Details' })).toBeInTheDocument()
    expect(onOpenChange).not.toHaveBeenCalled()
  })

  it('clones a close control without adding a nested button', async () => {
    render(
      <Popover defaultOpen>
        <PopoverTrigger>Open</PopoverTrigger>
        <PopoverContent aria-label="Details">
          <PopoverClose asChild><button data-consumer="close" type="button">Done</button></PopoverClose>
        </PopoverContent>
      </Popover>,
    )
    const close = screen.getByRole('button', { name: 'Done' })
    expect(close).toHaveAttribute('data-consumer', 'close')
    expect(close).toHaveClass('hl-popover-close')
    expect(close.querySelector('button')).toBeNull()
    await userEvent.setup().click(close)
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('publishes logical, token, theme, forced-color, reflow, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-popover-surface')
    expect(css).toContain('max-inline-size')
    expect(css).toContain('max-block-size')
    expect(css).toContain('overflow-wrap: anywhere')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-popover__content")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/transition:\s*none/)
    expect(css).toContain('.hl-popover-trigger:hover:not(:disabled)')
    expect(css).toContain('.hl-popover-trigger:active:not(:disabled)')
    expect(css).toContain('.hl-popover-trigger:disabled')
    expect(css).toContain('.hl-popover-trigger:focus-visible')
  })
})
