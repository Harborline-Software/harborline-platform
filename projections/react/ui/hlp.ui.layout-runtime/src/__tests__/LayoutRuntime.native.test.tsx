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
})
