# Harborline.Blocks.Scheduling

Harborline compatibility module for lease-serialized slot reservations and deterministic planning.

## The typed schedule definition (T-491/typed-definition, DES-0022 section 2)

`ScheduleDefinition` is content kind 11 and replaces the catalogue's opaque body: `SaveDraftAsync` takes the typed definition and every read returns `ScheduleDefinitionRevision`. A lone due date and a form are a complete schedule and name no solver (L1445), because the basis and all planning members are optional and `problem_class` defaults to `None` (L1149). The RRULE text is retained as authored and checked by the shared bounded-subset validator before publish; module meaning belongs to module admission.

- **Basis.** One of `fixed_calendar`, `completion_offset`, `threshold` or `event_anchored_chain`. A chain has one external anchor and each node follows it or an earlier node, so moving the anchor moves every node (L449).
- **Window.** Never a single point (L483). With no basis, its bounds are ISO 8601 instants with an offset. With a basis, they are ISO 8601 durations from the generated due point. Each chain node carries its own optional window beside its tolerance. Every window contains its due point; a schedule-level chain window refuses.
- **Envelope.** Required past authoring, with a required `Contract` (T-572). The store's document version is the one source of `Version`: a stored envelope omits it and the catalogue fills it in on read, so a restored draft publishes under its own version.
- **`SchedulableTrait`.** States the sealed trait and its slots `work_calendar`, `qualification_set` and `capacity_slots`, which admit a type to planning (L482, L1084).

## Authoring refusals (T-491/schedule-refusals, interface revision 9)

The shared catalogue validator refuses before storing a draft. Unsupported JSON configurations retain `scheduling.definition.body_invalid`; unadmitted modules retain `scheduling.definition.module_unsupported`. A `fixed_calendar` rule is checked at save by the recurrence producer's own `ValidateRule` parser (DES-0057 ck-1/cc-1).

Three nullable init-only intent properties preserve the existing 14-argument constructor and `Deconstruct`, participate in record equality and round-trip under the same JSON wire names. They make excluded planning requests explicit, with stable codes at their own pointers:

- `recovery_replanning: true` refuses `scheduling.definition.recovery_replanning_unsupported` (auth-21, L463). Absent/false leaves ordinary `record_and_replan` admitted.
- Any non-null `regulator_rule_set_requirement` refuses `scheduling.definition.regulator_rule_set_unsupported` (auth-23, L465). `legality_ref` remains an uninterpreted citation.
- Any non-null `input_plan_id` refuses `scheduling.definition.plan_input_unsupported` (auth-24, L478). The admitted `precedence` module still accepts operation dependencies within one plan (auth-25, L479).

`SchedulingCrossPackageAuthoring.Check(consumer, references, exposures)` is the authoring-only K9 facade (auth-28, T-724 ruling 57, [control #1358](https://github.com/Harborline-Software/harborline-control/pull/1358)). The host supplies resolved edges and producer exposures, as for Rules/Layout. The shared checker requires the consumer envelope's `Requires` capability to name the producer package and the producer to expose the target at the requested version and digest. Same-package edges pass. Scheduling codes are `scheduling.reference.dependency_undeclared`, `scheduling.reference.not_exposed` and `scheduling.reference.exposure_incompatible`; a target is disclosed only when exposed. The static admission callback is unchanged; this facade adds no production caller or publication/installation gate (T-615).

`conformance/hlp.blocks.scheduling/authoring-refusals.json` is the shared input/output corpus for both editor lanes, including literal codes, pointers and missing/required reasons. Tests execute each input through the store's production admission or the K9 facade. No second validator or UI implementation is introduced.

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

## Typed module admission and planning (T-491/module-admission)

`AddHarborlineSchedulingPlanner` registers `SchedulePlanner` and the two existing finite backends. The host supplies `ScheduleDefinitionCatalogue` and `Harborline.Foundation.Scheduling.Services.IAvailabilityRuntime`. Reservation and due-query registrations remain independent.

`PlanAsync` resolves an exact published `ScheduleDefinitionPin`. The declared `Project` or `JobShop` class compiles to `time_windows`, `resource_requirements`, `no_overlap` and `precedence`; the seven authored class names are Harborline rulings (DES-0022 open item 6), not a standard taxonomy. Other classes refuse. Every module advances `Elicited -> ShapeCandidate -> Admitted -> BackendSupported`, and every nonadjacent transition refuses `MODULE_ADMISSION_SKIP`. Unknown modules and parameter members refuse `MODULE_UNSUPPORTED` before candidate enumeration. Save and publish use the same admitted set and refuse `scheduling.definition.module_unsupported` at `/modules/{i}/module`, recording nothing.

The first three modules take absent or empty parameters. Precedence optionally adds typed constraints; every constraint requires `minimumGapSlots`, including an explicit zero:

```json
{"module":"precedence","parameters":{"constraints":[{"id":"after","beforeActivityId":"a","afterActivityId":"b","minimumGapSlots":1}]}}
```

The backend declares its supported module kinds, problem classes, guarantees and maximum deterministic work budget. The existing exhaustive solver offers `FeasibleProof`; first-fit offers `BoundedHeuristic`. Both accept nonnegative `int` work budgets, retaining their existing proposal status, incomplete-fact and budget-exhaustion semantics and stable solver IDs. Unsupported class, guarantee or budget requests name what is missing and required.

`Certified` exists as a typed rung but is unreachable: an attempt refuses `MODULE_CERTIFICATE_UNDEFINED` pending DES-0022 open ruling 3. This slice defines no evidence type or certification flag. Both solvers refuse any module that has not reached `BackendSupported` for that solver. Running requires backend support; proposals retain their independent status and guarantee and make no certification claim.

The planner takes an explicit UTC epoch and slot duration, plus one distinct exclusive `ResourceCapacity` without buffers per resource id. It ignores caller-supplied `AvailableSlots` without materializing intervening slots between activity windows and asks the shared runtime about each candidate's full interval and complete resource set. Runtime refusals propagate; an unavailable candidate is excluded. Pool and buffered bindings refuse rather than approximate the finite solver's exclusive-resource model. The calendar block retains all supply, occupancy, exception and buffer composition.

Planner search exhaustion returns `Indeterminate` with `UNPINNED_AVAILABILITY_READS`, because runtime answers carry no fact version. A found assignment remains a proposal; commit re-reads availability (C8). DES-0022 open ruling 4, owned by proposal-and-certificate, defines the future fact-pinning contract. Direct finite profiles retain their existing complete-pinned-fact proof behavior. Definition-backed proposal IDs include tenant, schedule identity and exact version; profiles without a definition pin retain the legacy ID.
