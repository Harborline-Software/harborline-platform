# Harborline.Blocks.Scheduling

Harborline compatibility module for lease-serialized slot reservations and deterministic planning.

## The typed schedule definition (T-491/typed-definition, DES-0022 section 2)

`ScheduleDefinition` is content kind 11 and replaces the catalogue's opaque body: `SaveDraftAsync` takes the typed definition and every read returns `ScheduleDefinitionRevision`. A window and a form are a complete schedule and name no solver (L1445), because the basis and all planning members are optional and `problem_class` defaults to `None` (L1149). This slice validates shape only. The RRULE text stays as authored (DES-0022 open ruling 1), and module meaning belongs to module admission.

- **Basis.** One of `fixed_calendar`, `completion_offset`, `threshold` or `event_anchored_chain`. A chain has one external anchor and each node follows it or an earlier node, so moving the anchor moves every node (L449).
- **Window.** Never a single point (L483). With no basis, its bounds are ISO 8601 instants with an offset. With a basis, they are ISO 8601 durations from the generated due point. A chain's nodes carry their own tolerance instead.
- **Envelope.** Required past authoring, with a required `Contract` (T-572). The store's document version is the one source of `Version`: a stored envelope omits it and the catalogue fills it in on read, so a restored draft publishes under its own version.
- **`SchedulableTrait`.** States the sealed trait and its slots `work_calendar`, `qualification_set` and `capacity_slots`, which admit a type to planning (L482, L1084).
