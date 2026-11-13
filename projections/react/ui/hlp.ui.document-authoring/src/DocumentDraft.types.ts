export interface DocumentOption { readonly id: string; readonly label: string }
export type DocumentCatalogueOption = string | DocumentOption
export interface DocumentCatalogue {
  readonly fields?: readonly DocumentCatalogueOption[]
  readonly regions?: readonly DocumentCatalogueOption[]
  readonly records?: readonly DocumentCatalogueOption[]
  readonly samples?: readonly DocumentCatalogueOption[]
}
export type DocumentRun = { readonly text: string; readonly fieldPath?: never; readonly fallback?: never } | { readonly text?: never; readonly fieldPath: string; readonly fallback?: string }
export interface DocumentLine { readonly runs: readonly DocumentRun[] }
export interface DocumentBlock {
  readonly id: string
  readonly lines: readonly DocumentLine[]
  readonly collapseBelow?: 'sm' | 'md' | 'lg'
  readonly breakBefore: boolean
  readonly breakAfter: boolean
  readonly breakInside: 'auto' | 'avoid_page'
}
export interface DocumentMasterVariant { readonly leftRegion?: string; readonly centerRegion?: string; readonly rightRegion?: string }
export interface DocumentDraft {
  readonly identity: string
  readonly revision: string | null
  readonly name: string
  readonly documentType: string
  readonly locale: { readonly kind: 'fixed' | 'from_record' | 'from_instance'; readonly tag?: string }
  readonly style: { readonly brandName?: string; readonly logoAssetRef?: string }
  readonly blocks: readonly DocumentBlock[]
  readonly masters: { readonly first: DocumentMasterVariant; readonly left: DocumentMasterVariant; readonly right: DocumentMasterVariant }
}
export interface DocumentPageFlow { readonly blocks: readonly { readonly id: string; readonly pageVariant: 'first' | 'left' | 'right'; readonly content: string }[] }
export interface DocumentPreviewRequest { readonly mode: 'preview'; readonly draft: DocumentDraft; readonly pageFlow: DocumentPageFlow; readonly target: { readonly kind: 'record' | 'sample'; readonly id: string } }
export interface DocumentPreviewResult { readonly minted: false; readonly localeTag: string; readonly blocks: readonly { readonly id: string; readonly content: string }[] }
export interface DocumentDenial { readonly identity: string; readonly revision: string | null; readonly generation: number; readonly diagnostics: readonly { readonly code: string; readonly pointer: string }[] }
export const documentOption = (option: DocumentCatalogueOption): DocumentOption => typeof option === 'string' ? { id: option, label: option } : option
export const emptyDocumentDraft = (): DocumentDraft => ({ identity: '', revision: null, name: '', documentType: '', locale: { kind: 'from_instance' }, style: {}, blocks: [], masters: { first: {}, left: {}, right: {} } })
