export interface DataExchangeOption { readonly id: string; readonly label: string }
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
  readonly sourceCapabilities: readonly DataExchangeOption[]
  readonly formats?: readonly DataExchangeOption[]
  readonly canonicalTargets: readonly DataExchangeOption[]
  readonly datatypes: readonly DataExchangeOption[]
  readonly transforms: readonly DataExchangeOption[]
  readonly schedules: readonly DataExchangeOption[]
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
  readonly authoringRefusals?: readonly DataExchangeAuthoringRefusal[]
  readonly onChange: (value: DataExchangeAuthoringDraft) => void
  readonly onDiscoverSource: () => void
  readonly onDryRun: () => void
  readonly onCommit: () => void
  readonly onSaveDraft?: () => void
  readonly onPublish?: () => void
}
