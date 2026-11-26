import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { expect, it } from 'vitest'
import { FormView } from '@harborline-platform/hlp.ui.form-view'
import { SchemaForm } from '../SchemaForm'
import { field, form, section } from './fixtures'
import cases from './picker-create-cases.json'

it('PE-5 renders the shared picker corpus into normalized actions for cross-shell comparison', () => {
  const actual = cases.map(row => {
    const capability = { targetKind: 'asset', displayName: 'asset', indefiniteName: 'an asset', canCreate: row.permission === 'granted' }
    const view = FormView.normalize(form([section('main', [field('asset', 'Asset', { controlHint: 'RecordPicker', permittedValues: row.members })])]))
    render(<SchemaForm view={view} recordCreateCapabilities={row.permission === 'absent' ? {} : { asset: capability }} onSubmit={() => undefined} />)
    const input = screen.getByRole('combobox')
    fireEvent.focus(input)
    if (row.query) fireEvent.change(input, { target: { value: row.query } })
    const actions = screen.queryAllByRole('option').map(option => ({ kind: option.hasAttribute('data-hl-action') ? 'create' : 'match', label: option.textContent?.trim() }))
    expect(actions, row.id).toEqual(row.expected)
    cleanup()
    return { id: row.id, actions }
  })
  const output = process.env.HARBORLINE_PICKER_PARITY_OUT
  if (output) writeFileSync(join(output, 'react-actions.json'), JSON.stringify(actual))
})
