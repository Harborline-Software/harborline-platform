import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { defaultStrings } from '@harborline-platform/hlp.ui.default-strings'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

// Contract: the chrome's default copy comes from the catalog (navigation.notifications.*), caller text
// overrides it, and no control or region is ever unnamed.
const items: NotificationCenterItem[] = [
  { id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal', read: false },
  { id: 'r1', title: 'Export ready', timestamp: 'Earlier', kind: 'result', read: false },
]
const French = { 'navigation.notifications.title': 'Avis', 'navigation.notifications.groups': "Groupes d'avis", 'navigation.notifications.dismiss': 'Ignorer : {title}', 'navigation.notifications.markAllRead': 'Tout marquer lu' }
const Arabic = { 'navigation.notifications.title': 'الإشعارات', 'navigation.notifications.groups': 'مجموعات الإشعارات', 'navigation.notifications.dismiss': 'تجاهل: {title}', 'navigation.notifications.markAllRead': 'تعليم الكل كمقروء' }
const center = (labels?: React.ComponentProps<typeof NotificationCenter>['labels']) => <NotificationCenter items={items} labels={labels} onDismiss={() => undefined} onMarkAllRead={() => undefined}/>
function chrome() {
  fireEvent.click(document.querySelector('.hl-notification-center__trigger')!)
  const result = {
    dialog: screen.getByRole('dialog').getAttribute('aria-label'),
    tablist: screen.getByRole('tablist').getAttribute('aria-label'),
    dismiss: document.querySelector('.hl-notification-center__dismiss')!.getAttribute('aria-label'),
    markAllRead: document.querySelector('.hl-notification-center__header button')!.textContent,
  }
  for (const value of Object.values(result)) expect((value ?? '').trim()).not.toBe('')
  fireEvent.click(document.querySelector('.hl-notification-center__trigger')!)
  return result
}

describe('NotificationCenter catalog-backed copy', () => {
  it('takes its default copy from the default catalog without a provider', () => {
    render(center())
    expect(chrome()).toEqual({
      dialog: defaultStrings['navigation.notifications.title'], tablist: defaultStrings['navigation.notifications.groups'],
      dismiss: defaultStrings['navigation.notifications.dismiss'].replace('{title}', 'Export ready'), markAllRead: defaultStrings['navigation.notifications.markAllRead'],
    })
  })

  it('uses the host catalog, follows a French to Arabic change, and caller text overrides it', () => {
    const view = render(<HarborlineLocaleProvider locale="fr-FR" catalog={French}>{center()}</HarborlineLocaleProvider>)
    expect(chrome()).toEqual({ dialog: 'Avis', tablist: "Groupes d'avis", dismiss: 'Ignorer : Export ready', markAllRead: 'Tout marquer lu' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{center()}</HarborlineLocaleProvider>)
    expect(chrome()).toEqual({ dialog: 'الإشعارات', tablist: 'مجموعات الإشعارات', dismiss: 'تجاهل: Export ready', markAllRead: 'تعليم الكل كمقروء' })
    view.rerender(<HarborlineLocaleProvider locale="ar-SA" catalog={Arabic}>{center({ groupLabel: 'Kinds' })}</HarborlineLocaleProvider>)
    expect(chrome().tablist).toBe('Kinds')
  })

  it('uses a pseudo-localized catalog under en-XA', () => {
    render(<HarborlineLocaleProvider locale="en-XA" catalog={{ 'navigation.notifications.title': '⟦ Ñøţîƒîçåţîøñš ··· ⟧' }}>{center()}</HarborlineLocaleProvider>)
    expect(chrome().dialog).toBe('⟦ Ñøţîƒîçåţîøñš ··· ⟧')
  })

  it('never renders an empty name for a blank catalog value', () => {
    render(<HarborlineLocaleProvider locale="fr-FR" catalog={{ 'navigation.notifications.groups': ' ', 'navigation.notifications.dismiss': '' }}>{center()}</HarborlineLocaleProvider>)
    expect(chrome()).toMatchObject({ tablist: 'Notification groups', dismiss: 'Dismiss: Export ready' })
  })
})
