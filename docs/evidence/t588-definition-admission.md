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
`runtime-ts-admission-green.log`. Typechecking remains unverified: the verifier invoked
a nonexistent package-local `node_modules/typescript/bin/tsc` and exited 1 before
executing the compiler. The installed command wrapper instead targets
`node_modules/@typescript/native/bin/tsc`; the next bounded lane must run that command.

## Remaining T-588 work

The shared-store consumer and removal of the old Rules catalogue are not complete.
Two catalogue mutation regressions still fail. The shared-owner listing API and its
tests are not implemented/verified. The Rules envelope/version composition, both
projection contracts, independent consumers, exact inventory mapping and full platform
gate remain owed. No T-588 PR has been opened, and neither T-588 nor T-620 is closed by
this evidence. T-589 has not started.

The content-kind/pillar correction, Options inventory, refusal-code count and policy
metadata record corrections remain unresolved. These tests do not grant authoring
authority, change pack wire values, or settle those record questions.
