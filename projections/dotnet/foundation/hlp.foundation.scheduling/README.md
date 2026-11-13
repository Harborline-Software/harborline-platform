# Harborline.Foundation.Scheduling

Harborline compatibility module for bounded RFC 5545 recurrence expansion and shared wall-clock conversion.

RRULE expansion runs on [Ical.Net](https://github.com/ical-org/ical.net) (MIT) behind `IRruleExpansionService`, with the bounded subset (DES-0057 `recurrence-expansion-ck-1`) checked in front of it, and `WallClockResolver` resolves local times through NodaTime (Apache-2.0) as RFC 5545 §3.3.5 requires (ADR-0122, ADR-0123 decision 4). Both are external dependencies of this package.

## Changelog — T-491/occurrence-generation (breaking pre-release move)

`IDueOccurrenceSource`, the RRULE due source, `IDueQueueQueryService`, `RruleDueQueueQueryService` and their completion/occurrence models moved to `Harborline.Blocks.Scheduling` and now carry instants and stable occurrence identities. No forwarding shim is provided: the foundation does not reference a block. This package retains the bounded recurrence producer, its cap exception, and `WallClockResolver`.

`WallClockResolver.ToLocalDateTime(instant, timezone)` is the inverse projection to an unspecified local `DateTime`, using the same NodaTime tzdb provider as the unchanged forward API. Literal Auckland/Honolulu and New York gap/overlap cases pin both directions.

## Shared availability contract (T-491/module-admission)

`IAvailabilityRuntime` now lives in `Harborline.Foundation.Scheduling.Services`. Its request, answer, refusal and capacity types, `ParticipantRef`, `TimeInterval` and `EventPadding` live in `Harborline.Foundation.Scheduling.Models`. Import these namespaces in consumers. This is a breaking pre-release namespace and assembly move with no forwarding shim; public signatures and guards are unchanged. `FreeBusyService`, the supply model, stores and all availability composition stay in the calendar block. Scheduling consumes the foundation contract and does not reference Calendar.
