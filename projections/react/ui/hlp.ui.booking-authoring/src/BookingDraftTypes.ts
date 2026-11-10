/** A JSON object whose members the editor does not interpret; every member is preserved as given. */
export type BookingJsonObject = { readonly [member: string]: unknown }

/**
 * The one projection-neutral Booking draft (T-606/member-editors): host-supplied lifecycle metadata around the producer body, a
 * Resource or a Bookable in the Booking grammar (DES-0025; BookingDefinitionAdmission). `body.kind` is `resource` or `bookable`;
 * `body.envelope` is host-owned and carried unchanged. expectedRevision is the producer revision as a canonical ASCII decimal Int64
 * STRING, carried byte-exactly and never parsed (the Records editors' rule). Absent means none: undefined, or a JSON null.
 */
export interface BookingDraft { readonly identity: string; readonly versionId: string; readonly expectedRevision?: string | null; readonly body: BookingJsonObject }
/** A host option: a labelled id, or a bare id that is also its label (the shared option shapes the data-exchange editors use). */
export interface BookingOption { readonly id: string; readonly label: string }
export type BookingCatalogueOption = BookingOption | string
/**
 * What the host offers. The editor never chooses an option on its own, and a current value the host does not offer is shown, not
 * rewritten. resourceTypes: record types bearing the bookable-resource trait (booking-auth-1). bookableTypes: record types a Bookable
 * is offered against (booking-auth-6). resources: admitted Resources a Bookable may require (booking-auth-7). maintenanceSources:
 * members of the Resource record's own state that mark it out of service (booking-auth-5): maintenance is read, never authored as blocks.
 */
export interface BookingCatalogue { readonly resourceTypes?: readonly BookingCatalogueOption[]; readonly bookableTypes?: readonly BookingCatalogueOption[]; readonly resources?: readonly BookingCatalogueOption[]; readonly maintenanceSources?: readonly BookingCatalogueOption[] }
/** A producer admission refusal rendered as given: a stable code at an RFC 6901 pointer. */
export interface BookingDiagnostic { readonly code: string; readonly pointer: string }
/** Host-supplied refusals for one exact draft identity, revision and local generation. Any mismatch is stale and never shown. */
export interface BookingDiagnosticSet { readonly identity: string; readonly expectedRevision?: string | null; readonly generation: number; readonly diagnostics: readonly BookingDiagnostic[] }
/** A local change: the draft and the generation it belongs to. The generation is a local counter, never a revision. */
export interface BookingDraftChange { readonly draft: BookingDraft; readonly generation: number }

export const emptyBookingDraft = (): BookingDraft => ({ identity: '', versionId: '', body: {} })
export const bookingOption = (entry: BookingCatalogueOption): BookingOption => typeof entry === 'string' ? { id: entry, label: entry } : entry

const largestRevision = '9223372036854775807'
/** True only for a canonical decimal revision string: "0" or [1-9][0-9]* in ASCII, at most Int64.MaxValue. Never numeric. */
export const isCanonicalBookingRevision = (text: unknown): text is string => {
  if (typeof text !== 'string' || text.length === 0 || text.length > largestRevision.length) return false
  for (let index = 0; index < text.length; index++) { const code = text.charCodeAt(index); if (code < 0x30 || code > 0x39) return false }
  if (text.length > 1 && text.charCodeAt(0) === 0x30) return false
  return text.length < largestRevision.length || text <= largestRevision
}
/** A host draft or refusal set is usable only when its revision is absent or canonical. Otherwise it is rejected, never repaired. */
export const hasAcceptableBookingRevision = (value: { readonly expectedRevision?: unknown }): boolean => value.expectedRevision === undefined || value.expectedRevision === null || isCanonicalBookingRevision(value.expectedRevision)
export const sameBookingRevision = (left: string | null | undefined, right: string | null | undefined): boolean => (left ?? null) === (right ?? null)
