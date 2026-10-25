/** A JSON object whose members the editor does not interpret; every member is preserved as given. */
export type RecordsJsonObject = { readonly [member: string]: unknown }

/** The current producer Record Type body (RecordTypeDocument, snake_case). Typed members are the ones the editor reads; every other member, known or unknown, is preserved as given. */
export interface RecordTypeBody { readonly name?: string; readonly envelope?: RecordsJsonObject & { readonly section?: string }; readonly fields?: readonly RecordTypeField[]; readonly retention_clock_field_id?: string; readonly class_id?: string; readonly record_class?: string; readonly creation_gate?: string; readonly creation_role_id?: string; readonly amendment_policy?: RecordsJsonObject & { readonly reason_required?: unknown; readonly mode?: string }; readonly history_policy?: string; readonly [member: string]: unknown }
export interface RecordTypeField { readonly field_key?: string; readonly display_name?: string; readonly binding?: RecordTypeFieldBinding; readonly reference?: RecordTypeFieldReference; readonly [member: string]: unknown }
export interface RecordTypeFieldBinding { readonly kind?: RecordsJsonObject & { readonly kind_id?: string; readonly version?: string; readonly parameters?: RecordsJsonObject }; readonly [member: string]: unknown }
export interface RecordTypeFieldReference { readonly target_type_id?: string; readonly target_class_id?: string; readonly cardinality?: string; readonly on_delete?: string; readonly parent?: unknown; readonly [member: string]: unknown }

/**
 * The one projection-neutral Records draft: host-supplied lifecycle metadata around the producer body. expectedRevision is the
 * producer revision as the host transports it, a canonical ASCII decimal Int64 STRING (never a number, so values above 2^53
 * survive). The editor carries it byte-exactly and never parses, rounds, increments or defaults it. Absent means none: undefined, or a
 * JSON null as the host wire's string? ExpectedRevision serializes it (RecordsDraftHostContract.cs:156); a null is carried unchanged.
 */
export interface RecordsDraft { readonly identity: string; readonly versionId: string; readonly expectedRevision?: string | null; readonly definitionKind?: 'Records'; readonly body: RecordTypeBody }
/** A field kind and version the host offers. The editor never selects one on its own. */
export interface RecordsKindOption { readonly kindId: string; readonly version: string }
/** A published Class the host offers as the type's catalogue home. The editor never selects one on its own. */
export interface RecordsClassOption { readonly classId: string }
/** A domain role the host offers as a role gate's creation role. The editor never selects one on its own. */
export interface RecordsRoleOption { readonly roleId: string }
/** A producer diagnostic rendered as given: a stable code at an RFC 6901 pointer, with the producer's target when it names one. */
export interface RecordsDiagnostic { readonly code: string; readonly pointer: string; readonly target?: string | null }
/** A producer warning rendered as given: a stable code at an RFC 6901 pointer. It never blocks and is never a refusal. */
export interface RecordsWarning { readonly code: string; readonly pointer: string }
/** Host-supplied diagnostics, and the publication-warning preview, for one exact draft identity, revision and local generation. */
export interface RecordsDiagnosticSet { readonly identity: string; readonly expectedRevision?: string | null; readonly generation: number; readonly diagnostics: readonly RecordsDiagnostic[]; readonly warnings?: readonly RecordsWarning[] }
/** A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision. Local state only; nothing is persisted. */
export interface RecordsDraftChange { readonly draft: RecordsDraft; readonly generation: number }

export const emptyRecordsDraft = (): RecordsDraft => ({ identity: '', versionId: '', body: {} })

// The disabled host contract's grammar (RecordsDraftHostContract.cs:56-57,72-99): "0" or [1-9][0-9]* in ASCII, at most
// 9223372036854775807. Checked on the text alone, so no Number, BigInt, culture or trimming can widen or round it.
const largestRevision = '9223372036854775807'
/** True only for a canonical decimal revision string. Anything else, a JSON number included, is not one. */
export const isCanonicalRecordsRevision = (text: unknown): text is string => {
  if (typeof text !== 'string' || text.length === 0 || text.length > largestRevision.length) return false
  for (let index = 0; index < text.length; index++) { const code = text.charCodeAt(index); if (code < 0x30 || code > 0x39) return false }
  if (text.length > 1 && text.charCodeAt(0) === 0x30) return false
  // Equal-length strings of ASCII digits without a leading zero order exactly as their values do.
  return text.length < largestRevision.length || text <= largestRevision
}
/** A host draft or diagnostic set is usable only when its revision is absent (undefined or null) or canonical. Otherwise it is rejected, never repaired. */
export const hasAcceptableRecordsRevision = (value: { readonly expectedRevision?: unknown }): boolean => value.expectedRevision === undefined || value.expectedRevision === null || isCanonicalRecordsRevision(value.expectedRevision)
/** Two revisions match only as identical strings, or when both are absent (undefined and null are both absent). Never numeric. */
export const sameRecordsRevision = (left: string | null | undefined, right: string | null | undefined): boolean => (left ?? null) === (right ?? null)
