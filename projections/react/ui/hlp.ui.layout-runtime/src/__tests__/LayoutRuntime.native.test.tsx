import { fireEvent, render, screen, within } from '@testing-library/react'
import { useState } from 'react'
import authored from '../../../../../../_shared/layout/authored-repeating-block.json'
import denyAll from '../../../../../../_shared/layout/deny-all-authority.json'
import type { LayoutAuthoringDraft, LayoutRuntimePlan } from '../LayoutRuntime.types'
import { LayoutAuthoringEditor, emptyLayoutAuthoringDraft } from '../LayoutAuthoringEditor'
import { LayoutRuntime } from '../LayoutRuntime'

describe('LayoutRuntime React projection', () => {
  it('renders ordered screen flow, static regions, and pinned definition provenance', () => {
    const { container } = render(<LayoutRuntime plan={{ definitionId: 'invoice', definitionVersionId: 'version-7', medium: 'screen', flow: [{ id: 'title', kind: 'layout.text', depth: 0 }, { id: 'table', kind: 'layout.table', depth: 1 }], staticRegions: [{ id: 'header', kind: 'layout.text', depth: 0, zone: 'header.center' }] }} />)
    expect(container.querySelector('[data-fragmentainer=screen]')).toBeInTheDocument()
    expect(container.querySelectorAll('[data-layout-block]')).toHaveLength(2)
    expect(container.querySelector('[data-layout-static-region=header\\.center]')).toBeInTheDocument()
    expect(container.firstElementChild).toHaveAttribute('data-definition-source', '{"definitionId":"invoice","definitionVersionId":"version-7"}')
  })

  it('layout-eng-26: a full-data, deny-all authority renders every block read-only and leaves zero live submit controls', () => {
    const plan = denyAll as LayoutRuntimePlan
    const { container } = render(<LayoutRuntime plan={plan} onSubmit={vi.fn()} />)
    const blocks = [...container.querySelectorAll('[data-layout-block]')]
    expect(blocks.map(block => block.getAttribute('data-layout-block'))).toEqual(['name', 'orders', 'orders-total'])
    for (const block of blocks) expect(block).toHaveAttribute('data-layout-readonly', 'true')
    expect(container.querySelectorAll('[data-layout-submit], button, input[type=submit]')).toHaveLength(0)
    // No authority at all is read-only too: the lane never assumes one.
    const bare = render(<LayoutRuntime plan={{ ...plan, authority: undefined }} />)
    expect(bare.container.querySelectorAll('button')).toHaveLength(0)
  })

  it('layout-eng-26: an admitted authority renders one submit control, which hands the submit to the host', () => {
    const submitted = vi.fn()
    const { container } = render(<LayoutRuntime plan={{ ...(denyAll as LayoutRuntimePlan), authority: { canSubmit: true } }} onSubmit={submitted} />)
    expect(container.querySelector('[data-layout-readonly]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))
    expect(container.querySelectorAll('[data-layout-submit]')).toHaveLength(1)
    expect(submitted).toHaveBeenCalledTimes(1)
  })

  it('renders persisted invalid values as diagnostics without normalization', () => {
    render(<LayoutRuntime plan={{ definitionId: 'invoice', definitionVersionId: 'version-7', medium: 'page', flow: [], staticRegions: [], diagnostics: [{ code: 'layout.numeric.out_of_range', pointer: '/blocks/0/placement/span' }] }} />)
    expect(screen.getByRole('alert')).toHaveTextContent('layout.numeric.out_of_range at /blocks/0/placement/span')
  })

  it('authors the closed layout grammar without a separate reading-order or numeric control', () => {
    const changed = vi.fn()
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), medium: 'page', blocks: [{ id: 'dashboard', kind: 'layout.dashboard-widget' }], pageRuns: [{ id: 'run-1', pageLayoutId: 'letter', pageMasterId: 'letter-master' }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }, { id: 'layout.dashboard-widget', label: 'Dashboard widget' }], zones: ['header.center'], staticRegions: ['header.center'], helmWidgets: [{ id: 'revenue-glance', label: 'Revenue glance' }], pageLayouts: [{ id: 'letter', label: 'Letter' }], pageMasters: [{ id: 'letter-master', label: 'Letter master' }] }} onChange={changed} />)
    expect(screen.getByLabelText('Collapse below')).toHaveTextContent('sm')
    expect(screen.getByLabelText('Collapse below')).not.toHaveTextContent('xl')
    expect(screen.getByLabelText('Default intent')).toHaveTextContent('capture')
    expect(screen.getByLabelText('Container flow')).toHaveTextContent('areas')
    expect(screen.getByLabelText('Block 1 Helm widget')).toHaveTextContent('Revenue glance')
    expect(screen.getByLabelText('Page run 1 layout')).toHaveTextContent('Letter')
    expect(screen.queryByText(/reading order/i)).not.toBeInTheDocument()
    expect(document.querySelector('input[type=number]')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Add block' }))
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ blocks: expect.arrayContaining([expect.objectContaining({ kind: 'layout.table' })]) }))
  })

  it('layout-auth-13..16: binds a block to any of the five kinds and names it from the catalogue', () => {
    const changed = vi.fn()
    const catalogue = {
      blockKinds: [{ id: 'layout.table', label: 'Table' }],
      zones: ['header.center'],
      bindables: {
        record_field: [{ id: 'invoice.supplier', label: 'Supplier' }],
        query: [{ id: 'views.open-invoices', label: 'Open invoices' }],
        measure: [{ id: 'invoice.total', label: 'Invoice total' }],
        template: [{ id: 'tpl.remittance', label: 'Remittance' }],
      },
    }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'total', kind: 'layout.table', binding: { kind: 'measure', name: 'invoice.total' } }] }} catalogue={catalogue} onChange={changed} />)

    // All five authored kinds are offered, and the name list follows the chosen kind.
    const kind = screen.getByLabelText('Block 1 binding kind')
    for (const label of ['Record field', 'Query', 'Measure', 'Template', 'Static content']) expect(kind).toHaveTextContent(label)
    expect(screen.getByLabelText('Block 1 binding name')).toHaveTextContent('Invoice total')
    expect(screen.getByLabelText('Block 1 binding name')).not.toHaveTextContent('Open invoices')

    // Rebinding to another kind preserves the block and clears the name rather than
    // deleting it (layout-auth-31).
    fireEvent.change(kind, { target: { value: 'query' } })
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ blocks: [{ id: 'total', kind: 'layout.table', binding: { kind: 'query', name: '' } }] }))
  })

  it('layout-auth-31: marks an unbound block as needing a binding instead of removing it', () => {
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'orphan', kind: 'layout.table', binding: { kind: 'query', name: '' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={vi.fn()} />)
    expect(screen.getByRole('status')).toHaveTextContent('Block 1 needs a binding')
    expect(screen.getByLabelText('Block 1 binding kind')).toBeInTheDocument()
  })

  it('layout-auth-18: a repeating block binds a collection and its row subtree is authored once', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'lines', kind: 'layout.table', binding: { kind: 'query' as const, name: 'views.invoice-lines' } },
      { id: 'amount', kind: 'layout.table', binding: { kind: 'record_field' as const, name: 'line.amount' }, parentId: 'lines' },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure' as const, name: 'invoice.total' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)

    // Only a collection binding (a query or a record field) can repeat; a measure cannot.
    expect(screen.queryByLabelText('Block 3 repeats per row')).toBeNull()
    fireEvent.click(screen.getByLabelText('Block 1 repeats per row'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], repeating: true, container: 'stack' }, blocks[1], blocks[2]] }))

    // The row subtree is authored once, as the repeating block's children: a block may be
    // placed inside it, and never inside itself or its own descendant.
    const parentOfLines = screen.getByLabelText('Block 1 parent')
    expect(within(parentOfLines).queryByRole('option', { name: 'Block 1' })).toBeNull()
    expect(within(parentOfLines).queryByRole('option', { name: 'Block 2' })).toBeNull()
    expect(within(parentOfLines).getByRole('option', { name: 'Block 3' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Block 3 parent'), { target: { value: 'lines' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], container: 'stack' }, blocks[1], { ...blocks[2], parentId: 'lines' }] }))
  })

  it('layout-auth-18: rebinding a repeating block to a kind that is not a collection stops it repeating', () => {
    const changed = vi.fn()
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'lines', kind: 'layout.table', binding: { kind: 'query', name: 'views.invoice-lines' }, repeating: true }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    expect(screen.getByLabelText('Block 1 repeats per row')).toBeChecked()
    fireEvent.change(screen.getByLabelText('Block 1 binding kind'), { target: { value: 'measure' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ id: 'lines', kind: 'layout.table', binding: { kind: 'measure', name: '' } }] }))
  })

  it('layout-auth-19: a related block traverses a declared Records relationship and persists only its key', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'supplier', kind: 'layout.table', binding: { kind: 'record_field' as const, name: 'supplier.name' } },
      { id: 'entry', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.reference' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], relationships: [{ id: 'invoice.supplier', label: 'Invoice supplier' }] }} onChange={changed} />)

    // A related block observes; a capture block is never offered a traversal.
    expect(screen.queryByLabelText('Block 2 related record')).toBeNull()
    // Only relationships Records declares are offered, and the block stores the key alone.
    const related = screen.getByLabelText('Block 1 related record')
    expect(within(related).getAllByRole('option').map(option => option.textContent)).toEqual(['Not related', 'Invoice supplier'])
    fireEvent.change(related, { target: { value: 'invoice.supplier' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], relatedRelationship: 'invoice.supplier' }, blocks[1]] }))
  })

  it('layout-auth-19: a related block that stops observing drops its relationship', () => {
    const changed = vi.fn()
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'supplier', kind: 'layout.table', binding: { kind: 'record_field', name: 'supplier.name' }, relatedRelationship: 'invoice.supplier' }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], relationships: [{ id: 'invoice.supplier', label: 'Invoice supplier' }] }} onChange={changed} />)
    fireEvent.change(screen.getByLabelText('Block 1 intent'), { target: { value: 'capture' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ id: 'supplier', kind: 'layout.table', binding: { kind: 'record_field', name: 'supplier.name' }, intent: 'capture' }] }))
  })

  it('layout-auth-20: show_when is written in Rules grammar and stored verbatim for the shared engine', () => {
    const changed = vi.fn()
    const block = { id: 'notice', kind: 'layout.table', binding: { kind: 'record_field' as const, name: 'invoice.note' } }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ ...block, showWhen: { expression: '{"var":"field.flagged"}' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    const guard = screen.getByLabelText('Block 1 show when')
    expect(guard).toHaveValue('{"var":"field.flagged"}')

    // The editor holds no conditional grammar of its own: the Rules expression is stored as written.
    const expression = ' {"==": [{"var": "field.status"}, "open"]}'
    fireEvent.change(guard, { target: { value: expression } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...block, showWhen: { expression } }] }))
    // Clearing the guard removes it rather than storing an empty expression.
    fireEvent.change(guard, { target: { value: '' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [block] }))
    expect(changed.mock.lastCall![0].blocks[0]).not.toHaveProperty('showWhen')
  })

  it('layout-auth-21: a capture block adds a requirement and named validation rules and never drops a declared requirement', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'reference', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.reference' } },
      { id: 'note', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.note' } },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure' as const, name: 'invoice.total' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], requiredFields: ['invoice.reference'], validationRules: [{ id: 'rules.iban', label: 'IBAN checksum' }] }} onChange={changed} />)

    // Records declares the reference required: the block shows it, and cannot remove it.
    const declared = screen.getByLabelText('Block 1 required')
    expect(declared).toBeChecked()
    expect(declared).toBeDisabled()
    // Only a capture block narrows capture; an observing block is offered neither control.
    expect(screen.queryByLabelText('Block 3 required')).toBeNull()
    expect(screen.queryByLabelText('Block 3 validation rule IBAN checksum')).toBeNull()

    // A block may add a requirement Records did not declare, and name a registered rule.
    expect(screen.getByLabelText('Block 2 required')).not.toBeChecked()
    fireEvent.click(screen.getByLabelText('Block 2 required'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [blocks[0], { ...blocks[1], capture: { required: true } }, blocks[2]] }))
    fireEvent.click(screen.getByLabelText('Block 2 validation rule IBAN checksum'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [blocks[0], { ...blocks[1], capture: { validationRules: ['rules.iban'] } }, blocks[2]] }))
  })

  it('layout-auth-21: a block that stops capturing drops its capture properties', () => {
    const changed = vi.fn()
    const block = { id: 'note', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.note' }, capture: { required: true, validationRules: ['rules.iban'] } }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [block] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], validationRules: [{ id: 'rules.iban', label: 'IBAN checksum' }] }} onChange={changed} />)
    expect(screen.getByLabelText('Block 1 validation rule IBAN checksum')).toBeChecked()
    fireEvent.click(screen.getByLabelText('Block 1 validation rule IBAN checksum'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...block, capture: { required: true } }] }))
    fireEvent.change(screen.getByLabelText('Block 1 intent'), { target: { value: 'observe' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ id: 'note', kind: 'layout.table', intent: 'observe', binding: { kind: 'record_field', name: 'invoice.note' } }] }))
  })

  it('layout-auth-22: a capture block overrides its prompt for this surface only', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'reference', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.reference' } },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure' as const, name: 'invoice.total' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)

    // A prompt override narrows capture, so an observing block is not offered one.
    expect(screen.queryByLabelText('Block 2 prompt override')).toBeNull()
    // The override lives on this block of this surface; the field's own prompt is untouched.
    fireEvent.change(screen.getByLabelText('Block 1 prompt override'), { target: { value: 'Supplier reference' } })
    expect(changed).toHaveBeenLastCalledWith({ ...emptyLayoutAuthoringDraft(), blocks: [{ ...blocks[0], capture: { promptOverride: 'Supplier reference' } }, blocks[1]] })
  })

  it('layout-auth-22: clearing a prompt override removes it', () => {
    const changed = vi.fn()
    const block = { id: 'reference', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.reference' } }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ ...block, capture: { required: true, promptOverride: 'Supplier reference' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    expect(screen.getByLabelText('Block 1 prompt override')).toHaveValue('Supplier reference')
    fireEvent.change(screen.getByLabelText('Block 1 prompt override'), { target: { value: '' } })
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual({ ...block, capture: { required: true } })
  })

  it('layout-auth-33: a block declares the selection it opens with, on that block only', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'status', kind: 'layout.list', binding: { kind: 'query' as const, name: 'views.invoice-statuses' } },
      { id: 'invoices', kind: 'layout.table', binding: { kind: 'query' as const, name: 'views.open-invoices' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [blocks[0], { ...blocks[1], defaultSelection: 'inv-1' }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }, { id: 'layout.list', label: 'List' }], zones: [] }} onChange={changed} />)
    expect(screen.getByLabelText('Block 2 default selection')).toHaveValue('inv-1')

    fireEvent.change(screen.getByLabelText('Block 1 default selection'), { target: { value: 'overdue' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], defaultSelection: 'overdue' }, { ...blocks[1], defaultSelection: 'inv-1' }] }))
    // Clearing the default removes it; the block then opens with no selection.
    fireEvent.change(screen.getByLabelText('Block 2 default selection'), { target: { value: '' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks }))
    expect(changed.mock.lastCall![0].blocks[1]).not.toHaveProperty('defaultSelection', '')
  })

  it('layout-auth-34: a block declares which other blocks on this surface its selection filters', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'status', kind: 'layout.table', binding: { kind: 'query' as const, name: 'views.invoice-statuses' } },
      { id: 'invoices', kind: 'layout.table', binding: { kind: 'query' as const, name: 'views.open-invoices' } },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure' as const, name: 'invoice.total' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ ...blocks[0], filterTargets: ['total'] }, blocks[1], blocks[2]] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)

    // The targets are the surface's other blocks; a block never filters itself.
    expect(screen.queryByLabelText('Block 1 filters Block 1')).toBeNull()
    expect(screen.getByLabelText('Block 1 filters Block 3')).toBeChecked()
    fireEvent.click(screen.getByLabelText('Block 1 filters Block 2'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], filterTargets: ['total', 'invoices'] }, blocks[1], blocks[2]] }))
    fireEvent.click(screen.getByLabelText('Block 1 filters Block 3'))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks }))
    expect(changed.mock.lastCall![0].blocks[0]).not.toHaveProperty('filterTargets', [])

    // Removing a block removes every edge to it, so no filter points outside the surface.
    fireEvent.click(screen.getByRole('button', { name: 'Remove block 3' }))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [blocks[0], blocks[1]] }))
  })

  it('layout-auth-35: the surface declares its drill-through targets by reference to released surfaces', () => {
    const changed = vi.fn()
    const value = { ...emptyLayoutAuthoringDraft(), drillThroughTargets: ['surface.supplier'] }
    render(<LayoutAuthoringEditor value={value} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], drillTargets: [{ id: 'surface.supplier', label: 'Supplier' }, { id: 'surface.payment', label: 'Payment' }] }} onChange={changed} />)

    expect(screen.getByLabelText('Drill through to Supplier')).toBeChecked()
    expect(screen.getByLabelText('Drill through to Payment')).not.toBeChecked()
    fireEvent.click(screen.getByLabelText('Drill through to Payment'))
    expect(changed).toHaveBeenLastCalledWith({ ...value, drillThroughTargets: ['surface.supplier', 'surface.payment'] })
    // Removing the last target leaves the surface with none, not an empty list.
    fireEvent.click(screen.getByLabelText('Drill through to Supplier'))
    expect(changed.mock.lastCall![0]).toStrictEqual(emptyLayoutAuthoringDraft())
  })

  it('layout-bound-3: a capture field picks its control from the registered field controls', () => {
    const changed = vi.fn()
    const blocks = [
      { id: 'amount', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.amount' }, capture: { required: true } },
      { id: 'notice', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'static' as const, name: 'Enter amounts in GBP' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], fieldControls: [{ id: 'currency', label: 'Currency' }, { id: 'text', label: 'Text' }] }} onChange={changed} />)

    // Only a captured field has a control; static content on a capture surface is offered none.
    expect(screen.getByLabelText('Block 2 required')).toBeInTheDocument()
    expect(screen.queryByLabelText('Block 2 field control')).toBeNull()
    const control = screen.getByLabelText('Block 1 field control')
    expect(within(control).getAllByRole('option').map(option => option.getAttribute('value'))).toEqual(['', 'currency', 'text'])
    fireEvent.change(control, { target: { value: 'currency' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ blocks: [{ ...blocks[0], capture: { required: true, control: 'currency' } }, blocks[1]] }))
    // The runtime default stores no control.
    fireEvent.change(control, { target: { value: '' } })
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual(blocks[0])
  })

  it('layout-bound-10: a field whose value domain picks its editor offers no authored control', () => {
    const blocks = [
      { id: 'status', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.status' } },
      { id: 'reference', kind: 'layout.table', intent: 'capture' as const, binding: { kind: 'record_field' as const, name: 'invoice.reference' } },
    ]
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], fieldControls: [{ id: 'text', label: 'Text' }], valueDomainFields: ['invoice.status'] }} onChange={vi.fn()} />)

    // The value domain's resolver picks the editor; Layout passes that choice through.
    expect(screen.queryByLabelText('Block 1 field control')).toBeNull()
    expect(screen.getByText('Block 1 control is chosen by its value domain')).toBeInTheDocument()
    expect(screen.getByLabelText('Block 2 field control')).toBeInTheDocument()
  })

  it('layout-auth-20: show_when is authored with the shared guided expression editor and lowered to Rules text (T-724 ruling 39)', () => {
    const changed = vi.fn()
    const guide = { kind: 'Call' as const, op: '==' as const, args: [{ kind: 'Ref' as const, name: 'field.status' }, { kind: 'Literal' as const, value: 'open', valueType: 'Text' as const }] }
    const guarded = { id: 'notice', kind: 'layout.table', binding: { kind: 'static' as const, name: 'Overdue' }, showWhen: { expression: '{"==":[{"var":"field.status"},"open"]}' }, showWhenGuide: guide }
    const unguarded = { id: 'total', kind: 'layout.table', binding: { kind: 'measure' as const, name: 'invoice.total' } }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [guarded, unguarded] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], guardReferences: [{ id: 'field.status', label: 'Status', valueType: 'Text' }] }} onChange={changed} />)

    // A block with no guard opens in the guided editor; the raw text box is not the default.
    expect(screen.getByLabelText('Block 2 show when expression shape')).toBeInTheDocument()
    expect(screen.queryByLabelText('Block 2 show when')).toBeNull()

    // Editing the guided expression stores the guide and the Rules text the shared engine compiles.
    fireEvent.change(screen.getByLabelText('Block 1 show when argument 2 literal value'), { target: { value: 'closed' } })
    const edited = changed.mock.lastCall![0].blocks[0]
    expect(edited.showWhen).toStrictEqual({ expression: '{"==":[{"var":"field.status"},"closed"]}' })
    expect(edited.showWhenGuide.args[1].value).toBe('closed')

    // Removing the guard removes both.
    fireEvent.click(screen.getByRole('button', { name: 'Remove block 1 show when' }))
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual({ id: 'notice', kind: 'layout.table', binding: { kind: 'static', name: 'Overdue' } })
  })

  it('layout-auth-20: raw Rules text stays available as the escape hatch (T-724 ruling 39)', () => {
    const changed = vi.fn()
    const guide = { kind: 'Ref' as const, name: 'field.flagged' }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'notice', kind: 'layout.table', binding: { kind: 'static', name: 'Flagged' }, showWhen: { expression: '{"var":"field.flagged"}' }, showWhenGuide: guide }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    expect(screen.queryByLabelText('Block 1 show when')).toBeNull()

    fireEvent.change(screen.getByLabelText('Block 1 show when authoring'), { target: { value: 'raw' } })
    expect(screen.getByLabelText('Block 1 show when')).toHaveValue('{"var":"field.flagged"}')
    fireEvent.change(screen.getByLabelText('Block 1 show when'), { target: { value: '{"!":[{"var":"field.flagged"}]}' } })
    // Raw text is stored verbatim and the guide, which no longer describes it, is dropped.
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual({ id: 'notice', kind: 'layout.table', binding: { kind: 'static', name: 'Flagged' }, showWhen: { expression: '{"!":[{"var":"field.flagged"}]}' } })
  })

  it('layout-ck-29: show_when cites a catalogue predicate by exact pin, and the guard holds exactly one form', () => {
    const changed = vi.fn()
    const pin = { name: 'invoice.overdue', version: '1.0.0', digest: 'a'.repeat(64) }
    const block = { id: 'notice', kind: 'layout.table', binding: { kind: 'static' as const, name: 'Overdue' } }
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ ...block, showWhen: { expression: '{"var":"field.flagged"}' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [], predicates: [{ label: 'Overdue', pin }] }} onChange={changed} />)

    fireEvent.change(screen.getByLabelText('Block 1 show when authoring'), { target: { value: 'predicate' } })
    fireEvent.change(screen.getByLabelText('Block 1 show when predicate'), { target: { value: 'invoice.overdue@1.0.0' } })
    // Picking a predicate replaces the expression: the stored guard carries the whole pin and nothing else.
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual({ ...block, showWhen: { predicate: pin } })
    fireEvent.change(screen.getByLabelText('Block 1 show when predicate'), { target: { value: '' } })
    expect(changed.mock.lastCall![0].blocks[0]).toStrictEqual(block)
  })

  function Authoring({ initial, onDraft }: { initial: LayoutAuthoringDraft; onDraft: (draft: LayoutAuthoringDraft) => void }) {
    const [value, setValue] = useState(initial)
    return <LayoutAuthoringEditor value={value} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={next => { setValue(next); onDraft(next) }} />
  }

  it('layout-auth-18: a repeating block authored here gets the container admission requires, and matches the shared admitted fixture (T-724 ruling 41)', () => {
    const drafts: LayoutAuthoringDraft[] = []
    render(<Authoring initial={{ ...emptyLayoutAuthoringDraft(), blocks: [
      { id: 'lines', kind: 'layout.table', binding: { kind: 'query', name: 'views.invoice-lines' } },
      { id: 'amount', kind: 'layout.table', binding: { kind: 'record_field', name: 'line.amount' } },
    ] }} onDraft={draft => drafts.push(draft)} />)
    expect(screen.queryByLabelText('Block 1 container')).toBeNull()

    fireEvent.click(screen.getByLabelText('Block 1 repeats per row'))
    fireEvent.change(screen.getByLabelText('Block 2 parent'), { target: { value: 'lines' } })
    // builder-definitions admits exactly these blocks (LayoutBoundRegisterTests, same fixture).
    expect(JSON.parse(JSON.stringify(drafts.at(-1)!.blocks))).toStrictEqual(authored.blocks)

    // The container is offered once a block repeats or has children, and can be changed, never removed.
    const container = screen.getByLabelText('Block 1 container')
    expect(within(container).getAllByRole('option').map(option => option.getAttribute('value'))).toEqual(['stack', 'flow', 'areas'])
    expect(screen.queryByLabelText('Block 2 container')).toBeNull()
    fireEvent.change(container, { target: { value: 'flow' } })
    expect(drafts.at(-1)!.blocks[0].container).toBe('flow')
  })

  it('layout-auth-18: placing a block inside another gives the new parent a default container (T-724 ruling 41)', () => {
    const drafts: LayoutAuthoringDraft[] = []
    render(<Authoring initial={{ ...emptyLayoutAuthoringDraft(), blocks: [
      { id: 'group', kind: 'layout.table', binding: { kind: 'static', name: 'Totals' } },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure', name: 'invoice.total' } },
    ] }} onDraft={draft => drafts.push(draft)} />)
    fireEvent.change(screen.getByLabelText('Block 2 parent'), { target: { value: 'group' } })
    expect(drafts.at(-1)!.blocks).toStrictEqual([
      { id: 'group', kind: 'layout.table', binding: { kind: 'static', name: 'Totals' }, container: 'stack' },
      { id: 'total', kind: 'layout.table', binding: { kind: 'measure', name: 'invoice.total' }, parentId: 'group' },
    ])
  })

  it('authors static content on the block rather than looking it up', () => {
    const changed = vi.fn()
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'notice', kind: 'layout.table', binding: { kind: 'static', name: '' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    expect(screen.queryByLabelText('Block 1 binding name')).toBeNull()
    fireEvent.change(screen.getByLabelText('Block 1 binding content'), { target: { value: 'Registered office: Leeds' } })
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ blocks: [expect.objectContaining({ binding: { kind: 'static', name: 'Registered office: Leeds' } })] }))
  })
})
