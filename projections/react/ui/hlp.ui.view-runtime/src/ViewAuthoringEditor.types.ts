export interface ViewAuthoringOption {
  readonly id: string
  readonly label: string
}

export interface ViewAuthoringColumn {
  readonly field: string
  readonly width: number
  readonly presentation: string
}

export interface ViewAuthoringSort {
  readonly field: string
  readonly direction: 'ascending' | 'descending'
}

export interface ViewAuthoringBinding {
  readonly name: string
  readonly parameters: string
}

export interface ViewAuthoringDraft {
  readonly name: string
  readonly recordType: string
  readonly viewKind: string
  readonly columns: readonly ViewAuthoringColumn[]
  readonly sorts: readonly ViewAuthoringSort[]
  readonly groupBy: string
  readonly filterPredicate: string
  readonly shapeRoles: Readonly<Record<'title' | 'placedBy' | 'groupedBy', string>>
  readonly measure: ViewAuthoringBinding
  readonly widget: ViewAuthoringBinding
  readonly rowBehavior: Readonly<{ openAction: string; inlineEdit: boolean }>
  readonly density: 'compact' | 'standard' | 'spacious'
  readonly ownership: 'system' | 'public' | 'personal'
}

export interface ViewAuthoringCatalogue {
  readonly recordTypes: readonly ViewAuthoringOption[]
  readonly viewKinds: readonly ViewAuthoringOption[]
  readonly fields: readonly ViewAuthoringOption[]
  readonly measures: readonly ViewAuthoringOption[]
  readonly widgets: readonly ViewAuthoringOption[]
  readonly rowActions: readonly ViewAuthoringOption[]
}

export interface ViewAuthoringEditorProps {
  readonly value: ViewAuthoringDraft
  readonly catalogue: ViewAuthoringCatalogue
  readonly onChange: (value: ViewAuthoringDraft) => void
}
