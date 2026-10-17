import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'

import { Gantt } from '../Gantt'
import type { GanttTask } from '../Gantt.types'

const tasks: readonly GanttTask[] = [
  { id: 'a', title: 'Inspect hull', start: '2026-08-01', end: '2026-08-03', progress: 45 },
  { id: 'b', title: 'Repair deck', start: '2026-08-04', end: '2026-08-06' },
]

function cell(position: string): HTMLElement {
  const element = document.querySelector<HTMLElement>(`[data-gantt-cell="${position}"]`)
  if (!element) throw new Error(`Missing Gantt cell: ${position}`)
  return element
}

function expectTabIndexes(expected: readonly number[]): void {
  expect([...document.querySelectorAll<HTMLElement>('[data-gantt-cell]')].map(element => element.tabIndex)).toEqual(expected)
}

describe('Gantt literal pane and keyboard behavior', () => {
  it('renders distinct literal title, start, end, and progress cells and replaces custom columns', () => {
    const rendered = render(
      <HarborlineLocaleProvider locale="en-US">
        <Gantt accessibleName="Dock schedule" columns={[
          { field: 'title', title: 'Task' },
          { field: 'start', title: 'Begins' },
          { field: 'end', title: 'Ends' },
          { field: 'progress', title: 'Done' },
        ]} tasks={tasks} />
      </HarborlineLocaleProvider>,
    )

    // Oracle: interface calendar-date/progress semantics and explicit en-US Gregorian date literals.
    const firstRow = screen.getByRole('row', { name: /Inspect hull/ })
    const secondRow = screen.getByRole('row', { name: /Repair deck/ })
    expect(within(firstRow).getAllByRole('cell').slice(0, 4).map(element => element.textContent)).toEqual([
      'Inspect hull', 'Aug 1, 2026', 'Aug 3, 2026', '45%',
    ])
    expect(within(secondRow).getAllByRole('cell').slice(0, 4).map(element => element.textContent)).toEqual([
      'Repair deck', 'Aug 4, 2026', 'Aug 6, 2026', '0%',
    ])

    rendered.rerender(
      <HarborlineLocaleProvider locale="en-US">
        <Gantt accessibleName="Dock schedule" columns={[{ field: 'progress', title: 'Completion' }]} tasks={tasks} />
      </HarborlineLocaleProvider>,
    )

    expect(screen.getAllByRole('columnheader')).toHaveLength(2)
    expect(screen.getByRole('columnheader', { name: 'Completion' })).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Begins' })).toBeNull()
    expect(within(screen.getByRole('row', { name: /Inspect hull/ })).getAllByRole('cell')[0]?.textContent).toBe('45%')
    expect(within(screen.getByRole('row', { name: /Repair deck/ })).getAllByRole('cell')[0]?.textContent).toBe('0%')
  })

  it('moves horizontally and clamps every grid boundary while keeping one tab stop', async () => {
    const user = userEvent.setup()
    render(<Gantt accessibleName="Dock schedule" tasks={tasks} />)
    act(() => cell('0:0').focus())

    // Oracle: gantt.quality.grid-navigation; two rows of four cells have one active tab stop.
    await user.keyboard('{ArrowRight}')
    expect(cell('0:1')).toHaveFocus()
    expectTabIndexes([-1, 0, -1, -1, -1, -1, -1, -1])

    await user.keyboard('{ArrowLeft}{ArrowLeft}{ArrowUp}')
    expect(cell('0:0')).toHaveFocus()
    expectTabIndexes([0, -1, -1, -1, -1, -1, -1, -1])

    await user.keyboard('{End}{ArrowRight}')
    expect(cell('0:3')).toHaveFocus()
    expectTabIndexes([-1, -1, -1, 0, -1, -1, -1, -1])

    await user.keyboard('{ArrowDown}{ArrowDown}')
    expect(cell('1:3')).toHaveFocus()
    expectTabIndexes([-1, -1, -1, -1, -1, -1, -1, 0])

    await user.keyboard('{ArrowUp}{ArrowUp}{Home}{ArrowLeft}')
    expect(cell('0:0')).toHaveFocus()
    expectTabIndexes([0, -1, -1, -1, -1, -1, -1, -1])

    act(() => cell('1:1').focus())
    expect(cell('1:1')).toHaveFocus()
    expectTabIndexes([-1, -1, -1, -1, -1, 0, -1, -1])
  })

  it('keeps the tab stop within the remaining rows and columns after replacement', async () => {
    const user = userEvent.setup()
    const rendered = render(<Gantt accessibleName="Dock schedule" tasks={tasks} />)
    act(() => cell('1:3').focus())
    expectTabIndexes([-1, -1, -1, -1, -1, -1, -1, 0])

    rendered.rerender(<Gantt accessibleName="Dock schedule" columns={[{ field: 'title' }]} tasks={[tasks[0]!]} />)

    // Oracle: replacement preserves access, without prescribing which remaining cell becomes active.
    const remainingTabIndexes = [...document.querySelectorAll<HTMLElement>('[data-gantt-cell]')].map(element => element.tabIndex)
    expect(remainingTabIndexes).toHaveLength(2)
    expect(remainingTabIndexes.filter(value => value === 0)).toHaveLength(1)
    expect(remainingTabIndexes.every(value => value === 0 || value === -1)).toBe(true)
    expect(document.querySelector('[data-gantt-cell="1:3"]')).toBeNull()
    act(() => cell('0:1').focus())
    await user.keyboard('{ArrowLeft}')
    expect(cell('0:0')).toHaveFocus()
    expectTabIndexes([0, -1])
  })
})
