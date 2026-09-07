import type { Meta, StoryObj } from '@storybook/react-vite'
import { ContextMenu, type ContextMenuGroup } from '@harborline-software/ui-react'

type ContextMenuScenarioId =
  | 'context-menu.closed'
  | 'context-menu.pointer'
  | 'context-menu.keyboard'
  | 'context-menu.groups'
  | 'context-menu.locale-en'
  | 'context-menu.locale-pseudo'
  | 'context-menu.locale-ar'
  | 'context-menu.theme-light'
  | 'context-menu.theme-dark'
  | 'context-menu.content'

const copy: Record<ContextMenuScenarioId, [string, string]> = {
  'context-menu.closed': ['Closed', 'The trigger remains available without rendering a latent menu.'],
  'context-menu.pointer': ['Pointer open', 'Pointer placement is clamped to the viewport and dismisses without selection.'],
  'context-menu.keyboard': ['Keyboard and focus', 'Shift+F10 enters the first enabled item and navigation skips disabled actions.'],
  'context-menu.groups': ['Groups and states', 'Separators, disabled actions, icons, and destructive intent retain native semantics.'],
  'context-menu.locale-en': ['English locale', 'The host supplies the accessible name and language direction.'],
  'context-menu.locale-pseudo': ['Pseudo locale', 'Expanded labels expose clipping and fixed-width assumptions.'],
  'context-menu.locale-ar': ['Arabic locale', 'RTL labels and logical alignment preserve action meaning.'],
  'context-menu.theme-light': ['Light theme', 'Default, active, disabled, danger, separator, focus, and motion states.'],
  'context-menu.theme-dark': ['Dark theme', 'The same state vocabulary on the Harborline dark surface.'],
  'context-menu.content': ['Content resilience', 'Hostile-but-valid labels remain text across the trigger and grouped menu.'],
}

function MenuIcon({children}: {children: string}) {
  return <span className="hl-gallery-icon" aria-hidden="true">{children}</span>
}

function ContextMenuScenario({scenarioId}: {scenarioId: ContextMenuScenarioId}) {
  const [title, description] = copy[scenarioId]
  const pseudo = scenarioId === 'context-menu.locale-pseudo'
  const arabic = scenarioId === 'context-menu.locale-ar'
  const content = scenarioId === 'context-menu.content'
  const groups: ContextMenuGroup[] = content ? [{items: [
    {id: 'long', label: 'Awaiting third-party structural certification review', onSelect() {}},
    {id: 'number', label: '1,284,905', onSelect() {}},
    {id: 'hostile', label: 'Bay 4 <grid C-7> & 8', onSelect() {}},
    {id: 'name', label: 'Ordnance Survey — Niño Ångström', onSelect() {}},
  ]}] : [{items: [
    {id: 'copy', label: pseudo ? '⟦ Çøþÿ îñšþëçţîøñ ···· ⟧' : arabic ? 'نسخ الفحص' : 'Copy inspection', icon: <MenuIcon>⧉</MenuIcon>, onSelect() {}},
    {id: 'paste-disabled', label: pseudo ? '⟦ Þåšţë ···· ⟧' : arabic ? 'لصق' : 'Paste', disabled: true, onSelect() {}},
  ]}, {items: [
    {id: 'delete', label: pseudo ? '⟦ Đëlëţë îñšþëçţîøñ ···· ⟧' : arabic ? 'حذف الفحص' : 'Delete inspection', icon: <MenuIcon>×</MenuIcon>, danger: true, onSelect() {}},
  ]}]
  const locale = arabic ? 'ar-SA' : pseudo ? 'en-XA' : 'en-US'
  const direction = arabic ? 'rtl' : 'ltr'
  const label = arabic ? 'قائمة السياق' : pseudo ? '[Çôñţëxţ mëñüü]' : 'Inspection actions'

  return <section className="hl-gallery-scene" data-gallery-probe data-gallery-scenario={scenarioId}
    data-theme={scenarioId === 'context-menu.theme-light' ? 'light' : scenarioId === 'context-menu.theme-dark' ? 'dark' : undefined}>
    <header className="hl-gallery-heading"><h2>{title}</h2><p>{description}</p></header>
    <div className="hl-gallery-stage hl-gallery-menu-stage">
      <ContextMenu groups={groups} accessibleLabel={label} locale={locale} direction={direction} className="hl-gallery-menu-trigger">
        <div data-context-trigger tabIndex={0} className="hl-gallery-record">
          {content ? <><strong>{'Awaiting third-party structural certification review'}</strong><span>{'1,284,905'}</span><span>{'Bay 4 <grid C-7> & 8'}</span><span>{'Ordnance Survey — Niño Ångström'}</span></> : <><strong>{pseudo ? '⟦ Îñšþëçţîøñ 42 ···· ⟧' : arabic ? 'الفحص 42' : 'Inspection 42'}</strong><span>{pseudo ? '⟦ Řîĝhţ-çļîçķ øř Þřëšš Šhîƒţ+F10 ······ ⟧' : arabic ? 'انقر بزر الماوس الأيمن أو اضغط Shift+F10' : 'Right-click or press Shift+F10'}</span></>}
        </div>
      </ContextMenu>
    </div>
  </section>
}

const meta = {
  title: 'Platform/Context Menu',
  component: ContextMenuScenario,
  tags: ['autodocs'],
  parameters: {controls: {disable: true}},
} satisfies Meta<typeof ContextMenuScenario>

export default meta
type Story = StoryObj<typeof meta>

export const PointerOpen: Story = {name: 'Pointer open', args: {scenarioId: 'context-menu.pointer'}}
export const Closed: Story = {args: {scenarioId: 'context-menu.closed'}}
export const KeyboardAndFocus: Story = {name: 'Keyboard and focus', args: {scenarioId: 'context-menu.keyboard'}}
export const GroupsAndStates: Story = {name: 'Groups and states', args: {scenarioId: 'context-menu.groups'}}
export const EnglishLocale: Story = {name: 'English locale', args: {scenarioId: 'context-menu.locale-en'}}
export const PseudoLocale: Story = {name: 'Pseudo locale', args: {scenarioId: 'context-menu.locale-pseudo'}}
export const ArabicLocale: Story = {name: 'Arabic locale', args: {scenarioId: 'context-menu.locale-ar'}}
export const LightTheme: Story = {name: 'Light theme', args: {scenarioId: 'context-menu.theme-light'}}
export const DarkTheme: Story = {name: 'Dark theme', args: {scenarioId: 'context-menu.theme-dark'}}
;export const Content: Story = {name: 'Content resilience', args: {scenarioId: 'context-menu.content'}}
