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

## Test oracles (T-724 ruling 121)

Read this before you write or change a test's expected value.

- **The expected value comes from outside the code under test.** Use a literal, the authoritative spec or design record (a constant generated from that record counts; a hand-written production constant does not), a fixture corpus the code under test does not read, a stated property, or a separately written reference implementation. The assertion must fail when the covered behaviour is violated: a literal can still assert the wrong thing, a round trip can pass when both directions share a defect, and a reference implementation can repeat the same mistaken assumption.
- **Forbidden:** reading the production symbol as its own expected value; deriving the expected result from production output in a way that preserves the defect (for example, sorting the output to get the expected order); building the expected object in the test instead of running the production builder; comparing two fixtures without running production code.
- **Required for items marked `risk: silent`** under ADR-0103 (the mark on the control design item, never inferred from its description); advisory for every other test.
- **Mutation evidence does not replace this.** A test that reads the production constant kills the mutant and still pins nothing, so name the oracle as well as the killing test.
- **Reviewers ask it on the Standards axis.** A PR that adds or changes a covering test names each oracle's source on the template's Oracle line.
