import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import userEvent from '@testing-library/user-event'

import { FormView } from '@harborline-platform/hlp.ui.form-view'

import { SchemaForm } from '../SchemaForm'
import { field, form, section } from './fixtures'

interface DomainCase {
  id: string
  input: {
    editor: string
    values?: string[]
    valueSequence?: { count: number; width: number }
    hostOptions: string[]
    query?: string
    invalidQuery?: string
    select?: string
    keyboardSelection?: string[]
  }
  expected: {
    editable?: boolean
    optionCount?: number
    selected?: string
    maximumVisibleOptions?: number
    candidateChanges?: number
    initialCandidateChanges?: number
    searchCandidateChanges?: number
  }
}

const document = JSON.parse(readFileSync(resolve(
  import.meta.dirname, '../../../../../../conformance/hlp.ui.schema-form/fixtures.yaml',
), 'utf8')) as { cases: DomainCase[] }
const cases = document.cases.filter(value => value.id.startsWith('schema-form.domain-'))
expect(cases.map(value => value.id)).toEqual([
  'schema-form.domain-none',
  'schema-form.domain-single',
  'schema-form.domain-choice',
  'schema-form.domain-radio',
  'schema-form.domain-record-picker',
  'schema-form.domain-taxonomy-picker',
  'schema-form.domain-search-candidate',
])

afterEach(cleanup)

it('real search typing and Enter never implicitly submit a SchemaForm', async () => {
  const user = userEvent.setup()
  const submit = vi.fn()
  const changed = vi.fn()
  const view = FormView.normalize(form([section('main', [field('status', 'Status', {
    controlHint: 'RecordPicker', permittedValues: ['Alpha', 'Beta'],
  })])]))
  render(<SchemaForm view={view} onChange={changed} onSubmit={submit} />)
  const input = screen.getByRole('combobox', { name: 'Status' })
  await user.type(input, 'Be')
  await user.keyboard('{Enter}')
  expect(changed).not.toHaveBeenCalled()
  expect(submit).not.toHaveBeenCalled()
  await user.keyboard('{ArrowDown}{Enter}')
  expect(changed).toHaveBeenCalledExactlyOnceWith({ status: 'Beta' })
  expect(submit).not.toHaveBeenCalled()
  await user.click(screen.getByRole('button', { name: 'Submit' }))
  expect(submit).toHaveBeenCalledExactlyOnceWith({ status: 'Beta' })
})

it('composes multiple choice and submits the explicit candidate without query text', () => {
  const submit = vi.fn()
  const changed = vi.fn()
  const view = FormView.normalize(form([section('main', [
    field('status', 'Status', { controlHint: 'RecordPicker', permittedValues: ['Alpha', 'Beta'] }),
    field('tags', 'Tags', { controlHint: 'multiselect' }),
  ])]), { tags: { options: [{ value: 'one', label: 'One' }, { value: 'two', label: 'Two' }] } })
  render(<SchemaForm view={view} onChange={changed} onSubmit={submit} />)
  fireEvent.click(screen.getByRole('button', { name: 'Tags' }))
  const list = screen.getByRole('listbox', { name: 'Tags' })
  expect(list).toHaveAttribute('aria-multiselectable', 'true')
  fireEvent.keyDown(list, { key: 'End' })
  fireEvent.keyDown(list, { key: ' ' })
  fireEvent.keyDown(list, { key: 'Escape' })
  const picker = screen.getByRole('combobox', { name: 'Status' })
  fireEvent.change(picker, { target: { value: 'Be' } })
  expect(submit).not.toHaveBeenCalled()
  expect(changed).toHaveBeenCalledTimes(1)
  fireEvent.keyDown(picker, { key: 'ArrowDown' })
  fireEvent.keyDown(picker, { key: 'Enter' })
  expect(changed).toHaveBeenLastCalledWith({ status: 'Beta', tags: ['two'] })
  fireEvent.submit(picker.closest('form')!)
  expect(submit).toHaveBeenCalledWith({ status: 'Beta', tags: ['two'] })
})

it('a runtime radio editor cannot obtain missing membership from host options', () => {
  const changed = vi.fn()
  const view = FormView.normalize(form([section('main', [field('status', 'Status', {
    controlHint: 'RadioGroup',
  })])]), { status: { options: [{ value: 'outside', label: 'Outside' }] } })

  render(<SchemaForm onChange={changed} onSubmit={() => undefined} view={view} />)

  expect(screen.queryAllByRole('radio')).toHaveLength(0)
  expect(screen.queryByText('Outside')).toBeNull()
  expect(changed).not.toHaveBeenCalled()
})

// Replacing a runtime editor with text, widening options, defaulting a value, or
// treating query text as a candidate must fail these shared renderer cases.
it.each(cases)('$id', ({ input, expected }) => {
  const values = input.values ?? Array.from(
    { length: input.valueSequence!.count },
    (_, index) => String(index).padStart(input.valueSequence!.width, '0'),
  )
  const changed = vi.fn()
  const view = FormView.normalize(form([section('main', [field('status', 'Status', {
    controlHint: input.editor,
    permittedValues: values,
  })])]), { status: { options: input.hostOptions.map(value => ({ value, label: value })) } })

  render(<SchemaForm onChange={changed} onSubmit={() => undefined} view={view} />)

  expect(screen.queryByRole('textbox', { name: 'Status' })).toBeNull()
  if (input.editor === 'None') {
    expect(screen.queryByRole('combobox', { name: 'Status' })).toBeNull()
    expect(screen.queryAllByRole('radio')).toHaveLength(0)
    expect(changed).toHaveBeenCalledTimes(expected.candidateChanges!)
    for (const outside of input.hostOptions) expect(screen.queryByText(outside)).toBeNull()
    return
  }

  expect(changed).toHaveBeenCalledTimes(expected.initialCandidateChanges!)
  if (input.editor === 'RadioGroup') {
    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(expected.optionCount!)
    for (const radio of radios) expect(radio).not.toBeChecked()
    for (const outside of input.hostOptions) expect(screen.queryByRole('radio', { name: outside })).toBeNull()
    fireEvent.click(screen.getByRole('radio', { name: input.select! }))
    expect(screen.getByRole('radio', { name: expected.selected! })).toBeChecked()
  } else if (input.editor === 'SingleValue' || input.editor === 'ChoiceList') {
    fireEvent.click(screen.getByRole('combobox', { name: 'Status' }))
    expect(screen.getAllByRole('option')).toHaveLength(expected.optionCount!)
    expect(screen.queryAllByRole('option', { selected: true })).toHaveLength(0)
    for (const outside of input.hostOptions) expect(screen.queryByRole('option', { name: outside })).toBeNull()
    fireEvent.click(screen.getByRole('option', { name: input.select! }))
  } else {
    const picker = screen.getByRole('combobox', { name: 'Status' })
    expect(picker).toHaveValue('')
    expect(screen.queryAllByRole('option').length).toBeLessThanOrEqual(expected.maximumVisibleOptions!)
    if (input.invalidQuery !== undefined) {
      fireEvent.change(picker, { target: { value: input.invalidQuery } })
      expect(screen.queryAllByRole('option')).toHaveLength(0)
      fireEvent.keyDown(picker, { key: 'Enter' })
      expect(changed).toHaveBeenCalledTimes(expected.searchCandidateChanges!)
    }
    fireEvent.change(picker, { target: { value: input.query! } })
    expect(changed).not.toHaveBeenCalled()
    expect(screen.getAllByRole('option').length).toBeLessThanOrEqual(expected.maximumVisibleOptions!)
    for (const outside of input.hostOptions) expect(screen.queryByRole('option', { name: outside })).toBeNull()
    if (input.keyboardSelection) {
      for (const key of input.keyboardSelection) fireEvent.keyDown(picker, { key })
    } else {
      fireEvent.click(screen.getByRole('option', { name: input.select! }))
    }
    expect(picker).toHaveValue(expected.selected!)
  }
  expect(changed).toHaveBeenLastCalledWith({ status: expected.selected })
})
