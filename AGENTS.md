# Agent guide — harborline-platform

## Mutation testing (Stryker.NET)

Read this before you add or change a test project's `stryker-config.json`, or claim mutation evidence for a ticket. `tooling/stryker.mjs` is the entry point; its header documents `check`, `run`, `full` and `baseline`.

- **One config mutates one direct reference.** Each test project's `stryker-config.json` mutates exactly one project that the test project references directly. Stryker cannot mutate a project the tests reach only transitively ("Could not find an assembly reference to a mutable assembly").
- **Logic invalidates a "no logic" exclusion.** When you add code with branches to a project listed in `tooling/stryker-exclusions.json` as having nothing to mutate, give that project its own test project and config, and remove the exclusion row. Register the new test project in `Harborline.Platform.slnx` and in `tooling/run-native.mjs`.
- **A new config needs a measured baseline.** Run `node tooling/stryker.mjs baseline <test-project-fragment>`. It records the score in `tooling/stryker-baselines.json` and sets `break`, with `low = max(60, break)` and `high = max(80, break)` (ruling 96).
- **Stage before checking.** `check` discovers configs from git, so `git add` a new test project before you run it.
- **MemberData theories need `"coverage-analysis": "off"`.** Per-test coverage capture misattributes theories fed by `MemberData`, and reports lines the tests plainly cover as `NoCoverage`. Set the option in that project's `stryker-config.json`; Stryker.NET 5 does not accept it as a command-line flag.
- **The report is the evidence, not the exit code.** Stryker can exit 0 having mutated nothing. Read the JSON report and name the killing test for each mutant a ticket lists (T-724 ruling 35). Mutant ids change between runs, so match survivors by file, line and mutator.
- **Run Stryker natively.** Inside the Codex sandbox it tries a network restore and fails with `NU1301`; record that and leave the run to the controller.
