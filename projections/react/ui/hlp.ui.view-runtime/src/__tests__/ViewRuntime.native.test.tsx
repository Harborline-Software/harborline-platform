import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { ViewRuntime } from '../ViewRuntime'
import type { ViewRenderPlan, ViewRuntimeRow } from '../ViewRuntime.types'
import { ViewAuthoringEditor, emptyViewAuthoringDraft } from '../ViewAuthoringEditor'
import type { ViewAuthoringCatalogue } from '../ViewAuthoringEditor.types'

const fixture = JSON.parse(readFileSync(resolve(process.cwd(), '../../../../conformance/hlp.ui.view-runtime/fixtures.yaml'), 'utf8')) as {
  cases: readonly { input: { plan: ViewRenderPlan; rows?: readonly ViewRuntimeRow[]; empty?: string; activateActions?: readonly string[] }; expected: { nodes?: number; columns?: readonly string[]; rowCount?: number; content?: string; actions?: readonly string[]; activated?: readonly string[] } }[]
}

const plan: ViewRenderPlan = { definitionHash: 'sha256:view-assets', definitionId: 'view-assets', definitionVersion: '1', packKey: 'harborline.platform', packVersion: '1.0.0', definitionKind: 'ViewDefinition', bindings: { viewKind: 'layout.table', parameters: { fields: [{ id: 'asset', label: 'Asset' }, { id: 'status', label: 'Status' }, { id: 'owner', label: 'Owner' }] } } }
const rows: readonly ViewRuntimeRow[] = [{ id: 'a1', asset: 'Pier', status: 'Open', owner: 'Riley' }, { id: 'a2', asset: 'Pump', status: 'Review', owner: 'Morgan' }]
const seededFormsList: ViewRenderPlan = { ...plan, definitionId: 'view-forms' }
const catalogue = new Map([[`${seededFormsList.definitionId}@${seededFormsList.definitionVersion}`, { definitionId: seededFormsList.definitionId, definitionVersion: seededFormsList.definitionVersion, packKey: seededFormsList.packKey }]])

describe('ViewRuntime React projection', () => {
  it('authors every Views grammar section from nothing and omits unavailable shapes', () => {
    const catalogue: ViewAuthoringCatalogue = {
      recordTypes: [{ id: 'asset', label: 'Asset' }],
      viewKinds: [{ id: 'layout.table', label: 'Table' }],
      fields: [{ id: 'name', label: 'Name' }, { id: 'status', label: 'Status' }],
      measures: [{ id: 'asset.count', label: 'Asset count' }],
      widgets: [{ id: 'metric', label: 'Metric' }],
      rowActions: [{ id: 'record.open', label: 'Open record' }],
    }
    const changed = vi.fn()
    render(<ViewAuthoringEditor value={emptyViewAuthoringDraft()} catalogue={catalogue} onChange={changed} />)

    for (const name of ['View name', 'Record type', 'Shape', 'Columns', 'Column treatment', 'Sort', 'Group by', 'Filter predicate', 'Shape roles', 'Measured by', 'Dashboard widget', 'Row behaviour', 'Density', 'Who it belongs to']) {
      expect(screen.getByText(name)).toBeInTheDocument()
    }
    expect(screen.getByRole('option', { name: 'Table' })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Board' })).toBeNull()

    fireEvent.change(screen.getByRole('textbox', { name: 'View name' }), { target: { value: 'Asset health' } })
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ name: 'Asset health' }))
    fireEvent.click(screen.getByRole('button', { name: 'Add column' }))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ columns: [{ field: 'name', width: 160, presentation: 'text' }] }))
    fireEvent.click(screen.getByRole('button', { name: 'Add sort' }))
    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ sorts: [{ field: 'name', direction: 'ascending' }] }))
  })
  it('retains the authored column width when a change is below the declared minimum', () => {
    const changed = vi.fn()
    const draft = { ...emptyViewAuthoringDraft(), columns: [{ field: 'name', width: 160, presentation: 'text' }] }
    render(<ViewAuthoringEditor value={draft} catalogue={{ recordTypes: [], viewKinds: [], fields: [], measures: [], widgets: [], rowActions: [] }} onChange={changed} />)

    fireEvent.change(screen.getByRole('spinbutton', { name: 'Column 1 width' }), { target: { value: '0' } })

    expect(changed).toHaveBeenLastCalledWith(expect.objectContaining({ columns: [{ field: 'name', width: 160, presentation: 'text' }] }))
  })
  it('groups declared actions as small secondary Buttons in plan order', () => {
    const activated: string[] = []
    render(<ViewRuntime plan={{ ...plan, bindings: { ...plan.bindings, actions: [{ id: 'publish', label: 'Publish' }, { id: 'revise', label: 'Revise' }] } }} rows={[]} onAction={id => activated.push(id)} />)
    const group = screen.getByRole('group', { name: 'View results' })
    expect(group).toHaveClass('hl-view-runtime__actions')
    const buttons = [...group.querySelectorAll('button')]
    expect(buttons.map(button => button.textContent)).toEqual(['Publish', 'Revise'])
    for (const button of buttons) {
      expect(button).toHaveAttribute('type', 'button')
      expect(button).toHaveAttribute('data-hl-intent', 'secondary')
      expect(button).toHaveAttribute('data-hl-size', 'sm')
      expect(button).toBeEnabled()
    }
    fireEvent.click(buttons[1])
    fireEvent.click(buttons[0])
    expect(activated).toEqual(['revise', 'publish'])
  })

  it('omits the action group when the plan declares no actions', () => {
    render(<ViewRuntime plan={plan} rows={rows} />)
    expect(screen.queryByRole('group')).toBeNull()
  })

  it('disables declared actions until the host enables them again', () => {
    const activated: string[] = []
    const actionPlan = { ...plan, bindings: { ...plan.bindings, actions: [{ id: 'publish', label: 'Publish' }] } }
    const view = render(<ViewRuntime plan={actionPlan} rows={rows} actionsDisabled onAction={id => activated.push(id)} />)
    const button = screen.getByRole('button', { name: 'Publish' })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('aria-disabled', 'true')
    fireEvent.click(button)
    expect(activated).toEqual([])
    view.rerender(<ViewRuntime plan={actionPlan} rows={rows} onAction={id => activated.push(id)} />)
    expect(button).toBeEnabled()
    fireEvent.click(button)
    expect(activated).toEqual(['publish'])
  })

  it('renders every shared fixture from its compiled plan input', () => {
    for (const scenario of fixture.cases) {
      const activated: string[] = []
      const { container, unmount } = render(<ViewRuntime
        plan={scenario.input.plan as ViewRenderPlan}
        rows={(scenario.input.rows ?? []) as readonly ViewRuntimeRow[]}
        empty={'empty' in scenario.input ? scenario.input.empty : undefined}
        onAction={id => activated.push(id)}
      />)
      if ('nodes' in scenario.expected) expect(container.childNodes).toHaveLength(scenario.expected.nodes)
      if ('columns' in scenario.expected) expect(screen.getAllByRole('columnheader').map(node => node.textContent)).toEqual(scenario.expected.columns)
      if ('rowCount' in scenario.expected) expect(container.querySelectorAll('[data-row-id]')).toHaveLength(scenario.expected.rowCount)
      if ('content' in scenario.expected) expect(container).toHaveTextContent(scenario.expected.content)
      const buttons = screen.queryAllByRole('button')
      expect(buttons.map(button => button.textContent)).toEqual(scenario.expected.actions ?? [])
      for (const id of scenario.input.activateActions ?? []) {
        const action = scenario.input.plan.bindings.actions!.find(candidate => candidate.id === id)!
        const button = screen.getByRole('button', { name: action.label })
        expect(button).toHaveAttribute('type', 'button')
        fireEvent.click(button)
      }
      expect(activated).toEqual(scenario.expected.activated ?? [])
      unmount()
    }
  })
  it('maps the seeded Forms list through one source-map accessor that round-trips to its catalogue definition', () => {
    render(<ViewRuntime plan={seededFormsList} rows={rows} />)
    expect(screen.getAllByRole('columnheader').map(node => node.textContent)).toEqual(['Asset', 'Status', 'Owner'])
    expect(screen.getAllByRole('row')).toHaveLength(3)
    const runtime = document.querySelector('.hl-view-runtime')
    const source = JSON.parse(runtime?.getAttribute('data-definition-source') ?? '')
    expect(source).toEqual({ definitionId: 'view-forms', definitionVersion: '1', packKey: 'harborline.platform' })
    expect(catalogue.get(`${source.definitionId}@${source.definitionVersion}`)).toEqual(source)
    expect(runtime).not.toHaveAttribute('data-definition-id')
    expect(runtime).not.toHaveAttribute('data-definition-version')
    expect(runtime).toHaveAttribute('title', `Definition source: ${JSON.stringify(source)}`)
  })
  it('keeps plan provenance at the React projection boundary without mutating the supplied artifact', () => {
    const artifact = Object.freeze({ ...plan, bindings: Object.freeze({ ...plan.bindings }) })
    render(<ViewRuntime plan={artifact} rows={rows} />)
    expect(artifact).toEqual(plan)
  })
  it('renders unknown kinds inertly and without console output', () => {
    const consoleSpies = (['log', 'info', 'warn', 'error', 'debug'] as const)
      .map(method => vi.spyOn(console, method).mockImplementation(() => undefined))
    try {
      const { container } = render(<ViewRuntime plan={{ ...plan, bindings: { ...plan.bindings, viewKind: 'views.unknown' } }} rows={rows} />)
      expect(container).toBeEmptyDOMElement()
      for (const consoleSpy of consoleSpies) expect(consoleSpy).not.toHaveBeenCalled()
    } finally {
      for (const consoleSpy of consoleSpies) consoleSpy.mockRestore()
    }
  })
  it('preserves the known grid definition when the caller supplies an empty row set', () => {
    render(<ViewRuntime plan={plan} empty="No matching assets." rows={[]} />)
    expect(screen.getAllByRole('columnheader')).toHaveLength(3)
    expect(screen.getByRole('gridcell')).toHaveTextContent('No matching assets.')
  })
  it('normalizes missing, null, and non-string row values before passing them to data-grid', () => {
    render(<ViewRuntime plan={plan} rows={[{ id: 'a1', asset: null, status: 42 }]} />)
    expect(screen.getAllByRole('gridcell').map(node => node.textContent)).toEqual(['', '42', ''])
  })
  it('forwards the shared row activation action', () => {
    const activated = vi.fn()
    render(<ViewRuntime plan={plan} rows={rows} onRowActivate={activated} />)
    fireEvent.doubleClick(document.querySelector('[data-row-id="a1"]')!)
    expect(activated).toHaveBeenCalledWith('a1')
  })
  it('preserves long caller content for the delegated grid', () => {
    const content = 'A caller-owned value that is deliberately long enough to exercise the runtime handoff.'
    render(<ViewRuntime plan={plan} rows={[{ id: 'a1', asset: content, status: 'Open', owner: 'Riley' }]} />)
    expect(screen.getByRole('gridcell', { name: content })).toHaveTextContent(content)
  })
  it('does not submit an enclosing form when an action has no handler', () => {
    const submitted = vi.fn(event => event.preventDefault())
    render(<form onSubmit={submitted}><ViewRuntime plan={{ ...plan, bindings: { ...plan.bindings, actions: [{ id: 'action', label: 'Act' }] } }} rows={[]} /></form>)
    fireEvent.click(screen.getByRole('button', { name: 'Act' }))
    expect(submitted).not.toHaveBeenCalled()
  })
})
