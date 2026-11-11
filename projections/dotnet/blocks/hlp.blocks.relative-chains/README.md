# Harborline.Blocks.RelativeChains

## Retired implementation — T-491/occurrence-generation

The generator, validation, versioned occurrence ledger and supersession evidence moved to `Harborline.Blocks.Scheduling`, behind its instant-valued due seam. Chains use one external anchor, ISO duration offsets and per-node validity windows and tolerance. Consume Scheduling for all chain work.

This empty build/package shell remains only because `.github/workflows/validate.yml` wiring is reserved for Chris. All 24 authored cases have their canonical source under Scheduling tests and remain linked into the old test target until that wiring changes.
