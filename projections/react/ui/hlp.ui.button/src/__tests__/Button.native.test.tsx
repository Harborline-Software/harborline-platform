import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { CSSProperties } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { Button, HarborlineLocaleProvider } from '../index'

describe('Button', () => {
  it('renders without crash', () => {
    render(<Button>Click me</Button>)
    expect(screen.getByRole('button', { name: 'Click me' })).toBeInTheDocument()
  })

  it('renders children text', () => {
    render(<Button>Submit</Button>)
    expect(screen.getByText('Submit')).toBeInTheDocument()
  })

  it('applies primary variant class', () => {
    render(<Button variant="primary">Primary</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — bg-primary replaces bg-blue-600
    expect(btn).toHaveClass('bg-primary')
  })

  it('applies secondary variant class by default', () => {
    render(<Button>Secondary</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — bg-background replaces bg-white
    expect(btn).toHaveClass('bg-background')
  })

  it('applies destructive variant class', () => {
    render(<Button variant="destructive">Delete</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — bg-destructive replaces bg-red-600
    expect(btn).toHaveClass('bg-destructive')
  })

  it('applies ghost variant class', () => {
    render(<Button variant="ghost">Ghost</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('bg-transparent')
  })

  it('defaults to type="button"', () => {
    render(<Button>Click</Button>)
    expect(screen.getByRole('button')).toHaveAttribute('type', 'button')
  })

  it('accepts type="submit"', () => {
    render(<Button type="submit">Submit</Button>)
    expect(screen.getByRole('button')).toHaveAttribute('type', 'submit')
  })

  it('disabled prevents click', async () => {
    const user = userEvent.setup()
    const onClick = vi.fn()
    render(
      <Button disabled onClick={onClick}>
        Disabled
      </Button>,
    )
    await user.click(screen.getByRole('button'))
    expect(onClick).not.toHaveBeenCalled()
  })

  it('disabled applies disabled attribute', () => {
    render(<Button disabled>Disabled</Button>)
    expect(screen.getByRole('button')).toBeDisabled()
  })

  it('loading shows spinner status element', () => {
    render(<Button loading>Saving</Button>)
    expect(screen.getByRole('status', { name: 'Loading' })).toHaveTextContent('Loading')
  })

  it('loading remains focusable while exposing an inert state', () => {
    render(<Button loading>Saving</Button>)
    expect(screen.getByRole('button')).not.toBeDisabled()
    expect(screen.getByRole('button')).toHaveAttribute('aria-disabled', 'true')
  })

  it('loading exposes aria-busy state', () => {
    render(<Button loading>Save</Button>)
    expect(screen.getByRole('button', { name: 'Save' })).toHaveAttribute('aria-busy', 'true')
  })

  it('loading still shows children text', () => {
    render(<Button loading>Saving…</Button>)
    expect(screen.getByText('Saving…')).toBeInTheDocument()
  })

  it('asChild renders children element instead of button', () => {
    render(
      <Button asChild variant="tertiary">
        <a href="#">Link</a>
      </Button>,
    )
    expect(screen.getByRole('link', { name: 'Link' })).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('renders leadingIcon when not loading', () => {
    render(<Button leadingIcon={<span data-testid="lead-icon">+</span>}>Add</Button>)
    expect(screen.getByTestId('lead-icon')).toBeInTheDocument()
  })

  it('replaces leadingIcon with spinner when loading', () => {
    render(
      <Button loading leadingIcon={<span data-testid="lead-icon">+</span>}>
        Add
      </Button>,
    )
    expect(screen.queryByTestId('lead-icon')).not.toBeInTheDocument()
    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument()
  })

  it('localizes its owned loading status through the public locale catalog', () => {
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'common.loading': 'جارٍ التحميل' }}>
        <Button loading>جارٍ الحفظ</Button>
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('status', { name: 'جارٍ التحميل' })).toHaveTextContent('جارٍ التحميل')
  })

  it('propagates locale language and direction to the public button element', () => {
    render(
      <HarborlineLocaleProvider locale="ar-SA">
        <Button>حفظ</Button>
      </HarborlineLocaleProvider>,
    )
    expect(screen.getByRole('button', { name: 'حفظ' })).toHaveAttribute('lang', 'ar-SA')
    expect(screen.getByRole('button', { name: 'حفظ' })).toHaveAttribute('dir', 'rtl')
  })

  it.each(['{Enter}', ' '])('activates once from the keyboard with %s and keeps focus', async key => {
    const user = userEvent.setup()
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Run</Button>)
    const button = screen.getByRole('button', { name: 'Run' })
    button.focus()
    await user.keyboard(key)
    expect(onClick).toHaveBeenCalledTimes(1)
    expect(button).toHaveFocus()
  })

  it('renders trailingIcon', () => {
    render(<Button trailingIcon={<span data-testid="trail-icon">→</span>}>Next</Button>)
    expect(screen.getByTestId('trail-icon')).toBeInTheDocument()
  })

  it('applies tertiary variant class', () => {
    render(<Button variant="tertiary">Tertiary</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — text-primary replaces text-blue-600
    expect(btn).toHaveClass('text-primary')
  })

  it('fires onClick when enabled', async () => {
    const user = userEvent.setup()
    const onClick = vi.fn()
    render(<Button onClick={onClick}>Click</Button>)
    await user.click(screen.getByRole('button'))
    expect(onClick).toHaveBeenCalledTimes(1)
  })

  it('loading suppresses onClick', async () => {
    const user = userEvent.setup()
    const onClick = vi.fn()
    render(
      <Button loading onClick={onClick}>
        Saving
      </Button>,
    )
    await user.click(screen.getByRole('button'))
    expect(onClick).not.toHaveBeenCalled()
  })

  it('merges custom className', () => {
    render(<Button className="my-custom-class">Styled</Button>)
    expect(screen.getByRole('button')).toHaveClass('my-custom-class')
  })

  it('passes through aria-label attribute', () => {
    render(
      <Button aria-label="Close dialog" size="icon">
        ✕
      </Button>,
    )
    expect(screen.getByRole('button', { name: 'Close dialog' })).toBeInTheDocument()
  })

  it('rejects unnamed icon-only use', () => {
    expect(() => render(<Button size="icon">✕</Button>)).toThrow(/accessible-name-required/)
  })

  it('maps every canonical intent to a non-empty appearance', () => {
    const intents = [
      'primary', 'secondary', 'danger', 'warning', 'info',
      'success', 'light', 'dark', 'subtle', 'transparent',
    ] as const
    const { rerender } = render(<Button intent={intents[0]}>Intent</Button>)
    for (const intent of intents) {
      rerender(<Button intent={intent}>Intent</Button>)
      expect(screen.getByRole('button').className.trim()).not.toBe('')
    }
  })

  it('trailingIcon remains visible during loading', () => {
    render(
      <Button loading trailingIcon={<span data-testid="trail-icon">→</span>}>
        Saving
      </Button>,
    )
    expect(screen.getByTestId('trail-icon')).toBeInTheDocument()
  })

  it('accepts type="reset"', () => {
    render(<Button type="reset">Reset</Button>)
    expect(screen.getByRole('button')).toHaveAttribute('type', 'reset')
  })

  it('asChild does not forward type prop', () => {
    render(
      <Button asChild type="submit">
        <a href="#">Link</a>
      </Button>,
    )
    // Slot renders an <a>; the type prop must not be forwarded
    expect(screen.getByRole('link')).not.toHaveAttribute('type', 'submit')
  })

  it('applies size="icon" square class', () => {
    render(
      <Button size="icon" aria-label="Delete">
        ✕
      </Button>,
    )
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('w-10')
  })

  it('emits stable visual axes for package CSS consumers', () => {
    render(<Button size="lg" fillMode="outline" themeColor="success" rounded="full">Ship</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveAttribute('data-hl-intent', 'success')
    expect(btn).toHaveAttribute('data-hl-fill', 'outline')
    expect(btn).toHaveAttribute('data-hl-rounded', 'full')
    expect(btn).toHaveAttribute('data-hl-size', 'lg')
  })

  it('accepts public theme tokens and theme metadata through host attributes', () => {
    const style = { '--hl-button-primary': '#123456' } as CSSProperties
    render(<Button intent="primary" style={style} data-theme="dark">Ship</Button>)
    const button = screen.getByRole('button')
    expect(button).toHaveStyle({ '--hl-button-primary': '#123456' })
    expect(button).toHaveAttribute('data-theme', 'dark')
  })

  // §appearance-axes — fillMode
  it('applies solid fillMode with primary themeColor gives primary background', () => {
    render(<Button fillMode="solid" themeColor="primary">Primary Solid</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — bg-primary replaces bg-blue-600
    expect(btn).toHaveClass('bg-primary')
  })

  it('applies flat fillMode removing background', () => {
    render(<Button variant="primary" fillMode="flat">Flat</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('bg-transparent')
  })

  it('applies outline fillMode with border', () => {
    render(<Button variant="primary" fillMode="outline">Outline</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('border')
    expect(btn).toHaveClass('bg-transparent')
  })

  it('applies link fillMode', () => {
    render(<Button variant="primary" fillMode="link">Link</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('bg-transparent')
  })

  it('applies clear fillMode', () => {
    render(<Button variant="primary" fillMode="clear">Clear</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('bg-transparent')
  })

  // §appearance-axes — themeColor
  it('applies info themeColor', () => {
    render(<Button themeColor="info">Info</Button>)
    const btn = screen.getByRole('button')
    // Wave 4: tokenized — bg-accent-brand replaces bg-blue-500
    expect(btn).toHaveClass('bg-accent-brand')
  })

  it('applies success themeColor', () => {
    render(<Button themeColor="success">Success</Button>)
    const btn = screen.getByRole('button')
    // Wave 4: tokenized — bg-success replaces bg-green-500
    expect(btn).toHaveClass('bg-success')
  })

  it('applies warning themeColor', () => {
    render(<Button themeColor="warning">Warning</Button>)
    const btn = screen.getByRole('button')
    // Wave 4: tokenized — bg-warning replaces bg-yellow-500
    expect(btn).toHaveClass('bg-warning')
  })

  it('applies error themeColor', () => {
    render(<Button themeColor="error">Error</Button>)
    const btn = screen.getByRole('button')
    // Wave 3: tokenized — bg-destructive replaces bg-red-600
    expect(btn).toHaveClass('bg-destructive')
  })

  // §appearance-axes — rounded
  it('applies rounded=small class', () => {
    render(<Button rounded="small">Small</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('rounded-sm')
  })

  it('applies rounded=medium class', () => {
    render(<Button rounded="medium">Medium</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('rounded-md')
  })

  it('applies rounded=large class', () => {
    render(<Button rounded="large">Large</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('rounded-lg')
  })

  it('applies rounded=full pill class', () => {
    render(<Button rounded="full">Pill</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('rounded-full')
  })

  it('applies rounded=none class', () => {
    render(<Button rounded="none">None</Button>)
    const btn = screen.getByRole('button')
    expect(btn).toHaveClass('rounded-none')
  })
})
