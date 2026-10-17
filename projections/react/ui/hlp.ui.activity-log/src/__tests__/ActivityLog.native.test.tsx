import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useIsMobile } from '@harborline-platform/hlp.ui.use-is-mobile'

import { ActivityLog, type ActivityEntry } from '../ActivityLog'
import { qualityCases } from './fixtures'

vi.mock('@harborline-platform/hlp.ui.use-is-mobile', () => ({ useIsMobile: vi.fn() }))

const mockedUseIsMobile = vi.mocked(useIsMobile)

describe('ActivityLog React projection quality', () => {
  beforeEach(() => mockedUseIsMobile.mockReturnValue(false))

  it('consumes every frozen quality case', () => {
    expect(qualityCases.map(value => value.id)).toEqual([
      'activity-log.quality.structure',
      'activity-log.quality.timestamps',
      'activity-log.quality.focus',
      'activity-log.quality.forced-colors',
      'activity-log.quality.reflow',
      'activity-log.quality.locales',
      'activity-log.quality.rtl',
      'activity-log.quality.pseudo',
      'activity-log.quality.formatting-boundary',
      'activity-log.quality.themes',
      'activity-log.quality.tokens',
      'activity-log.quality.contrast',
      'activity-log.quality.reduced-motion',
      'activity-log.quality.visual-parity',
    ])
  })

  it('keeps caller display text separate from optional machine timestamps', () => {
    const data: ActivityEntry[] = [
      { id: 'with', actor: 'A', action: 'updated', timestamp: 'قبل دقيقة', machineTimestamp: '2026-08-10T12:00:00Z', tone: 'info' },
      { id: 'without', actor: 'B', action: 'reviewed', timestamp: 'Recently', category: 'success' },
    ]
    render(<div dir="rtl"><ActivityLog entries={data} layout="list" label="سجل النشاط" /></div>)
    expect(screen.getByText('قبل دقيقة')).toHaveAttribute('datetime', '2026-08-10T12:00:00Z')
    expect(screen.getByText('Recently')).not.toHaveAttribute('datetime')
    expect(screen.getAllByRole('listitem')[0]).toHaveAttribute('data-hl-tone', 'info')
    expect(screen.getAllByRole('listitem')[1]).toHaveAttribute('data-hl-tone', 'positive')
  })

  it('allows every built-in chrome string to be overridden and preserves native activation', async () => {
    const onShowMore = vi.fn()
    render(<ActivityLog
      entries={[
        { id: 'a', actor: 'A', action: 'created', timestamp: 'Now' },
        { id: 'b', actor: 'B', action: 'edited', timestamp: 'Then' },
      ]}
      layout="table"
      maxVisible={1}
      onShowMore={onShowMore}
      label="History"
      empty="Nothing"
      actorLabel="Person"
      actionLabel="Change"
      timestampLabel="Moment"
      showMoreLabel="Load older entries"
    />)
    expect(screen.getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Person', 'Change', 'Moment'])
    const button = screen.getByRole('button', { name: 'Load older entries' })
    button.focus()
    await userEvent.setup().keyboard('{Enter}')
    expect(onShowMore).toHaveBeenCalledOnce()
  })

  it.each([0, -1])('shows every entry without disclosure for nonpositive limit %s even with a callback', maxVisible => {
    // Interface invariants: nonpositive limits are unlimited, preserving caller order.
    const onShowMore = vi.fn()
    render(<ActivityLog
      entries={[
        { id: 'z', actor: 'Zulu', action: 'created', timestamp: 'Now' },
        { id: 'a', actor: 'Alpha', action: 'edited', timestamp: 'Then' },
        { id: 'm', actor: 'Mike', action: 'reviewed', timestamp: 'Before' },
      ]}
      layout="list"
      maxVisible={maxVisible}
      onShowMore={onShowMore}
    />)
    expect(screen.getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getAllByText(/^(Zulu|Alpha|Mike)$/).map(actor => actor.textContent)).toEqual(['Zulu', 'Alpha', 'Mike'])
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
    expect(onShowMore).not.toHaveBeenCalled()
  })

  it.each(['list', 'table'] as const)('discloses only hidden entries at the exact %s limit boundary', async layout => {
    // Literal corpus order is deliberately different from alphabetical order.
    const data: readonly ActivityEntry[] = Object.freeze([
      Object.freeze({ id: 'z', actor: 'Zulu', action: 'created', timestamp: 'Now' }),
      Object.freeze({ id: 'a', actor: 'Alpha', action: 'edited', timestamp: 'Then' }),
      Object.freeze({ id: 'm', actor: 'Mike', action: 'reviewed', timestamp: 'Before' }),
    ])
    const onShowMore = vi.fn()
    const { rerender } = render(<ActivityLog entries={data.slice(0, 2)} layout={layout} maxVisible={2} onShowMore={onShowMore} />)
    expect(screen.getAllByText(/^(Zulu|Alpha|Mike)$/).map(actor => actor.textContent)).toEqual(['Zulu', 'Alpha'])
    expect(screen.queryByRole('button', { name: 'Show more' })).not.toBeInTheDocument()
    rerender(<ActivityLog entries={data} layout={layout} maxVisible={2} onShowMore={onShowMore} />)
    expect(screen.getAllByText(/^(Zulu|Alpha|Mike)$/).map(actor => actor.textContent)).toEqual(['Zulu', 'Alpha'])
    expect(screen.queryByText('Mike')).not.toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Show more' }))
    expect(onShowMore).toHaveBeenCalledOnce()
    expect(screen.getAllByText(/^(Zulu|Alpha|Mike)$/).map(actor => actor.textContent)).toEqual(['Zulu', 'Alpha'])
    expect(data).toEqual([
      { id: 'z', actor: 'Zulu', action: 'created', timestamp: 'Now' },
      { id: 'a', actor: 'Alpha', action: 'edited', timestamp: 'Then' },
      { id: 'm', actor: 'Mike', action: 'reviewed', timestamp: 'Before' },
    ])
  })

  it('keeps legacy categories within the declared five-tone vocabulary', () => {
    const { container } = render(<ActivityLog
      entries={[
        { id: 'none', actor: 'A', action: 'created', timestamp: 'Now' },
        { id: 'error', actor: 'B', action: 'failed', timestamp: 'Now', category: 'error' },
        { id: 'pending', actor: 'C', action: 'waited', timestamp: 'Now', category: 'pending' },
        { id: 'system', actor: 'D', action: 'updated', timestamp: 'Now', category: 'system' },
        { id: 'other', actor: 'E', action: 'reviewed', timestamp: 'Now', category: 'unrelated' },
      ]}
      layout="list"
    />)
    // quality-fixtures.yaml declares this closed set; it does not prescribe a legacy dictionary.
    const tones = ['neutral', 'positive', 'warning', 'danger', 'info']
    const rows = screen.getAllByRole('listitem')
    const markers = container.querySelectorAll('.hl-activity-log__marker')
    expect(rows).toHaveLength(5)
    expect(markers).toHaveLength(5)
    for (const element of [...rows, ...markers]) expect(tones).toContain(element.getAttribute('data-hl-tone'))
  })

  it('preserves an explicit tone when a compatibility category is also supplied', () => {
    render(<ActivityLog
      entries={[{ id: 'conflict', actor: 'A', action: 'waited', timestamp: 'Now', tone: 'warning', category: 'success' }]}
      layout="list"
    />)
    expect(screen.getByRole('listitem')).toHaveAttribute('data-hl-tone', 'warning')
  })

  it('publishes closed tone, logical, theme, forced-color, reflow, and motion styles', () => {
    const css = readFileSync(resolve(import.meta.dirname, '../style.css'), 'utf8')
    expect(css).toContain('--hl-activity-log-positive')
    expect(css).toContain("[data-hl-tone='warning']")
    expect(css).toContain("[data-hl-tone='danger']")
    expect(css).toContain("[data-hl-tone='info']")
    expect(css).toContain('padding-inline-start')
    expect(css).toContain('overflow-x: auto')
    expect(css).toContain("[data-theme='dark'] .hl-activity-log")
    expect(css).toContain('@media (forced-colors: active)')
    expect(css).toContain('@media (prefers-reduced-motion: reduce)')
  })
})
