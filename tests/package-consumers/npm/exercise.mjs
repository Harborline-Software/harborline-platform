import assert from 'node:assert/strict'
import React from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import {
  Accordion,
  ActionMenu,
  ActivityLog,
  Alert,
  Badge,
  Breadcrumb,
  Button,
  Chart,
  Chat,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  CheckBox,
  Chip,
  Collapsible,
  ContextMenu,
  ConversationList,
  DateField,
  DateTimeField,
  DataExportButton,
  DataGrid,
  ErrorCard,
  FormView,
  FormViewField,
  FormField,
  GuardedControl,
  Gantt,
  IconButton,
  Input,
  LayersRail,
  LoadingState,
  MediaQuery,
  NumberField,
  NumericTextBox,
  NotificationCenter,
  Page,
  Popover,
  PopoverContent,
  PopoverTrigger,
  RadioGroup,
  SearchInput,
  SegmentedControl,
  SelectField,
  Switch,
  Toaster,
  UserMenu,
  AppLayout,
  createToastService,
  Sheet,
  SheetContent,
  SheetTrigger,
  Spotlight,
  Spinner,
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextArea,
  Tooltip,
  Window,
  WindowActionsBar,
  EmptyState,
  HarborlineLocaleProvider,
  Separator,
  clampContextMenuPosition,
  cn,
  createLayersState,
  createSideNavGroup,
  defaultStrings,
  defaultRailLabels,
  directionForLocale,
  interpolate,
  resolveText,
  touchTarget,
  touchTargetPseudo,
  touchTargetPseudoOverlay,
  toneStyle,
  useCanShowMasterDetail,
  useCanShowRail,
  useIsMobile,
  useMediaQuery,
  useNavCollapsed,
  useOutsideClick,
  useScrollAffordance,
  handleScrollAffordanceKeyDown,
} from '@harborline-software/ui-react'

const html = renderToStaticMarkup(React.createElement(
  Button,
  { type: 'button', 'data-fixture': 'packed' },
  'Continue',
))
assert.match(html, /<button/)
assert.match(html, /type="button"/)
assert.match(html, /data-fixture="packed"/)
assert.match(html, />Continue<\/button>/)

const waveTwoUi = renderToStaticMarkup(React.createElement(
  Card,
  { variant: 'raised', padding: 'lg', separators: true, 'data-fixture': 'wave-02' },
  React.createElement(CardHeader, null, React.createElement(CardTitle, null, 'Inspection')),
  React.createElement(CardContent, null,
    React.createElement(Accordion, {
      type: 'single',
      collapsible: false,
      items: [{ value: 'details', title: 'Details', children: 'Condition' }],
    }),
    React.createElement(Separator, { decorative: false, label: 'Actions' }),
    React.createElement(IconButton, { 'aria-label': 'Save inspection', loading: true }, 'S'),
  ),
))
assert.match(waveTwoUi, /data-hl-variant="raised"/)
assert.match(waveTwoUi, /aria-expanded="true"/)
assert.match(waveTwoUi, /role="separator"/)
assert.match(waveTwoUi, /aria-busy="true"/)
assert.equal(createLayersState(['details', 'history']).activeId, 'details')
assert.equal(directionForLocale('ar-SA'), 'rtl')

const waveTwoTwoUi = renderToStaticMarkup(React.createElement(
  Collapsible,
  { title: 'Structure', defaultOpen: true },
  React.createElement(Breadcrumb, {
    items: [
      { id: 'site', label: 'Site', href: '/site' },
      { id: 'structure', label: 'Structure' },
    ],
  }),
  React.createElement(CheckBox, { checked: 'mixed', label: 'Include spatial capture' }),
))
assert.match(waveTwoTwoUi, /aria-expanded="true"/)
assert.match(waveTwoTwoUi, /aria-current="page"/)
assert.match(waveTwoTwoUi, /aria-checked="mixed"/)
assert.equal(toneStyle('info').swatch, 'var(--color-secondary)')
assert.equal(typeof useCanShowMasterDetail, 'function')
assert.equal(typeof useIsMobile, 'function')
assert.equal(typeof useCanShowRail, 'function')

const waveTwoThreeUi = renderToStaticMarkup(React.createElement(
  FormField,
  { name: 'starts', label: 'Start time', required: true, hint: 'Local wall-clock time' },
  React.createElement(DateTimeField, { name: 'starts', value: '2026-08-09T14:30', onChange() {} }),
))
assert.match(waveTwoThreeUi, /for="starts"/)
assert.match(waveTwoThreeUi, /type="datetime-local"/)
assert.match(waveTwoThreeUi, /required=""/)
assert.match(waveTwoThreeUi, /aria-describedby="starts-hint"/)
const packedFields = renderToStaticMarkup(React.createElement('div', null,
  React.createElement(DateField, { name: 'issued', value: '2026-08-09', onChange() {}, 'aria-label': 'Issue date' }),
  React.createElement(NumberField, { name: 'quantity', value: '42.50', step: .5, onChange() {}, 'aria-label': 'Quantity' }),
  React.createElement(RadioGroup, { name: 'billing', value: 'monthly', onChange() {}, 'aria-label': 'Billing', options: [{ value: 'monthly', label: 'Monthly' }, { value: 'annual', label: 'Annual' }] }),
))
assert.match(packedFields, /type="date"/)
assert.match(packedFields, /type="number"/)
assert.match(packedFields, /role="radiogroup"/)
const packedPopover = renderToStaticMarkup(React.createElement(Popover, null,
  React.createElement(PopoverTrigger, null, 'Open details'),
  React.createElement(PopoverContent, null, 'Details'),
))
assert.match(packedPopover, /aria-haspopup="dialog"/)

const waveTwoFourUi = renderToStaticMarkup(React.createElement('div', null,
  React.createElement(Sheet, { defaultOpen: false },
    React.createElement(SheetTrigger, null, 'Open filters'),
    React.createElement(SheetContent, { closeLabel: 'Close filters' }, 'Filters')),
  React.createElement(Spotlight, { open: false, onOpenChange() {}, query: '', onQueryChange() {}, sections: [], ariaLabel: 'Search structures' }),
  React.createElement(TextArea, { defaultValue: 'Inspection notes', showCounter: true, maxLength: 80, 'aria-label': 'Inspection notes' }),
  React.createElement(Tooltip, { content: 'View details', defaultOpen: true }, React.createElement('button', { type: 'button' }, 'Details')),
  React.createElement(Table, { density: 'sm' },
    React.createElement(TableCaption, null, 'Structures'),
    React.createElement(TableHead, null, React.createElement(TableRow, null, React.createElement(TableHeaderCell, null, 'Name'))),
    React.createElement(TableBody, null, React.createElement(TableRow, null, React.createElement(TableCell, null, 'North Pier')))),
))
assert.match(waveTwoFourUi, /aria-haspopup="dialog"/)
assert.match(waveTwoFourUi, /hl-text-area__counter/)
assert.match(waveTwoFourUi, /role="tooltip"/)
assert.match(waveTwoFourUi, /<caption/)
assert.equal(typeof Window, 'function')
assert.equal(typeof WindowActionsBar, 'function')

const waveThreeOneUi = renderToStaticMarkup(React.createElement('div', null,
  React.createElement(Alert, { variant: 'success', title: 'Saved', closable: true, dismissLabel: 'Close' }, 'Record updated'),
  React.createElement(Badge, { themeColor: 'warning', fillMode: 'outline' }, 'Pending'),
  React.createElement(DataExportButton, { formats: ['csv', 'pdf'], onExport() {} }),
  React.createElement(Spinner, { type: 'converging', label: 'Loading records' }),
))
assert.match(waveThreeOneUi, /role="status"/)
assert.match(waveThreeOneUi, /Pending/)
assert.match(waveThreeOneUi, /aria-haspopup="menu"/)
assert.match(waveThreeOneUi, /Loading records/)
assert.equal(createSideNavGroup('main', [{ id: 'home', label: 'Home' }]).items[0].id, 'home')

const waveThreeTwoUi = renderToStaticMarkup(React.createElement('div', null,
  React.createElement(ActionMenu, {
    accessibleLabel: 'Inspection actions',
    items: [{ id: 'approve', label: 'Approve' }, { type: 'separator' }, { id: 'remove', label: 'Remove', destructive: true }],
    onSelect() {},
  }),
  React.createElement(ActivityLog, {
    label: 'Inspection activity',
    entries: [{ id: 'created', actor: 'Morgan', action: 'created the inspection', timestamp: 'Today', machineTimestamp: '2026-08-10T12:00:00Z' }],
  }),
  React.createElement(Chip, { removable: true, text: 'North Pier', onRemove() {} }),
  React.createElement(ConversationList, {
    conversations: [{ id: 'inspection-42', title: 'Inspection 42', timestamp: 'Today', preview: 'North Pier' }],
    activeId: 'inspection-42',
    onSelect() {},
  }),
))
assert.match(waveThreeTwoUi, /aria-haspopup="menu"/)
assert.match(waveThreeTwoUi, /Inspection activity/)
assert.match(waveThreeTwoUi, /North Pier/)
assert.match(waveThreeTwoUi, /aria-current="true"/)
assert.equal(typeof useNavCollapsed, 'function')
assert.equal(typeof useScrollAffordance, 'function')
assert.equal(typeof handleScrollAffordanceKeyDown, 'function')

const waveThreeThreeUi = renderToStaticMarkup(React.createElement(Page, {
  title: 'Inspection 42',
  subtitle: 'North Pier',
  actions: React.createElement(GuardedControl, {
    action: 'delete',
    classificationId: 'destructive',
    label: 'Delete inspection',
    onCommit() {},
  }),
}, React.createElement('div', null,
  React.createElement(Input, { value: 'North Pier', onChange() {}, prefix: 'Site', 'aria-label': 'Structure' }),
  React.createElement(SearchInput, { value: 'north', onChange() {}, accessibleLabel: 'Search structures' }),
  React.createElement(NotificationCenter, {
    items: [{ id: 'approval', title: 'Approval needed', timestamp: 'Today', kind: 'proposal' }],
  }),
)))
assert.match(waveThreeThreeUi, /Inspection 42/)
assert.match(waveThreeThreeUi, /data-guard-state="covered"/)
assert.match(waveThreeThreeUi, /hl-input__prefix/)
assert.match(waveThreeThreeUi, /role="searchbox"/)
assert.match(waveThreeThreeUi, /Notifications/)
assert.equal(typeof LayersRail, 'function')

const packedToastService = createToastService()
packedToastService.success('Ready')
const waveThreeFourUi = renderToStaticMarkup(React.createElement(AppLayout, {
  railCapable: true,
  sideNav: React.createElement('div', null, 'App navigation'),
  body: React.createElement('div', null,
    React.createElement(SegmentedControl, { accessibleName: 'View', options: [{ value: 'day', label: 'Day' }, { value: 'week', label: 'Week' }], value: 'day', onValueChange() {} }),
    React.createElement(SelectField, { name: 'status', accessibleName: 'Status', options: [{ value: 'active', label: 'Active' }], value: 'active', onValueChange() {} }),
    React.createElement(Switch, { accessibleName: 'Alerts', checked: true, onCheckedChange() {} }),
    React.createElement(UserMenu, { identity: { name: 'Ada Lovelace' }, items: [] }),
    React.createElement(Toaster, { service: packedToastService }),
  ),
}))
assert.match(waveThreeFourUi, /role="radiogroup"/)
assert.match(waveThreeFourUi, /role="combobox"/)
assert.match(waveThreeFourUi, /role="switch"/)
assert.match(waveThreeFourUi, /Account menu for Ada Lovelace/)
assert.match(waveThreeFourUi, /role="status"/)
assert.equal((waveThreeFourUi.match(/<main/g) ?? []).length, 1)
assert.equal((waveThreeFourUi.match(/<nav/g) ?? []).length, 1)

const waveThreeFiveUi = renderToStaticMarkup(React.createElement('div', null,
  React.createElement(Chart, { definition: { categories: ['Bay 1', 'Bay 2'], series: [{ name: 'Captured', values: [12, null] }] } }),
  React.createElement(Chat, { messages: [], user: { name: 'Inspector' }, inputValue: '', onInputValueChange() {}, onSubmit() {}, suggestions: [{ title: 'Summarize', prompt: 'Summarize the inspection' }] }),
  React.createElement(DataGrid, { accessibleName: 'Structures', rows: [{ id: 'north', structure: 'North Pier', photos: 4 }], columns: [{ id: 'structure', field: 'structure', header: 'Structure', removalPriority: 20 }, { id: 'photos', field: 'photos', header: 'Photos', removalPriority: 10, valueKind: 'number' }], getRowId: row => row.id, zebra: true }),
  React.createElement(Gantt, { accessibleName: 'Placement schedule', tasks: [{ id: 'capture', title: 'Capture', start: '2026-08-11', end: '2026-08-12', progress: 50 }], zoom: 'week' }),
  React.createElement(NumericTextBox, { value: 42.5, onChange() {}, min: 0, max: 100, decimals: 2, 'aria-label': 'Placement progress' }),
))
assert.match(waveThreeFiveUi, /class="hl-chart"/)
assert.match(waveThreeFiveUi, /role="log"/)
assert.match(waveThreeFiveUi, /aria-label="Structures"[^>]*role="grid"/)
assert.match(waveThreeFiveUi, /data-task-id="capture"/)
assert.match(waveThreeFiveUi, /aria-label="Placement progress"/)

const localized = renderToStaticMarkup(React.createElement(
  HarborlineLocaleProvider,
  {
    locale: 'ar-SA',
    direction: 'rtl',
    catalog: { 'common.loading': 'جارٍ التحميل' },
  },
  React.createElement(Button, { loading: true }, 'حفظ'),
))
assert.match(localized, /lang="ar-SA"/)
assert.match(localized, /dir="rtl"/)
assert.match(localized, /role="status"/)
assert.match(localized, /aria-label="جارٍ التحميل"/)

const contextMenu = renderToStaticMarkup(React.createElement(
  ContextMenu,
  {
    accessibleLabel: 'Inspection actions',
    locale: 'ar-SA',
    direction: 'rtl',
    groups: [{ items: [{ id: 'approve', label: 'Approve', onSelect() {} }] }],
  },
  React.createElement('span', null, 'Inspection 42'),
))
assert.match(contextMenu, /Inspection 42/)
assert.deepEqual(
  clampContextMenuPosition({ x: 398, y: 298 }, { width: 120, height: 90 }, { width: 400, height: 300 }),
  { x: 272, y: 202 },
)

assert.equal(cn('p-2', ['text-sm', { active: true }], 'p-4', false), 'text-sm active p-4')
assert.equal(Object.keys(defaultStrings).length, 713)
assert.equal(defaultStrings['common.loading'], 'Loading')
assert.equal(
  interpolate('Step {current} of {total}', { current: 2 }),
  'Step 2 of {total}',
)

const errorCard = renderToStaticMarkup(React.createElement(ErrorCard, {
  title: 'Unable to load inspection',
  message: 'Try the request again.',
  onRetry() {},
  retryLabel: 'Try again',
  variant: 'compact',
  'data-fixture': 'packed-error',
}))
assert.match(errorCard, /role="alert"/)
assert.match(errorCard, /hl-error-card--compact/)
assert.match(errorCard, />Try again<\/button>/)
assert.match(errorCard, /data-fixture="packed-error"/)

const loadingState = renderToStaticMarkup(React.createElement(LoadingState, {
  label: 'Loading inspection',
  variant: 'inline',
  'data-fixture': 'packed-loading',
}))
assert.match(loadingState, /^<p/)
assert.match(loadingState, /role="status"/)
assert.match(loadingState, /aria-live="polite"/)
assert.match(loadingState, /hl-loading-state--inline/)

const emptyState = renderToStaticMarkup(React.createElement(EmptyState, {
  variant: 'actionable',
  title: 'No inspections yet',
  description: 'Create the first inspection.',
  action: { label: 'Create inspection', onClick() {} },
  'data-fixture': 'packed-empty',
}))
assert.match(emptyState, /data-hl-variant="actionable"/)
assert.match(emptyState, /aria-hidden="true"/)
assert.match(emptyState, /type="button"/)
assert.match(emptyState, /data-fixture="packed-empty"/)

const mediaQuery = renderToStaticMarkup(React.createElement(MediaQuery, {
  query: '(min-width: 48rem)',
  children: matches => React.createElement('span', { 'data-matches': matches }, String(matches)),
}))
assert.match(mediaQuery, /data-matches="false"/)
assert.equal(typeof useMediaQuery, 'function')
assert.equal(typeof useOutsideClick, 'function')
assert.equal(defaultRailLabels.viewingLens('Errors'), 'Viewing: Errors')
assert.equal(touchTarget, 'min-h-11 min-w-11')
assert.match(touchTargetPseudoOverlay, /before:h-11/)
assert.match(touchTargetPseudo, /^relative /)

const canonicalField = {
  name: 'amount',
  label: { defaultLocale: 'en', values: { en: 'Amount', ar: 'المبلغ' } },
  isSensitive: true,
  isReadable: false,
  value: 'must-not-render',
  rules: { visible: true, required: true, readOnly: false },
}
const normalizedField = FormViewField.normalize(canonicalField, {
  valueKind: 'decimal-string',
  required: false,
})
assert.equal(normalizedField.value, null)
assert.equal(normalizedField.readOnly, true)
assert.equal(normalizedField.required, true)
assert.equal(normalizedField.valueKind, 'decimal-string')
assert.equal(
  resolveText(canonicalField.label, ['ar-AE'], 'Field'),
  'المبلغ',
)

const normalizedView = FormView.normalize({
  formId: 'inspection',
  version: '1.0.0',
  sections: [{
    id: 'details',
    title: { defaultLocale: 'en', values: { en: 'Details' } },
    fields: [canonicalField],
  }],
}, { amount: { valueKind: 'decimal-string' } })
assert.equal(normalizedView.sections[0].fields[0].value, null)

process.stdout.write('packed npm aggregate rendered App waves through wave-03-05, including Chart, Chat, Data Grid, Gantt, and Numeric Text Box\n')
