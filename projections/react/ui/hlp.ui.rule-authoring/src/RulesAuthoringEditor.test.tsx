import { fireEvent, render, screen } from '@testing-library/react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import { serializeRuleDefinition, validateRuleDefinitionJson, type RuleDefinitionExpression } from '@harborline-software/rule-authoring'
import { GuidedExpressionEditor, RulesAuthoringEditor, emptyRulesDraft, type RulesMaterialization, type RulesOperationRequest } from './RulesAuthoringEditor'

const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.blocks.builder-definitions/rules-editor-contract-fixtures.json'), 'utf8')) as { lifecycle: { responses: readonly { materialization?: RulesMaterialization }[] }; preview: { clockUtc: string; label: string; outcomeKinds: readonly string[]; cases: readonly { expected: { kind: string; value?: string; validity?: string; visibility?: string; presentation?: string; code?: string; ruleName: string; memberName: string } }[] }; referenceForms: { palette: readonly { id: string; label: string; valueType: 'Number' | 'Text' | 'Boolean' }[]; cases: readonly { id: string; ref: string; action: string; scope: string; scopeTarget: string; lowered: unknown }[] } }
const contracts = [{ site: 'rule', returnContract: 'typed value', executionTimeContract: 'preview', palette: [{ id: 'amount', label: 'Amount', valueType: 'Number' }] }] as const
const props = (overrides: Partial<React.ComponentProps<typeof RulesAuthoringEditor>> = {}) => ({ value: emptyRulesDraft(), expressionContracts: contracts, previewKind: 'real' as const, onChange: vi.fn(), onOperation: vi.fn(), ...overrides })

describe('Rules authoring React projection', () => {
  it('replays every supplied producer preview outcome and lifecycle identity from the exact fixture', () => {
    expect(fixture.lifecycle.responses).toHaveLength(6)
    for (const item of fixture.preview.cases) { const expected = item.expected; const empty = emptyRulesDraft(); const { unmount } = render(<RulesAuthoringEditor {...props({ value: { ...empty, identity: expected.ruleName, draft: { ...empty.draft, scopeTarget: expected.memberName } }, outcome: { ...expected, kind: expected.kind as never, inputLabel: fixture.preview.label, clockUtc: fixture.preview.clockUtc } })} />); expect(screen.getByLabelText('Rule identity')).toHaveTextContent(expected.ruleName); expect(screen.getByText(new RegExp(expected.kind === 'Value' ? expected.value! : expected.kind === 'Validity' ? expected.validity! : expected.kind === 'Visibility' ? expected.visibility! : expected.kind === 'Presentation' ? expected.presentation! : expected.kind === 'Pending' ? 'Preview pending' : expected.code!))).toHaveTextContent(expected.ruleName); expect(screen.getByText(new RegExp(fixture.preview.clockUtc))).toBeInTheDocument(); unmount() }
  })
  it('authors a valid literal formula and a decision table from empty without raw JSON', () => {
    const changed = vi.fn(); const requested = vi.fn(); render(<RulesAuthoringEditor {...props({ onChange: changed, onOperation: requested })} />)
    fireEvent.change(screen.getByLabelText('Rule name'), { target: { value: 'Amount route' } })
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: 'route' } })
    fireEvent.change(screen.getByLabelText('Rule literal value'), { target: { value: 'ready' } })
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(requested).toHaveBeenLastCalledWith(expect.objectContaining({ draft: expect.objectContaining({ name: 'Amount route', draft: expect.objectContaining({ kind: 'Formula', scopeTarget: 'route', expression: { kind: 'Literal', value: 'ready', valueType: 'Text' } }) }) }))

    fireEvent.click(screen.getByLabelText('Decision table')); fireEvent.click(screen.getByRole('button', { name: 'Add column' }))
    fireEvent.change(screen.getByLabelText('Column 1 input'), { target: { value: 'amount' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add row' }))
    fireEvent.change(screen.getByLabelText('Row 1 column-1 cell kind'), { target: { value: 'Range' } })
    fireEvent.change(screen.getByLabelText('Row 1 column-1 range lower bound'), { target: { value: '0' } })
    fireEvent.change(screen.getByLabelText('Row 1 column-1 range upper bound'), { target: { value: '100' } })
    fireEvent.change(screen.getByLabelText('Row 1 output'), { target: { value: 'low' } })
    fireEvent.change(screen.getByLabelText('Default output'), { target: { value: 'high' } })
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(requested).toHaveBeenLastCalledWith(expect.objectContaining({ draft: expect.objectContaining({ draft: expect.objectContaining({ kind: 'Table', scopeTarget: 'route', columns: [{ id: 'column-1', input: 'amount', valueType: 'Number' }], rows: [{ id: 'row-1', cells: { 'column-1': { kind: 'Range', lo: '0', hi: '100' } }, output: 'low', priority: 1 }], noMatch: { kind: 'Default', value: 'high' } }) }) }))
    expect(screen.queryByRole('textbox', { name: /json/i })).toBeNull()
  })
  it('offers the complete producer formula operator vocabulary', () => {
    render(<GuidedExpressionEditor site="rule" value={{ kind: 'Call', op: 'cat', args: [] }} contract={contracts[0]} onChange={vi.fn()} />)
    expect(screen.getAllByRole('option').map(option => option.textContent)).toEqual(expect.arrayContaining([
      'missing', 'missing_some', '==', '!=', '===', '!==', '!', '!!', 'and', 'or', 'if', '>', '>=', '<', '<=', '+', '-', '*', '/', '%', 'min', 'max', 'in', 'cat', 'agg', 'money.add', 'money.sub', 'money.mul', 'date.add', 'date.diff', 'date.today', 'coding.is',
    ]))
  })
  it('shares the guided editor across accept-when, default-value, and rule contexts, including editable call arguments', () => {
    for (const site of ['accept-when', 'default', 'rule'] as const) { const change = vi.fn(); const label = site === 'accept-when' ? 'Accept when' : site === 'default' ? 'Default value' : 'Rule'; const { unmount } = render(<GuidedExpressionEditor site={site} value={{ kind: 'Call', op: 'cat', args: [{ kind: 'Literal', value: 'A', valueType: 'Text' }] }} contract={contracts[0]} onChange={change} />); fireEvent.change(screen.getByLabelText(`${label} argument 1 literal value`), { target: { value: 'B' } }); expect(change).toHaveBeenLastCalledWith(expect.objectContaining({ args: [{ kind: 'Literal', value: 'B', valueType: 'Text' }] })); unmount() }
  })
  it('authors nested Ref, Binary, If, and Call nodes and passes producer admission', () => {
    let authored: RuleDefinitionExpression = { kind: 'Literal', value: '', valueType: 'Text' }
    function Harness() { const [value, setValue] = useState<RuleDefinitionExpression>(authored); return <GuidedExpressionEditor site="rule" value={value} contract={contracts[0]} onChange={next => { authored = next; setValue(next) }} /> }
    render(<Harness />)
    fireEvent.change(screen.getByLabelText('Rule expression shape'), { target: { value: 'If' } })
    fireEvent.change(screen.getByLabelText('Rule condition left expression shape'), { target: { value: 'Ref' } })
    fireEvent.change(screen.getByLabelText('Rule then branch expression shape'), { target: { value: 'Binary' } })
    fireEvent.change(screen.getByLabelText('Rule then branch left operand expression shape'), { target: { value: 'Ref' } })
    fireEvent.change(screen.getByLabelText('Rule then branch right operand literal type'), { target: { value: 'Number' } })
    fireEvent.change(screen.getByLabelText('Rule then branch right operand literal value'), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText('Rule else branch expression shape'), { target: { value: 'Call' } })
    fireEvent.change(screen.getByLabelText('Rule else branch formula operator'), { target: { value: 'date.today' } })
    fireEvent.click(screen.getByRole('button', { name: 'Remove Rule else branch argument 1' }))
    const json = serializeRuleDefinition({ envelope: { id: 'nested', version: '1.0.0', tenant: 'tenant-a', cascadeLayer: 'domain-package', provenance: { kind: 'test' }, requires: [] }, name: 'Nested', tier: 'JsonLogic', draft: { kind: 'Formula', scope: 'Field', scopeTarget: 'total', outputType: 'Compute', inputs: [{ id: 'amount', ref: 'amount', type: 'Number' }], expression: authored } })
    expect(validateRuleDefinitionJson(json, 'Author').diagnostics).toEqual([])
  })
  it('edits compare values, range bounds, and a real Any catch-all row', () => {
    const changed = vi.fn(); render(<RulesAuthoringEditor {...props({ onChange: changed })} />); fireEvent.click(screen.getByLabelText('Decision table')); fireEvent.click(screen.getByRole('button', { name: 'Add column' })); fireEvent.click(screen.getByRole('button', { name: 'Add row' })); fireEvent.change(screen.getByLabelText('Row 1 column-1 cell kind'), { target: { value: 'Compare' } }); fireEvent.change(screen.getByLabelText('Row 1 column-1 compare value'), { target: { value: '10' } }); expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ draft: expect.objectContaining({ rows: [expect.objectContaining({ cells: { 'column-1': { kind: 'Compare', op: '>=', value: '10' } } })] }) })); fireEvent.change(screen.getByLabelText('Row 1 column-1 cell kind'), { target: { value: 'Range' } }); fireEvent.change(screen.getByLabelText('Row 1 column-1 range lower bound'), { target: { value: '1' } }); fireEvent.change(screen.getByLabelText('Row 1 column-1 range upper bound'), { target: { value: '9' } }); fireEvent.click(screen.getByRole('button', { name: 'Add catch-all row' })); expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ draft: expect.objectContaining({ noMatch: { kind: 'CatchAll' }, rows: expect.arrayContaining([expect.objectContaining({ cells: { 'column-1': { kind: 'Any' } } })]) }) }))
  })
  it('exposes immutable pin selection and reuses an intent id for a retry until a matching response arrives', () => {
    const requests: RulesOperationRequest[] = []
    render(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1' }, onOperation: request => { requests.push(request) } })} />)
    fireEvent.change(screen.getByLabelText('Version selection'), { target: { value: 'Pinned' } })
    fireEvent.change(screen.getByLabelText('Pinned version'), { target: { value: 'fixture-v1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    expect(requests).toHaveLength(2)
    expect(requests[1].requestId).toBe(requests[0].requestId)
    expect(requests[1].draft).toEqual(expect.objectContaining({ versionSelection: 'Pinned', pinnedVersionId: 'fixture-v1' }))
  })
  it('retains dirty local edits, replaces distinct identities on discard, and fences an out-of-order response', async () => {
    const changed = vi.fn(); const requests: RulesOperationRequest[] = []; let resolveFirst: ((value: unknown) => void) | undefined; const first = new Promise(resolve => { resolveFirst = resolve }); const view = render(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1' }, onChange: changed, onOperation: request => { requests.push(request); return requests.length === 1 ? first as Promise<never> : undefined } })} />); fireEvent.change(screen.getByLabelText('Rule name'), { target: { value: 'local' } }); view.rerender(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '2', name: 'server' }, onChange: changed, onOperation: request => { requests.push(request); return undefined } })} />); expect(screen.getByRole('alert')).toHaveTextContent('Revision changed'); fireEvent.click(screen.getByRole('button', { name: 'Keep edits' })); expect(screen.getByLabelText('Rule name')).toHaveValue('local'); fireEvent.click(screen.getByRole('button', { name: 'Preview' })); resolveFirst?.({ requestId: requests[0].requestId, identity: 'amount-rule', expectedRevision: '1', generation: requests[0].generation, outcome: { kind: 'Value', value: 'stale', inputLabel: 'sample', clockUtc: fixture.preview.clockUtc } }); await Promise.resolve(); expect(screen.queryByText(/Value: stale/)).toBeNull(); view.rerender(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'other-rule', expectedRevision: '1', name: 'other' }, onChange: changed, onOperation: vi.fn() })} />); fireEvent.click(screen.getByRole('button', { name: 'Discard edits' })); expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ identity: 'other-rule', name: 'other' }))
  })
  it('advances authoritative revisions and materializes Latest as a concrete immutable pin', async () => {
    const changed = vi.fn(); let request!: RulesOperationRequest
    const view = render(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1', versionSelection: 'Latest' }, onChange: changed, onOperation: value => { request = value } })} />)
    fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
    const materialization = fixture.lifecycle.responses[4].materialization!
    view.rerender(<RulesAuthoringEditor {...props({ value: { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1', versionSelection: 'Latest' }, onChange: changed, onOperation: vi.fn(), response: { requestId: request.requestId, identity: request.identity, expectedRevision: request.expectedRevision, generation: request.generation, authoritative: { identity: 'amount-rule', revision: '2', status: 'Published' }, materialization } })} />)
    await Promise.resolve()
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ expectedRevision: '2', versionSelection: 'Pinned', pinnedVersionId: 'fixture-v1' }))
    expect(screen.getByLabelText('Version selection')).toHaveValue('Pinned')
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ materialization }))
  })
  // T-712: DES-0018 rules-auth-1, -3 and -5 at the editor (rules-auth-2 and -6 are the shared-editor and catch-all tests above).
  it('rules-auth-1: holds exactly the one action the author picks, for every action', () => {
    const requested = vi.fn(); render(<RulesAuthoringEditor {...props({ onOperation: requested })} />)
    const offered = Array.from((screen.getByLabelText('Rule action') as HTMLSelectElement).options).map(option => option.value)
    expect(offered).toEqual(['Visibility', 'Required', 'ReadOnly', 'Validate', 'Compute', 'Presentation', 'Options'])
    for (const action of offered) {
      fireEvent.change(screen.getByLabelText('Rule action'), { target: { value: action } })
      fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
      expect(requested.mock.lastCall![0].draft.draft.outputType).toBe(action)
      expect(screen.getByLabelText('Rule action')).toHaveValue(action)
    }
  })
  it('rules-auth-3: carries every reference form through producer admission and receives the reference the producer lowered', () => {
    const palette = [{ site: 'rule' as const, returnContract: 'typed value', executionTimeContract: 'preview', palette: fixture.referenceForms.palette }]
    for (const item of fixture.referenceForms.cases) {
      const requested = vi.fn(); const { unmount } = render(<RulesAuthoringEditor {...props({ expressionContracts: palette, onOperation: requested })} />)
      fireEvent.change(screen.getByLabelText('Rule action'), { target: { value: item.action } })
      fireEvent.change(screen.getByLabelText('Rule scope'), { target: { value: item.scope } })
      fireEvent.change(screen.getByLabelText('Target'), { target: { value: item.scopeTarget } })
      // Producer admission refuses an undeclared reference, so the author declares it first.
      fireEvent.click(screen.getByRole('button', { name: 'Add declared input' }))
      fireEvent.change(screen.getByLabelText('Input 1 reference'), { target: { value: item.ref } })
      fireEvent.change(screen.getByLabelText('Rule expression shape'), { target: { value: 'Ref' } })
      fireEvent.change(screen.getByLabelText('Rule reference'), { target: { value: item.ref } })
      fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
      const draft = requested.mock.lastCall![0].draft.draft
      expect(draft.expression, item.id).toEqual({ kind: 'Ref', name: item.ref })
      const json = serializeRuleDefinition({ envelope: { id: item.id, version: '1.0.0', tenant: 'tenant-a', cascadeLayer: 'domain-package', provenance: { kind: 'test' }, requires: [] }, name: item.id, tier: 'JsonLogic', draft })
      const admitted = validateRuleDefinitionJson(json, 'Author')
      expect(admitted.diagnostics, item.id).toEqual([])
      expect(admitted.lowered, item.id).toEqual(item.lowered)
      unmount()
    }
  })
  it('rules-auth-5: offers exactly FirstMatch and Priority and round-trips the choice', () => {
    const requested = vi.fn(); render(<RulesAuthoringEditor {...props({ onOperation: requested })} />)
    fireEvent.click(screen.getByLabelText('Decision table'))
    // Fails if a third policy becomes selectable.
    expect(Array.from((screen.getByLabelText('Hit policy') as HTMLSelectElement).options).map(option => option.value)).toEqual(['FirstMatch', 'Priority'])
    for (const policy of ['Priority', 'FirstMatch']) {
      fireEvent.change(screen.getByLabelText('Hit policy'), { target: { value: policy } })
      fireEvent.click(screen.getByRole('button', { name: 'Publish' }))
      expect(requested.mock.lastCall![0].draft.draft.hitPolicy).toBe(policy)
    }
  })
  it('acknowledges a correlated outcome so the next retry receives a new intent id', () => {
    const requests: RulesOperationRequest[] = []
    const value = { ...emptyRulesDraft(), identity: 'amount-rule', expectedRevision: '1' }
    const view = render(<RulesAuthoringEditor {...props({ value, onOperation: request => { requests.push(request) } })} />)
    fireEvent.click(screen.getByRole('button', { name: 'Preview' }))
    const first = requests[0]
    view.rerender(<RulesAuthoringEditor {...props({ value, onOperation: request => { requests.push(request) }, outcome: { kind: 'Value', inputLabel: 'sample', clockUtc: fixture.preview.clockUtc, value: 'ready', requestId: first.requestId, identity: first.identity, expectedRevision: first.expectedRevision, generation: first.generation } })} />)
    fireEvent.click(screen.getByRole('button', { name: 'Preview' }))
    expect(requests[1].requestId).not.toBe(first.requestId)
    expect(screen.getByRole('heading', { name: 'Preview (sample input)' })).toBeInTheDocument()
  })
})
