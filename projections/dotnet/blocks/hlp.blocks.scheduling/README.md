# Harborline.Blocks.Scheduling

Harborline compatibility module for lease-serialized slot reservations and deterministic planning.

## The typed schedule definition (T-491/typed-definition, DES-0022 section 2)

`ScheduleDefinition` is content kind 11 and replaces the catalogue's opaque body: `SaveDraftAsync` takes the typed definition and every read returns `ScheduleDefinitionRevision`. A lone due date and a form are a complete schedule and name no solver (L1445), because the basis and all planning members are optional and `problem_class` defaults to `None` (L1149). The RRULE text is retained as authored and checked by the shared bounded-subset validator before publish; module meaning belongs to module admission.

- **Basis.** One of `fixed_calendar`, `completion_offset`, `threshold` or `event_anchored_chain`. A chain has one external anchor and each node follows it or an earlier node, so moving the anchor moves every node (L449).
- **Window.** Never a single point (L483). With no basis, its bounds are ISO 8601 instants with an offset. With a basis, they are ISO 8601 durations from the generated due point. Each chain node carries its own optional window beside its tolerance. Every window contains its due point; a schedule-level chain window refuses.
- **Envelope.** Required past authoring, with a required `Contract` (T-572). The store's document version is the one source of `Version`: a stored envelope omits it and the catalogue fills it in on read, so a restored draft publishes under its own version.
- **`SchedulableTrait`.** States the sealed trait and its slots `work_calendar`, `qualification_set` and `capacity_slots`, which admit a type to planning (L482, L1084).

## The constraint-expression environment (T-491/expression-environment, DES-0022 scheduling-ck-23)

`SchedulingExpressionEnvironment.Declaration` states, in Rules' shared `BorrowerEnvironmentDeclaration`, what a constraint expression may read and when it runs. It equals Rules' released `scheduling-ck-23` entry in `conformance/hlp.foundation.rule-runtime/borrower-contracts.json`, canonical form included.

- **Reads.** `field` (a candidate assignment, or a resource, chain or occurrence fact) and `clock` (the planning clock, in UTC). Missing fields read null.
- **Functions.** Rules' kernel functions without table aggregation. Field reads are the only effect.
- **When.** The Run phase only; every other phase refuses.
- **Not in the inner loop.** The solver's inner loop stays local arithmetic (`scheduling-eng-11`). `SchedulingInnerLoopArchitectureTests` fails if any file under `Planning/` names Rules' evaluators, this environment or JsonLogic. Attaching an expression to a constraint and evaluating it is a later slice.

## Due occurrences (T-491/occurrence-generation)

`AddHarborlineSchedulingDueQueries` opts a host into due queries separately from reservation registration. The host registers its `KernelClock` and calls `AddFoundationScheduling` for the shared producer; Scheduling supplies no default clock.

`IDueQueueQueryService.Query` reads the kernel clock once and dispatches the lone due date or the declared basis through `IDueOccurrenceSource`. Occurrences carry an instant-valued due point, optional earliest/latest, actual completion time, distinct on-time/early/late/void classification, and a stable identity. A late tolerance is not the validity deadline. A no-window occurrence never becomes void.

- Fixed calendar binds a local start and IANA zone on each record's series and calls the shared RRULE producer; EXDATE wins over an override. Identity is series id plus the original local recurrence date-time, excluding the instant and node.
- Completion offset uses the last actual completion plus its ISO duration. Before any completion, the series must supply its first due point. Identity is series id plus the completion id, or the first occurrence marker.
- Threshold refuses missing readings, latches one occurrence until completed, and re-arms on completion. A still-past reading opens the next occurrence immediately. Identity includes series id, triggering reading id and re-arm completion id.
- Event-anchored chains keep the append-only ledger, exact predecessor revisions, compare-and-append version and explicit supersession evidence. The one external anchor re-anchors every branch. Node offsets and windows retain calendar units until generation.

`WallClockResolver` supplies both conversion directions using one NodaTime IANA database. Calendar months/years/days are applied in the series zone; the `PT` component is elapsed time.

### Breaking pre-release package change

The due seam, queue, completion models, generators and chain ledger types live in `Harborline.Blocks.Scheduling`. There is no forwarding shim.
