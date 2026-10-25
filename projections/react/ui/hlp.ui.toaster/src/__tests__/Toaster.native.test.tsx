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
})
