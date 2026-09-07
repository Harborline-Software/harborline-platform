import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '@harborline-platform/hlp.ui.form-field-context'
import { TextArea, type TextAreaFillMode, type TextAreaResize, type TextAreaRounded, type TextAreaSize } from '../TextArea'
import { fixture, sharedCases } from './fixtures'

describe('TextArea shared fixtures', () => {
  it('text-area.defaults', () => {
    fixture(sharedCases, 'text-area.defaults')
    render(<TextArea aria-label="Notes" />)
    const control = screen.getByRole('textbox', { name: 'Notes' })
    expect(control).toHaveAttribute('rows', '3')
    expect(control).toHaveAttribute('data-hl-resize', 'vertical')
    expect(control).toHaveAttribute('data-hl-size', 'md')
    expect(control).toHaveAttribute('data-hl-fill-mode', 'solid')
    expect(control).toHaveAttribute('data-hl-rounded', 'medium')
    expect(document.querySelector('.hl-text-area__counter')).toBeNull()
  })

  it('text-area.raw-change', () => {
    const value = fixture(sharedCases, 'text-area.raw-change')
    const onChange = vi.fn()
    render(<TextArea aria-label="Notes" onChange={onChange} />)
    fireEvent.change(screen.getByRole('textbox'), { target: { value: value.input.typed } })
    expect(onChange).toHaveBeenCalledWith(value.expected.emitted)
  })

  it('text-area.controlled', () => {
    fixture(sharedCases, 'text-area.controlled')
    const onChange = vi.fn()
    render(<TextArea aria-label="Notes" onChange={onChange} value="before" />)
    const control = screen.getByRole('textbox')
    fireEvent.change(control, { target: { value: 'after' } })
    expect(onChange).toHaveBeenCalledWith('after')
    expect(control).toHaveValue('before')
  })

  it('text-area.uncontrolled', () => {
    fixture(sharedCases, 'text-area.uncontrolled')
    const onChange = vi.fn()
    render(<TextArea aria-label="Notes" defaultValue="before" onChange={onChange} />)
    const control = screen.getByRole('textbox')
    fireEvent.change(control, { target: { value: 'after' } })
    expect(control).toHaveValue('after')
    expect(onChange).toHaveBeenCalledWith('after')
  })

  it('text-area.resize', () => {
    fixture(sharedCases, 'text-area.resize')
    const values: TextAreaResize[] = ['none', 'vertical', 'horizontal', 'both']
    const rendered = render(<TextArea aria-label="Notes" resize="none" />)
    const control = screen.getByRole('textbox')
    for (const resize of values) {
      rendered.rerender(<TextArea aria-label="Notes" resize={resize} />)
      expect(control).toHaveAttribute('data-hl-resize', resize)
      expect(control).toHaveClass(`hl-text-area--resize-${resize}`)
    }
  })

  it('text-area.auto-resize', () => {
    fixture(sharedCases, 'text-area.auto-resize')
    render(<TextArea aria-label="Notes" autoResize resize="both" />)
    expect(screen.getByRole('textbox')).toHaveAttribute('data-hl-resize', 'none')
    expect(screen.getByRole('textbox')).toHaveClass('hl-text-area--resize-none')
    expect(document.querySelector('.hl-text-area__root')).toHaveAttribute('data-hl-auto-resize', 'true')
  })

  it('text-area.variants', () => {
    fixture(sharedCases, 'text-area.variants')
    const sizes: Array<[TextAreaSize, string]> = [
      ['sm', 'sm'], ['small', 'sm'], ['md', 'md'], ['medium', 'md'], ['lg', 'lg'], ['large', 'lg'],
    ]
    const fills: TextAreaFillMode[] = ['solid', 'outline', 'flat']
    const roundedValues: TextAreaRounded[] = ['small', 'medium', 'large', 'full']
    const rendered = render(<TextArea aria-label="Notes" />)
    const control = screen.getByRole('textbox')

    for (const [size, expected] of sizes) {
      rendered.rerender(<TextArea aria-label="Notes" size={size} />)
      expect(control).toHaveAttribute('data-hl-size', expected)
    }
    for (const fillMode of fills) {
      rendered.rerender(<TextArea aria-label="Notes" fillMode={fillMode} />)
      expect(control).toHaveAttribute('data-hl-fill-mode', fillMode)
    }
    for (const rounded of roundedValues) {
      rendered.rerender(<TextArea aria-label="Notes" rounded={rounded} />)
      expect(control).toHaveAttribute('data-hl-rounded', rounded)
    }
  })

  it('text-area.error-alias', () => {
    fixture(sharedCases, 'text-area.error-alias')
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    try {
      render(<TextArea aria-label="Notes" error={false} invalid />)
      expect(screen.getByRole('textbox')).toHaveAttribute('aria-invalid', 'true')
      expect(screen.getByRole('textbox')).toHaveClass('hl-text-area--error')
      expect(warning).toHaveBeenCalledOnce()
    } finally {
      warning.mockRestore()
    }
  })

  it('text-area.form-context', () => {
    fixture(sharedCases, 'text-area.form-context')
    render(
      <FormFieldContext.Provider value={{
        id: 'notes',
        labelId: 'notes-label',
        describedBy: 'notes-hint notes-error',
        required: true,
        disabled: true,
      }}>
        <span id="notes-label">Notes</span>
        <span id="notes-hint">Hint</span>
        <span id="notes-error">Error</span>
        <TextArea disabled={false} required={false} />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByRole('textbox', { name: 'Notes' })
    expect(control).toHaveAttribute('id', 'notes')
    expect(control).toHaveAttribute('aria-labelledby', 'notes-label')
    expect(control).toHaveAttribute('aria-describedby', 'notes-hint notes-error')
    expect(control).not.toBeDisabled()
    expect(control).not.toBeRequired()
    expect(control).not.toHaveAttribute('aria-required')
  })

  it('text-area.counter', () => {
    fixture(sharedCases, 'text-area.counter')
    const rendered = render(<TextArea aria-label="Notes" showCounter value="café" />)
    expect(document.querySelector('.hl-text-area__counter')).toHaveTextContent('4')
    rendered.rerender(<TextArea aria-label="Notes" showCounter value="مرحبا" />)
    expect(document.querySelector('.hl-text-area__counter')).toHaveTextContent('5')
    expect(screen.getByRole('textbox')).toHaveValue('مرحبا')
  })

  it('text-area.maximum', () => {
    fixture(sharedCases, 'text-area.maximum')
    render(<TextArea aria-label="Notes" maxLength={10} showCounter value="abc" />)
    expect(screen.getByRole('textbox')).toHaveAttribute('maxlength', '10')
    expect(document.querySelector('.hl-text-area__counter')).toHaveTextContent('3/10')
  })

  it('text-area.host-attributes', () => {
    fixture(sharedCases, 'text-area.host-attributes')
    render(
      <TextArea
        aria-label="Notes"
        autoComplete="off"
        className="consumer"
        data-case="consumer"
        name="notes"
        placeholder="Localized"
        readOnly
        spellCheck={false}
      />,
    )
    const control = screen.getByRole('textbox')
    expect(control).toHaveClass('hl-text-area', 'consumer')
    expect(control).toHaveAttribute('autocomplete', 'off')
    expect(control).toHaveAttribute('data-case', 'consumer')
    expect(control).toHaveAttribute('name', 'notes')
    expect(control).toHaveAttribute('placeholder', 'Localized')
    expect(control).toHaveAttribute('readonly')
    expect(control).toHaveAttribute('spellcheck', 'false')
  })

  it('text-area.projection-equivalence', () => {
    fixture(sharedCases, 'text-area.projection-equivalence')
    render(<TextArea aria-label="Notes" fillMode="outline" maxLength={12} required rows={4} showCounter value="raw" />)
    const control = screen.getByRole('textbox', { name: 'Notes' })
    expect(control.tagName).toBe('TEXTAREA')
    expect(control).toHaveValue('raw')
    expect(control).toHaveAttribute('rows', '4')
    expect(control).toHaveAttribute('aria-required', 'true')
    expect(control).toHaveAttribute('data-hl-fill-mode', 'outline')
    expect(document.querySelector('.hl-text-area__counter')).toHaveTextContent('3/12')
  })
})
