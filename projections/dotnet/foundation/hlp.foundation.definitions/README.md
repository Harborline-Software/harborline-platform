# Harborline Foundation Definitions

The shared definition-admission refusal envelope: `DefinitionAdmissionPhase`, `DefinitionRefusal`,
`DefinitionRefusalReport`, and `DefinitionRefusalException`. These types are forwarded from
`hlp.blocks.builder-definitions` for binary compatibility; see T-724 ruling 116.

## Content-kind and pillar registry (T-738)

`PackContentKindRegistry` is the platform's one table of pack content kinds: each kind's wire value, the
pillar it groups under, and whether it is active on the wire (`Shipped`) or the platform only holds the
value (`Reserved`). Shipped values are the api's `PackContentKind` and `PackPillar` and never renumber
(owner ruling, 2026-09-29). `ReleasedNavigationDefinition` 21 is `Shipped` because platform seed 2.0.0
emits it; the api adds it at that value when it takes 2.0.0 in (T-584). Reserved today: `AssistanceDefinition` 20.

`Export()` writes `_shared/content-kinds/content-kinds.json`, which is embedded in the
assembly and packed at the same path for the api to consume. Regenerate it and the nine
`*PackIdentity.g.cs` files with
`dotnet run --project tooling/content-kind-export -- --root .`;
`VerifyCheckedInExport` fails the tests when it is stale. `PackContentKindDriftArchitectureTests` fails
when any platform `*PackIdentity` constant, or any `PackContentKind`/`PackPillar` enum, disagrees with
the registry.

Check all generated identities and the export without writing: `dotnet run --project tooling/content-kind-export -- --root . --check`. Normal builds use the checked-in files and do not run generation. The command returns 0 on success, 1 for drift, and 2 for malformed arguments.

## Solver refusal detail (T-491)

DefinitionRefusal keeps its three-argument constructor and Deconstruct. Its optional init-only Detail is a JSON object, serialized as detail and omitted when null (DES-0014 C3). Equality compares detail values structurally, ignoring object member order and preserving array order; Target still names only a safely exposed fetchable definition.

## Refusal telemetry (T-1257)

`DefinitionRefusalTelemetry` exposes `MeterName` (`Harborline.Definitions`) and
`InstrumentName` (`definition.refusals`) for host subscription. `Record` counts each refusal with
exactly `tenant_id`, `stage` (the admission phase name), `definition_id` and `code`. It counts only
when the opaque tenant and definition id are both non-blank; constructor and key-listing refusals
have no definition identity and are not counted. Listener faults cannot replace the refusal.
The emitter uses `System.Diagnostics.Metrics`; hosts compose their own OpenTelemetry listener.
