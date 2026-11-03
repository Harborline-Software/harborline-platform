import { act, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { createToastService, Toaster } from '../Toaster'

// Contract: DismissLabel is caller-overridable or locale-catalog-backed and supplies the close
// control's accessible name. Unset means the current localized catalog value (key
// common.dismissNotification); explicit caller text overrides it; the name is never empty.
function closeName() {
  const button = screen.getByRole('button', { name: /.+/, hidden: false })
  const name = button.getAttribute('aria-label') ?? ''
  expect(name.trim()).not.toBe('')
  return name
}

function showError(service: ReturnType<typeof createToastService>) {
  act(() => { service.error('Upload failed') })
}

describe('Toaster dismiss label', () => {
  it('uses the default catalog value without a provider or caller text', () => {
    const service = createToastService()
    render(<Toaster service={service}/>)
    showError(service)
    expect(closeName()).toBe('Dismiss notification')
  })

  it('uses the host catalog value when the caller supplies none', () => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.dismissNotification': 'Fermer la notification' }}><Toaster service={service}/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe('Fermer la notification')
  })

  it('lets explicit caller text override the catalog', () => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.dismissNotification': 'Fermer la notification' }}><Toaster service={service} dismissLabel="Close upload alert"/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe('Close upload alert')
  })

  it('follows a locale and catalog change while the toast is shown', () => {
    const service = createToastService()
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.dismissNotification': 'Fermer la notification' }}><Toaster service={service}/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe('Fermer la notification')
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={{ 'common.dismissNotification': 'إغلاق الإشعار' }}><Toaster service={service}/></HarborlineLocaleProvider>)
    expect(closeName()).toBe('إغلاق الإشعار')
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={{ 'common.dismissNotification': 'إغلاق الإشعار' }}><Toaster service={service} dismissLabel="Close upload alert"/></HarborlineLocaleProvider>)
    expect(closeName()).toBe('Close upload alert')
  })

  it('never renders an empty name for blank caller text', () => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.dismissNotification': 'Fermer la notification' }}><Toaster service={service} dismissLabel="   "/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe('Fermer la notification')
  })

  it('uses a pseudo-localized catalog value under en-XA without truncation', () => {
    const pseudo = '⟦ Đîšmîšš ñøţîƒîçåţîøñ ···· ⟧'
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'common.dismissNotification': pseudo }}><Toaster service={service}/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe(pseudo)
  })

  it('falls back to the default catalog value when the host catalog value is blank', () => {
    const service = createToastService()
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'common.dismissNotification': '  ' }}><Toaster service={service}/></HarborlineLocaleProvider>)
    showError(service)
    expect(closeName()).toBe('Dismiss notification')
  })
})
