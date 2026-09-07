import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Button, type ButtonIntent } from '../index'

function fixture(caseId: string) {
  const value = JSON.parse(process.env.HARBORLINE_CONFORMANCE_FIXTURE ?? 'null') as { id?: string; input?: unknown; expected?: unknown } | null
  expect(value?.id).toBe(caseId)
  expect(value?.input).toBeDefined()
  expect(value?.expected).toBeDefined()
  return value!
}

describe('Button shared conformance', () => {
  it('button.defaults', () => {
    fixture('button.defaults')
    render(<Button>Save</Button>)
    const button = screen.getByRole('button', { name: 'Save' })
    expect(button).toHaveAttribute('type', 'button')
    expect(button.className).toContain('bg-background')
  })

  it('button.activation', async () => {
    fixture('button.activation')
    const activation = vi.fn()
    render(<Button onClick={activation}>Save</Button>)
    await userEvent.setup().click(screen.getByRole('button'))
    expect(activation).toHaveBeenCalledTimes(1)
  })

  it('button.disabled', async () => {
    fixture('button.disabled')
    const activation = vi.fn()
    render(<Button disabled onClick={activation}>Save</Button>)
    const button = screen.getByRole('button')
    await userEvent.setup().click(button)
    expect(button).toBeDisabled()
    expect(activation).not.toHaveBeenCalled()
  })

  it('button.loading', async () => {
    fixture('button.loading')
    const activation = vi.fn()
    render(<Button loading onClick={activation}>Saving</Button>)
    const button = screen.getByRole('button')
    button.focus()
    await userEvent.setup().click(button)
    expect(button).toHaveFocus()
    expect(button).not.toBeDisabled()
    expect(button).toHaveAttribute('aria-busy', 'true')
    expect(button).toHaveAttribute('aria-disabled', 'true')
    expect(button).toHaveTextContent('Saving')
    expect(activation).not.toHaveBeenCalled()
  })

  it('button.form', () => {
    fixture('button.form')
    const { rerender } = render(<Button form="profile">Save</Button>)
    expect(screen.getByRole('button')).toHaveAttribute('type', 'button')
    expect(screen.getByRole('button')).toHaveAttribute('form', 'profile')
    for (const type of ['submit', 'reset'] as const) {
      rerender(<Button type={type} form="profile">Save</Button>)
      expect(screen.getByRole('button')).toHaveAttribute('type', type)
    }
  })

  it('button.content-order', () => {
    fixture('button.content-order')
    const { rerender } = render(<Button leadingIcon={<span>L</span>} trailingIcon={<span>T</span>}>Save</Button>)
    expect(screen.getByRole('button')).toHaveTextContent('LSaveT')
    rerender(<div dir="rtl"><Button leadingIcon={<span>L</span>} trailingIcon={<span>T</span>}>Save</Button></div>)
    expect(screen.getByRole('button')).toHaveTextContent('LSaveT')
  })

  it('button.accessible-name', () => {
    fixture('button.accessible-name')
    expect(() => render(<Button size="icon">+</Button>)).toThrow(/accessible-name-required/)
  })

  it('button.host-attributes', () => {
    fixture('button.host-attributes')
    render(<Button aria-controls="panel" data-case="shared" className="consumer" form="profile">Save</Button>)
    const button = screen.getByRole('button')
    expect(button).toHaveAttribute('aria-controls', 'panel')
    expect(button).toHaveAttribute('data-case', 'shared')
    expect(button).toHaveClass('consumer')
    expect(button).toHaveAttribute('form', 'profile')
  })

  it('button.appearance', () => {
    const contract = fixture('button.appearance')
    const intents = (contract.input as { intents: ButtonIntent[] }).intents
    const { rerender } = render(<Button intent={intents[0]}>Save</Button>)
    for (const intent of intents) {
      rerender(<Button intent={intent}>Save</Button>)
      expect(screen.getByRole('button').className.trim()).not.toBe('')
    }
  })
})
