import { fireEvent, render, screen } from '@testing-library/react'
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

  it('binds a block to any of the five kinds and names it from the catalogue', () => {
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

  it('marks an unbound block as needing a binding instead of removing it', () => {
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'orphan', kind: 'layout.table', binding: { kind: 'query', name: '' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={vi.fn()} />)
    expect(screen.getByRole('status')).toHaveTextContent('Block 1 needs a binding')
    expect(screen.getByLabelText('Block 1 binding kind')).toBeInTheDocument()
  })

  it('authors static content on the block rather than looking it up', () => {
    const changed = vi.fn()
    render(<LayoutAuthoringEditor value={{ ...emptyLayoutAuthoringDraft(), blocks: [{ id: 'notice', kind: 'layout.table', binding: { kind: 'static', name: '' } }] }} catalogue={{ blockKinds: [{ id: 'layout.table', label: 'Table' }], zones: [] }} onChange={changed} />)
    expect(screen.queryByLabelText('Block 1 binding name')).toBeNull()
    fireEvent.change(screen.getByLabelText('Block 1 binding content'), { target: { value: 'Registered office: Leeds' } })
    expect(changed).toHaveBeenCalledWith(expect.objectContaining({ blocks: [expect.objectContaining({ binding: { kind: 'static', name: 'Registered office: Leeds' } })] }))
  })
})
