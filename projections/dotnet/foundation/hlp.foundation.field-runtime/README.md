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
check reached the service index with HTTP 200. A subsequent one-time host restore, explicitly
approved in chat, restored all five kernel-test graph projects with `NuGetAudit=true` and
exit code 0. No audit bypass was used. The bounded schema-binding lane then passed the full
kernel project (44 tests) and field-runtime project (140 tests), with zero failures or skips
and `--no-restore`. Its red evidence was six missing-metadata projection cases and twelve
schema-integration failures. The initial missing-runtime test already passed because the
existing strict dialect refused the unknown keyword; that baseline was not a red test.

The next bounded lane connected real Forms rendering and validation, Views column domains,
and an independent test host around DataExchange's existing protected-payload port. One test
now crosses all three real callers; the host refuses unreadable and outside-domain mapped
values before storing a proposed payload. This does not ship a new DataExchange adapter.
The lane passed 145 field-runtime tests, 44 kernel tests, 68 Views tests, 20 focused Forms
consumer tests, and five wire-sync checks. The full Forms.Engine project had 167 passes and
six failures: its restart tests could not discover the repository from the external build
output directory. Those tests were not changed or excluded. A normal-layout rerun, renderer
integration, further consumer review, package-only evidence, and the platform gate remain.
Records still needs its own binding; these consumer tests do not close that work.

The next correction lane observed two React renderer failures before implementing radio
selection and bounded local typeahead over the supplied authorized membership. Its complete
schema-form, form-view and radio-group suites then passed 38, 24 and 18 tests respectively.
No TypeScript build, Blazor parity or platform-gate result is claimed. The .NET test attempt
stopped before execution on a missing targeting pack, and both subsequent audited restores
failed with `NU1900`. Required/domain submission checks, cardinality choices and publication
admission therefore still have unrun regressions; that lane made no .NET production changes.
The package-only schema and domain probes are prepared but have not run against packed artifacts.

The editor-completion lane then observed four additional React behavior failures: None,
SingleValue, ChoiceList and TaxonomyPicker were falling through to text. After mapping those
runtime verdicts, the complete schema-form, form-view, radio-group and select-field suites
passed 42, 24, 18 and 9 tests respectively. The aggregate React build and declared TypeScript
typecheck also passed. None renders no editor; the choice controls do not select defaults;
both picker kinds search only the supplied membership with a bounded display. These are
React results, not Blazor parity or platform-gate evidence. A normal-output audited restore
using the permitted package and HTTP caches still failed with NU1900, so that lane made no
.NET production changes and did not execute the prepared .NET regressions.

Schema-form now declares seven neutral domain-renderer cases at interface revision 3;
all seven executed in the React lane, whose four whole suites again passed 93 tests.
The corresponding Blazor fixture entry point now reads those inputs and asserts real
rendering and candidate callbacks, but its new assertions remain unrun. FormView revision 2
binds canonical Forms revision 6 and declares five membership/redaction cases. Three genuine
React failures exposed retained redacted membership and host options; the corrected binding
passed all five cases, the whole FormView suite (29 tests), and SchemaForm (42 tests), plus
build and typecheck. The .NET UI support counterpart is prepared but unrun. Neither these
renderer results nor metadata consistency checks establish full producer or cross-lane parity.

After Chris approved up to three host-side audited restore attempts when the platform lane
was free, the first pass restored all six affected test graphs with `NuGetAudit=true` and
exit code 0: field-runtime, kernel schema-validation, Forms.Engine, Views, the Blazor UI
aggregate, and .NET UI support. The pass used normal project-local outputs and ran no host
builds or tests. The new .NET regressions still require sandboxed execution; restore success
is not implementation or test evidence.

The schema binding retains validator identity as well as execution. A number kind
with `total_digits: 3, fraction_digits: 1` and one with `total_digits: 4,
fraction_digits: 1` have the same standard schema projection, but disagree on `123.4`.
The generated `x-harborline-field-kind` keyword now carries the exact kind identity, revision
and all parameters into canonical schema content. Registry injection uses `IFieldKindRuntime`
and binds the validator at registration. A keyword without the runtime, with malformed or
duplicate metadata, or with an unresolved kind refuses registration. The integration tests
register both declarations in both orders: the first refuses `123.4`, the second admits it,
and `12.30` still exceeds `fraction_digits: 1`. Arrays, local references and escaped property
names retain field refusals at their instance pointers. Successful `anyOf` branches do not
leak irrelevant field errors, including when an unrelated property fails. The Records compiler
still needs to consume this projection; a registry test does not close that caller's work.

Records remains in the kernel schema-validation projection. Shared declarations live in
`Harborline.Contracts.Fields`, contributed to the existing `Harborline.Contracts` assembly.
The interpreter stays here. Records' eventual binding crosses shared contracts; the tier
fence still forbids a kernel-to-foundation assembly reference. This does not add a library
or an exemption to that fence.
