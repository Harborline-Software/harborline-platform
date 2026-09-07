import { createRef } from 'react'

import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '../Table'
import { fixture, sharedCases } from './fixtures'

function SampleTable({ density = 'md' as const }) {
  return (
    <Table density={density}>
      <TableCaption>Capability provenance</TableCaption>
      <TableHead>
        <TableRow><TableHeaderCell>Name</TableHeaderCell><TableHeaderCell>State</TableHeaderCell></TableRow>
      </TableHead>
      <TableBody>
        <TableRow><TableCell>A</TableCell><TableCell>Ready</TableCell></TableRow>
        <TableRow><TableCell>B</TableCell><TableCell>Held</TableCell></TableRow>
      </TableBody>
    </Table>
  )
}

describe('Table shared fixtures', () => {
  it('table.semantic-elements', () => {
    fixture(sharedCases, 'table.semantic-elements')
    const rendered = render(<SampleTable />)
    const table = screen.getByRole('table', { name: 'Capability provenance' })
    expect(table.tagName).toBe('TABLE')
    expect(rendered.container.querySelectorAll('thead')).toHaveLength(1)
    expect(rendered.container.querySelectorAll('tbody')).toHaveLength(1)
    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(screen.getAllByRole('columnheader')).toHaveLength(2)
    expect(screen.getAllByRole('cell')).toHaveLength(4)
    expect(screen.getAllByRole('row').slice(1).map(row => row.textContent)).toEqual(['AReady', 'BHeld'])
  })

  it('table.header-scope', () => {
    fixture(sharedCases, 'table.header-scope')
    render(
      <Table><TableBody><TableRow>
        <TableHeaderCell>Column</TableHeaderCell>
        <TableHeaderCell scope="row">Row</TableHeaderCell>
      </TableRow></TableBody></Table>,
    )
    expect(screen.getByRole('columnheader', { name: 'Column' })).toHaveAttribute('scope', 'col')
    expect(screen.getByRole('rowheader', { name: 'Row' })).toHaveAttribute('scope', 'row')
  })

  it('table.caption', () => {
    fixture(sharedCases, 'table.caption')
    render(<Table><TableCaption>Capability provenance</TableCaption><TableBody /></Table>)
    expect(screen.getByText('Capability provenance').tagName).toBe('CAPTION')
    expect(screen.getByRole('table')).toHaveAccessibleName('Capability provenance')
  })

  it('table.density-default', () => {
    fixture(sharedCases, 'table.density-default')
    render(<SampleTable />)
    expect(screen.getByRole('table')).toHaveAttribute('data-hl-density', 'md')
    screen.getAllByRole('columnheader').forEach(cell => expect(cell).toHaveAttribute('data-hl-density', 'md'))
    screen.getAllByRole('cell').forEach(cell => expect(cell).toHaveAttribute('data-hl-density', 'md'))
  })

  it('table.density-small', () => {
    fixture(sharedCases, 'table.density-small')
    render(<SampleTable density="sm" />)
    expect(screen.getByRole('table')).toHaveAttribute('data-hl-density', 'sm')
    screen.getAllByRole('columnheader').forEach(cell => expect(cell).toHaveAttribute('data-hl-density', 'sm'))
    screen.getAllByRole('cell').forEach(cell => expect(cell).toHaveAttribute('data-hl-density', 'sm'))
  })

  it('table.host-attributes', () => {
    fixture(sharedCases, 'table.host-attributes')
    const ref = createRef<HTMLTableElement>()
    const onClick = vi.fn()
    const rendered = render(
      <Table aria-label="Data" className="consumer" data-case="table" onClick={onClick} ref={ref}>
        <TableBody><TableRow><TableCell colSpan={2} data-case="cell">value</TableCell></TableRow></TableBody>
      </Table>,
    )
    const table = screen.getByRole('table', { name: 'Data' })
    const cell = screen.getByRole('cell')
    const wrapper = rendered.container.querySelector('.hl-table__overflow')
    expect(ref.current).toBe(table)
    expect(table).toHaveClass('hl-table', 'consumer')
    expect(table).toHaveAttribute('data-case', 'table')
    expect(cell).toHaveAttribute('colspan', '2')
    expect(cell).toHaveAttribute('data-case', 'cell')
    expect(wrapper).not.toHaveClass('consumer')
    expect(wrapper).not.toHaveAttribute('data-case', 'table')
    table.click()
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('table.overflow-wrapper', () => {
    fixture(sharedCases, 'table.overflow-wrapper')
    const rendered = render(<SampleTable />)
    const table = screen.getByRole('table')
    const wrapper = table.parentElement
    expect(wrapper).toBe(rendered.container.querySelector('.hl-table__overflow'))
    expect(wrapper).toHaveAttribute('data-hl-presentational', 'true')
    expect(wrapper).not.toHaveAttribute('role')
    expect(wrapper).not.toHaveAttribute('tabindex')
  })

  it('table.ordered-replacement', () => {
    fixture(sharedCases, 'table.ordered-replacement')
    const rendered = render(
      <Table><TableBody>{['A', 'B'].map(value => <TableRow key={value}><TableCell>{value}</TableCell></TableRow>)}</TableBody></Table>,
    )
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['A', 'B'])
    rendered.rerender(<Table><TableBody><TableRow><TableCell>latest</TableCell></TableRow></TableBody></Table>)
    expect(screen.getAllByRole('cell').map(cell => cell.textContent)).toEqual(['latest'])
    expect(screen.queryByText('A')).toBeNull()
    expect(screen.queryByText('B')).toBeNull()
  })

  it('table.projection-equivalence', () => {
    fixture(sharedCases, 'table.projection-equivalence')
    render(<SampleTable density="sm" />)
    expect(screen.getByRole('table')).toHaveAttribute('data-hl-density', 'sm')
    expect(screen.getByRole('table').querySelectorAll('thead > tr > th')).toHaveLength(2)
    expect(screen.getByRole('table').querySelectorAll('tbody > tr > td')).toHaveLength(4)
    expect(screen.getByRole('table').querySelector('caption')).toHaveTextContent('Capability provenance')
  })
})
