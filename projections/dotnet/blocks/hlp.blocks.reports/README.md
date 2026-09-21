# Harborline.Blocks.Reports

Harborline compatibility module for deterministic, snapshot-marked report execution over a Harborline-owned neutral read port. Includes Trial Balance, Balance Sheet, Profit and Loss, Profit and Loss by Property, AR Aging, AP Aging, and Rent Roll cartridges.

## Measures

`Measures/ReportMeasureEntries.All()` registers those same seven computations in the shared
`Harborline.Blocks.MeasureCatalogue` as bound entries. Nothing is re-implemented there: each entry
runs its existing cartridge over the `ReportMeasureBasis` the caller supplies, behind the
catalogue's bound Access predicate, and projects the cartridge's figures into typed cells.

A platform consumer that wants one of these figures resolves its catalogue address — for example
`finance.trial-balance` — and cannot tell that the entry behind it is written in code. It must not
reach the math any other way; `MeasureCatalogueArchitectureTests` refuses a consumer-side copy.

The runner, the cartridge registry, the snapshot marker and the run envelope stay here: Reports
still owns layout, basis and issuance, and the catalogue owns only the math (ADR 0075, DES-0031).
