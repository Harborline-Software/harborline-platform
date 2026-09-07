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
