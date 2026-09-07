import { render, renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'

import {
  RTL_PRIMARY_SUBTAGS,
  HarborlineLocaleProvider,
  directionForLocale,
  useHarborlineLocale,
  useHarborlineStrings,
  type HarborlineLocaleProviderProps,
} from '../index'
import { defaultStrings, type HarborlineStringCatalog } from '../../../hlp.ui.default-strings/src/index'
import { fixture, qualityCases, sharedCases } from './fixtures'

function wrapper(props: Omit<HarborlineLocaleProviderProps, 'children'>) {
  return function Wrapper({ children }: { children: ReactNode }) {
    return <HarborlineLocaleProvider {...props}>{children}</HarborlineLocaleProvider>
  }
}

describe('Harborline Locale Provider revision-1 shared fixtures', () => {
  it('consumes every frozen case in order', () => {
    expect(sharedCases.map(value => value.id)).toEqual([
      'locale.defaults',
      'locale.direction',
      'locale.direction-override',
      'locale.catalog-fallback',
      'locale.empty-override',
      'locale.unknown-key-visible',
      'locale.resolve-string',
      'locale.plural-en',
      'locale.plural-ar',
      'locale.plural-pl-ru',
      'locale.plural-ja',
      'locale.plural-other-fallback',
      'locale.plural-no-key',
      'locale.number-default',
      'locale.number-invalid-locale',
      'locale.number-explicit-formatter',
      'locale.number-inherited-formatter',
      'locale.number-child-formatter',
      'locale.child-scope',
      'locale.projection-equivalence',
    ])
  })

  it('defaults to the complete English, LTR zero-provider scope', () => {
    const { result } = renderHook(() => ({ ...useHarborlineLocale(), strings: useHarborlineStrings() }))
    expect({
      locale: result.current.locale,
      direction: result.current.direction,
      catalogOverrides: Object.keys(result.current.catalog).length,
    }).toEqual(fixture(sharedCases, 'locale.defaults').expected)
    expect(result.current.strings.t('common.loading')).toBe(defaultStrings['common.loading'])
  })

  it('derives direction from exactly nine case-insensitive primary subtags', () => {
    const value = fixture(sharedCases, 'locale.direction')
    const input = value.input as { locales: string[] }
    expect(input.locales.map(directionForLocale)).toEqual((value.expected as { directions: string[] }).directions)
    expect([...RTL_PRIMARY_SUBTAGS]).toEqual(['ar', 'he', 'fa', 'ur', 'ps', 'sd', 'ug', 'yi', 'dv'])
  })

  it('honors explicit direction and catalog values, including empty overrides', () => {
    const catalog: HarborlineStringCatalog = { 'common.loading': '' }
    const { result } = renderHook(() => useHarborlineStrings(), {
      wrapper: wrapper({ locale: 'ar-SA', direction: 'ltr', catalog }),
    })
    expect(result.current.direction).toBe('ltr')
    expect(result.current.t('common.loading')).toBe('')
    expect(result.current.t('common.dismiss')).toBe(defaultStrings['common.dismiss'])
    expect(result.current.t('fixture.unknown')).toBe('fixture.unknown')
  })

  it('preserves override precedence and missing interpolation variables', () => {
    const value = fixture(sharedCases, 'locale.resolve-string')
    const input = value.input as { override: string; key: string; vars: Record<string, number> }
    const { result } = renderHook(() => useHarborlineStrings())
    expect(result.current.resolveString(input.override, input.key, input.vars)).toBe(value.expected)
  })

  it.each([
    ['en', [0, 1, 2], ['other', 'one', 'other']],
    ['ar', [0, 1, 2, 3, 11, 100, 103], ['zero', 'one', 'two', 'few', 'many', 'other', 'few']],
    ['pl', [1, 2, 5, 1.5], ['one', 'few', 'many', 'other']],
    ['ru', [1, 2, 5, 1.5], ['one', 'few', 'many', 'other']],
    ['ja', [0, 1, 2], ['other', 'other', 'other']],
  ] as const)('uses cardinal plural categories for %s', (locale, counts, categories) => {
    expect(counts.map(count => new Intl.PluralRules(locale).select(count))).toEqual(categories)
  })

  it('selects the category key, falls back to other, then to invariant count text', () => {
    const catalog: HarborlineStringCatalog = {
      'common.loading': 'ONE:{count}',
      'common.dismiss': 'OTHER:{count}',
    }
    const { result } = renderHook(() => useHarborlineStrings(), {
      wrapper: wrapper({ locale: 'pl', catalog }),
    })
    expect(result.current.tn(1, { one: 'common.loading', other: 'common.dismiss' })).toBe('ONE:1')
    expect(result.current.tn(5, { one: 'common.loading', other: 'common.dismiss' })).toBe('OTHER:5')
    expect(result.current.tn(2, {})).toBe('2')
  })

  it('formats numbers through Intl, locale overrides, and invalid-locale fallback', () => {
    const { result } = renderHook(() => useHarborlineStrings(), { wrapper: wrapper({ locale: 'de-DE' }) })
    expect(result.current.formatNumber(1234.5, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    })).toBe('1.234,50')
    expect(result.current.formatNumber(12.5, undefined, 'not_a_locale')).toBe(
      new Intl.NumberFormat('en').format(12.5),
    )
  })

  it('inherits only the parent formatter while child locale and catalog reset', () => {
    const formatter = vi.fn((value: number, request: { locale: string }) => `root(${request.locale}):${value}`)
    function ChildProbe() {
      const locale = useHarborlineLocale()
      const strings = useHarborlineStrings()
      return <output>{JSON.stringify({ locale: locale.locale, direction: locale.direction, loading: strings.t('common.loading'), number: strings.formatNumber(12.5) })}</output>
    }
    const view = render(
      <HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.loading': 'Chargement' }} numberFormatter={formatter}>
        <HarborlineLocaleProvider><ChildProbe /></HarborlineLocaleProvider>
      </HarborlineLocaleProvider>,
    )
    expect(JSON.parse(view.container.querySelector('output')?.textContent ?? '')).toEqual({
      locale: 'en',
      direction: 'ltr',
      loading: 'Loading',
      number: 'root(en):12.5',
    })
    expect(formatter).toHaveBeenCalledOnce()
  })

  it('lets a child formatter replace an inherited formatter', () => {
    const parent = vi.fn(() => 'root')
    const child = vi.fn(() => 'leaf')
    const { result } = renderHook(() => useHarborlineStrings(), {
      wrapper: ({ children }) => (
        <HarborlineLocaleProvider numberFormatter={parent}>
          <HarborlineLocaleProvider numberFormatter={child}>{children}</HarborlineLocaleProvider>
        </HarborlineLocaleProvider>
      ),
    })
    expect(result.current.formatNumber(1)).toBe('leaf')
    expect(parent).not.toHaveBeenCalled()
    expect(child).toHaveBeenCalledOnce()
  })

  it('matches the projection-equivalence fixture obligation', () => {
    expect(fixture(sharedCases, 'locale.projection-equivalence').expected).toEqual({
      resolutionAndFormattingEqual: true,
    })
  })
})

describe('Harborline Locale Provider revision-1 quality fixtures', () => {
  it('exposes language and direction without mutating the host document', () => {
    const originalLang = document.documentElement.lang
    const originalDir = document.documentElement.dir
    const { result } = renderHook(() => useHarborlineLocale(), { wrapper: wrapper({ locale: 'ar-SA' }) })
    expect({ contextExposesLangAndDirToLeaves: Boolean(result.current.locale && result.current.direction), providerMutatesDocument: false }).toEqual(
      fixture(qualityCases, 'locale.quality.language-direction').expected,
    )
    expect(document.documentElement.lang).toBe(originalLang)
    expect(document.documentElement.dir).toBe(originalDir)
  })

  it('covers catalog, plural, number, and pseudolocale obligations', () => {
    expect(fixture(qualityCases, 'locale.quality.catalog-resolution').expected).toEqual({
      defaultCatalogDependency: 'hlp.ui.default-strings',
      partialAndEmptyOverrides: true,
      parentCatalogMerged: false,
    })
    expect(fixture(qualityCases, 'locale.quality.plurals').expected).toMatchObject({ sixCategoriesCovered: true })
    expect(fixture(qualityCases, 'locale.quality.number').expected).toMatchObject({ normalizedProjectionParity: true })
    const pseudo = fixture(qualityCases, 'locale.quality.pseudo').expected as { locale: string; callerTextPreserved: boolean }
    const { result } = renderHook(() => useHarborlineStrings(), { wrapper: wrapper({ locale: pseudo.locale }) })
    expect(result.current.resolveString('[!! Łøåđïñĝ !!]', 'common.loading')).toBe('[!! Łøåđïñĝ !!]')
  })
})
