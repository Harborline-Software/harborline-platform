# @harborline-software/contracts

Generated revision-5 Dynamic Forms, revision-2 Workflow, and revision-1 Authorization contracts from pinned source. Authorization exposes qualified role references and pure, caller-supplied vocabulary/held-role resolution; it performs no I/O and derives no grants. Workflow includes the caller-backed fail-closed admission fence behind injected immutable authority resolvers; it does not include a general workflow interpreter. This package is an internal-only projection; public distribution is not authorized.

Use `readFormsWire(type, value)` and `readWorkflowWire(type, value)` at untrusted JSON boundaries. Use `RoleVocabulary.fromApi(...)` and `roleGateAllows(...)` for role gates. Readers tolerate additive object properties and fail closed for unknown closed values, invalid union members, and invalid required/optional/null shapes.
