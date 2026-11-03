# @harborline-software/contracts

Maintained revision-9 Forms, revision-3 Workflow, and revision-1 Rules, Text, Presentation and Authorization contracts. Authorization exposes qualified role references and pure, caller-supplied vocabulary/held-role resolution; it performs no I/O and derives no grants. Workflow includes the caller-backed fail-closed admission fence behind injected immutable authority resolvers; it does not include a general workflow interpreter. This package is an internal-only projection; public distribution is not authorized.

Use `readFormsWire(type, value)` and `readWorkflowWire(type, value)` at untrusted JSON boundaries. Use `RoleVocabulary.fromApi(...)` and `roleGateAllows(...)` for role gates. Readers tolerate additive object properties and fail closed for unknown closed values, invalid union members, and invalid required/optional/null shapes.

Import rule declarations from `@harborline-software/contracts/rules`, locale text from
`@harborline-software/contracts/text`, and hints from `@harborline-software/contracts/presentation`.
The Forms entry point re-exports those types for existing source imports. Workflow imports
shared owner declarations directly. Wire expressions remain strings; parsed Runtime expressions
are a separate Runtime API shape.

The committed projection manifest model, maintained TypeScript declarations and literal neutral
fixtures define the wire authority. The existing generator owns `wire.ts` and C# ModelJson data.
C# declaration carriers and validation support are maintained bindings in the existing Contracts
assembly. Their namespace migration requires source recompilation and a fresh content-bound package
version; the npm package identity remains unchanged and private.
