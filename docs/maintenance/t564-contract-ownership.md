# Shared rule contract migration

Rules, Text and Presentation own their declarations inside the existing `Harborline.Contracts` assembly and `@harborline-software/contracts` package. Forms and Workflow borrow those declarations. The Rules runtime consumes the owners directly.

This changes nominal CLR identities. Recompile consumers against the new content-bound package version and migrate their namespace imports together. Existing compiled callers are not binary compatible with renamed types. Do not publish changed bytes at an existing immutable package version.

| Former `Harborline.Contracts.Forms` declaration | Current owner |
| --- | --- |
| `RuleDefinition`, `RuleTier`, `RuleScope`, `RuleActionKind` | `Harborline.Contracts.Rules` |
| `InternationalizedText` | `Harborline.Contracts.Text` |
| `PresentationHint`, `PresentationHintSeverityValue` | `Harborline.Contracts.Presentation` |
| `Optional<T>`, `OptionalJsonConverterFactory`, `OptionalJsonConverter<T>`, `ClosedEnumJsonConverter<TEnum>` | `Harborline.Contracts.Serialization` |

`RuleCompiler.Compile`, `IGuardEvaluator.EvaluateGuard` and `EvaluateValue`, and `CompiledRule.Source` now expose the Rules-owned `RuleDefinition`. `PresentationOutcome.Badge` exposes the Text-owned `InternationalizedText`. Forms and Workflow properties retain their JSON names and optionality while using these new nominal types and the Serialization-owned `Optional<T>`. Forms state convenience records retain their existing locale helpers and mapping behavior.

TypeScript consumers can retain Forms entry-point type imports because Forms reexports the moved declarations. New consumers import `contracts/rules`, `contracts/text` and `contracts/presentation`. Runtime derives its rule input from the canonical Rules declaration and changes only `expression` to the existing `string | Json` input. Wire expressions remain strings. Runtime's public declarations require the Contracts peer. Local builds resolve its built declarations; clean package consumers install both packed artifacts.

The Forms manifest, owner TypeScript declarations and literal Forms and Workflow fixtures specify the wire contract. C# declaration and validation files are maintained bindings. Their former generated-file markers did not describe the actual generator. `generate-authorization-bindings.mjs` continues to produce `wire.ts` and the embedded `ModelJson` region in `FormsJson.g.cs`. Run the existing generator when its inputs change. Keep handwritten declaration and validator changes outside that generated region.

Forms interface revision 9 and Workflow revision 3 record this ownership migration. Rules, Text and Presentation start at revision 1. Rule Runtime revision 5 composes the frozen T493 revision 4 contract. T564 changes no constructor, capture position, operator, admission priority or evaluation budget. Mac final integration composes the frozen T493 source and shared provenance records.

`RulesContractOwnershipArchitectureTests` reads the actual RuleEngine metadata to reject Forms type references, including nested types and generic arguments. Its source check also rejects erased Forms aliases and imports while allowing explanatory comments. The neutral declaration check follows property, field, method and converter type dependencies. Existing literal wire fixtures pin required fields, closed values, and absent versus null behavior. Rules conformance compares both evaluation tiers against the same outcomes.
