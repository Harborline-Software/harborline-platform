import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { SchemaForm } from '../SchemaForm'

const root = resolve(import.meta.dirname, '../../../../../../')
const pack = JSON.parse(readFileSync(resolve(root, '_shared/packs/platform/platform-pack.export.json'), 'utf8'))
const fixture = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.builder-definitions/activation.json'), 'utf8'))
const payload = pack.items.find((item: { id: string }) => item.id === 'platform-package-ck-7').content.payload

it.each(fixture.cases)('renders the released $status activation status through SchemaForm', ({ status, values }: {
  status: string
  values: Record<string, string>
}) => {
  const cut = render(<SchemaForm view={payload.configurationActivationDetail} values={values} readOnly onSubmit={vi.fn()} />)
  expect(cut.getByRole('form', { name: 'Configuration activation' })).toBeTruthy()
  expect(cut.container.querySelector('#status')?.textContent).toBe(payload.configurationActivationStatuses[status].values.en)
  for (const [name, value] of Object.entries(values)) {
    expect(cut.container.querySelector(`#${name}`)?.textContent).toBe(value)
  }
  expect(cut.container.querySelectorAll('input,select,textarea,button')).toHaveLength(0)
  if (status === 'effective') {
    expect(values.effectiveDigest).toBe(values.candidateDigest)
    expect(values.refusals).toBe('')
  } else {
    expect(values.effectiveDigest).toBe(values.expectedBaselineDigest)
    expect(values.effectiveDigest).not.toBe(values.candidateDigest)
  }
  if (status === 'refused') {
    expect(cut.container.querySelector('#status')?.textContent).not.toBe('Effective')
    expect(cut.container.querySelector('#refusals')).toHaveTextContent('projection-failed (forms/invoice)')
  }
})
