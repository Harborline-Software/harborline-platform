import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { SchemaForm } from '../SchemaForm'

it('renders the released complete generation detail separately from package versions', () => {
  const root = resolve(import.meta.dirname, '../../../../../../')
  const pack = JSON.parse(readFileSync(resolve(root, '_shared/packs/platform/platform-pack.export.json'), 'utf8'))
  const fixture = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.builder-definitions/generation.json'), 'utf8'))
  const view = pack.items.find((item: { id: string }) => item.id === 'platform-package-ck-7').content.payload.configurationGenerationDetail
  const cut = render(<SchemaForm view={view} values={fixture.values} readOnly onSubmit={vi.fn()} />)
  expect(cut.container.querySelector('#generationDigest')).toHaveTextContent(fixture.digest)
  expect(cut.getByRole('form', { name: 'Effective configuration generation' })).toBeTruthy()
  expect(cut.container).toHaveTextContent('Individual package versions are constituent references.')
  for (const [name, value] of Object.entries(fixture.values)) {
    expect(cut.container.querySelector(`#${name}`)?.textContent).toBe(value)
  }
  expect(cut.container.querySelectorAll('input,select,textarea,button')).toHaveLength(0)
})
