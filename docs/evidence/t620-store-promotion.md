# T-620 shared store promotion

## Source and ownership

The source is the Layout session's `LayoutDefinitionStore.cs` in platform commit
`e2f4bb6cbe6062d9aab0b590a1708608b8f8bcc2`, preserved as
`handoff/t620-layout-store-source`. Its staged source blob is
`8fccb1eaef442bd9aa05f0e4862ed4351716ea26`.

The promotion retains the locked revision dictionary, defensive snapshot boundary,
semantic-version comparator and restore-as-draft lifecycle. Registry-neutral immutable JSON
strings replace Layout object serialization. A required host-supplied member validator replaces
Layout admission. Stream revisions, immutable version pins, append-only lifecycle events and exact
replay strengthen the original implementation. Layout export and typed source remain outside the
shared store contract. The original source remains recoverable; this work does not edit the Layout
worktree or its handoff commit.

The four ADR-0096 registry families are Data exchange, Reports, Scheduling and Views, as named by
T-552 and DES-0007's C10 map. `DefinitionKind` is a catalogue namespace, not a pack content-kind
or primitive number. The existing Forms=0, Workflows=1 and in-flight Layout=3 identities remain.

## Verification so far

- Initial sandbox test command stopped during restore with NU1900 because NuGet vulnerability
  data was unreachable. No tests ran; this is not a code failure or a passing gate.
- Controller dependency-only `dotnet restore` for the builder-definitions test project, using
  `--artifacts-path C:/Users/Chris/AppData/Local/Temp/harborline-t620-artifacts`, completed with
  exit 0 and vulnerability auditing intact.
- The sandbox command below then compiled the existing producer but failed test compilation with
  nine expected missing-API errors: four missing shared types and five absent registry enum values.
  Dotnet exit code was 1. No test bodies ran. The shared implementation was added after this RED.

```text
dotnet test projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj --artifacts-path C:/Users/Chris/AppData/Local/Temp/harborline-t620-artifacts --no-restore -p:UseSharedCompilation=false -nodeReuse:false
```

The first execution reached 63 tests: 62 passed and the numeric-semver boundary test failed
because the promoted parser limited core components to Int32. Core components now compare as
validated digit strings by length and ordinal value. The next sandbox run exited 0:

```text
Passed!  - Failed:     0, Passed:    63, Skipped:     0, Total:    63, Duration: 68 ms - Harborline.Blocks.BuilderDefinitions.Tests.dll (net11.0)
```

The run emitted the existing analyzer baseline and new CA1720 (`Pointer`), CA1859
(`_admissions`) and CA1861 (test array) warnings. No warnings were suppressed. Earlier test-only
compilation issues (xUnit2031 and a mistaken assignment to a lambda parameter) were corrected
before the behavioral run; neither was counted as behavioral RED.

## Coverage and evidence still owed

The passing `VersionedDefinitionStoreTests.cs` cases exercise the four
registries, draft exclusion, immutable version pins across head changes, equal-version conflict,
restore and byte preservation, member refusals without clamping, malformed versions, unknown
registries, exact replay and concurrent edit fencing. Additional passing probes target direct
edits of a publication, restore over its identity, a draft changed during publication admission,
cancellation before commit, returned-history mutation and semantic-version components beyond Int32.

After that green run, supplementary probes were added for a known but unregistered namespace,
invalid JSON, trailing version whitespace and restore from an unpublished source. The expanded
suite and architecture checks then passed in one serial workspace-write lane, each with exit 0:

```text
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 48 ms - Harborline.Architecture.Tests.dll (net11.0)
Passed!  - Failed:     0, Passed:    69, Skipped:     0, Total:    69, Duration: 54 ms - Harborline.Blocks.BuilderDefinitions.Tests.dll (net11.0)
```

The architecture command used the same isolated artifacts and no-restore/shared-compilation
settings as the store command, targeting
`projections/dotnet/architecture/hlp.architecture.tests/Harborline.Architecture.Tests.csproj`
with `--filter FullyQualifiedName~DefinitionStoreOwnershipArchitectureTests`. Dependency-only
restore completed first with auditing intact. A manifest hardlink and projections junction under
the temporary artifacts root let the source inventory read this exact worktree; no producer file
was copied or changed for the test.

The verified architecture inventory fence names the existing Forms and Workflows concrete
stores; Aggregates currently exposes a port without a concrete store to inventory. It
rejects new stores elsewhere. The three existing copies awaiting their member migrations are
frozen by exact source digest, not allowed to evolve. Each member migration removes its copy and
its freeze entry. This transitional ratchet is not evidence that those migrations are done or that
the final three-family-only exception set has been reached.

## PR62 reconciliation and gate blocker

PR62 landed as `e3ec8871ce36b11a6796ff73b3e72e30cf72ff43`. Reconciliation keeps its
Layout producer, embedded placement schema, Forms admission changes, and guarded detach
behavior unchanged. The two conflicts retain all shared registry values with `Layout = 3`
and both README contracts. A serial workspace-write lane then returned exit 0 for both:

```text
Passed!  - Failed:     0, Passed:    94, Skipped:     0, Total:    94, Duration: 143 ms - Harborline.Blocks.BuilderDefinitions.Tests.dll (net11.0)
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 55 ms - Harborline.Architecture.Tests.dll (net11.0)
```

The earlier full host gate failed only when it reached the two Data Exchange gallery parity
cases. The narrow producer correction and fresh native/browser evidence are recorded in
`t620-data-exchange-parity.md`; both focused browser cases now pass without retries and
with zero measured pixel difference. That does not replace the full gate after reconciliation.

Still owed: a green full platform gate, T-620's separate PR and merge, and the subsequent T-588 Rules
consumer migration with deletion of the old `RuleCatalog` implementation and its interim prose.
T-620 must not be closed as fully accepted before its required first-consumer evidence exists.

The member-neutral store does not own schema compilation effects, transport, signing, installation,
authority or policy. No durable host adapter, member migration, owner approval, readiness promotion
or full gate success is claimed by this draft.
