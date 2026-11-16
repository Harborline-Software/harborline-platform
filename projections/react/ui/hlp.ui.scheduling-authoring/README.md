# hlp.ui.scheduling-authoring

Local Schedule draft editing (T-491/schedule-editor; DES-0022 `scheduling-eng-19`) for the React
(`SchedulingAuthoringEditor`, `SchedulingAuthoringScreen`) and Blazor (`HarborlineSchedulingAuthoringEditor`,
`HarborlineSchedulingAuthoringScreen`) projections. Both lanes ship through the existing `hlp.ui.button` aggregate. This is not
a new package. Neutral interface: `specs/modules/ui/hlp.ui.scheduling-authoring/interface.yaml`.

## What it does

- Holds one projection-neutral draft: `{ identity, versionId, expectedRevision?, body }`, where `body` is a ScheduleDefinition in
  the producer's snake_case grammar (`ScheduleDefinitionAdmission`). `envelope` is host-owned, and every member the editor does not
  author (grace, escalation, input plan, recovery replanning, unknown ones) is preserved.
- Authors a Schedule from nothing: name, the record type it runs on, what it starts (a form, workflow or work item, chosen from
  host-offered targets), its basis (a single due date, fixed calendar, completion offset, threshold, or event-anchored chain),
  window, tolerance, missed behaviour, problem class and legality rule set.
- An event-anchored chain lists its nodes. Each node states its scope in words ("follows the chain anchor", "follows node X"), not
  by colour alone (WCAG 2.2 SC 1.4.1).
- A problem class other than None shows the bound solver: a host-registered backend, one of that backend's declared guarantees, a
  whole-number work budget and the backend parameters as a JSON object. It also shows one admitted constraint module
  (`time_windows`, `resource_requirements`, `no_overlap`, `precedence`); precedence constraints are authored row by row, and any
  further modules are carried unchanged.
- Supply is shown read-only: it is read from the shared availability runtime, from each Resource's own availability source, set
  on the Resource in the Booking editor (scheduling-bound-6). Nothing here writes it.
- Admission is the producer's. The Refusal list shows every refusal as `code at pointer (Field)`, before publish and only for the
  exact identity, revision and local generation. A polite live region announces refusals that were not in the previous set.

## Shared rules

- `readOnly` disables every control and emits nothing, including Keep and Discard.
- Host options take either shape: `{ id, label }` or a bare id string. A current value the host does not offer is shown, not
  rewritten.
- A host draft with a different identity or revision replaces a clean draft; over local edits it waits behind Keep edits and
  Discard edits. A revision that is not a canonical decimal string is refused, never repaired.
- Numbers are read with the JSON number grammar; text that does not parse leaves the draft unchanged. The grid reflows to one
  column at narrow widths.
