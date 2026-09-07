# Harborline.Blocks.RelativeChains

Wave-1 deterministic generator substrate for ticket 086. It expands immutable external anchors and predecessor dependencies into append-only, date/revision-versioned occurrences, emits explicit supersession records, reports typed failures, and adapts only successful current occurrences to `IDueOccurrenceSource`.

Persistence, compare-and-append serialization, booking, medical policy, repair commits, and ambient clock/timezone reads are outside this package. Consumers persist the rich ledger and enforce `ExpectedPriorLedgerVersion` atomically.
