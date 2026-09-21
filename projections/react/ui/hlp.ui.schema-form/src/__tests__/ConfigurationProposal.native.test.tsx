import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { SchemaForm } from '../SchemaForm'

// T-461. The React half of the one Records-and-Forms example. Same exported pack, same fixture and
// same assertions as the Blazor renderer test, so the two lanes complete one example through the
// shared contract rather than two similar ones of their own.
const root = resolve(import.meta.dirname, '../../../../../../')
const pack = JSON.parse(readFileSync(resolve(root, '_shared/packs/platform/platform-pack.export.json'), 'utf8'))
const fixture = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.builder-definitions/proposal.json'), 'utf8'))
const payload = pack.items.find((item: { id: string }) => item.id === 'platform-package-ck-7').content.payload

it.each(fixture.cases)('completes the records-and-forms example at $step', ({ step, status, values }: {
  step: string
  status: string
  values: Record<string, string>
}) => {
  const cut = render(<SchemaForm view={payload.configurationProposalDetail} values={values} readOnly onSubmit={vi.fn()} />)
  const text = (id: string) => cut.container.querySelector(`#${id}`)?.textContent

  // Acceptance 6: the domain-facing vocabulary is on the surface, from the released pack.
  expect(cut.getByRole('form', { name: 'Proposed change' })).toBeTruthy()
  expect(text('status')).toBe(payload.configurationProposalStatuses[status].values.en)
  for (const [name, value] of Object.entries(values)) expect(text(name)).toBe(value)
  expect(cut.container.querySelectorAll('input,select,textarea,button')).toHaveLength(0)

  // Acceptance 1: the proposed change names its baseline, and editing it leaves the effective
  // generation alone. Only the stale case differs, and it refuses rather than releasing.
  expect(text('baselineDigest')).toBe(fixture.baselineDigest)
  if (step === 'released-against-a-stale-baseline') {
    expect(text('effectiveDigest')).not.toBe(text('baselineDigest'))
    expect(text('refusals')).toContain('configuration-baseline-stale')
  } else {
    expect(text('effectiveDigest')).toBe(text('baselineDigest'))
  }

  // Acceptance 2 and 3: only a Saved version carries authorship and rationale, and only a
  // successful release shows a Released package digest.
  if (status === 'proposed') {
    expect(text('savedVersion')).toBe('')
    expect(text('savedBy')).toBe('')
  } else {
    expect(text('savedBy')).toBe('dana.okafor')
    expect(text('rationale')).not.toBe('')
  }
  if (status === 'released') {
    // Acceptance 5: the digest on the surface is the exported artifact's digest.
    expect(text('releasedPackage')).toContain(fixture.releasedPackageDigest)
    expect(text('refusals')).toBe('')
  } else {
    expect(text('releasedPackage')).toBe('')
    expect(text('status')).not.toBe('Released package')
  }
  if (step === 'check-invalidated-by-a-later-edit') {
    expect(text('checkState')).toContain('Invalidated by a later edit')
    expect(text('refusals')).toContain('configuration-check-invalidated')
  }
})
