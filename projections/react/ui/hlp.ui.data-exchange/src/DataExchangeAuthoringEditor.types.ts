export interface DataExchangeOption { readonly id: string; readonly label: string }
/** A catalogue entry is either a labelled option or a bare id that is also its label (ADR 0096 option shapes). */
export type DataExchangeCatalogueOption = DataExchangeOption | string
export interface DiscoveredSourceColumn {
  readonly name: string
  readonly selected: boolean
}
export interface DataExchangeMappingRow {
  readonly sourceColumn: string
  readonly canonicalTarget: string
  readonly targetPointer: string
  readonly datatype: string
  readonly required: boolean
  readonly nullValue: string
  readonly defaultValue: string
  readonly separator: string
  readonly transform: string
}
export interface DataExchangeAuthoringDraft {
  /** Server identity of the persisted definition; absent for a new draft. */
  readonly identity?: string
  /** Server revision the draft was loaded from; a change is a revision change. */
  readonly expectedRevision?: string
  readonly name: string
  readonly sourceCapability: string
  readonly connectorVersion: string
  readonly formatCapability: string
  readonly secretReference: string
  readonly discoveredColumns: readonly DiscoveredSourceColumn[]
  readonly mappings: readonly DataExchangeMappingRow[]
  readonly externalKeyColumns: readonly string[]
  readonly replayPolicy: 'append' | 'overwrite' | 'append_dedup'
  readonly scheduleReference: string
  readonly referenceDataset: string
  readonly packDistribution: string
  readonly feedDistribution: string
}
export interface DataExchangeAuthoringCatalogue {
  readonly sourceCapabilities: readonly DataExchangeCatalogueOption[]
  readonly formats?: readonly DataExchangeCatalogueOption[]
  readonly canonicalTargets: readonly DataExchangeCatalogueOption[]
  readonly datatypes: readonly DataExchangeCatalogueOption[]
  readonly transforms: readonly DataExchangeCatalogueOption[]
  readonly schedules: readonly DataExchangeCatalogueOption[]
}
export interface DataExchangeRunCensus { readonly applied: number; readonly skipped: number; readonly conflicted: number; readonly rejected: number; readonly failed: number; readonly halted: number }
export interface DataExchangeRunSummary {
  readonly dryRunId: string
  readonly status: string
  readonly stale: boolean
  readonly candidateCheckpoint: string
  readonly census: DataExchangeRunCensus
  readonly refusals: readonly string[]
  readonly batchIdentity?: string
}
export interface DataExchangeAuthoringRefusal { readonly stage: string; readonly code: string; readonly targetHref: string }
export interface DataExchangeAuthoringEditorProps {
  readonly value: DataExchangeAuthoringDraft
  readonly catalogue: DataExchangeAuthoringCatalogue
  readonly run?: DataExchangeRunSummary
  readonly canCommit: boolean
  readonly canPublish?: boolean
  /** Read-only admission: every authoring control and intent is disabled. */
  readonly readOnly?: boolean
  readonly authoringRefusals?: readonly DataExchangeAuthoringRefusal[]
  readonly onChange: (value: DataExchangeAuthoringDraft) => void
  readonly onDiscoverSource: () => void
  readonly onDryRun: () => void
  readonly onCommit: () => void
  readonly onSaveDraft?: () => void
  readonly onPublish?: () => void
}
