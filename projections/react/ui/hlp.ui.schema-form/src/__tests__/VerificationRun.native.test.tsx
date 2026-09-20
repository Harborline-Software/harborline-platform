import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { render } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { SchemaForm } from '../SchemaForm'

// T-463. The React half of the one Records-and-Rules example. Same exported pack, same fixture and
// same assertions as the Blazor renderer test, so both lanes run one canonical suite rather than
// two similar ones of their own.
const root = resolve(import.meta.dirname, '../../../../../../')
const pack = JSON.parse(readFileSync(resolve(root, '_shared/packs/platform/platform-pack.export.json'), 'utf8'))
const fixture = JSON.parse(readFileSync(resolve(root, 'conformance/hlp.blocks.builder-definitions/verification.json'), 'utf8'))
const payload = pack.items.find((item: { id: string }) => item.id === 'platform-package-ck-7').content.payload

it.each(fixture.runs)('renders the records-and-rules run $step', ({ step, status, values }: {
  step: string
  status: string
  values: Record<string, string>
}) => {
  const cut = render(<SchemaForm view={payload.verificationRunDetail} values={values} readOnly onSubmit={vi.fn()} />)
  const text = (id: string) => cut.container.querySelector(`#${id}`)?.textContent

  // Acceptance 6: the vocabulary is the released one, from the pack, not authored here.
  expect(cut.getByRole('form', { name: 'Verification run' })).toBeTruthy()
  expect(text('status')).toBe(payload.verificationRunStatuses[status].values.en)
  for (const [name, value] of Object.entries(values)) expect(text(name)).toBe(value)
  expect(cut.container.querySelectorAll('input,select,textarea,button')).toHaveLength(0)

  // Acceptance 5: the receipt on the surface binds candidate, baseline, suite, fixtures and engines.
  expect(text('candidateDigest')).toBe(fixture.candidateDigest)
  expect(text('baselineDigest')).toBe(fixture.baselineDigest)
  expect(text('suite')).toContain(fixture.suiteDigest)
  expect(text('engines')).toContain(fixture.catalogueDigest)
  expect(text('fixtures')?.split('\n')).toHaveLength(2)

  // Acceptance 2: the declared inputs are on the surface, so a reader can see what was controlled.
  for (const declared of ['Europe/London', 'en-GB', 'seed=t-463-invoice', 'ordering=ordinal-by-key'])
    expect(text('determinism')).toContain(declared)

  // Acceptance 7: each defect fails its own claim and not the other one.
  if (step === 'passed') {
    expect(text('failures')).toBe('')
    expect(text('cases')).not.toContain('Failed')
  } else if (step === 'business-rule-defect') {
    expect(text('failures')).toContain('invoice-total[ten-at-one-hundred] record.number:/total: expected 1000, actual 110')
    expect(text('failures')).not.toContain('approval-authority')
    expect(text('cases')).toContain('approval-authority — Only an approver may create an invoice already marked approved.: Passed')
  } else {
    expect(text('failures')).toContain('approval-authority authorization.decision: expected "refused", actual "allowed"')
    expect(text('failures')).not.toContain('invoice-total')
  }
})

// Acceptance 4: an empty case is refused when it is authored, so no run of it ever reaches a surface.
it('refuses a case that asserts nothing before it can be run', () => {
  expect(fixture.emptyCase.refusals).toHaveLength(1)
  expect(fixture.emptyCase.refusals[0].code).toBe('verification-assertion-required')
  expect(fixture.suite.cases.map((item: { caseId: string }) => item.caseId))
    .not.toContain(fixture.emptyCase.caseId)
  for (const item of fixture.suite.cases) expect(item.assertions.length).toBeGreaterThan(0)
})
