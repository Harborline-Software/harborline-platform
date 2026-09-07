import type { Meta, StoryObj } from '@storybook/react-vite'
import { NotificationCenter, HarborlineLocaleProvider } from '@harborline-software/ui-react'

type ScenarioId =
  | 'notification-center.empty-badges'
  | 'notification-center.groups'
  | 'notification-center.actions'
  | 'notification-center.keyboard-dismissal'
  | 'notification-center.locale-en'
  | 'notification-center.locale-pseudo'
  | 'notification-center.locale-ar'
  | 'notification-center.theme-light'
  | 'notification-center.theme-dark'
  | 'notification-center.content'

const copy: Record<ScenarioId, [string, string]> = {
  'notification-center.empty-badges': ['Empty and badge modes', 'Unread and decision counts produce bounded badges and complete bell names.'],
  'notification-center.groups': ['Groups and filtering', 'Group order follows the caller and filtering never reorders items.'],
  'notification-center.actions': ['Item actions', 'Proposal decisions, informational activation, mark-read, dismiss, and view-all dispatch bounded callbacks.'],
  'notification-center.keyboard-dismissal': ['Keyboard, focus, and dismissal', 'The bell opens a dialog, focus enters, and every dismissal path has a deterministic focus outcome.'],
  'notification-center.locale-en': ['English locale', 'Caller and catalog chrome remain localized without formatting timestamps.'],
  'notification-center.locale-pseudo': ['Pseudo locale', 'Expanded titles, tabs, and actions expose clipping and reflow defects.'],
  'notification-center.locale-ar': ['Arabic locale', 'Logical edges follow RTL while tab and action order remain stable.'],
  'notification-center.theme-light': ['Light theme', 'Unread, hover, focus, danger, dialog, and badge states use public light-theme tokens.'],
  'notification-center.theme-dark': ['Dark theme', 'The same inbox shell on the Harborline dark surface.'],
  'notification-center.content': ['Content resilience', 'Hostile notification content carries a title longer than the popover row, a grouped large number, escaped angle brackets and an ampersand, and an actor name with diacritics and an em dash.'],
}

interface GalleryNotification {
  id: string
  title: string
  body?: string
  preview?: string
  actor?: string
  timestamp: string
  kind: string
  read: boolean
}

const englishItems: GalleryNotification[] = [
  { id: 'proposal-1', title: 'Approve blueprint placement', body: 'Bay 4 matches the captured pose.', preview: 'Place photo marker at grid C-7', actor: 'Avery', timestamp: '2 min ago', kind: 'proposal', read: false },
  { id: 'result-1', title: 'Pose calculation complete', body: 'Orientation confidence is 97%.', timestamp: '10 min ago', kind: 'result', read: false },
  { id: 'system-1', title: 'Blueprint indexed', timestamp: 'Yesterday', kind: 'system', read: true },
]

const contentItems: GalleryNotification[] = [
  { id: 'content-1', title: 'Awaiting third-party structural certification review', body: '1,284,905', preview: 'Bay 4 <grid C-7> & 8', actor: 'Ordnance Survey — Niño Ångström', timestamp: 'Now', kind: 'result', read: false },
]

function NotificationFixture({ scenarioId, items = englishItems, badgeMode = 'unread' as const }: { scenarioId: ScenarioId; items?: GalleryNotification[]; badgeMode?: 'unread' | 'decisions' }) {
  const pseudo = scenarioId === 'notification-center.locale-pseudo'
  const arabic = scenarioId === 'notification-center.locale-ar'
  const labels = arabic ? {
    title: 'الإشعارات', all: 'الكل', confirm: 'تأكيد', deny: 'رفض', markAllRead: 'وضع علامة مقروء', viewAll: 'عرض الكل',
    empty: 'لا توجد إشعارات', groupLabel: 'مجموعات الإشعارات', unread: 'غير مقروء', dismiss: (itemTitle: string) => `تجاهل: ${itemTitle}`,
  } : pseudo ? {
    title: '⟦ Ñøţîƒîçåţîøñš ···· ⟧', all: '⟦ Åļļ ·· ⟧', confirm: '⟦ Çøñƒîřm ··· ⟧', deny: '⟦ Đëñÿ ·· ⟧',
    markAllRead: '⟦ Måřķ åļļ řëåđ ···· ⟧', viewAll: '⟦ Ṽîëŵ åļļ ··· ⟧', empty: '⟦ Ñø ñøţîƒîçåţîøñš ······ ⟧',
    groupLabel: '⟦ Ñøţîƒîçåţîøñ ĝřøûþš ······ ⟧', unread: '⟦ Ûñřëåđ ··· ⟧', dismiss: (itemTitle: string) => `⟦ Đîšmîšš: ${itemTitle} ··· ⟧`,
  } : undefined
  const groups = [
    { kind: 'proposal', label: arabic ? 'الموافقات' : pseudo ? '⟦ Åþþřøṽåļš ··· ⟧' : 'Approvals' },
    { kind: 'result', label: arabic ? 'النتائج' : pseudo ? '⟦ Řëšûļţš ··· ⟧' : 'Results' },
    { kind: 'system', label: arabic ? 'النظام' : pseudo ? '⟦ Šÿšţëm ··· ⟧' : 'System' },
  ]
  return <NotificationCenter
    items={items}
    groups={groups}
    labels={labels}
    direction={arabic ? 'rtl' : 'ltr'}
    badgeMode={badgeMode}
    maxVisible={2}
    formatCount={(count: number) => arabic ? new Intl.NumberFormat('ar-SA').format(count) : `#${count}`}
    getBellLabel={({ pending, unread, mode }: { pending: number; unread: number; mode: 'unread' | 'decisions' }) => arabic
      ? `الإشعارات، ${mode === 'decisions' ? pending : unread}`
      : pseudo
        ? `⟦ Ñøţîƒîçåţîøñš, ${mode === 'decisions' ? pending : unread} ···· ⟧`
        : `Notifications, ${mode === 'decisions' ? pending + ' approvals pending' : unread + ' unread'}`}
    onConfirm={() => {}}
    onDeny={() => {}}
    onAction={() => {}}
    onMarkAllRead={() => {}}
    onDismiss={() => {}}
    onViewAll={() => {}}
  />
}

function NotificationCenterScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'notification-center.locale-pseudo'
  const arabic = scenarioId === 'notification-center.locale-ar'
  const theme = scenarioId === 'notification-center.theme-light' ? 'light' : scenarioId === 'notification-center.theme-dark' ? 'dark' : undefined
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const items = scenarioId === 'notification-center.content' ? contentItems : arabic ? [
    { ...englishItems[0], title: 'الموافقة على موضع المخطط', body: 'يتطابق الموضع مع الصورة.', actor: 'أمل', timestamp: 'منذ دقيقتين' },
    { ...englishItems[1], title: 'اكتمل حساب الوضعية', body: 'دقة الاتجاه ٩٧٪.', timestamp: 'منذ عشر دقائق' },
    { ...englishItems[2], title: 'تمت فهرسة المخطط', timestamp: 'أمس' },
  ] : pseudo ? englishItems.map(item => ({ ...item, title: `⟦ ${item.title} ······ ⟧`, body: item.body ? `⟦ ${item.body} ······ ⟧` : undefined })) : englishItems

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid">
      <HarborlineLocaleProvider locale={locale}>
        {scenarioId === 'notification-center.empty-badges' ? <>
          <NotificationFixture scenarioId={scenarioId} items={[]} />
          <NotificationFixture scenarioId={scenarioId} items={items} badgeMode="unread" />
          <NotificationFixture scenarioId={scenarioId} items={items} badgeMode="decisions" />
        </> : scenarioId === 'notification-center.content' ? <NotificationFixture scenarioId={scenarioId} items={contentItems} badgeMode="unread" /> : <NotificationFixture scenarioId={scenarioId} items={items} badgeMode={scenarioId === 'notification-center.actions' ? 'decisions' : 'unread'} />}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/Notification Center', component: NotificationCenterScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof NotificationCenterScenario>
export default meta
type Story = StoryObj<typeof meta>

export const EmptyAndBadgeModes: Story = { name: 'Empty and badge modes', args: { scenarioId: 'notification-center.empty-badges' } }
export const GroupsAndFiltering: Story = { name: 'Groups and filtering', args: { scenarioId: 'notification-center.groups' } }
export const ItemActions: Story = { name: 'Item actions', args: { scenarioId: 'notification-center.actions' } }
export const KeyboardFocusAndDismissal: Story = { name: 'Keyboard, focus, and dismissal', args: { scenarioId: 'notification-center.keyboard-dismissal' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'notification-center.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'notification-center.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'notification-center.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'notification-center.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'notification-center.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'notification-center.content' } }
