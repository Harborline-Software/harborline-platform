# Harborline.Foundation.FieldRuntime

The shared field substrate described by DES-0030 and built under T-621. It owns field-kind
registrations, permitted-value domains, narrowing, kind limits and editor selection.
Records owns its grammar and JSON Schema compiler; this producer owns no definition store
and no authoring destination.

## Promotion source

The initial implementation is promoted from `codex/t-615-records` at `a625fdd`:

- `FieldKindRegistry.cs`: exact immutable kind/version lookup and scalar shape admission.
- `RecordsDefinitions.cs`: the three-source `ValueDomainDefinition`, field-kind references,
  scalar shapes, governance defaults, shared constraints and domain-source refusals.
- `RecordsConstraintIntersection.cs`: folding required, multiplicity, access and domains.

Records-specific field traversal, trait-slot binding, identity and reference rules remain
with Records. Substrate refusals use `field.*`, not `records.*`.

## Scope

T-621 adds authority-aware Taxonomy and record-query domain resolution, resolved-membership
narrowing, numeric and byte-size limits, and domain-driven editor selection. Expression
evaluation, record authorization and commit orchestration remain at their existing owners.

Numeric limits use the two explicit kind-parameter names `total_digits` and
`fraction_digits`, as ruled in T-621 on 2026-09-18. Leading zeroes and the sign do not count
toward `total_digits`; trailing zeroes after the point do. `fraction_digits` counts digits
after the point, and cannot exceed a declared `total_digits`. Both refuse overflow without
rounding. Schema compilation must carry `fraction_digits` as `multipleOf` and retain an
executable `total_digits` validator. The producer suite covers these rules; the Records
consumer path still needs integration evidence.

Implementation and verification are in progress. This README is not release evidence.

The source now includes typed and raw JSON domain admission, exact kind lookup, and
`IFieldKindRuntime.Bind`, which pairs a compiled schema with its complete value validator.
Limits include the two digit facets, inclusive numeric bounds, Unicode scalar length and
UTF-8 byte size. Refusals preserve the authored or submitted RFC 6901 location.

On 2026-09-18 the bounded workspace-write resolver lane passed 137 tests, with zero failures
or skips, using `dotnet test --no-restore` after an audited restore. The tested slice adds
literal, exact-version taxonomy and record-query resolution; complete-membership intersection
and narrowing; inherited read-authority filtering; detached inputs and outputs; and editor
choice. This is focused producer evidence, not the platform gate or consumer binding evidence.

Review then found that a Rules liveness timeout was being converted into a field admission
refusal. The next lane reproduced three failures in 140 tests: that timeout and two unsupported
operator cases over empty sources. The corrected timeout path then passed its focused test,
preserving the original infrastructure exception and caller cancellation token. T-588's shared RuleCompiler fix
(`df1e908`) was consumed locally as `3639798` after both empty-set cases were observed failing.
That dependency has not yet landed upstream. The next producer run observed 139 passing
tests and one expected-code mismatch: the populated unknown-operator case now refuses at
compilation rather than evaluation. After that one assertion was updated, the entire producer
project passed 140 tests, with zero failures or skips, without restore or test filters.
Both empty-source cases passed unchanged. No private operator validator was added here.

The same lane's audited restore of the kernel test graph stopped on `NU1900`: the sandbox
could not reach NuGet's vulnerability service. Audit was not disabled. A host-side read-only
check reached the service index with HTTP 200. The executable-kind binding test remains
prepared but unrun; its implementation has not started. Kernel-to-Contracts and test-to-runtime
project references are scaffolding, not completed consumer evidence.

The executable-kind schema binding and real Forms/Views/mapped-row integration remain to be
completed. Exposing the shared contract is not proof that those consumers or Records use it.

The Records binding must retain validator identity as well as execution. A number kind
with `total_digits: 3, fraction_digits: 1` and one with `total_digits: 4,
fraction_digits: 1` have the same standard schema projection, but disagree on `123.4`.
The current schema registry hashes only canonical JSON Schema content and stores one
entry per hash. That schema ID alone cannot identify the complete compiled field binding.
The integration regression must register both declarations, validate through the consumer,
and prove the first refuses `123.4` while the second admits it, in either registration order.
It must also preserve the submitted spelling so `12.30` still exceeds `fraction_digits: 1`.
The current Records compiler registers schema text only; that path does not yet carry
these validators. A passing direct call to `ICompiledFieldKind.Validate` does not close it.

Records remains in the kernel schema-validation projection. Shared declarations live in
`Harborline.Contracts.Fields`, contributed to the existing `Harborline.Contracts` assembly.
The interpreter stays here. Records' eventual binding crosses shared contracts; the tier
fence still forbids a kernel-to-foundation assembly reference. This does not add a library
or an exemption to that fence.
