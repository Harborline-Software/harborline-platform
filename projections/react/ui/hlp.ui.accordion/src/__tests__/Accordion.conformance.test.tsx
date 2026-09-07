import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Accordion, type AccordionItem } from '../Accordion'
import { fixture, sharedCases } from './fixtures'

const items: AccordionItem[] = [
  { value: 'a', title: 'Section A', children: 'Content A' },
  { value: 'b', title: 'Section B', children: 'Content B' },
  { value: 'c', title: 'Section C', children: 'Content C' },
]

const trigger = (name: string) => screen.getByRole('button', { name })

describe('Accordion revision-1 shared fixtures', () => {
  it('consumes every frozen neutral case', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'accordion.single.initial',
      'accordion.single.noncollapsible',
      'accordion.multiple.independent',
      'accordion.controlled-change',
      'accordion.disabled-noop',
      'accordion.invalid-defaults',
      'accordion.keyboard-next-previous',
      'accordion.keyboard-boundaries',
      'accordion.relationships',
      'accordion.host-attributes',
      'accordion.projection-equivalence',
      'accordion.empty',
      'accordion.blankEmptyText',
    ])
  })

  it('accordion.empty', () => {
    const value = fixture(sharedCases, 'accordion.empty')
    const expected = value.expected as { emptyText: string; defaultEmptyText: string; headers: number }
    const noItems = value.input.items as AccordionItem[]
    const { rerender } = render(<Accordion empty={value.input.empty as string} items={noItems} />)
    expect(screen.getByText(expected.emptyText)).toHaveClass('hl-accordion__empty')
    expect(screen.queryAllByRole('button')).toHaveLength(expected.headers)
    rerender(<Accordion items={noItems} />)
    expect(screen.getByText(expected.defaultEmptyText)).toHaveAttribute('data-hl-empty', 'true')
  })

  it('accordion.blankEmptyText', () => {
    const value = fixture(sharedCases, 'accordion.blankEmptyText')
    const expected = value.expected as { defaultEmptyText: string; headers: number }
    const noItems = value.input.items as AccordionItem[]
    render(<Accordion empty={value.input.empty as string} items={noItems} />)
    expect(screen.getByText(expected.defaultEmptyText)).toHaveAttribute('data-hl-empty', 'true')
    expect(screen.queryAllByRole('button')).toHaveLength(expected.headers)
  })

  it('accordion.single.initial', () => {
    const value = fixture(sharedCases, 'accordion.single.initial')
    render(<Accordion items={items.slice(0, 2)} defaultValue={value.input.defaultExpanded as string[]} />)
    expect(trigger('Section A')).toHaveAttribute('aria-expanded', 'true')
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'false')
  })

  it('accordion.single.noncollapsible', async () => {
    fixture(sharedCases, 'accordion.single.noncollapsible')
    const onValueChange = vi.fn()
    render(
      <Accordion
        collapsible={false}
        items={[{ ...items[0]!, disabled: true }, items[1]!]}
        onValueChange={onValueChange}
      />,
    )
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'true')
    await userEvent.setup().click(trigger('Section B'))
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'true')
    expect(onValueChange).not.toHaveBeenCalled()
  })

  it('accordion.multiple.independent', async () => {
    fixture(sharedCases, 'accordion.multiple.independent')
    const user = userEvent.setup()
    render(<Accordion items={items} type="multiple" />)
    await user.click(trigger('Section A'))
    await user.click(trigger('Section B'))
    await user.click(trigger('Section A'))
    expect(trigger('Section A')).toHaveAttribute('aria-expanded', 'false')
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'true')
  })

  it('accordion.controlled-change', async () => {
    fixture(sharedCases, 'accordion.controlled-change')
    const onValueChange = vi.fn()
    render(<Accordion items={items} value={['a']} onValueChange={onValueChange} />)
    await userEvent.setup().click(trigger('Section B'))
    expect(trigger('Section A')).toHaveAttribute('aria-expanded', 'true')
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'false')
    expect(onValueChange).toHaveBeenCalledOnce()
    expect(onValueChange).toHaveBeenCalledWith(['b'])
  })

  it('accordion.disabled-noop', async () => {
    fixture(sharedCases, 'accordion.disabled-noop')
    const onValueChange = vi.fn()
    render(<Accordion items={[items[0]!, { ...items[1]!, disabled: true }]} value={[]} onValueChange={onValueChange} />)
    await userEvent.setup().click(trigger('Section B'))
    expect(trigger('Section B')).toBeDisabled()
    expect(trigger('Section B')).toHaveAttribute('aria-expanded', 'false')
    expect(onValueChange).not.toHaveBeenCalled()
  })

  it('accordion.invalid-defaults', () => {
    fixture(sharedCases, 'accordion.invalid-defaults')
    render(
      <Accordion
        defaultValue={['missing', 'disabled']}
        items={[{ value: 'disabled', title: 'Disabled', children: 'No', disabled: true }, items[0]!]}
        type="multiple"
      />,
    )
    expect(trigger('Disabled')).toHaveAttribute('aria-expanded', 'false')
    expect(trigger('Section A')).toHaveAttribute('aria-expanded', 'false')
  })

  it('accordion.keyboard-next-previous', async () => {
    fixture(sharedCases, 'accordion.keyboard-next-previous')
    const user = userEvent.setup()
    render(<Accordion items={[items[0]!, { ...items[1]!, disabled: true }, items[2]!]} />)
    trigger('Section A').focus()
    await user.keyboard('{ArrowDown}')
    expect(trigger('Section C')).toHaveFocus()
    await user.keyboard('{ArrowUp}')
    expect(trigger('Section A')).toHaveFocus()
  })

  it('accordion.keyboard-boundaries', async () => {
    fixture(sharedCases, 'accordion.keyboard-boundaries')
    const user = userEvent.setup()
    render(<Accordion items={[items[0]!, { ...items[1]!, disabled: true }, items[2]!]} />)
    trigger('Section A').focus()
    await user.keyboard('{End}')
    expect(trigger('Section C')).toHaveFocus()
    await user.keyboard('{ArrowDown}')
    expect(trigger('Section A')).toHaveFocus()
    await user.keyboard('{Home}')
    expect(trigger('Section A')).toHaveFocus()
    await user.keyboard('{ArrowUp}')
    expect(trigger('Section C')).toHaveFocus()
  })

  it('accordion.relationships', () => {
    fixture(sharedCases, 'accordion.relationships')
    render(<Accordion items={items.slice(0, 1)} defaultValue={['a']} />)
    const button = trigger('Section A')
    const panel = document.getElementById(button.getAttribute('aria-controls')!)
    expect(button).toHaveAttribute('type', 'button')
    expect(button).toHaveAttribute('aria-expanded', 'true')
    expect(panel).toHaveAttribute('role', 'region')
    expect(panel).toHaveAttribute('aria-labelledby', button.id)
  })

  it('accordion.host-attributes', () => {
    fixture(sharedCases, 'accordion.host-attributes')
    render(<Accordion className="consumer" dir="rtl" items={items} lang="ar" data-case="accordion" />)
    const root = trigger('Section A').closest('.hl-accordion')
    expect(root).toHaveClass('consumer')
    expect(root).toHaveAttribute('lang', 'ar')
    expect(root).toHaveAttribute('dir', 'rtl')
    expect(root).toHaveAttribute('data-case', 'accordion')
  })

  it('accordion.projection-equivalence', () => {
    fixture(sharedCases, 'accordion.projection-equivalence')
    render(<Accordion items={items} type="multiple" value={['a', 'c']} />)
    expect(screen.getAllByRole('button').map(button => button.getAttribute('aria-expanded'))).toEqual([
      'true', 'false', 'true',
    ])
    expect(document.querySelectorAll('[role="region"]')).toHaveLength(3)
  })
})
