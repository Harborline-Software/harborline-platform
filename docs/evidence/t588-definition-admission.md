# T-588 definition admission — work in progress

This is partial producer evidence, not T-588 acceptance or a readiness promotion.
The worktree starts at platform `14f4ef026005e23f127647d5411bbfa76132d915` (PR63).

## Compiler admission

Both compiler implementations previously accepted unsupported operators, unknown tiers
and unknown actions. Unsupported operators were rejected only during evaluation, so an
empty record set could never exercise that rejection. Compilation now refuses those
declarations independently of record evaluation, including an unsupported operator in
an unreachable `if` branch. Arrays and multi-property objects remain literal data under
the existing evaluator contract; their contents are not treated as executable syntax.

Tests:

- `projections/dotnet/foundation/hlp.foundation.rule-runtime.tests/RuleEngineUnitTests.cs`:
  `CompilerRefusesUnsupportedOperatorsWithoutNeedingAnyRecords`,
  `CompilerDoesNotInterpretAnUnknownTierAsJsonLogic`,
  `CompilerDoesNotInterpretAnUnknownActionAsVisibility`,
  `OperatorAdmissionDoesNotInterpretLiteralDataAsExecutableSyntax`.
- `projections/typescript/foundation/hlp.foundation.rule-runtime/src/__tests__/definition-admission.test.ts`:
  matching unsupported-operator, tier/action and literal-data cases.
- Both files also prove the inclusive default reference and dependency-depth bounds
  at 64 and refusal at 65.

Actual workspace-write results, 2026-09-18, via serial `codex exec` Astra/high lanes
with subprocess stdin closed:

```text
Native compiler regression baseline:
Failed: 7, Passed: 27, Skipped: 0, Total: 34; child exit 1

TypeScript compiler regression baseline:
Tests 11 failed | 6 passed (17); child exit 1

Complete native runtime after the fix:
Passed! - Failed: 0, Passed: 161, Skipped: 0, Total: 161; child exit 0

Complete TypeScript runtime after the fix:
Test Files 16 passed (16)
Tests 192 passed (192); child exit 0
```

Full local outputs are in ignored `artifacts/t588-local/compiler-red.log`,
`compiler-ts-red.log`, `runtime-admission-green.log` and
`runtime-ts-admission-green.log`. The initial typecheck invoked a nonexistent
package-local `node_modules/typescript/bin/tsc` and exited 1 without checking source.
The corrected command, `node node_modules/@typescript/native/bin/tsc -p tsconfig.json
--noEmit`, subsequently passed with child exit 0 and no diagnostics. Its complete
output is `artifacts/t588-local/ts-intent-06-runtime-typecheck.log`.

## TypeScript definition intent

The typed source projection preserves the native envelope and skin discriminants.
Its strict JSON reader rejects duplicate members, including escaped-equivalent names,
and unknown or malformed members by code and RFC 6901 pointer. Author, Publish and
Persisted validation retain their phase. Validated source remains separate from the
derived AST; skin lowering and the full engine compiler run before acceptance.
The projection owns no persistence, version parser or evaluator. Limits come from
the existing engine contract. Invalid persisted source is not clamped.

Behavioral tests live in
`projections/typescript/foundation/hlp.foundation.rule-authoring/src/__tests__/definition-intent.test.ts`.
They include native closed actions/scopes, malformed cells and typed values, cycles,
256/257 AST nodes, 4096/4097 literal characters, source detachment and advisory lint.

Actual workspace-write Astra/high results, with each child serial and stdin closed:

```text
Initial compiling-stub baseline: 69 failed, 1 passed, 70 total; child exit 1
Initial implementation: 1 failed, 69 passed, 70 total; child exit 1
Expanded operator baseline: 3 failed, 75 passed, 78 total; child exit 1
Final focused tests: 78 passed, 78 total; child exit 0
Complete authoring tests: 8 files passed, 113 tests passed; child exit 0
Runtime standard typecheck: child exit 0
Authoring/runtime source-resolution typecheck: child exit 0
Authoring standard package typecheck: child exit 1 (missing runtime dist declarations)
```

Logs are `artifacts/t588-local/ts-intent-01-red.log` through
`ts-intent-10-source-typecheck.log`; `ts-intent-handoff.md` maps each command to its
output. The source-resolution check uses an isolated configuration under artifacts.
It does not substitute for the still-unverified generated-declaration consumption path.

The focused literal regression exposed an overload ambiguity: the skin compiler returns
a text literal as a string, while the full compiler accepts a string as encoded JSON.
The intent adapter now encodes the lowered expression with the existing canonical
writer before full compilation. It does not reinterpret or change the authored literal.

## Native shared-store slice

The Rules adapter now uses `IVersionedDefinitionStore` for history, expected-revision
publication, exact request replay, restore and immutable version identities. The shared
owner supplies tenant/kind-scoped key listing; Rules adds no dictionary or local store.
Source identity, tenant and semantic-version label are separated from opaque body bytes,
so restoring a publication preserves its body while reconstructing the new metadata.
Both admission phases and persisted-source decoding run the full intent validator.

The ordinary workspace-write audited restore succeeded without host execution or audit
suppression. Actual results from the completed native slice:

```text
Native shared-corpus baseline: 107 passed, 3 failed, 110 total; child exit 1
TypeScript shared-corpus baseline: 75 passed, 3 failed, 78 total; child exit 1
Builder compiling-stub baseline: 95 passed, 24 failed, 119 total; child exit 1
Final builder suite: 134 passed, 0 failed; child exit 0
Native authoring: 155 passed, 2 legacy catalogue failures, 157 total; child exit 1
TypeScript authoring: 191 passed, 0 failed, 9 files; child exit 0
Normal TypeScript runtime build: child exit 0
Standard authoring typecheck using runtime dist declarations: child exit 0
```

Both readers pass the same 26 JSON sources across three phases (78 cases each).
Their initial three failures exposed provenance `1e400` becoming nonfinite. Admission
now refuses it with `rules.definition.invalid_document` and the exact pointer rather
than emitting invalid canonical JSON. Native finite controls also caught and corrected
an overly narrow float conversion; finite double values remain valid.

Tests are `RuleDefinitionCatalogTests.cs` and `VersionedDefinitionStoreTests.cs` in
the builder-definitions test project, `RuleDefinitionIntentTests.cs` and
`RuleDefinitionIntentConformanceTests.cs` in native authoring, and
`definition-intent-conformance.test.ts` in TypeScript authoring. Full local logs and
actual command/exit records are `artifacts/t588-local/slice2-01-*` through
`slice2-15-*`; `native-slice2-results.md` maps every run. Two initial builder attempts
stopped at compilation and are not behavioral RED evidence.

## Catalogue retirement slice

The bounded retirement worker exited naturally with exit 0. The old catalogue/store
and publication-admission implementations are deleted in both projections. Their
behavioral coverage moved to the real shared-store consumer before the old suites
were removed. In particular, input/loaded collection mutation cannot change history,
and equal-version/different-body publication cannot replace an immutable pin.

Archive now controls list visibility, not resolution. Consumer version policy is no
longer authored source; unknown source members still refuse. The three regressions
first failed for the intended behavior (archived pin returned NotFound; both readers
refused source without versionPolicy), each with child exit 1. Catalogue operation
stubs and a later latest-draft selection defect also produced behavioral RED before
their corrections. The shared store now owns creation, load/list, duplicate, archive,
publication, replay, restore and caller-supplied version identities and labels.

Final focused results, independently inspected from complete saved logs and exits:

```text
Native builder: 173 passed, 0 failed; child exit 0
Native authoring: 149 passed, 0 failed; child exit 0
TypeScript authoring: 189 passed, 7 files; child exit 0
Native runtime: 161 passed, 0 failed; child exit 0
TypeScript runtime: 224 passed, 22 files; child exit 0
Normal TypeScript authoring/runtime builds and typechecks: child exit 0 each
Focused packed npm basic Rules smoke and 12-case calculations consumer: child exit 0 each
```

Both intent readers now use 29 raw JSON sources across three phases (87 cases),
including exact canonical numeric bytes, escaped overflow location and duplicate-member
precedence. Corpus SHA256 is
`48b8798a27f42fcf7026e9e8929f982a8d9fc202d7b87a344e53e959512adbfd`.
The npm consumer also runs those same 87 cases. Its first basic smoke failed because
the assertion compared plain and prototype-free objects; the corrected assertion
checks canonical persisted round-trip, and the rerun passed.

Logs are `artifacts/t588-local/retirement-01-*` through `retirement-27-*` and
`packed-*`. `retirement-results.md` records commands, exits and the exact old-to-new
test mapping. Authoring interface revision is now 2; builder-definitions is 5.
The builder depends on foundation authoring, never the reverse. Only the obsolete
Rules architecture exception was removed. Protected export/seed contract text and
unrelated preview/runtime fixture controls are unchanged.

## Remaining T-588 work

Architecture execution and native packed consumers remain unverified. An ordinary
audited restore failed with NU1900 because the NuGet service index was unreachable;
the architecture attempt did not run tests. Packed NuGet restore first failed NU1301
from command-line source normalization, then NU1900 with ordinary NuGet.Config sources.
No audit suppression, failed-source ignore, foreign assets or host approval was used.
Native packages produced earlier in the slice predate the final load correction and
are not final acceptance evidence. Repack and run both native consumers after restore
is available. Packed cross-projection agreement, architecture and the full platform
gate remain owed. No T-588 PR has been opened.
Control separately closed T-620 against platform PR63 in control PR722; that closure
does not prove the first-consumer work assigned here. T-589 has not started.

The content-kind/pillar correction, Options inventory, refusal-code count and policy
metadata record corrections remain unresolved. These tests do not grant authoring
authority, change pack wire values, or settle those record questions.

## Inventory audit — incomplete, not a closure claim

T-588 assigns the following rows. A passing suite is not blanket evidence for all of
them. The table records the relevant producer and the remaining acceptance work.
All abbreviated paths below are under `projections/dotnet/`; TypeScript peers use
`projections/typescript/` with the same module id.

| Assigned rows | Producer/evidence boundary | Remaining check |
| --- | --- | --- |
| rules-ck-1 | StandardsCatalog content-kind/pillar mismatch | Governing record correction remains unresolved; no new wire value authored |
| rules-ck-2 | API `PackSafetyFloors.ExtractFloors` in `packages/foundation-packs/Install/PackInstallModel.cs` | Installation-owned elementwise-max union over StandardsCatalog items, feeding the installed S-8 watermark. The author-side guard reuses it for advisory guidance. No T-588 producer work; see the classification below |
| rules-ck-3 | Platform `PackageSafetyFloorReattachment`, shipped in PR64 (`f90829c`) | Existing builder-definitions producer and four unit cases; the packed NuGet consumer asserts malformed-member refusal code and member. Retain/re-run these when reconciling current main; T-655 owns API consumption divergence |
| rules-ck-4, rules-ck-5, rules-ck-6 | `foundation/hlp.foundation.rule-authoring/RuleDefinitionDocument.cs`, codec and full compiler | Packed typed-source admission and exact action/presentation coverage must be audited after migration |
| rules-ck-7, rules-ck-8, rules-ck-9, rules-ck-10, rules-ck-11 | Existing rule-runtime scope grammar, closed operators, aggregate and domain operators | Preserve operator-catalog and shared runtime conformance through the full gate; authoring adds no evaluator |
| rules-ck-12 | Existing runtime outcome contracts | Options inventory is an unresolved record correction; no outcome count is promoted |
| rules-ck-13, rules-ck-14, rules-ck-15, rules-ck-16 | Typed table/formula codec and skin lowering | Shared intent corpus passes; packed source round-trip remains owed |
| rules-ck-17 | New `blocks/hlp.blocks.builder-definitions/RuleDefinitionCatalog.cs` | Named listing/create/duplicate/archive tests pass and old store is deleted; native packed execution remains owed |
| rules-ck-18, rules-ck-26 | Shared semantic-version heads, exact immutable versions and caller policies | Store tests prove publication/replay/restore, archive-safe pins and older-history head protection; native packed execution remains owed |
| rules-ck-19 | Existing RuleEngineLimits; intent schema exposes those limits | Inclusive static bounds are tested; full gate must retain existing runtime bound tests |
| rules-ck-20 | Existing stable runtime/skin codes plus located intent diagnostics | Disputed refusal count remains unresolved; do not equate an added code with a settled inventory |
| rules-ck-25 | Provider-neutral authored envelope and metadata/body split | Round-trip and forbidden retention/hold tests pass; policy-owned fields remain outside authorship |
| rules-eng-1 | Shared-store policy resolution | Published/draft controls and archive-safe immutable pin pass; full gate remains owed |
| rules-eng-2, rules-eng-3, rules-eng-4 | Existing SkinLowering and skin compilers | Preserve `PriorityCatchAllKeepsTheSameWinningRowAndValueInPreviewAndRuntime` and both projection probes |
| rules-eng-5, rules-eng-6, rules-eng-7 | Full intent admission at author/publish/persisted boundaries | No-match, unsupported tier and zero-history refusals pass; old callers migrated, native packed execution remains owed |
| rules-eng-8, rules-eng-9 | Full compiler static bounds and cycle diagnostics | Compiler tests cover dependency depth/reference boundaries; intent tests cover AST/literal boundaries and cycle path |
| rules-eng-21 | Shared catalogue lifecycle plus existing preview | Catalogue migration and packed npm pass; native packed consumer proof remains owed |
| rules-eng-22 | RuleLint and intent no-match admission | Advisory nonterminal wildcard remains admitted; lint must not become a publication veto |
| rules-auth-21, rules-auth-22, rules-auth-23, rules-auth-24, rules-auth-25 | Typed diagnostics carrying phase, code, pointer and compiler rule/cycle details | Both intent readers pass the same sources; packed editor-facing diagnostics remain owed |
| rules-auth-29 | Shared corpus hand-written emptiness case | Accepted and preserved as authored expression, not converted to canonical intent |

The earlier missing-producer conclusion described the old `14f4ef0` baseline. It is
superseded by the current-main inspection and task direction recorded below. Neither
floor row requires a new Rules producer, and neither is assigned to T-536 here.

## Bounded retirement continuation — implementation complete, verification blocked

This continuation supersedes the earlier migration-state statements above, without closing the
unresolved inventory, safety-floor or policy-metadata rows. Full logs, actual exits, retired-test
mapping and exact file inventory are in `artifacts/t588-local/retirement-results.md`.

Both projections' RuleCatalog/store and PublishAdmission implementations are deleted. The blocks
RuleDefinitionCatalog now composes the real shared versioned store and existing lifecycle store
for create/load/list/duplicate/archive, fenced saves/publication, restore and caller-policy resolution.
Foundation contains source, codecs and intent diagnostics with no upward dependency. Rules' frozen
architecture entry was removed with its source; the checker remains intact. Authoring interface and
catalogue revision is 2; builder-definitions is 5, including the authoring dependency.

Actual RED preceded the archive-pin fix, both source-policy fixes and the catalogue implementation.
A further RED proved an older publication could hide a newer draft: load/list now prefer the latest
open draft, falling back to the generic published head. Archive affects listing visibility only.
VersionPolicy is a caller selector and an unknown authored member. Generic SemVer owns admission
and head selection; older history cannot lower the head. The offline RuleRegistry remains separate.

Both old failing assertions have passing real-store replacements:
`InputAndLoadedCollectionsCannotMutatePublishedHistory` and
`EqualVersionWithDifferentBodyCannotReplaceAnImmutablePin`. RuleDefinitionCatalogTests also covers
publication failure propagation, unknown rules, collisions, duplicate/archive isolation, advisory
lint, CAS concurrency, exact replay, restore, draft exclusion and malformed versions. Automatic
Rules patch allocation was explicitly retired because generic callers supply labels.

Final serial suites, all exit 0: builder **173/173**, native authoring **149/149**, TS authoring
**189/189**, native runtime **161/161**, TS runtime **224/224**. Normal TS builds/typechecks passed.
Both readers pass all 29 raw sources in three phases (87 cases), preserving duplicate members,
overflow syntax, finite-number provenance and optional exact canonical bytes. Builder additionally
runs all 29 sources against actual history, proving refused source makes zero writes.

Packed npm basic smoke and the 12-case calculations fixture passed, including all 87 shared intent
phase cases. Seven preview controls and the graph-cycle fixture remain intact. Native package
fixtures now consume the same corpus and packed builder library, but **remain uncompiled/unrun**:
ordinary audited restore failed NU1900 because NuGet's vulnerability service was unreachable.
Architecture execution is blocked by the same failure. No audit bypass or host approval was used.
Initial native packages predate the final load-selection correction and do not prove final package
acceptance. Architecture, native packed execution and the full gate remain outstanding.

Protected export/seed text, unrelated modules and basic NuGet smoke outside Rules were checked
unchanged. No cleanup, commit, push, PR, full gate or T589 work occurred. All children exited
naturally. This is slice evidence, not T588 closure or readiness promotion.

## Audited sandbox retry after shared-cache refresh

On 2026-09-18 (2026-09-19 01:17 UTC), a fresh bounded Astra/high workspace-write lane
retried the architecture restore after another session refreshed ordinary shared caches.
The exact command was `dotnet restore
projections/dotnet/architecture/hlp.architecture.tests/Harborline.Architecture.Tests.csproj
-p:NuGetAudit=true -v:minimal`, with stdin closed. It exited naturally with **1**:
NU1900, unable to load the NuGet service index for vulnerability data. The reported
4 of 25 projects already up-to-date did not make the restore successful.

No architecture tests, final-source package builds or consumer tests ran in this retry.
No host execution, foreign assets, audit override or repeated restore was used.
`git diff --check` exited 0. Complete output and actual exit records are under
`artifacts/t588-local/verification-retry/`; `results.md` maps the commands to logs.
The controller inspected the complete restore output and exit record. The worker
then exited naturally and the shared lane returned to T621. At that checkpoint the
restore, final native consumer proof, full gate and floor classification remained open.

## Safety-floor evidence and installation classification

Following the task's correction, current platform `origin/main` was fetched and
PR64 was inspected at merge `f90829c0e2c825754aa795d2a104df2af27bd4d1`.
`projections/dotnet/blocks/hlp.blocks.builder-definitions/PackageSafetyFloorReattachment.cs`
already owns raise-only reattachment. Lower integers are clamped, missing members
and removed objects are restored, and a present non-integer member refuses the whole
reattachment with `platform-package-safety-floor-malformed` and the member name.
`PackageSafetyFloorReattachmentTests.cs` covers all four cases. At that revision,
`tests/package-consumers/nuget/Program.cs:469` calls the packed contract and asserts
the refusal code and `retention` member. This is existing producer/consumer evidence,
not a new T-588 implementation or a claim that this worktree has re-run those probes.
The worktree still needs reconciliation with that landed source before its gate.

For `rules-ck-2`, the union is **installation**, not Rules definition authoring.
At API `origin/main` `7b39648a`, `PackSafetyFloors.ExtractFloors` in
`packages/foundation-packs/Install/PackInstallModel.cs:158` selects StandardsCatalog
items and folds their declared floors with elementwise `Max`. `PackInstaller.cs:888`
uses the result for S-8 installed-watermark checks; its committed watermark also
accumulates floors with `Max`. The author-side `PackAuthorFloorGuard.Check` calls the
same extractor at `Export/PackAuthorFloorGuard.cs:43`, but documents its result as
best-effort guidance against the author's prior lineage, never installation authority.
That reuse does not move ownership to authoring. The row's existing API evidence
points to the installation responsibility; its wording should name that responsibility.
No union producer or pack-export change is added by T-588. The StandardsCatalog
install fence is unchanged; inspecting the extraction path does not claim that fenced
content can install live.

Control `origin/main` `fc0da61f` records T-536 DONE for API PR181 and T-655 for the
two reattachment copies' divergence. T-588 grades `rules-ck-3` against the released
platform refusal contract. T-655 remains API work and is not absorbed here. This
classification was reported before any implementation; no new Chris approval is implied.

## Restore diagnosis resolved without an exemption

On 2026-09-19 local time (2026-09-20 01:25 UTC), one ordinary audited architecture
restore succeeded inside the same workspace-write lane: **exit 0, zero warnings,
zero errors**. `NuGetAudit=true` remained explicit, and warnings-as-errors remained
unchanged. The log shows cached NuGet vulnerability index, base and update data being
used successfully. There was no host restore, source override, manual cache change,
foreign assets, permission change or warning exemption. This supersedes the restore
blocker recorded above, not the outstanding architecture/native consumer/full-gate work.

The sandbox source list is only enabled nuget.org. Worktree and root NuGet.Config are
byte-identical and both use `<clear/>`. The SDK's additional `library-packs` location
comes from its own targets, not an added local feed. The old 25-project graph's listed
package files and expected package files were readable; architecture alone resolved
17 libraries with 573 listed files. Its recorded restore failure was solely NU1900,
promoted by `TreatWarningsAsErrors`, not a missing-package resolution error. The analyzer
safety valve does not exempt it. The new architecture cache reports success with no logs.

Full source output: `artifacts/t588-local/restore-diagnosis/20260920T012309Z-sources-fbac1ede.log`.
Full restore output: `artifacts/t588-local/restore-diagnosis/20260920T012543Z-audited-restore-34a7e086.log`.
Adjacent `.exit.json` records contain exact commands, cwd, stdin policy and actual exits.
`restore-diagnosis/results.md` records the inspection and its limitations. The controller
inspected these outputs after natural worker exit. The earlier network cause remains
unproven; the successful host gates did not establish earlier sandbox connectivity.

TICKET_READY was reported for continuation after this restore result. No architecture
suite, new native consumer run or full gate is claimed by this diagnostic. T-588 still
needs reconciliation with landed main, those verification steps, one PR, merge and
control closure. T-589 remains unstarted until T-588 merges.

## Reconciled architecture and external consumers

Reconciled with platform main `4bbba70f064958dda3cddcf3712760f5d7f710c4`,
preserving the released PR64 safety-floor contract and subsequent configuration contracts.
Builder interface/catalogue revision is 7; Rules authoring remains revision 2.
The architecture freeze retains Views and removes both migrated Rules and DataExchange
entries. The clock inventory initially failed 24/25 because it still named the removed
RuleCatalog; removing that single stale entry made all 25 pass. Scanner and canaries
are unchanged. Protected export, seed and DataExchange source match main exactly.

Fresh audited workspace-write verification on 2026-09-20 UTC:

- Architecture: 25 passed, 0 failed, 0 skipped.
- Builder definitions: 218 passed, including all four existing safety-floor cases.
- Native rule authoring: 149 passed, 0 failed, 0 skipped.
- Standard focused packed check: exit 0, `status: PASS`, consumers outside the repository,
  zero source/project dependencies. Complete basic NuGet, Forms Engine, Inspection Review,
  Workflow and calculations consumers ran. Existing safety-floor code/member assertions,
  export/seed assertions and unrelated checks remained enabled.
- Calculations comparison: 12 cases, 12 cross-lane verdicts, zero mismatches, seven client
  previews and three refusals. Corpus SHA-256
  `c616d4115aa2b555303a13ba75480bbf7fdb15387fe65d0cccb362f2b1539162`.

The first packed attempt used a controller-created scratch parent inside the worktree,
accidentally inheriting central package management and failing NU1008. Moving only the
ignored wrapper's isolated scratch parent to ordinary sandbox OS temp resolved that error.
The external compile then exposed a missing `System.IO` import in the new Rules fixture;
adding that import made the complete standard run pass. No consumer assertion was removed,
no project setting weakened, and no host execution or audit/warning exemption was used.

Actual command: `node tooling/verify-package-fixtures.mjs --only calculations-capability-vertical`.
PID 58708 exited 0 naturally at 2026-09-20 01:54:59 UTC. Full combined output is
`artifacts/t588-local/reconciled-verification/20260920T015317Z-external-consumers-import-calculations-da86ec29.log`;
the adjacent exit JSON records command, cwd, stdin and validated external scratch ancestry.
Native suite logs in that directory are `20260920T014115Z-resumed-architecture-test-637703b7.log`,
`20260920T014138Z-resumed-builder-test-8f9124ae.log` and
`20260920T014155Z-resumed-rule-authoring-test-e6cdde70.log`, each with actual exit 0 records.

All 26 newly packed native libraries identify `0.0.0-alpha.0.h9fa7e96dd92c`.
Actual authoring tarball `@harborline-software/rule-authoring@0.1.0-alpha.0` has SHA-256
`3171f943be05404319ac5815e035f78eceebae1edd63c25918e7c7eb53f26272`; its 19 entries contain
no retired catalog/admission implementation files or symbols. Full identity/hash inspection:
`20260920T015506Z-external-consumers-import-artifact-inspection-6d197177.log` in the same directory.

These results satisfy the focused architecture and native-consumer checks, not the full gate.
The gate, PR, merge and control closure remain outstanding. T-589 has not started.
