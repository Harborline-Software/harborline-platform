import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import { HarborlineLocaleProvider, UserMenu } from '@harborline-software/ui-react'

type ScenarioId =
  | 'user-menu.identity' | 'user-menu.simple-menu' | 'user-menu.custom-dialog' | 'user-menu.placement-dismissal'
  | 'user-menu.disclosure'
  | 'user-menu.locale-en' | 'user-menu.locale-pseudo' | 'user-menu.locale-ar'
  | 'user-menu.theme-light' | 'user-menu.theme-dark'
  | 'user-menu.content'

const copy: Record<ScenarioId, [string, string]> = {
  'user-menu.identity': ['Identity, avatar, and initials', 'Structured identity remains authoritative and avatar failure preserves a stable trigger name.'],
  'user-menu.simple-menu': ['Simple menu and selection', 'Ordered actions and links use a complete menu keyboard model with explicit disabled and close policy.'],
  'user-menu.custom-dialog': ['Custom controls dialog', 'Embedded native controls change the popup pattern to dialog and retain their own keyboard behavior.'],
  'user-menu.placement-dismissal': ['Placement, touch, and dismissal', 'Logical alignment, viewport clamping, touch sizing, and focus restoration remain deterministic.'],
  'user-menu.disclosure': ['Disclosure, open and closed', 'Both disclosure states drawn together: the trigger closed, and the panel opened through defaultOpen rather than a click.'],
  'user-menu.locale-en': ['English locale', 'Catalog-backed trigger, panel, and sign-out labels include caller identity.'],
  'user-menu.locale-pseudo': ['Pseudo locale', 'Expanded identity and action copy reflow without clipping.'],
  'user-menu.locale-ar': ['Arabic locale', 'Logical placement follows RTL while item order remains stable.'],
  'user-menu.theme-light': ['Light theme', 'Closed, open, hover, focus, disabled, and danger states use public tokens.'],
  'user-menu.theme-dark': ['Dark theme', 'The same account surface on the Harborline dark theme.'],
  'user-menu.content': ['Content resilience', 'Hostile-but-valid identity and action labels remain text.'],
}

function MenuFixture({ scenarioId, placement = 'bottom', defaultOpen = false }: { scenarioId: ScenarioId; placement?: 'top' | 'bottom'; defaultOpen?: boolean }) {
  const content = scenarioId === 'user-menu.content'
  const pseudo = scenarioId === 'user-menu.locale-pseudo'
  const arabic = scenarioId === 'user-menu.locale-ar'
  const [compact, setCompact] = useState(false)
  const identity = {
    name: content ? 'Ordnance Survey — Niño Ångström' : arabic ? 'آدا لوفليس' : pseudo ? '⟦ Åđå ßÿřøñ Ļøṽëļåçë ······ ⟧' : 'Ada Lovelace',
    email: content ? '1,284,905' : 'ada@example.test',
    role: content ? 'Bay 4 <grid C-7> & 8' : arabic ? 'محللة' : 'Analyst',
  }
  const items = scenarioId === 'user-menu.custom-dialog'
    ? [{ id: 'density', kind: 'custom' as const, label: 'Display density', closeOnSelect: false, content: <label><input type="checkbox" checked={compact} onChange={event => setCompact(event.currentTarget.checked)} /> Compact rows</label> }]
    : content ? [
      { id: 'long', kind: 'action' as const, label: 'Awaiting third-party structural certification review', onActivate: () => {} },
      { id: 'number', kind: 'action' as const, label: '1,284,905', onActivate: () => {} },
      { id: 'hostile', kind: 'action' as const, label: 'Bay 4 <grid C-7> & 8', onActivate: () => {} },
      { id: 'name', kind: 'action' as const, label: 'Ordnance Survey — Niño Ångström', onActivate: () => {} },
    ]
    : [
      { id: 'profile', kind: 'action' as const, label: arabic ? 'الملف الشخصي' : pseudo ? '⟦ Þřøƒîļë ··· ⟧' : 'Profile', onActivate: () => {} },
      { id: 'pin', kind: 'action' as const, label: arabic ? 'تثبيت الشريط' : 'Pin navigation', closeOnSelect: false, onActivate: () => {} },
      { id: 'separator-1', kind: 'separator' as const },
      { id: 'billing', kind: 'link' as const, label: arabic ? 'الفوترة' : 'Billing', destination: '/billing', disabled: true },
    ]
  return content ? <UserMenu identity={identity} items={items} labels={{trigger: name => `Account for ${name}`, panel: 'Content resilience account', signOut: 'Sign out'}} /> : <UserMenu
    identity={identity}
    items={items}
    defaultOpen={defaultOpen}
    onSignOut={() => {}}
    placement={placement}
    alignment={arabic ? 'inline-start' : 'inline-end'}
    direction={arabic ? 'rtl' : 'ltr'}
    canShowRail={scenarioId !== 'user-menu.locale-ar' && scenarioId !== 'user-menu.placement-dismissal'}
    labels={{
      trigger: name => arabic ? `حساب ${name}` : `Account for ${name}`,
      panel: arabic ? 'الحساب' : 'Account',
      signOut: arabic ? 'تسجيل الخروج' : pseudo ? '⟦ Šîĝñ øûţ ··· ⟧' : 'Sign out',
    }}
  />
}

function UserMenuScenario({ scenarioId }: { scenarioId: ScenarioId }) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'user-menu.locale-pseudo'
  const arabic = scenarioId === 'user-menu.locale-ar'
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const theme = scenarioId === 'user-menu.theme-light' ? 'light' : scenarioId === 'user-menu.theme-dark' ? 'dark' : undefined
  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId} data-theme={theme} dir={arabic ? 'rtl' : 'ltr'}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-feedback-grid" style={{ minHeight: scenarioId === 'user-menu.disclosure' ? 470 : 320, alignItems: 'end' }}>
      <HarborlineLocaleProvider locale={locale}>
        <MenuFixture scenarioId={scenarioId} />
        {scenarioId === 'user-menu.placement-dismissal' && <MenuFixture scenarioId={scenarioId} placement="top" />}
        {scenarioId === 'user-menu.disclosure' && <MenuFixture scenarioId={scenarioId} defaultOpen />}
      </HarborlineLocaleProvider>
    </div>
  </section>
}

const meta = { title: 'Platform/User Menu', component: UserMenuScenario, tags: ['autodocs'], parameters: { layout: 'padded', controls: { disable: true } } } satisfies Meta<typeof UserMenuScenario>
export default meta
type Story = StoryObj<typeof meta>
export const IdentityAvatarAndInitials: Story = { name: 'Identity, avatar, and initials', args: { scenarioId: 'user-menu.identity' } }
export const SimpleMenuAndSelection: Story = { name: 'Simple menu and selection', args: { scenarioId: 'user-menu.simple-menu' } }
export const CustomControlsDialog: Story = { name: 'Custom controls dialog', args: { scenarioId: 'user-menu.custom-dialog' } }
export const PlacementTouchAndDismissal: Story = { name: 'Placement, touch, and dismissal', args: { scenarioId: 'user-menu.placement-dismissal' } }
export const DisclosureOpenAndClosed: Story = { name: 'Disclosure, open and closed', args: { scenarioId: 'user-menu.disclosure' } }
export const EnglishLocale: Story = { name: 'English locale', args: { scenarioId: 'user-menu.locale-en' } }
export const PseudoLocale: Story = { name: 'Pseudo locale', args: { scenarioId: 'user-menu.locale-pseudo' } }
export const ArabicLocale: Story = { name: 'Arabic locale', args: { scenarioId: 'user-menu.locale-ar' } }
export const LightTheme: Story = { name: 'Light theme', args: { scenarioId: 'user-menu.theme-light' } }
export const DarkTheme: Story = { name: 'Dark theme', args: { scenarioId: 'user-menu.theme-dark' } }
;export const Content: Story = { name: 'Content resilience', args: { scenarioId: 'user-menu.content' } }
