import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { HarborlineLocaleProvider } from '@harborline-platform/hlp.ui.locale-provider'
import { NotificationCenter, type NotificationCenterItem } from '../NotificationCenter'

// Contract (interface revision 3, G6), React twin of the Blazor NotificationCenterPartialLabelsTests: each label the
// caller omits, or supplies blank, comes from the catalog (host, then default); every other label is kept exactly.
const French = {
  'navigation.notifications.title': 'Avis', 'navigation.notifications.all': 'Tout', 'navigation.notifications.confirm': 'Confirmer',
  'navigation.notifications.deny': 'Refuser', 'navigation.notifications.markAllRead': 'Tout marquer comme lu', 'navigation.notifications.viewAll': 'Tout voir',
  'navigation.notifications.empty': 'Aucun avis', 'navigation.notifications.groups': "Groupes d'avis", 'navigation.notifications.dismiss': 'Ignorer : {title}',
  'navigation.notifications.unread': 'Non lu',
}
const Arabic = {
  'navigation.notifications.title': 'الإشعارات', 'navigation.notifications.all': 'الكل', 'navigation.notifications.confirm': 'تأكيد',
  'navigation.notifications.deny': 'رفض', 'navigation.notifications.markAllRead': 'تعليم الكل كمقروء', 'navigation.notifications.viewAll': 'عرض الكل',
  'navigation.notifications.empty': 'لا توجد إشعارات', 'navigation.notifications.groups': 'مجموعات الإشعارات', 'navigation.notifications.dismiss': 'تجاهل: {title}',
  'navigation.notifications.unread': 'غير مقروء',
}
const ArabicChrome = {
  dialog: 'الإشعارات', markAllRead: 'تعليم الكل كمقروء', tablist: 'مجموعات الإشعارات', all: 'الكل', confirm: 'تأكيد', deny: 'رفض',
  unread: 'غير مقروء', dismiss: 'تجاهل: Export ready', viewAll: 'عرض الكل', empty: 'لا توجد إشعارات',
}
const EnglishChrome = {
  dialog: 'Notifications', markAllRead: 'Mark all read', tablist: 'Notification groups', all: 'All', confirm: 'Confirm', deny: 'Deny',
  unread: 'Unread', dismiss: 'Dismiss: Export ready', viewAll: 'View all', empty: 'No notifications',
}
type Labels = React.ComponentProps<typeof NotificationCenter>['labels']
const items: NotificationCenterItem[] = [
  { id: 'p1', title: 'Approve export', timestamp: 'Now', kind: 'proposal' },
  { id: 'r1', title: 'Export ready', timestamp: 'Now', kind: 'result' },
]
const toggle = () => fireEvent.click(document.querySelector('.hl-notification-center__trigger')!)
const center = (locale: string, catalog: Record<string, string>, labels: Labels, list = items) => (
  <HarborlineLocaleProvider locale={locale} catalog={catalog}>
    <NotificationCenter items={list} labels={labels} onConfirm={() => undefined} onDeny={() => undefined} onDismiss={() => undefined} onMarkAllRead={() => undefined} onViewAll={() => undefined}/>
  </HarborlineLocaleProvider>
)
// Every visible label, the same reading as the Blazor AllChrome helper.
function allChrome(locale: string, catalog: Record<string, string>, labels: Labels) {
  const view = render(center(locale, catalog, labels))
  toggle()
  const actions = document.querySelector('.hl-notification-center__actions')!.querySelectorAll('button')
  const names: Record<string, string | null> = {
    dialog: screen.getByRole('dialog').getAttribute('aria-label'),
    markAllRead: document.querySelector('.hl-notification-center__header button')!.textContent,
    tablist: screen.getByRole('tablist').getAttribute('aria-label'),
    all: screen.getAllByRole('tab')[0]!.textContent,
    confirm: actions[0]!.textContent,
    deny: actions[1]!.textContent,
    unread: document.querySelector('.hl-notification-center__unread')!.getAttribute('aria-label'),
    dismiss: document.querySelector('.hl-notification-center__dismiss')!.getAttribute('aria-label'),
    viewAll: document.querySelector('footer button')!.textContent,
  }
  view.unmount()
  const empty = render(center(locale, catalog, labels, []))
  toggle()
  names.empty = document.querySelector('.hl-notification-center__empty')!.textContent
  empty.unmount()
  return names
}

describe('NotificationCenter partial label overrides', () => {
  it('keeps the supplied field and takes the rest from the Arabic catalog', () => {
    expect(allChrome('ar-SA', Arabic, { confirm: 'Approve' })).toEqual({ ...ArabicChrome, confirm: 'Approve' })
    expect(allChrome('ar-SA', Arabic, { dismiss: title => `Hide ${title}` })).toEqual({ ...ArabicChrome, dismiss: 'Hide Export ready' })
  })

  it('falls back to the catalog for an omitted or blank field', () => {
    expect(allChrome('ar-SA', Arabic, { title: ' ', deny: 'Decline', empty: '', dismiss: () => '\n' })).toEqual({ ...ArabicChrome, deny: 'Decline' })
    expect(allChrome('ar-SA', Arabic, {})).toEqual(ArabicChrome)
  })

  it('treats only the ECMA-262 white space and line terminator set as blank', () => {
    // The 25 characters String.prototype.trim strips, the same list as the Blazor test (T-1107).
    const ecmaBlank = '\t\n\v\f\r \u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000\ufeff'
    expect(ecmaBlank.length).toBe(25)
    for (const blank of [...ecmaBlank, ecmaBlank]) {
      const labels = { title: blank, all: blank, confirm: blank, deny: blank, markAllRead: blank, viewAll: blank, empty: blank, dismiss: () => blank, unread: blank, groupLabel: blank }
      expect(allChrome('ar-SA', Arabic, labels)).toEqual(ArabicChrome)
    }
    // U+FEFF is blank; U+0085 (NEL) is not. Both lanes agree.
    expect(allChrome('ar-SA', Arabic, { title: '\ufeff', deny: '\ufeff', unread: '\u0085', groupLabel: '\u0085', dismiss: () => '\ufeff' })).toEqual({ ...ArabicChrome, unread: '\u0085', tablist: '\u0085' })
  })

  it('updates the fallback fields on a locale change and keeps the explicit one', () => {
    const labels = { confirm: 'Approve' }
    const view = render(center('fr-FR', French, labels))
    const read = () => {
      toggle()
      const actions = document.querySelector('.hl-notification-center__actions')!.querySelectorAll('button')
      const result = [screen.getByRole('dialog').getAttribute('aria-label'), actions[0]!.textContent, actions[1]!.textContent, document.querySelector('.hl-notification-center__dismiss')!.getAttribute('aria-label')]
      toggle()
      return result
    }
    expect(read()).toEqual(['Avis', 'Approve', 'Refuser', 'Ignorer : Export ready'])
    view.rerender(center('ar-SA', Arabic, labels))
    expect(read()).toEqual(['الإشعارات', 'Approve', 'رفض', 'تجاهل: Export ready'])
  })

  it('keeps an explicit group label', () => {
    expect(allChrome('ar-SA', Arabic, { groupLabel: 'Kinds' })).toEqual({ ...ArabicChrome, tablist: 'Kinds' })
  })

  it('keeps a fully specified English labels object in English under an Arabic catalog', () => {
    const english = { title: 'Notifications', all: 'All', confirm: 'Confirm', deny: 'Deny', markAllRead: 'Mark all read', viewAll: 'View all', empty: 'No notifications', dismiss: (title: string) => `Dismiss: ${title}`, unread: 'Unread', groupLabel: 'Notification groups' }
    expect(allChrome('ar-SA', Arabic, english)).toEqual(EnglishChrome)
  })
})
