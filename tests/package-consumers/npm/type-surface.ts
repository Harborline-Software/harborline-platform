import {
  Breadcrumb,
  Chart,
  Chat,
  Alert,
  Badge,
  CheckBox,
  Collapsible,
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
  Input,
  LayersRail,
  LoadingState,
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
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextArea,
  Tooltip,
  Window,
  WindowActionsBar,
  cn,
  defaultStrings,
  interpolate,
  resolveText,
  type AspectLens,
  type BreadcrumbProps,
  type ChartProps,
  type ChatProps,
  type AlertProps,
  type BadgeProps,
  type CanvasModel,
  type CheckBoxProps,
  type CollapsibleProps,
  type DateFieldProps,
  type DateTimeFieldProps,
  type DataExportButtonProps,
  type DataGridProps,
  type ErrorCardProps,
  type FormFieldProps,
  type FormValues,
  type GuardedControlProps,
  type GanttProps,
  type InputProps,
  type InternationalizedText,
  type LensTone,
  type LayersRailProps,
  type LoadingStateProps,
  type NumberFieldProps,
  type NumericTextBoxProps,
  type NotificationCenterProps,
  type PageProps,
  type PopoverProps,
  type RadioGroupProps,
  type SearchInputProps,
  type SegmentedControlProps,
  type SelectFieldProps,
  type SwitchProps,
  type ToastService,
  type ToasterProps,
  type UserMenuProps,
  type AppLayoutProps,
  type SheetProps,
  type SpotlightProps,
  type SideNavGroup,
  type SpinnerProps,
  type TableProps,
  type TextAreaProps,
  type TooltipProps,
  type WindowProps,
  type ProvenanceInfo,
  type HarborlineStringCatalog,
  type HarborlineStringKey,
  type ValidationResult,
} from '@harborline-software/ui-react'

const text: InternationalizedText = {
  defaultLocale: 'en',
  values: { en: 'Amount' },
}

const lensTone: LensTone = 'accent'
const lens: AspectLens = {
  id: 'required',
  label: 'Required fields',
  tone: lensTone,
  kind: 'filter',
  project: nodeId => ({ active: nodeId === 'amount' }),
}
const canvas: CanvasModel = { nodes: [], selectedId: null, select() {} }
const provenance: ProvenanceInfo = {
  source: 'unknown',
  chain: ['unknown'],
  overridden: false,
  locked: true,
  resolved: false,
}

const key: HarborlineStringKey = 'common.loading'
const catalog: HarborlineStringCatalog = defaultStrings
const values: FormValues = { amount: 12 }
const validation: ValidationResult = { isValid: true, errors: [] }
const errorProps: ErrorCardProps = { title: 'Unable to load', variant: 'compact' }
const loadingProps: LoadingStateProps = { label: 'Loading', variant: 'inline' }
const breadcrumbProps: BreadcrumbProps = { items: [{ label: 'Current' }] }
const alertProps: AlertProps = { children: 'Saved', variant: 'success' }
const badgeProps: BadgeProps = { children: 'Ready', fillMode: 'outline' }
const checkBoxProps: CheckBoxProps = { checked: 'mixed', label: 'Include' }
const collapsibleProps: CollapsibleProps = { title: 'Details' }
const dateProps: DateFieldProps = { name: 'issued', value: '2026-08-09', onChange() {} }
const dateTimeProps: DateTimeFieldProps = { name: 'starts', value: '2026-08-09T14:30', onChange() {} }
const dataExportProps: DataExportButtonProps = { formats: ['csv', 'pdf'], onExport() {} }
const formFieldProps: FormFieldProps = { name: 'issued', label: 'Issue date', children: null }
const numberProps: NumberFieldProps = { name: 'quantity', value: '', onChange() {} }
const popoverProps: PopoverProps = { defaultOpen: false }
const radioProps: RadioGroupProps = { name: 'billing', value: '', onChange() {}, options: [] }
const sheetProps: SheetProps = { defaultOpen: false, modal: true }
const spotlightProps: SpotlightProps = { open: false, onOpenChange() {}, query: '', onQueryChange() {}, sections: [], ariaLabel: 'Search' }
const sideNavGroup: SideNavGroup = { id: 'main', items: [{ id: 'home', label: 'Home' }] }
const spinnerProps: SpinnerProps = { label: 'Loading', type: 'converging' }
const tableProps: TableProps = { density: 'md' }
const textAreaProps: TextAreaProps = { defaultValue: 'Notes', resize: 'vertical' }
const tooltipProps: TooltipProps = { content: 'Details', children: null }
const windowProps: WindowProps = { title: 'Inspection', labels: { close: 'Close' } }
const guardedControlProps: GuardedControlProps = { action: 'delete', classificationId: 'destructive', label: 'Delete', onCommit() {} }
const inputProps: InputProps = { value: 'North Pier', onChange() {}, fillMode: 'outline' }
const layersRailProps = null as unknown as LayersRailProps
const notificationCenterProps: NotificationCenterProps = { items: [] }
const pageProps: PageProps = { title: 'Inspection' }
const searchInputProps: SearchInputProps = { value: '', onChange() {} }
const segmentedControlProps: SegmentedControlProps = { accessibleName: 'View', options: [{ value: 'day', label: 'Day' }], value: 'day', onValueChange() {} }
const selectFieldProps: SelectFieldProps = { name: 'status', value: 'active', options: [{ value: 'active', label: 'Active' }], onValueChange() {}, accessibleName: 'Status' }
const switchProps: SwitchProps = { checked: true, onCheckedChange() {}, accessibleName: 'Alerts' }
const toastService: ToastService = createToastService()
const toasterProps: ToasterProps = { service: toastService, maximumVisible: 3 }
const userMenuProps: UserMenuProps = { identity: { name: 'Ada Lovelace' }, items: [] }
const appLayoutProps: AppLayoutProps = { body: null, sideNavMode: 'auto', contentScroll: 'main' }
const chartProps: ChartProps = { definition: { categories: ['Bay 1'], series: [{ name: 'Captured', values: [12] }] } }
const chatProps: ChatProps = { messages: [], user: { name: 'Inspector' }, inputValue: '', onInputValueChange() {}, onSubmit() {} }
type GridRow = { id: string; structure: string; photos: number }
const dataGridProps: DataGridProps<GridRow> = { accessibleName: 'Structures', rows: [], columns: [{ id: 'structure', field: 'structure', header: 'Structure', removalPriority: 1 }], getRowId: row => row.id }
const ganttProps: GanttProps = { tasks: [{ id: 'capture', title: 'Capture', start: '2026-08-11', end: '2026-08-12' }], zoom: 'week' }
const numericTextBoxProps: NumericTextBoxProps = { value: 42.5, onChange() {}, min: 0, max: 100, decimals: 2, 'aria-label': 'Progress' }

const field = FormViewField.normalize({
  name: 'amount',
  label: text,
  isSensitive: false,
  isReadable: true,
})
const view = FormView.normalize({
  formId: 'inspection',
  version: '1.0.0',
  sections: [{ id: 'details', title: text, fields: [field] }],
})

void [
  ErrorCard,
  Alert,
  alertProps,
  Badge,
  badgeProps,
  Breadcrumb,
  Chart,
  chartProps,
  Chat,
  chatProps,
  CheckBox,
  Collapsible,
  breadcrumbProps,
  checkBoxProps,
  collapsibleProps,
  DateField,
  DateTimeField,
  DataExportButton,
  DataGrid,
  dataGridProps,
  dataExportProps,
  dateProps,
  dateTimeProps,
  FormField,
  formFieldProps,
  GuardedControl,
  Gantt,
  ganttProps,
  guardedControlProps,
  Input,
  inputProps,
  LayersRail,
  layersRailProps,
  LoadingState,
  NumberField,
  NumericTextBox,
  numericTextBoxProps,
  numberProps,
  NotificationCenter,
  notificationCenterProps,
  Page,
  pageProps,
  Popover,
  PopoverContent,
  PopoverTrigger,
  popoverProps,
  RadioGroup,
  radioProps,
  SearchInput,
  searchInputProps,
  SegmentedControl,
  segmentedControlProps,
  SelectField,
  selectFieldProps,
  Switch,
  switchProps,
  Toaster,
  toastService,
  toasterProps,
  UserMenu,
  userMenuProps,
  AppLayout,
  appLayoutProps,
  Sheet,
  SheetContent,
  SheetTrigger,
  sheetProps,
  Spotlight,
  Spinner,
  spinnerProps,
  sideNavGroup,
  spotlightProps,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  tableProps,
  TextArea,
  textAreaProps,
  Tooltip,
  tooltipProps,
  Window,
  WindowActionsBar,
  windowProps,
  canvas,
  catalog,
  cn('p-2', 'p-4'),
  errorProps,
  field,
  interpolate('{value}', { value: 12 }),
  key,
  lens,
  loadingProps,
  provenance,
  resolveText(text, ['en-US']),
  validation,
  values,
  view,
]
