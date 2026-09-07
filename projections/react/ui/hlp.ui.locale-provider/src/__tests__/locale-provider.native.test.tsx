import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'

import { HarborlineLocaleProvider, useHarborlineStrings } from '../index'

describe('Harborline Locale Provider native behavior', () => {
  it.each(['', ' ', '\t'])('rejects an empty effective locale', locale => {
    const wrapper = ({ children }: { children: ReactNode }) => (
      <HarborlineLocaleProvider locale={locale}>{children}</HarborlineLocaleProvider>
    )
    expect(() => renderHook(() => useHarborlineStrings(), { wrapper })).toThrow('locale-required')
  })

  it('passes a per-call locale override and options to a host formatter unchanged', () => {
    const requests: unknown[] = []
    const wrapper = ({ children }: { children: ReactNode }) => (
      <HarborlineLocaleProvider
        locale="en-US"
        numberFormatter={(value, request) => {
          requests.push({ value, request })
          return 'formatted'
        }}
      >
        {children}
      </HarborlineLocaleProvider>
    )
    const { result } = renderHook(() => useHarborlineStrings(), { wrapper })
    const options = { minimumFractionDigits: 3 }
    expect(result.current.formatNumber(12.5, options, 'fr-FR')).toBe('formatted')
    expect(requests).toEqual([{ value: 12.5, request: { locale: 'fr-FR', options } }])
  })

  it('does not expose date, currency, list, or relative-time formatting', () => {
    const { result } = renderHook(() => useHarborlineStrings())
    expect(Object.keys(result.current).sort()).toEqual([
      'direction',
      'formatNumber',
      'locale',
      'resolveString',
      't',
      'tn',
    ])
  })
})
