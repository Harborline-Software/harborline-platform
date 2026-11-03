import { act, fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { HarborlineLocaleProvider, useHarborlineStrings, type HarborlineLocaleCatalog } from '@harborline-platform/hlp.ui.locale-provider'
import { createToastService, Toaster, type ToastService } from '../Toaster'

describe('Toaster React projection', () => {
  it('isolates hosts, bounds newest entries, and applies roles without aria-live', () => {
    const service = createToastService({ maximumVisible: 2 })
    const other = createToastService()
    render(<Toaster service={service}/>)
    act(() => { service.show('A'); service.success('B'); service.error('C') })
    expect(screen.queryByText('A')).toBeNull()
    expect(screen.getByText('B').closest('[role="status"]')).not.toHaveAttribute('aria-live')
    expect(screen.getByText('C').closest('[role="alert"]')).not.toHaveAttribute('aria-live')
    expect(document.querySelectorAll('.hl-toaster__marker svg')).toHaveLength(2)
    for (const marker of document.querySelectorAll('.hl-toaster__marker')) expect(marker).not.toHaveTextContent(/[✓!•…]/)
    expect(other.getSnapshot().open).toHaveLength(0)
  })

  it('keeps persistent variants closable and invokes actions once without dismissal', () => {
    const action = vi.fn()
    const service = createToastService()
    render(<Toaster service={service} dismissLabel="Close toast"/>)
    act(() => { service.error('Failed', { action: { label: 'Retry', onActivate: action } }) })
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(action).toHaveBeenCalledOnce()
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close toast' }).querySelector('svg')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close toast' })).not.toHaveTextContent('×')
    fireEvent.click(screen.getByRole('button', { name: 'Close toast' }))
    expect(screen.queryByText('Failed')).toBeNull()
  })

  it('preserves identity across asynchronous success', async () => {
    const service = createToastService()
    render(<Toaster service={service}/>)
    await act(async () => expect(await service.trackAsync(Promise.resolve(7), { loading: 'Loading', success: value => `Ready ${value}`, error: 'Failed' })).toBe(7))
    expect(screen.getByText('Ready 7').closest('[data-toast-id]')).toHaveAttribute('data-toast-id', 'toast-1')
  })

  it('rejects invalid input deterministically', () => {
    const service = createToastService()
    expect(() => service.show('')).toThrow('toast-message-required')
    expect(() => service.configure({ maximumVisible: 0 })).toThrow('invalid-toast-limit')
    expect(() => service.show('x', { duration: -1 })).toThrow('invalid-toast-duration')
  })
})

// T-1107. The toast region's accessible name is the host's to set, through the aria-label that
// ToasterProps already accepts (it extends React.HTMLAttributes), and with none it is Blazor's
// AccessibleLabel default. hlp.ui.default-strings revision 1 has no key for this name, so a host
// localizes it from its own catalog, as the Blazor gallery story does through AccessibleLabel.
function CatalogToaster({ service }: { service: ToastService }) {
  const { t, direction } = useHarborlineStrings()
  return <Toaster service={service} aria-label={t('host.notifications')} dismissLabel={t('common.dismissNotification')} direction={direction}/>
}

const arabicCatalog: HarborlineLocaleCatalog = { 'host.notifications': 'الإشعارات', 'common.dismissNotification': 'إغلاق الإشعار' }
const pseudoCatalog: HarborlineLocaleCatalog = { 'host.notifications': '⟦ Ñøţîƒîçåţîøñš ···· ⟧', 'common.dismissNotification': '⟦ Đîšmîšš ñøţîƒîçåţîøñ ···· ⟧' }

describe('Toaster region name (T-1107)', () => {
  it('defaults to the name Blazor AccessibleLabel defaults to', () => {
    render(<Toaster service={createToastService()}/>)
    expect(screen.getByRole('region')).toHaveAccessibleName('Notifications')
  })

  it('keeps the default when aria-label is passed as undefined', () => {
    render(<Toaster service={createToastService()} aria-label={undefined}/>)
    expect(screen.getByRole('region')).toHaveAccessibleName('Notifications')
  })

  it('names the region with the caller aria-label instead of overwriting it', () => {
    render(<Toaster service={createToastService()} aria-label="Workspace alerts" id="toasts"/>)
    expect(screen.getByRole('region', { name: 'Workspace alerts' })).toHaveAttribute('id', 'toasts')
    expect(screen.queryByRole('region', { name: 'Notifications' })).toBeNull()
  })

  it.each([
    ['ar-SA', arabicCatalog, 'rtl'],
    ['en-XA', pseudoCatalog, 'ltr'],
  ] as const)('announces the %s host catalog name for the region and its toasts', (locale, catalog, direction) => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale={locale} catalog={catalog}><CatalogToaster service={service}/></HarborlineLocaleProvider>)
    act(() => { service.error('Placement failed') })
    const region = screen.getByRole('region', { name: catalog['host.notifications'] })
    expect(region).toHaveAttribute('dir', direction)
    expect(region).toContainElement(screen.getByRole('alert'))
    expect(screen.getByRole('button', { name: catalog['common.dismissNotification'] })).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Notifications' })).toBeNull()
  })

  it('renames the region when the host catalog changes', () => {
    const service = createToastService()
    const { rerender } = render(<HarborlineLocaleProvider locale="en-XA" catalog={pseudoCatalog}><CatalogToaster service={service}/></HarborlineLocaleProvider>)
    expect(screen.getByRole('region')).toHaveAccessibleName('⟦ Ñøţîƒîçåţîøñš ···· ⟧')
    rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={arabicCatalog}><CatalogToaster service={service}/></HarborlineLocaleProvider>)
    expect(screen.getByRole('region')).toHaveAccessibleName('الإشعارات')
    expect(screen.getByRole('region')).toHaveAttribute('dir', 'rtl')
  })

  // A section with an empty or whitespace-only aria-label has no accessible name, so it is not a
  // region landmark (HTML-AAM). A blank name is not a usable override: it keeps the default name,
  // the way an absent one does. The whitespace case is what tells this apart from `||`.
  it.each([
    ['empty', ''],
    ['whitespace-only', ' \t\n '],
  ])('keeps the default region name when the caller aria-label is %s', (_kind, blank) => {
    render(<Toaster service={createToastService()} aria-label={blank}/>)
    expect(screen.getByRole('region')).toHaveAccessibleName('Notifications')
    expect(document.querySelector('section.hl-toaster')).toHaveAttribute('aria-label', 'Notifications')
  })

  // Untyped JavaScript can pass any value, for example `aria-label={localized && t(key)}` gives false
  // and a count gives a number. Anything that is not a non-blank string keeps the default, as an
  // absent name does, instead of throwing from the render. Blazor's ToasterTests pin the same rule
  // for AccessibleLabel and for a splatted aria-label.
  it.each([
    ['null', null],
    ['false', false],
    ['0', 0],
    ['5', 5],
  ])('keeps the default region name when untyped JavaScript passes %s as aria-label', (_kind, value) => {
    render(<Toaster service={createToastService()} aria-label={value as unknown as string}/>)
    expect(screen.getByRole('region')).toHaveAccessibleName('Notifications')
    expect(document.querySelector('section.hl-toaster')).toHaveAttribute('aria-label', 'Notifications')
  })

  // T-1107 review 3: the blank check is one character set in both lanes, ECMA-262's WhiteSpace and
  // LineTerminator, the set String.prototype.trim strips. .NET's char.IsWhiteSpace differs at two
  // characters, counting U+0085 (NEL) as white space and U+FEFF (ZWNBSP) as not, so Blazor lists the
  // same set instead of using it. A NEL-only name is not blank and a ZWNBSP-only one is, in both
  // lanes; Blazor's twin is ToasterTests.BlankCheckMatchesReactAtNelAndZwnbsp.
  it.each([
    ['U+0085 alone', '\u0085', '\u0085'],
    ['U+0085 and a space', '\u0085 ', '\u0085 '],
    ['U+FEFF alone', '\uFEFF', 'Notifications'],
    ['U+FEFF among white space', ' \uFEFF\t\u3000', 'Notifications'],
  ])('resolves an aria-label of %s as Blazor does', (_kind, name, expected) => {
    render(<Toaster service={createToastService()} aria-label={name}/>)
    expect(document.querySelector('section.hl-toaster')).toHaveAttribute('aria-label', expected)
  })

  it('keeps the default region name when the host catalog entry is an untranslated blank', () => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="ar-SA" catalog={{ ...arabicCatalog, 'host.notifications': '' }}><CatalogToaster service={service}/></HarborlineLocaleProvider>)
    act(() => { service.error('Placement failed') })
    const region = screen.getByRole('region', { name: 'Notifications' })
    expect(region).toContainElement(screen.getByRole('alert'))
    expect(region).toHaveAttribute('dir', 'rtl')
  })
})

// The region's dir. A dir written after the spread overwrote the caller's dir attribute even when
// direction was omitted, so toast motion did not mirror. Blazor writes dir="@Direction" before
// @attributes, so a splatted dir wins there; React now gives the caller's dir the same precedence,
// as Gantt and Scheduler do with `dir ?? direction`.
describe('Toaster region direction', () => {
  it('keeps the caller dir when direction is omitted', () => {
    render(<Toaster service={createToastService()} dir="rtl"/>)
    expect(screen.getByRole('region')).toHaveAttribute('dir', 'rtl')
  })

  it('takes direction when the caller passes no dir', () => {
    render(<Toaster service={createToastService()} direction="rtl"/>)
    expect(screen.getByRole('region')).toHaveAttribute('dir', 'rtl')
  })

  it('lets the caller dir win over direction, as a splatted dir does in Blazor', () => {
    render(<Toaster service={createToastService()} dir="ltr" direction="rtl"/>)
    expect(screen.getByRole('region')).toHaveAttribute('dir', 'ltr')
  })

  it('renders no dir when neither is passed', () => {
    render(<Toaster service={createToastService()}/>)
    expect(screen.getByRole('region')).not.toHaveAttribute('dir')
  })
})
