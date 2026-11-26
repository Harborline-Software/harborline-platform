import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { FormView } from '@harborline-platform/hlp.ui.form-view'
import { SchemaForm } from '../SchemaForm'
import { field, form, section } from './fixtures'

afterEach(cleanup)
const capability = { targetKind: 'asset', displayName: 'asset', indefiniteName: 'an asset', canCreate: true }
function view(members = ['Alpha', 'Alpine', 'Beta'], hint = 'RecordPicker') {
  return FormView.normalize(form([section('main', [field('asset', 'Asset', {
    controlHint: hint, permittedValues: members,
  })])]))
}

it('PE-1 lists create after every match and emits one request without changing form values', () => {
  const request = vi.fn(), changed = vi.fn(), submit = vi.fn()
  const host = { recordCreateCapabilities: { asset: capability }, onRecordCreate: request }
  render(<SchemaForm {...host} view={view()} initialValues={{ asset: 'Beta', other: 'unsaved' }} onChange={changed} onSubmit={submit} />)
  const input = screen.getByRole('combobox', { name: 'Asset' })
  fireEvent.change(input, { target: { value: 'Al' } })
  expect(screen.getAllByRole('option').map(option => option.textContent)).toEqual(['Alpha', 'Alpine', "Create asset 'Al'"])
  fireEvent.keyDown(input, { key: 'ArrowUp' })
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(request).toHaveBeenCalledExactlyOnceWith({ fieldName: 'asset', targetKind: 'asset', text: 'Al' })
  expect(changed).not.toHaveBeenCalled()
  fireEvent.submit(input.closest('form')!)
  expect(submit).toHaveBeenCalledExactlyOnceWith({ asset: 'Beta', other: 'unsaved' })
})

it.each([undefined, { ...capability, canCreate: false }])('PE-2 hides create with absent or denied capability: %j', cap => {
  const host = { recordCreateCapabilities: cap ? { asset: cap } : {}, onRecordCreate: vi.fn() }
  render(<SchemaForm {...host} view={view()} onSubmit={() => undefined} />)
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'missing' } })
  expect(screen.queryAllByRole('option')).toHaveLength(0)
  expect(host.onRecordCreate).not.toHaveBeenCalled()
})

it('PE-3 explains denied empty matches literally and removes the explanation when a match exists', () => {
  const host = { recordCreateCapabilities: { asset: { ...capability, canCreate: false } } }
  render(<SchemaForm {...host} view={view()} onSubmit={() => undefined} />)
  const input = screen.getByRole('combobox')
  fireEvent.change(input, { target: { value: 'missing' } })
  expect(screen.getByRole('status').textContent).toBe("No asset you can see match 'missing'. Creating an asset needs permission you don't have.")
  fireEvent.change(input, { target: { value: 'Al' } })
  expect(screen.queryByRole('status')).toBeNull()
})

it('PE-4 hides create for empty and whitespace text', () => {
  const host = { recordCreateCapabilities: { asset: capability } }
  render(<SchemaForm {...host} view={view()} onSubmit={() => undefined} />)
  const input = screen.getByRole('combobox')
  fireEvent.focus(input)
  expect(screen.queryByText(/Create asset/)).toBeNull()
  fireEvent.change(input, { target: { value: '   ' } })
  expect(screen.queryByText(/Create asset/)).toBeNull()
})

it('offers create for an empty record domain and never offers it for taxonomy', () => {
  const host = { recordCreateCapabilities: { asset: capability } }
  const cut = render(<SchemaForm {...host} view={view([])} onSubmit={() => undefined} />)
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'New' } })
  expect(screen.getByRole('option').textContent).toBe("Create asset 'New'")
  cut.rerender(<SchemaForm {...host} view={view(['Alpha'], 'TaxonomyPicker')} onSubmit={() => undefined} />)
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'New' } })
  expect(screen.queryByText(/Create asset/)).toBeNull()
})

it('revocation removes the action before selection', () => {
  const request = vi.fn()
  const host = { recordCreateCapabilities: { asset: capability }, onRecordCreate: request }
  const cut = render(<SchemaForm {...host} view={view()} onSubmit={() => undefined} />)
  const input = screen.getByRole('combobox')
  fireEvent.change(input, { target: { value: 'Al' } })
  fireEvent.keyDown(input, { key: 'ArrowUp' })
  cut.rerender(<SchemaForm {...host} recordCreateCapabilities={{ asset: { ...capability, canCreate: false } }} view={view()} onSubmit={() => undefined} />)
  fireEvent.keyDown(input, { key: 'Enter' })
  expect(request).not.toHaveBeenCalled()
})

it('uses form strings for create and prerequisite labels', () => {
  const host = { recordCreateCapabilities: { asset: capability } }
  const strings = { recordCreate: 'Ajouter {kind}: {text}', recordCreatePrerequisite: 'Permission requise: {indefiniteKind} ({text})' }
  const cut = render(<SchemaForm {...host} strings={strings} view={view()} onSubmit={() => undefined} />)
  fireEvent.change(screen.getByRole('combobox'), { target: { value: 'New' } })
  expect(screen.getByRole('option').textContent).toBe('Ajouter asset: New')
  cut.rerender(<SchemaForm {...host} recordCreateCapabilities={{ asset: { ...capability, canCreate: false } }} strings={strings} view={view()} onSubmit={() => undefined} />)
  expect(screen.getByRole('status').textContent).toBe('Permission requise: an asset (New)')
})

it.each([{ disabled: true }, { readOnly: true }])('locked forms cannot request creation: %j', locked => {
  const request = vi.fn()
  render(<SchemaForm {...locked} recordCreateCapabilities={{ asset: capability }} onRecordCreate={request} view={view()} onSubmit={() => undefined} />)
  const input = screen.queryByRole('combobox')
  if (input) { fireEvent.change(input, { target: { value: 'Al' } }); fireEvent.keyDown(input, { key: 'ArrowUp' }); fireEvent.keyDown(input, { key: 'Enter' }) }
  expect(request).not.toHaveBeenCalled()
})

it('does not offer create from an existing selection before typing', () => {
  render(<SchemaForm recordCreateCapabilities={{ asset: capability }} initialValues={{ asset: 'Beta' }} view={view()} onSubmit={() => undefined} />)
  fireEvent.focus(screen.getByRole('combobox'))
  expect(screen.queryByText(/Create asset/)).toBeNull()
})
