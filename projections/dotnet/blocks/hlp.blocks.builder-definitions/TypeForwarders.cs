// Stryker disable all : assembly-level attributes are compile-time constants; Stryker's instrumentation wraps a
// mutated expression in a runtime conditional, which is not legal inside an attribute argument, so every mutant
// Stryker tries here is a guaranteed compile error (CI: "It looks like all mutants resulted in compile errors").
// There is nothing here to mutation-test: this file only restates, verbatim, the four typeof() targets moved to
// hlp.foundation.definitions (owner ruling Q48 / T-724 ruling 116); DefinitionEnvelopeCompatibilityTests proves
// the forwarding actually works at runtime.
using System.Runtime.CompilerServices;

[assembly: TypeForwardedTo(typeof(Harborline.Blocks.BuilderDefinitions.DefinitionAdmissionPhase))]
[assembly: TypeForwardedTo(typeof(Harborline.Blocks.BuilderDefinitions.DefinitionRefusal))]
[assembly: TypeForwardedTo(typeof(Harborline.Blocks.BuilderDefinitions.DefinitionRefusalReport))]
[assembly: TypeForwardedTo(typeof(Harborline.Blocks.BuilderDefinitions.DefinitionRefusalException))]
