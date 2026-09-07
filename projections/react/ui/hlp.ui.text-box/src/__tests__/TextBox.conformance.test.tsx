import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { TextBox } from '../TextBox'
import { sharedCases } from './fixtures'

interface ContractDocument { cases: Array<{ id: string }> }
const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../specs/modules/ui/hlp.ui.text-box/interface.yaml'), 'utf8')) as ContractDocument

describe('TextBox revision-1 shared fixtures', () => {
  it('consumes every behavior case in frozen contract order', () => {
    expect(sharedCases.map(value => value.id)).toEqual(contract.cases.map(value => value.id))
  })

  it('supports exact controlled requests and external replacement', () => {
    const changed = vi.fn()
    const rendered = render(<TextBox aria-label="Query" onChange={changed} value="alpha" />)
    const input = screen.getByRole('textbox')
    fireEvent.change(input, { target: { value: 'beta' } })
    expect(changed).toHaveBeenCalledWith('beta')
    expect(input).toHaveValue('alpha')
    rendered.rerender(<TextBox aria-label="Query" onChange={changed} value="omega" />)
    expect(input).toHaveValue('omega')
  })

  it('supports an uncontrolled default initialized once', () => {
    const changed = vi.fn()
    const rendered = render(<TextBox aria-label="Query" defaultValue="alpha" onChange={changed} />)
    const input = screen.getByRole('textbox')
    fireEvent.change(input, { target: { value: 'beta' } })
    expect(input).toHaveValue('beta')
    expect(changed).toHaveBeenCalledWith('beta')
    rendered.rerender(<TextBox aria-label="Query" defaultValue="omega" onChange={changed} />)
    expect(input).toHaveValue('beta')
  })

  it('shows clear only for enabled nonempty values and preserves controlled ownership', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    const rendered = render(<TextBox aria-label="Query" clearButton onChange={changed} value="alpha" />)
    const input = screen.getByRole('textbox')
    await user.click(screen.getByRole('button', { name: 'Clear' }))
    expect(changed).toHaveBeenCalledWith('')
    expect(input).toHaveValue('alpha')
    expect(input).toHaveFocus()
    rendered.rerender(<TextBox aria-label="Query" clearButton disabled onChange={changed} value="alpha" />)
    expect(screen.queryByRole('button', { name: 'Clear' })).not.toBeInTheDocument()
  })

  it('toggles password visibility without changing value and resolves instance labels', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(
      <TextBox
        aria-label="Password"
        hidePasswordLabel="Conceal"
        onChange={changed}
        showPasswordLabel="Reveal"
        showReveal
        type="password"
        value="secret"
      />,
    )
    const input = document.querySelector('input')!
    expect(input).toHaveAttribute('type', 'password')
    await user.click(screen.getByRole('button', { name: 'Reveal' }))
    expect(input).toHaveAttribute('type', 'text')
    expect(input).toHaveValue('secret')
    expect(input).toHaveFocus()
    expect(screen.getByRole('button', { name: 'Conceal' })).toHaveAttribute('aria-pressed', 'true')
    await user.click(screen.getByRole('button', { name: 'Conceal' }))
    expect(input).toHaveAttribute('type', 'password')
    expect(changed).not.toHaveBeenCalled()
  })

  it('removes reveal behavior while disabled', () => {
    render(<TextBox aria-label="Password" disabled showReveal type="password" value="secret" />)
    expect(document.querySelector('input')).toHaveAttribute('type', 'password')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('merges FormField metadata with explicit precedence and keeps host attributes on the input', () => {
    const onKeyDown = vi.fn()
    render(
      <FormFieldContext.Provider value={{ id: 'ambient-id', labelId: 'ambient-label', describedBy: 'ambient-help', required: true, disabled: true }}>
        <span id="explicit-label">Search</span>
        <TextBox
          aria-describedby="explicit-help"
          aria-labelledby="explicit-label"
          autoComplete="off"
          disabled={false}
          error
          id="query"
          name="query"
          onKeyDown={onKeyDown}
          placeholder="Search"
          required={false}
          tabIndex={2}
        />
      </FormFieldContext.Provider>,
    )
    const input = screen.getByRole('textbox', { name: 'Search' })
    expect(input).toHaveAttribute('id', 'query')
    expect(input).toHaveAttribute('name', 'query')
    expect(input).toHaveAttribute('autocomplete', 'off')
    expect(input).toHaveAttribute('placeholder', 'Search')
    expect(input).toHaveAttribute('tabindex', '2')
    expect(input).toHaveAttribute('aria-describedby', 'explicit-help')
    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).not.toBeDisabled()
    expect(input).not.toBeRequired()
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(onKeyDown).toHaveBeenCalledOnce()
  })

  it('orders prefix, input, caller suffix, clear, and reveal as separate controls', () => {
    render(
      <TextBox
        aria-label="Password"
        clearButton
        prefix={<span data-testid="prefix">$</span>}
        showReveal
        suffix={<span data-testid="suffix">USD</span>}
        type="password"
        value="secret"
      />,
    )
    const root = document.querySelector('.hl-input')!
    expect(root.querySelector('.hl-input__prefix')).toContainElement(screen.getByTestId('prefix'))
    expect(root.querySelector('.hl-input__control')).toBe(document.querySelector('input'))
    const suffix = root.querySelector('.hl-input__suffix')!
    expect(suffix).toContainElement(screen.getByTestId('suffix'))
    expect([...suffix.querySelectorAll('button')].map(button => button.dataset.hlAction)).toEqual(['clear', 'reveal'])
  })

  it('uses catalog labels and RTL direction while retaining keyboard-reachable actions', async () => {
    const user = userEvent.setup()
    render(
      <HarborlineLocaleProvider locale="ar-SA" catalog={{ 'common.clear': 'امسح', 'forms.textBox.showPassword': 'اعرض' }}>
        <TextBox aria-label="الحقل" clearButton showReveal type="password" value="سر" />
      </HarborlineLocaleProvider>,
    )
    const input = document.querySelector('input')!
    expect(input).toHaveAttribute('dir', 'rtl')
    input.focus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'امسح' })).toHaveFocus()
    await user.tab()
    expect(screen.getByRole('button', { name: 'اعرض' })).toHaveFocus()
  })

  it('normalizes sizes and preserves all appearance variants through Input', () => {
    const rendered = render(<TextBox aria-label="Query" fillMode="outline" rounded="full" size="small" />)
    const input = screen.getByRole('textbox')
    expect(input).toHaveAttribute('data-hl-size', 'sm')
    expect(input).toHaveAttribute('data-hl-fill', 'outline')
    expect(input).toHaveAttribute('data-hl-rounded', 'full')
    rendered.rerender(<TextBox aria-label="Query" fillMode="flat" rounded="large" size="large" />)
    expect(input).toHaveAttribute('data-hl-size', 'lg')
    expect(input).toHaveAttribute('data-hl-fill', 'flat')
    expect(input).toHaveAttribute('data-hl-rounded', 'large')
  })
})
