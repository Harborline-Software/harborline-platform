import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Collapsible } from '../Collapsible'
import { qualityCases } from './fixtures'

describe('Collapsible React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'collapsible.quality.relationships',
      'collapsible.quality.keyboard',
      'collapsible.quality.focus',
      'collapsible.quality.closed-focus',
      'collapsible.quality.reflow',
      'collapsible.quality.caller-content',
      'collapsible.quality.rtl',
      'collapsible.quality.pseudo',
      'collapsible.quality.light-dark',
      'collapsible.quality.tokens',
      'collapsible.quality.forced-colors',
      'collapsible.quality.reduced-motion',
      'collapsible.quality.visual-parity',
    ])
  })

  it.each(['{Enter}', ' '] as const)('uses native %s activation and retains trigger focus', async key => {
    const onOpenChange = vi.fn()
    render(<Collapsible onOpenChange={onOpenChange} title="Settings">Content</Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    trigger.focus()
    await userEvent.setup().keyboard(key)
    expect(onOpenChange).toHaveBeenCalledOnce()
    expect(trigger).toHaveFocus()
  })

  it('keeps the controlled content relationship in the DOM while closed and removes it from focus', () => {
    render(<Collapsible open={false} title="Settings"><button type="button">Save</button></Collapsible>)
    const trigger = screen.getByRole('button', { name: 'Settings' })
    const content = document.getElementById(trigger.getAttribute('aria-controls')!)
    expect(content).not.toBeNull()
    expect(content).toHaveAttribute('hidden')
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull()
  })

  it('exports only the bounded titled panel implementation', async () => {
    const publicModule = await import('../index')
    expect(Object.keys(publicModule)).toEqual(['Collapsible'])
    expect(publicModule).not.toHaveProperty('CollapsibleTrigger')
    expect(publicModule).not.toHaveProperty('CollapsibleContent')
    expect(publicModule).not.toHaveProperty('ExpansionPanel')
  })

  it('publishes reflow, logical, token, focus, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-collapsible-surface')
    expect(css).toMatch(/flex-wrap:\s*wrap/)
    expect(css).toContain('padding-inline')
    expect(css).toContain('margin-inline-start')
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
    expect(css).toContain("[data-theme='dark'] .hl-collapsible")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
