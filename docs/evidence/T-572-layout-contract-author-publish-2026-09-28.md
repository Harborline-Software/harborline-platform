# T-572 Layout contract declaration: first slice

Scope: Layout envelope and public author/publish admission only. This is not T-572 acceptance for the five envelopes, supported-window boundaries, install/render routes, or HTTP responses.

RED: `dotnet test projections/dotnet/blocks/hlp.blocks.builder-definitions.tests/Harborline.Blocks.BuilderDefinitions.Tests.csproj --filter FullyQualifiedName~MissingContractRefusesAuthoringAndPublication --no-restore --verbosity quiet` failed 1/1: `Assert.Throws() Failure: No exception was thrown` at the public author admission call.

GREEN: the same focused filter passed 1/1. The affected test project passed 374/374 with `HARBORLINE_CONTROL_REPO=C:\Users\Chris\.codex\worktrees\kernel-control-ck5\harborline-control` and `dotnet test ... --no-build --verbosity quiet -p:RunAnalyzers=false`. Without that environment variable, ten unrelated `InstallReceiptTests` failed while locating DES-0007; the Layout tests passed.

Mutation: repository-pinned `dotnet-stryker` 5.0.0, from `hlp.blocks.builder-definitions.tests`, invoked once with `--mutate LayoutDefinitionAdmission.cs --mutate LayoutDefinition.cs --output .stryker/ck8-layout-contract --verbosity info`. The existing `stryker-config.json` supplies its default coverage analysis. Initialization failed: `Project ... simulated build failed`, `No project found`, `Failed to analyze project builds`. No mutants or report were produced. No unchanged retry was made. Mutation evidence and survivor review remain pending.
