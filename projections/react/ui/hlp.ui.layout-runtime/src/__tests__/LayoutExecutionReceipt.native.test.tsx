import { act, fireEvent, render, screen } from '@testing-library/react'
import observation from '../../../../../../_shared/layout/execution-observation.json'
import type { LayoutAccessTrace, LayoutRunReceipt } from '../LayoutRuntime.types'
import { LayoutExecutionReceipt } from '../LayoutExecutionReceipt'

// The one platform model: LayoutExecutionObservationTests derives these traces from the same fixture's reads.
const receipt = observation.receipt as LayoutRunReceipt
const traces = observation.traces as Record<string, LayoutAccessTrace>

// jsdom fires `toggle` itself when `open` changes, as a browser does.
async function toggle(details: HTMLDetailsElement, open: boolean) {
  await act(async () => { details.open = open; await new Promise(resolve => setTimeout(resolve, 0)) })
}

describe('LayoutExecutionReceipt React projection', () => {
  it("layout-eng-30: renders the receipt's run identity, status, execution trace and Access's four ordered, versioned stages from the shared fixture", async () => {
    const read = vi.fn(async () => traces.valid)
    const { container } = render(<LayoutExecutionReceipt receipt={receipt} readAccessTrace={read} />)
    expect(container.querySelector('[data-layout-run-id]')).toHaveTextContent(receipt.runId)
    expect(container.querySelector('[data-layout-run-status]')).toHaveTextContent(receipt.status)
    expect([...container.querySelectorAll('[data-layout-trace-ordinal]')].map(step => step.textContent)).toEqual(['submitted: succeeded', 'approval: succeeded', 'posted: succeeded'])
    expect(container.querySelector('[data-layout-access-decision]')).toHaveTextContent(receipt.accessDecisionId!)
    await toggle(container.querySelector('details')!, true)
    expect(read).toHaveBeenCalledWith(receipt.accessDecisionId)
    expect([...container.querySelectorAll('[data-layout-access-stage]')].map(stage => stage.getAttribute('data-layout-access-stage'))).toEqual(['act', 'effective-roles', 'standings', 'verdict'])
    expect(container.querySelector('[data-layout-access-version]')).toHaveTextContent('Version 2')
    expect(container.querySelector('[data-layout-deciding-grant]')).toHaveTextContent('Deciding grant: grant-163@1')
  })

  it('layout-eng-30: the Access trace is read only when its disclosure opens; closing triggers no read, and a failed read retries', async () => {
    const read = vi.fn<(id: string) => Promise<LayoutAccessTrace>>().mockRejectedValueOnce(new Error('offline')).mockResolvedValue(traces.valid)
    const { container } = render(<LayoutExecutionReceipt receipt={receipt} readAccessTrace={read} />)
    const details = container.querySelector('details')!
    expect(read).not.toHaveBeenCalled()
    await toggle(details, true)
    expect(screen.getByRole('alert')).toHaveTextContent('Unable to read the authorization trace.')
    await toggle(details, false)
    expect(read).toHaveBeenCalledTimes(1)
    await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Retry trace read' })) })
    expect(read).toHaveBeenCalledTimes(2)
    expect(container.querySelector('[data-layout-access-evidence=valid]')).toBeInTheDocument()
    await toggle(details, false)
    await toggle(details, true)
    expect(read).toHaveBeenCalledTimes(2)
  })

  it.each(['missing', 'forbidden', 'malformed'])('layout-eng-30: %s evidence renders distinctly from absent, the other evidence and valid', async evidence => {
    const { container } = render(<LayoutExecutionReceipt receipt={receipt} readAccessTrace={async () => traces[evidence]} />)
    await toggle(container.querySelector('details')!, true)
    const shown = container.querySelector('[data-layout-access-evidence]')!
    expect(shown.getAttribute('data-layout-access-evidence')).toBe(evidence)
    expect(container.querySelector('[data-layout-access-stage], [data-layout-deciding-grant]')).toBeNull()
    const texts = new Set([render(<LayoutExecutionReceipt receipt={observation.absentReceipt as LayoutRunReceipt} readAccessTrace={vi.fn()} />).container.querySelector('[data-layout-access-evidence]')!.textContent])
    for (const other of ['missing', 'forbidden', 'malformed']) {
      const view = render(<LayoutExecutionReceipt receipt={receipt} readAccessTrace={async () => traces[other]} />)
      await toggle(view.container.querySelector('details')!, true)
      texts.add(view.container.querySelector('[data-layout-access-evidence]')!.textContent)
    }
    expect(texts.size).toBe(4)
  })

  it('layout-eng-30: no recorded decision is absence: no disclosure, no read', () => {
    const read = vi.fn()
    const { container } = render(<LayoutExecutionReceipt receipt={observation.absentReceipt as LayoutRunReceipt} readAccessTrace={read} />)
    expect(container.querySelector('[data-layout-run-id]')).toHaveTextContent('run.index-rebuild.0002')
    expect(container.querySelector('details')).toBeNull()
    expect(container.querySelector('[data-layout-access-evidence=absent]')).toBeInTheDocument()
    expect(read).not.toHaveBeenCalled()
  })

  it('layout-eng-30: mixed deciding facts render the declared grant, never the first generic deciding prefix', async () => {
    const { container } = render(<LayoutExecutionReceipt receipt={receipt} readAccessTrace={async () => traces.mixed} />)
    await toggle(container.querySelector('details')!, true)
    expect(container.querySelector('[data-layout-deciding-grant]')).toHaveTextContent('Deciding grant: grant-164@3')
  })
})
