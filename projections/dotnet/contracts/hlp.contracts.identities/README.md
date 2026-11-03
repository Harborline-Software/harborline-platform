# Harborline.Contracts local shadow

This package contains the revision-2 `hlp.contracts.identities` implementation, maintained revision-9 Forms and revision-3 Workflow bindings,
and revision-1 Rules, Text, Presentation and Authorization contributions. Authorization
preserves the API's qualified role and owner shapes, validates the two admitted vocabularies, and
exposes pure fail-closed role-gate resolution without I/O, grants, or standing derivation.

The draft `hlp.contracts.fields` contribution carries the shared field-kind, governance,
three-source value-domain, constraint and refusal declarations under `Harborline.Contracts.Fields`.
Its enforcement lives in `Harborline.Foundation.FieldRuntime`, not in this assembly. Keeping
the declarations here lets kernel Records consume the vocabulary without an outward dependency.

The package is a local, distribution-blocked shadow. At capture time the earlier source held aggregate contract
identity authority; the aggregate cutover was approved separately and this repository now carries that authority.

The manifest model in `projections/typescript/contracts/hlp.contracts.forms/projection-manifest.json`,
maintained TypeScript owner declarations and literal neutral fixtures define the Forms wire.
C# declaration and validation files are maintained bindings. The normal generator writes only
`wire.ts` and the `FormsJson.g.cs` ModelJson data; it does not emit declaration carriers.

Rules grammar lives under `Harborline.Contracts.Rules`, locale text under `.Text`, hints under
`.Presentation`, and shared optional/closed-enum helpers under `.Serialization`. Forms and
Workflow borrow these types. This namespace relocation requires recompiling source consumers;
it does not preserve the former Forms-qualified CLR type identities. JSON fields, requiredness,
closed values, absence and null semantics remain unchanged. The package identity is unchanged;
normal packing derives a fresh version from its changed content.
