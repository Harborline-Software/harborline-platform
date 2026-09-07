import * as React from 'react'

import { defaultStrings, interpolate } from '@harborline-platform/hlp.ui.default-strings'

export type HarborlineDirection = 'ltr' | 'rtl'
export type HarborlineLocaleCatalog = Readonly<Record<string, string | undefined>>

export interface HarborlineNumberFormatRequest {
  readonly locale: string
  readonly options?: Intl.NumberFormatOptions
}

export type HarborlineNumberFormatter = (
  value: number,
  request: HarborlineNumberFormatRequest,
) => string

export interface HarborlineLocaleContextValue {
  readonly locale: string
  readonly direction: HarborlineDirection
  readonly catalog: HarborlineLocaleCatalog
  readonly numberFormatter?: HarborlineNumberFormatter
}

export interface HarborlineLocaleProviderProps {
  readonly locale?: string
  readonly catalog?: HarborlineLocaleCatalog
  readonly direction?: HarborlineDirection
  readonly numberFormatter?: HarborlineNumberFormatter
  readonly children: React.ReactNode
}

export interface UseHarborlineStringsResult {
  readonly locale: string
  readonly direction: HarborlineDirection
  readonly t: (key: string, vars?: Record<string, string | number>) => string
  readonly resolveString: (
    override: string | undefined,
    key: string,
    vars?: Record<string, string | number>,
  ) => string
  readonly tn: (
    count: number,
    keys: Partial<Record<Intl.LDMLPluralRule, string>>,
    vars?: Record<string, string | number>,
  ) => string
  readonly formatNumber: (
    value: number,
    options?: Intl.NumberFormatOptions,
    localeOverride?: string,
  ) => string
}

export const RTL_PRIMARY_SUBTAGS: ReadonlySet<string> = new Set([
  'ar',
  'he',
  'fa',
  'ur',
  'ps',
  'sd',
  'ug',
  'yi',
  'dv',
])

const DEFAULT_LOCALE = 'en'

const HarborlineLocaleContext = React.createContext<HarborlineLocaleContextValue>({
  locale: DEFAULT_LOCALE,
  direction: 'ltr',
  catalog: {},
})

function requireLocale(locale: string): string {
  if (locale.trim().length === 0) throw new Error('locale-required')
  return locale
}

export function directionForLocale(locale: string): HarborlineDirection {
  const primary = locale.toLowerCase().split('-')[0]
  return primary !== undefined && RTL_PRIMARY_SUBTAGS.has(primary) ? 'rtl' : 'ltr'
}

function intlNumberFormatter(value: number, request: HarborlineNumberFormatRequest): string {
  try {
    return new Intl.NumberFormat(request.locale, request.options).format(value)
  } catch (error) {
    if (request.locale === DEFAULT_LOCALE) throw error
    return new Intl.NumberFormat(DEFAULT_LOCALE, request.options).format(value)
  }
}

export function HarborlineLocaleProvider({
  locale = DEFAULT_LOCALE,
  catalog = {},
  direction,
  numberFormatter,
  children,
}: HarborlineLocaleProviderProps) {
  const parent = React.useContext(HarborlineLocaleContext)
  const effectiveLocale = requireLocale(locale)
  const value = React.useMemo<HarborlineLocaleContextValue>(
    () => ({
      locale: effectiveLocale,
      direction: direction ?? directionForLocale(effectiveLocale),
      catalog,
      numberFormatter: numberFormatter ?? parent.numberFormatter,
    }),
    [effectiveLocale, direction, catalog, numberFormatter, parent.numberFormatter],
  )

  return <HarborlineLocaleContext.Provider value={value}>{children}</HarborlineLocaleContext.Provider>
}

export function useHarborlineLocale(): HarborlineLocaleContextValue {
  return React.useContext(HarborlineLocaleContext)
}

export function useHarborlineStrings(): UseHarborlineStringsResult {
  const { locale, direction, catalog, numberFormatter } = useHarborlineLocale()

  const t = React.useCallback(
    (key: string, vars?: Record<string, string | number>): string => {
      const overrides = catalog as Readonly<Record<string, string | undefined>>
      const defaults = defaultStrings as Readonly<Record<string, string | undefined>>
      return interpolate(overrides[key] ?? defaults[key] ?? key, vars)
    },
    [catalog],
  )

  const resolveString = React.useCallback(
    (override: string | undefined, key: string, vars?: Record<string, string | number>): string =>
      override === undefined ? t(key, vars) : interpolate(override, vars),
    [t],
  )

  const tn = React.useCallback(
    (
      count: number,
      keys: Partial<Record<Intl.LDMLPluralRule, string>>,
      vars?: Record<string, string | number>,
    ): string => {
      let category: Intl.LDMLPluralRule = 'other'
      try {
        category = new Intl.PluralRules(locale).select(count)
      } catch {
        category = count === 1 ? 'one' : 'other'
      }
      const key = keys[category] ?? keys.other
      return key === undefined ? String(count) : t(key, { count, ...vars })
    },
    [locale, t],
  )

  const formatNumber = React.useCallback(
    (value: number, options?: Intl.NumberFormatOptions, localeOverride?: string): string => {
      const request = {
        locale: requireLocale(localeOverride ?? locale),
        options,
      }
      return (numberFormatter ?? intlNumberFormatter)(value, request)
    },
    [locale, numberFormatter],
  )

  return { locale, direction, t, resolveString, tn, formatNumber }
}
