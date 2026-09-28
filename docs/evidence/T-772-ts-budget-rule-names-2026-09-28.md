# T-772 TS budget refusal rule names

The public seam is `GuardEvaluator.evaluateGuard` and `evaluateValue` over runtime-owned snapshots. With `stepBudget: 0`, the two parameterized test cases each failed red because `rule.budget_exceeded` carried empty params instead of naming its rule. Passing `rule.id` into the shared evaluation path made both focused cases pass. The affected rule-runtime package then passed 332/332 tests, type checking, lint and build. No refactor was needed.

Repository-pinned StrykerJS 10.0.0 used `tooling/strykerjs.config.json`, default `perTest` coverage analysis, `--mutate src/guard.ts:147-147` and `--testFiles src/__tests__/reactive.test.ts`. All three selected mutants were Killed, with no survivors, no-coverage results, timeouts or errors. The rule-param string mutant was killed by the new guard case; the existing unexpected-fault test killed the always-true budget-type mutant. The unchanged raw [mutation report](mutation/t772-ts-budget-rule-names-2026-09-28.json) has SHA-256 `16E5FC1563FEEA2B5A870899BE485070390C766AE5701558CDCF318E2E77C8A5`.

This verifies rule naming in the TS guard and value budget paths. Field-path reporting, graph outcomes, live host consumption and the rest of `kernel-core-ck-7` remain open. The .NET companion evidence is [here](T-772-dotnet-budget-rule-names-2026-09-28.md).
