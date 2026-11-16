/** A JSON object whose members the editor does not interpret; every member is preserved as given. */
export type SchedulingJsonObject = { readonly [member: string]: unknown }

/**
 * The one projection-neutral Schedule draft (T-491/schedule-editor): host-supplied lifecycle metadata around the producer body, a
 * ScheduleDefinition in its snake_case grammar (DES-0022; ScheduleDefinitionAdmission). `body.envelope` is host-owned and carried
 * unchanged. expectedRevision is the producer revision as a canonical ASCII decimal Int64 STRING, carried byte-exactly and never
 * parsed (the Records editors' rule). Absent means none: undefined, or a JSON null.
 */
export interface SchedulingDraft { readonly identity: string; readonly versionId: string; readonly expectedRevision?: string | null; readonly body: SchedulingJsonObject }
/** A host option: a labelled id, or a bare id that is also its label (the shared option shapes the data-exchange editors use). */
export interface SchedulingOption { readonly id: string; readonly label: string }
export type SchedulingCatalogueOption = SchedulingOption | string
/** A registered solver backend and the guarantees it declares (the solver catalogue); only these guarantees are offered for it. */
export interface SchedulingBackendOption { readonly id: string; readonly label: string; readonly guarantees: readonly string[] }
/**
 * What the host offers. The editor never chooses an option on its own, and a current value the host does not offer is shown, not
 * rewritten. types: record types a Schedule runs on (scheduling-auth-1). forms, workflows, workItems: what a Schedule starts
 * (scheduling-auth-12). legalityRuleSets: legality rule sets cited by id (scheduling-auth-13). backends: registered solver backends
 * (scheduling-bound-1, -4).
 */
export interface SchedulingCatalogue { readonly types?: readonly SchedulingCatalogueOption[]; readonly forms?: readonly SchedulingCatalogueOption[]; readonly workflows?: readonly SchedulingCatalogueOption[]; readonly workItems?: readonly SchedulingCatalogueOption[]; readonly legalityRuleSets?: readonly SchedulingCatalogueOption[]; readonly backends?: readonly SchedulingBackendOption[] }
/** A producer admission refusal rendered as given: a stable code at an RFC 6901 pointer. */
export interface SchedulingDiagnostic { readonly code: string; readonly pointer: string }
/** Host-supplied refusals for one exact draft identity, revision and local generation. Any mismatch is stale and never shown. */
export interface SchedulingDiagnosticSet { readonly identity: string; readonly expectedRevision?: string | null; readonly generation: number; readonly diagnostics: readonly SchedulingDiagnostic[] }
/** A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision. */
export interface SchedulingDraftChange { readonly draft: SchedulingDraft; readonly generation: number }

export const emptySchedulingDraft = (): SchedulingDraft => ({ identity: '', versionId: '', body: {} })
export const schedulingOption = (entry: SchedulingCatalogueOption): SchedulingOption => typeof entry === 'string' ? { id: entry, label: entry } : entry

const largestRevision = '9223372036854775807'
/** True only for a canonical decimal revision string: "0" or [1-9][0-9]* in ASCII, at most Int64.MaxValue. Never numeric. */
export const isCanonicalSchedulingRevision = (text: unknown): text is string => {
  if (typeof text !== 'string' || text.length === 0 || text.length > largestRevision.length) return false
  for (let index = 0; index < text.length; index++) { const code = text.charCodeAt(index); if (code < 0x30 || code > 0x39) return false }
  if (text.length > 1 && text.charCodeAt(0) === 0x30) return false
  return text.length < largestRevision.length || text <= largestRevision
}
/** A host draft or refusal set is usable only when its revision is absent or canonical. Otherwise it is rejected, never repaired. */
export const hasAcceptableSchedulingRevision = (value: { readonly expectedRevision?: unknown }): boolean => value.expectedRevision === undefined || value.expectedRevision === null || isCanonicalSchedulingRevision(value.expectedRevision)
export const sameSchedulingRevision = (left: string | null | undefined, right: string | null | undefined): boolean => (left ?? null) === (right ?? null)
