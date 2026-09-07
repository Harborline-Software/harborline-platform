import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { Table } from '../Table'
import { fixture, qualityCases } from './fixtures'
import {
  createTierCRows,
  renderTierCBody,
  tierCCellsPerRow,
  tierCReplacementCount,
  tierCRowCount,
} from './tier-c'

describe('Table Tier-C structural proof', () => {
  it('renders exactly 256 four-cell rows without inventing virtualization', () => {
    fixture(qualityCases, 'table.performance.large-data')
    render(<Table aria-label="Tier C data">{renderTierCBody(createTierCRows(0))}</Table>)
    expect(screen.getAllByRole('row')).toHaveLength(tierCRowCount)
    expect(screen.getAllByRole('cell')).toHaveLength(tierCRowCount * tierCCellsPerRow)
    expect(document.querySelector('[data-row-id="row-0"]')).toHaveTextContent('r0-row0-cell0')
    expect(document.querySelector('[data-row-id="row-255"]')).toHaveTextContent('r0-row255-cell3')
    expect(document.querySelector('[data-virtualized]')).toBeNull()
  }, 30_000)

  it('keeps only the latest complete content across 96 deterministic replacements', () => {
    fixture(qualityCases, 'table.performance.repeated-update')
    const rendered = render(<Table aria-label="Tier C updates">{renderTierCBody(createTierCRows(0))}</Table>)

    for (let revision = 1; revision <= tierCReplacementCount; revision += 1) {
      rendered.rerender(
        <Table aria-label="Tier C updates">{renderTierCBody(createTierCRows(revision))}</Table>,
      )
    }

    const rows = rendered.container.querySelectorAll('tbody > tr')
    const cells = rendered.container.querySelectorAll('tbody > tr > td')
    expect(rows).toHaveLength(tierCRowCount)
    expect(cells).toHaveLength(tierCRowCount * tierCCellsPerRow)
    expect([...cells].every(cell => cell.textContent?.startsWith(`r${tierCReplacementCount}-`) === true)).toBe(true)
    expect(screen.queryByText('r95-row0-cell0')).toBeNull()
  }, 30_000)
})
