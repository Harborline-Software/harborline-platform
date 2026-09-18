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

## Remaining T-588 work

The shared-store consumer and removal of the old Rules catalogue are not complete.
The last native authoring run still had two old-catalogue mutation failures. The
shared-owner listing and adapter currently have preparatory stubs, not verified
behavior. Native builder tests are blocked before compilation by audited restore
NU1900; auditing has not been disabled. The three new version-label tests produced
29 passes and 3 failures before removal of the competing Rules version parser; that
change still needs native verification.

The shared-store consumer, cross-projection fixture agreement, independent consumers,
exact inventory mapping and full platform gate remain owed. No T-588 PR has been opened.
Control separately closed T-620 against platform PR63 in control PR722; that closure
does not prove the first-consumer work assigned here. T-589 has not started.

The content-kind/pillar correction, Options inventory, refusal-code count and policy
metadata record corrections remain unresolved. These tests do not grant authoring
authority, change pack wire values, or settle those record questions.
