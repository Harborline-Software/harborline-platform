# hlp.ui.booking-authoring

Local Booking Resource and Bookable draft editing (T-606/member-editors; DES-0025 `booking-eng-20`) for the React
(`BookingAuthoringEditor`, `BookingAuthoringScreen`) and Blazor (`HarborlineBookingAuthoringEditor`,
`HarborlineBookingAuthoringScreen`) projections. Both lanes ship through the existing `hlp.ui.button` aggregate. This is not
a new package. Neutral interface: `specs/modules/ui/hlp.ui.booking-authoring/interface.yaml`.

## What it does

- Holds one projection-neutral draft: `{ identity, versionId, expectedRevision?, body }`, where `body` is a Booking Resource or
  Bookable in the producer's snake_case grammar (`BookingDefinitionAdmission`). `envelope` is host-owned, and every member the
  editor does not author (unknown ones, the hold policy, `book_gate`, `eligibility_expression`, `require_all`) is preserved.
- An empty draft asks for the definition kind, Resource or Bookable, before showing anything else.
- **Resource** (`booking-auth-1`, `-2`, `-3`, `-5`): name; the record type it admits, chosen only from host-offered types; capacity,
  exclusive or pool, with a pool size for a pool (choosing exclusive removes the pool size); setup and cleanup minutes; maintenance,
  chosen from host-offered members of the resource record's own state, never authored as blocks; and the availability source as a
  plain field.
- **Bookable** (`booking-auth-6`, `-7`, `-10`): name; the record type it is offered against; its duration intervals in minutes; the
  required resources, all of which must be free together; and the waitlist flag, which says only that a queue exists.
- The editor holds no interval arithmetic and references no calendar code. Admission is the producer's: refusals are rendered by
  field, as given, and only for the exact identity, revision and local generation.

## Shared rules

- `readOnly` disables every control and emits nothing, including Keep and Discard.
- Host options take either shape: `{ id, label }` or a bare id string. A current value the host does not offer is shown, not
  rewritten, and an empty option list shows a fallback note instead of an empty control.
- A host draft with a different identity or revision replaces a clean draft; over local edits it waits behind Keep edits and
  Discard edits. A revision that is not a canonical decimal string is refused, never repaired.
- Numbers are read invariantly from the input text; empty or non-finite text leaves the draft unchanged (an emptied buffer or
  setup field removes the member). The grid reflows to one column at narrow widths.
