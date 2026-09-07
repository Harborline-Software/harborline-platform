import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { FormFieldContext } from '../../../hlp.ui.form-field/src/FormFieldContext'

import { SwitchField } from '../SwitchField'
import { sharedCases } from './fixtures'

interface ContractDocument { cases: Array<{ id: string }> }
const contract = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../specs/modules/ui/hlp.ui.switch-field/interface.yaml'), 'utf8')) as ContractDocument

describe('SwitchField revision-1 shared fixtures', () => {
  it('consumes every behavior case in frozen contract order', () => {
    expect(sharedCases.map(value => value.id)).toEqual(contract.cases.map(value => value.id))
  })

  it('maps name to id and hidden form identity while remaining controlled', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<SwitchField checked={false} label="Published" name="published" onCheckedChange={changed} />)
    const control = screen.getByRole('switch', { name: 'Published' })
    expect(control).toHaveAttribute('id', 'published')
    expect(control).toHaveAttribute('aria-checked', 'false')
    expect(document.querySelector('input[name="published"]')).toHaveValue('off')
    await user.click(control)
    expect(changed).toHaveBeenCalledWith(true)
    expect(control).toHaveAttribute('aria-checked', 'false')
  })

  it('honors explicit id and delegates pointer, Enter, and Space exactly once', async () => {
    const user = userEvent.setup()
    const changed = vi.fn()
    render(<SwitchField aria-label="Published" checked={false} id="custom" name="published" onCheckedChange={changed} />)
    const control = screen.getByRole('switch')
    expect(control).toHaveAttribute('id', 'custom')
    await user.click(control)
    control.focus()
    await user.keyboard('[Enter]')
    await user.keyboard('[Space]')
    expect(changed.mock.calls).toEqual([[true], [true], [true]])
  })

  it('delegates FormField metadata, validation, size, and disabled suppression', () => {
    const changed = vi.fn()
    render(
      <FormFieldContext.Provider value={{ id: 'ambient', labelId: 'field-label', describedBy: 'field-help', required: true, disabled: true }}>
        <span id="field-label">Enabled</span>
        <SwitchField checked error name="enabled" onCheckedChange={changed} size="large" />
      </FormFieldContext.Provider>,
    )
    const control = screen.getByRole('switch', { name: 'Enabled' })
    expect(control).toHaveAttribute('id', 'enabled')
    expect(control).toBeDisabled()
    expect(control).toHaveAttribute('aria-describedby', 'field-help')
    expect(control).toHaveAttribute('aria-required', 'true')
    expect(control).toHaveAttribute('aria-invalid', 'true')
    expect(control.parentElement).toHaveAttribute('data-hl-size', 'lg')
    fireEvent.click(control)
    fireEvent.keyDown(control, { key: 'Enter' })
    expect(changed).not.toHaveBeenCalled()
  })

  it('rejects an empty name', () => {
    expect(() => render(<SwitchField aria-label="Published" checked={false} name=" " onCheckedChange={vi.fn()} />)).toThrow('missing-name')
  })
})
