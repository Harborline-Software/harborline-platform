# Harborline.Foundation.Scheduling

Harborline compatibility module for bounded RFC 5545 recurrence expansion and derived due queues.

RRULE expansion runs on [Ical.Net](https://github.com/ical-org/ical.net) (MIT) behind `IRruleExpansionService`, with the bounded subset (DES-0057 `recurrence-expansion-ck-1`) checked in front of it, and `WallClockResolver` resolves local times through NodaTime (Apache-2.0) as RFC 5545 §3.3.5 requires (ADR-0122, ADR-0123 decision 4). Both are external dependencies of this package.
