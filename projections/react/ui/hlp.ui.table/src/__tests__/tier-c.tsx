import type { ReactElement } from 'react'

import { TableBody, TableCell, TableRow } from '../Table'

export const tierCRowCount = 256
export const tierCCellsPerRow = 4
export const tierCReplacementCount = 96

export interface TierCRow {
  id: string
  cells: readonly [string, string, string, string]
}

export function createTierCRows(revision: number): TierCRow[] {
  return Array.from({ length: tierCRowCount }, (_, rowIndex) => ({
    id: `row-${rowIndex}`,
    cells: [0, 1, 2, 3].map(cellIndex => `r${revision}-row${rowIndex}-cell${cellIndex}`) as TierCRow['cells'],
  }))
}

export function renderTierCBody(rows: readonly TierCRow[]): ReactElement {
  return (
    <TableBody>
      {rows.map(row => (
        <TableRow data-row-id={row.id} key={row.id}>
          {row.cells.map((cell, cellIndex) => (
            <TableCell data-cell-index={cellIndex} key={cellIndex}>{cell}</TableCell>
          ))}
        </TableRow>
      ))}
    </TableBody>
  )
}
