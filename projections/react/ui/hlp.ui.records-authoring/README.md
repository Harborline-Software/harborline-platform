# hlp.ui.records-authoring

Local Record Type draft editing for the React (`RecordTypeDraftEditor`) and Blazor (`HarborlineRecordTypeDraftEditor`)
projections. Both lanes ship through the existing `hlp.ui.button` aggregate. This is not a new package and makes no
catalogue conformance claim. Neutral interface: `specs/modules/ui/hlp.ui.records-authoring/interface.yaml`.

This is a bounded subset of T618. It covers local editing only, not all of T618.

## What it does

- Holds one projection-neutral draft: `{ identity, versionId, expectedRevision?, definitionKind?, body }`. `body` is the
  current producer Record Type body in snake_case, carried as extensible JSON (a typed record in React, a `JsonObject`
  in Blazor). Members the editor does not edit are preserved unchanged, including unknown ones.
- An empty draft asks for the definition kind before it shows Name. `Records` (Record type) is the only kind.
- Edits name, `envelope.section`, field key and display name, binding kind and version, existing string and number kind parameter values only (other JSON types stay unchanged; invalid numeric input is a no-op),
  existing reference target type, cardinality and on-delete, and the retention clock field. You can add a field
  (`field_key` and `display_name` only) and remove a field.
- Edits the Record Type header (interface revision 3): the Class, chosen only from host-supplied `classOptions`; the record
  class (`reference`, `master` or `transactional`); the creation gate (`form_only`, `role` or `engine`, absent meaning
  `form_only`) and, for the role gate only, the creation role, chosen only from host-supplied `roleOptions`; whether
  amendments require a reason, with an optional `supersession` mode; and whether history is kept. Choosing a gate other
  than `role` removes the creation role in the same edit, because the producer refuses one on any other gate. A current
  Class or role the host does not offer is shown as "(not offered)" and left unchanged.
  A policy the host draft already declares cannot be removed (its checkbox is disabled): the producer has no "off"
  value, and save carries an omitted declaration forward (ADR-0097). On a draft not yet created, creation sets a
  policy that is absent or null, so its box shows checked and disabled with a note. An empty or non-string header value shows as not set.
- Creates and edits reference fields (interface revision 4): "Make field N a reference" on a field with neither a
  binding nor a reference, the target kind (Record type, with the free-text target type, or Class, chosen only from
  host-supplied `classOptions`), cardinality, delete behaviour and the parent flag, and "Remove reference from field N".
  Choosing a target kind removes the other target, because a reference names exactly one. `pin` and
  `required_trait_id` are carried unchanged.
- Shows each host-supplied diagnostic's target, and the host's warnings in their own list after the note "Warnings do
  not block publication." (interface revision 2).
- Kind and version can only be chosen from host-supplied `kindOptions`, and the editor never picks one for you. A
  current kind the host does not offer is shown as "(not offered)" and left unchanged.
- Reports each local change as `{ draft, generation }`. The draft's identity, version and revision are read-only.
- Diagnostics come only from the host, and only when their identity, revision and generation match the current draft.
  They render verbatim and in order. Diagnostics from older generations stay hidden.
- Incoming host values follow these rules:
  - A clean draft is replaced.
  - A dirty draft holds the incoming value behind inline Keep edits and Discard edits buttons.
  - An echo with the same identity, version and revision does not reset anything.
- `readOnly` disables every control and turns every edit handler into a no-op. It is a local display flag. It does not
  authorize anything.

## What it does not do

- It has no Save, Create, Publish, Preview or evaluation action, request type or callback, so a local change is never
  persisted. The UI says so.
- It does not compute, reorder, merge or invent diagnostics or producer refusals.
- It does not edit `binding.constraints`. The existing Boolean `required` floor is carried through, with no checkbox.
  It does not author or serialize required conditions, and it writes no `null` or `false` sentinel.
- It does not author governance, defaults provenance, traits or new bindings.
  Those members are preserved, but this editor does not claim to cover them.
- It applies no compatibility clearing or defaults materialization when a kind or version changes.
- It reads no permission names.

## Fixture coverage (source fixtures in `conformance/hlp.ui.records-authoring/source-fixtures`)

| Case | Local mechanism exercised | Stays producer-only |
| --- | --- | --- |
| G1 | Local edits keep `expectedStoredBody` intact apart from the edited members | Canonical serialization, admission, minting |
| R1–R6 | The supplied ordered refusal list renders verbatim | Refusal computation; R2 collision and history; R4 raw catalogue save |
| R7 | A section edit leaves identity unassigned, and the host-supplied identity is shown | Minting `finance.asset-class` |
| P1 | Each P1 input list renders in order | — |
| P2 | Clean replace, parent echo, dirty Keep/Discard, stale generation ignored | `definition.revision_conflict` itself |
| P3 | A local date 1.0.0 → 2.0.0 change, with no clearing; the supplied refusal renders | Capability validation |
| P4 | Read-only emits nothing; the supplied list is kept exactly | Producer name-before-kind ordering |

Tests: `src/RecordTypeDraftEditor.test.tsx` (React, `npm run test:records-authoring` from `hlp.ui.button`) and
`hlp.ui.records-authoring.tests/RecordsDraftTests.cs` (Blazor aggregate tests). Neither has been run yet. Kind parameter
editing has no fixture case, because no source case binds a non-empty parameter.
