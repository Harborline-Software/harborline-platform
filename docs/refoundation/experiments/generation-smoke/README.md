# Generation-smoke rehearsal

## Scope

The generator was designed against `hlp.ui.button`, `hlp.ui.dialog`, and `hlp.ui.switch-field`. It consumes only each module's `interface.yaml`, `quality.yaml`, and `scenarios.json`. It deliberately does not read either implementation lane.

The current corpus supports generation of a compilable Razor component library containing one neutral scenario shell per declared scenario. The shell preserves module ID, scenario ID, surface, and name so the existing gallery/conformance harness can discover it after a future content-authority promotion.

## Finding

Compilation sufficiency is testable now. Visual or behavioral parity is not. In particular, `specs/modules/ui/hlp.ui.dialog/scenarios.json` does not contain the copy, controls, fixture values, or lifecycle wiring in the hand-built React and Blazor scenarios. Generation-smoke must therefore be reported as a compilation smoke proof, never as parity evidence.

`hlp.ui.switch-field` also demonstrates why `style.css` is not a universal generator input: the adapter delegates presentation to `hlp.ui.switch` and has no spec style artifact. The required generator inputs are consequently the interface, quality profile, and scenario declaration for catalog rows whose `presentation.disposition` is `visual`.

## Expected rehearsal

The writer lane should run:

```powershell
node --test tooling/tests/generation-smoke.test.mjs
node tooling/generate-blazor-smoke.mjs --module hlp.ui.button --module hlp.ui.dialog --module hlp.ui.switch-field
```

The first command proves deterministic generation plus missing-artifact and empty-scenario negative controls. The second generates all three answer-key shells in an OS scratch directory, builds the aggregate Razor project with the repository-pinned SDK, emits a JSON PASS report, and removes the scratch directory.

After that rehearsal passes, run `node tooling/run-phase-4-gate.mjs` through the normal gate workflow. The new step must report the complete catalog-derived visual-module count and a nonzero scenario count.
