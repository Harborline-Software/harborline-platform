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
assembly and packed at the same path for the api to consume. Regenerate it with
`dotnet run --project tooling/content-kind-export -- _shared/content-kinds/content-kinds.json`;
`VerifyCheckedInExport` fails the tests when it is stale. `PackContentKindDriftArchitectureTests` fails
when any platform `*PackIdentity` constant, or any `PackContentKind`/`PackPillar` enum, disagrees with
the registry.
