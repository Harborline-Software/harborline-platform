import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Accordion, type AccordionItem } from '../Accordion'
import { fixture, qualityCases } from './fixtures'

const items: AccordionItem[] = [
  { value: 'a', title: 'Alpha', children: 'Alpha panel' },
  { value: 'b', title: 'Beta', children: 'Beta panel' },
]

describe('Accordion React projection', () => {
  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'accordion.quality.relationships',
      'accordion.quality.keyboard',
      'accordion.quality.focus',
      'accordion.quality.reflow',
      'accordion.quality.caller-content',
      'accordion.quality.rtl',
      'accordion.quality.pseudo',
      'accordion.quality.light-dark',
      'accordion.quality.tokens',
      'accordion.quality.forced-colors',
      'accordion.quality.reduced-motion',
      'accordion.quality.visual-parity',
    ])
  })

  it('emits controlled multiple snapshots in logical item order', async () => {
    const onValueChange = vi.fn()
    render(<Accordion items={items} type="multiple" value={['b']} onValueChange={onValueChange} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Alpha' }))
    expect(onValueChange).toHaveBeenCalledWith(['a', 'b'])
  })

  it('keeps instance relationships unique for repeated item values', () => {
    render(<><Accordion items={items} /><Accordion items={items} /></>)
    const alphaButtons = screen.getAllByRole('button', { name: 'Alpha' })
    expect(alphaButtons[0]?.id).not.toBe(alphaButtons[1]?.id)
    for (const button of alphaButtons) {
      expect(document.getElementById(button.getAttribute('aria-controls')!)).toHaveAttribute(
        'aria-labelledby',
        button.id,
      )
    }
  })

  it('handles an all-disabled noncollapsible group without opening an item', () => {
    render(<Accordion collapsible={false} items={items.map(item => ({ ...item, disabled: true }))} />)
    expect(screen.getAllByRole('button').every(button => button.getAttribute('aria-expanded') === 'false')).toBe(true)
  })

  it('retains caller-owned rich heading and panel content', () => {
    fixture(qualityCases, 'accordion.quality.caller-content')
    render(
      <Accordion items={[{
        value: 'rich',
        title: <span lang="fr">Équipement critique</span>,
        children: <strong>Inspection requise</strong>,
      }]} defaultValue={['rich']} />,
    )
    expect(screen.getByRole('button', { name: 'Équipement critique' })).toBeInTheDocument()
    expect(screen.getByText('Inspection requise')).toBeVisible()
  })

  it('publishes logical, tokenized, forced-color, focus, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-accordion-surface')
    expect(css).toContain('border-block-start')
    expect(css).toContain('padding-inline')
    expect(css).toContain("[data-theme='dark'] .hl-accordion")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/:focus-visible\s*\{[\s\S]*outline: 2px/)
  })
})
