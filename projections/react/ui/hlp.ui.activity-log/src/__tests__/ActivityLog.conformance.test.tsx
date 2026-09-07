import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useIsMobile } from '@harborline-platform/hlp.ui.use-is-mobile'

import { ActivityLog, type ActivityEntry } from '../ActivityLog'
import { fixture, sharedCases } from './fixtures'

vi.mock('@harborline-platform/hlp.ui.use-is-mobile', () => ({ useIsMobile: vi.fn() }))

const mockedUseIsMobile = vi.mocked(useIsMobile)
const entries: ActivityEntry[] = [
  { id: 'approved', actor: 'Avery', action: 'approved the review', timestamp: '1 min ago', machineTimestamp: '2026-08-10T12:00:00Z', tone: 'positive', detail: 'Approved after review' },
  { id: 'pending', actor: 'Blake', action: 'requested clarification', timestamp: 'Yesterday', tone: 'warning' },
  { id: 'system', actor: 'System', action: 'synchronized the record', timestamp: 'Aug 8', category: 'system' },
]

describe('ActivityLog revision-1 shared fixtures', () => {
  beforeEach(() => mockedUseIsMobile.mockReturnValue(false))

  it('activity-log.empty', () => {
    fixture(sharedCases, 'activity-log.empty')
    render(<ActivityLog entries={[]} />)
    expect(screen.getByRole('region', { name: 'Activity log' })).toBeInTheDocument()
    expect(screen.getByText('No activity yet')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('activity-log.list', () => {
    fixture(sharedCases, 'activity-log.list')
    render(<ActivityLog entries={entries.slice(0, 2)} layout="list" />)
    expect(screen.getByRole('list').tagName).toBe('OL')
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('activity-log.table', () => {
    fixture(sharedCases, 'activity-log.table')
    render(<ActivityLog entries={entries.slice(0, 2)} layout="table" />)
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Actor', 'Action', 'Timestamp'])
    expect(within(table).getAllByRole('row')).toHaveLength(3)
  })

  it('activity-log.responsive', () => {
    fixture(sharedCases, 'activity-log.responsive')
    mockedUseIsMobile.mockReturnValue(true)
    const { rerender } = render(<ActivityLog entries={entries} />)
    expect(screen.getByRole('region')).toHaveAttribute('data-hl-layout', 'list')
    mockedUseIsMobile.mockReturnValue(false)
    rerender(<ActivityLog entries={entries} />)
    expect(screen.getByRole('region')).toHaveAttribute('data-hl-layout', 'table')
  })

  it('activity-log.explicit-layout', () => {
    fixture(sharedCases, 'activity-log.explicit-layout')
    mockedUseIsMobile.mockReturnValue(true)
    render(<ActivityLog entries={entries} layout="table" />)
    expect(screen.getByRole('table')).toBeInTheDocument()
    expect(screen.getByRole('region')).toHaveAttribute('data-hl-layout', 'table')
  })

  it('activity-log.label', () => {
    fixture(sharedCases, 'activity-log.label')
    render(<ActivityLog entries={entries} label="Case activity" />)
    expect(screen.getByRole('region', { name: 'Case activity' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Case activity' })).toBeVisible()
  })

  it('activity-log.detail-tone', () => {
    fixture(sharedCases, 'activity-log.detail-tone')
    render(<ActivityLog entries={entries.slice(0, 1)} layout="list" />)
    const row = screen.getByRole('listitem')
    expect(row).toHaveAttribute('data-hl-tone', 'positive')
    expect(within(row).getByText('Approved after review')).toBeVisible()
    expect(within(row).getByText('approved the review')).toBeVisible()
  })

  it('activity-log.timestamp', () => {
    fixture(sharedCases, 'activity-log.timestamp')
    render(<ActivityLog entries={entries.slice(0, 1)} layout="list" />)
    const time = screen.getByText('1 min ago')
    expect(time).toHaveAttribute('datetime', '2026-08-10T12:00:00Z')
    expect(time).toHaveTextContent('1 min ago')
  })

  it('activity-log.limit', () => {
    fixture(sharedCases, 'activity-log.limit')
    render(<ActivityLog entries={entries} layout="list" maxVisible={2} onShowMore={vi.fn()} />)
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Show more' })).toBeInTheDocument()
  })

  it('activity-log.show-more', async () => {
    fixture(sharedCases, 'activity-log.show-more')
    const onShowMore = vi.fn()
    const original = [...entries]
    render(<ActivityLog entries={entries} layout="list" maxVisible={2} onShowMore={onShowMore} />)
    await userEvent.setup().click(screen.getByRole('button', { name: 'Show more' }))
    expect(onShowMore).toHaveBeenCalledOnce()
    expect(entries).toEqual(original)
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
  })

  it('activity-log.no-show-more', () => {
    fixture(sharedCases, 'activity-log.no-show-more')
    const { rerender } = render(<ActivityLog entries={entries.slice(0, 2)} layout="list" maxVisible={0} />)
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
    rerender(<ActivityLog entries={entries} layout="list" maxVisible={1} />)
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
  })

  it('activity-log.table-classes', () => {
    const expected = fixture(sharedCases, 'activity-log.table-classes').expected as {
      tableScrollClasses: string[]
      actionHeaderClasses: string[]
      markerClasses: string[]
    }
    const { container } = render(<ActivityLog entries={entries.slice(0, 2)} layout="table" />)
    const scroll = container.querySelector('div.hl-activity-log__table-scroll')!
    expect([...scroll.classList]).toEqual(expected.tableScrollClasses)
    expect([...screen.getByRole('columnheader', { name: 'Action' }).classList]).toEqual(expected.actionHeaderClasses)
    const markers = container.querySelectorAll('.hl-activity-log__marker')
    expect(markers.length).toBeGreaterThan(0)
    for (const marker of markers) expect([...marker.classList]).toEqual(expected.markerClasses)
  })

  it('activity-log.invalid-input', () => {
    fixture(sharedCases, 'activity-log.invalid-input')
    expect(() => render(<ActivityLog entries={[entries[0]!, { ...entries[1]!, id: entries[0]!.id }]} />)).toThrow('duplicate-entry-id')
    expect(() => render(<ActivityLog entries={[]} label=" " />)).toThrow('accessible-name-required')
  })
})
