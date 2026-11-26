# Creating a reference target

The host supplies `recordCreateCapabilities` / `RecordCreateCapabilities`, keyed by field name,
and handles `onRecordCreate` / `OnRecordCreate`. Each capability carries `targetKind`,
`displayName`, `indefiniteName` and `canCreate` (PascalCase in Blazor). For example, kind
`asset`, display name `asset`, indefinite name `an asset`. These names come from the host's
localized kind metadata. Missing capability grants nothing. The form does not resolve permission.

A nonempty typed query offers the authorized create action after the visible matches. Choosing
it by pointer or keyboard emits `{ fieldName, targetKind, text }`, closes the picker and leaves
form values unchanged. The host owns opening the target's form and checking permission at save.
Changing the capability to denied removes the action before selection. Disabled, read-only and
submitting forms cannot request creation.

`recordCreate` and `recordCreatePrerequisite` in the form strings replace the default sentences.
Their placeholders are `{kind}`, `{indefiniteKind}` and `{text}`. Replacement is single-pass,
so literal placeholder text in a query stays literal. Rendering escapes names and query text.
Only RecordPicker uses this capability; taxonomy and other enumerated fields keep their controls.

The underlying select adds a trailing action within the existing combobox/listbox keyboard
navigation. The action is an option with no selected record value; it contains no nested interactive
control. The prerequisite appears as a live status outside the listbox.

From the repository root, compare both projections' actual rendered action lists:

```text
node projections/react/ui/hlp.ui.schema-form/src/__tests__/run-picker-parity.mjs <existing-receipt-directory>
```

The runner executes both tests against `picker-create-cases.json`, checks each against the
hand-written expected list, and compares the emitted React and Blazor lists. No production
component reads the fixture. PE-1/2/3 use ADR-0176 decisions 5 and 11 as their string and ordering
oracles. This slice does not open or save the target or return a newly selected value.
