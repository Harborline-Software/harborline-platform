import { createRef } from 'react'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { Table, TableBody, TableCaption, TableCell, TableHeaderCell, TableRow } from '../Table'
import { qualityCases } from './fixtures'

describe('Table React projection', () => {
  it('consumes every frozen quality and performance case', () => {
    expect([...new Set(qualityCases.map(value => value.id))]).toEqual([
      'table.quality.native',
      'table.quality.caption',
      'table.quality.keyboard',
      'table.quality.focus',
      'table.quality.reflow',
      'table.quality.caller-copy',
      'table.quality.rtl',
      'table.quality.pseudo',
      'table.quality.light-dark',
      'table.quality.tokens',
      'table.quality.forced-colors',
      'table.quality.reduced-motion',
      'table.quality.visual-parity',
      'table.performance.large-data',
      'table.performance.repeated-update',
    ])
  })

  it('does not invent a grid role, tab stop, keyboard model, or dismissal behavior', () => {
    render(<Table aria-label="Data"><TableBody><TableRow><TableCell>value</TableCell></TableRow></TableBody></Table>)
    const table = screen.getByRole('table', { name: 'Data' })
    expect(table).not.toHaveAttribute('role', 'grid')
    expect(table).not.toHaveAttribute('tabindex')
    expect(table).not.toHaveAttribute('aria-modal')
    expect(table).not.toHaveAttribute('aria-expanded')
  })

  it('preserves native focus and activation for interactive descendants', async () => {
    const onClick = vi.fn()
    render(
      <Table aria-label="Actions"><TableBody><TableRow><TableCell>
        <button onClick={onClick} type="button">Open</button>
      </TableCell></TableRow></TableBody></Table>,
    )
    const user = userEvent.setup()
    await user.tab()
    expect(screen.getByRole('button', { name: 'Open' })).toHaveFocus()
    await user.keyboard('{Enter}')
    expect(onClick).toHaveBeenCalledOnce()
  })

  it('forwards refs and opaque localized caption and cell content', () => {
    const captionRef = createRef<HTMLTableCaptionElement>()
    const cellRef = createRef<HTMLTableCellElement>()
    render(
      <Table dir="rtl">
        <TableCaption ref={captionRef}>سجل القدرات الموسّع</TableCaption>
        <TableBody><TableRow><TableHeaderCell scope="row">الاسم</TableHeaderCell><TableCell ref={cellRef}>جاهز</TableCell></TableRow></TableBody>
      </Table>,
    )
    expect(captionRef.current).toHaveTextContent('سجل القدرات الموسّع')
    expect(cellRef.current).toHaveTextContent('جاهز')
    expect(screen.getByRole('table')).toHaveAttribute('dir', 'rtl')
    expect(screen.getByRole('rowheader', { name: 'الاسم' })).toHaveAttribute('scope', 'row')
  })

  it('uses logical, contained-overflow, token, dark-theme, forced-color, and reduced-motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-table-foreground')
    expect(css).toContain('min-inline-size: 0')
    expect(css).toContain('max-inline-size: 100%')
    expect(css).toContain('overflow-x: auto')
    expect(css).toContain('padding-inline')
    expect(css).toContain('border-block-end')
    expect(css).toContain('text-align: start')
    expect(css).toContain("[data-theme='dark'] .hl-table__overflow")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
    expect(css).toMatch(/prefers-reduced-motion:[\s\S]*transition: none/)
  })
})
